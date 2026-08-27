using System.Globalization;
using System.Text.Json;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Telemetry;

// Sends a batch of events to ConfigDirector. Telemetry is best-effort background work, so a
// request that fails is reported back rather than thrown.
internal sealed class HttpEventReporter : IAsyncDisposable
{
    private const string Path = "server/telemetry/v1";

    // Telemetry waits a good deal less than a transport does before giving up, and lets the next
    // flush carry the events instead. Nothing here streams, so bounding the whole exchange is
    // what is wanted.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly string _serverSdkKey;
    private readonly Uri _url;
    private readonly ILogger _logger;
    private readonly HttpClient _http = new() { Timeout = RequestTimeout };

    private bool _sendRequests = true;

    internal HttpEventReporter(string serverSdkKey, Uri baseUrl, ILoggerFactory loggerFactory)
    {
        _serverSdkKey = serverSdkKey;
        _url = Transports.Resolve(baseUrl, Path);
        _logger = loggerFactory.CreateLogger<HttpEventReporter>();
    }

    internal async Task<ReporterResponse> ReportAsync(EventReport report, CancellationToken cancellationToken)
    {
        if (!_sendRequests)
        {
            return new ReporterResponse(false, Fatal: true);
        }

        if (report.IsEmpty)
        {
            return new ReporterResponse(true);
        }

        var response = await SendAsync(Payload(report), cancellationToken).ConfigureAwait(false);
        if (response.Fatal)
        {
            _sendRequests = false;
        }

        return response;
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return default;
    }

    private async Task<ReporterResponse> SendAsync(byte[] body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = Transports.JsonBody(body),
            };

            foreach (var header in Transports.RequestHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return Outcome((int)response.StatusCode);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            Log.SendFailed(_logger, error);
            return new ReporterResponse(false);
        }
    }

    private ReporterResponse Outcome(int status)
    {
        if (Transports.IsFatalStatus(status))
        {
            Log.Rejected(_logger, status, null);
            return new ReporterResponse(false, Fatal: true);
        }

        if (status is >= 200 and < 300)
        {
            Log.Sent(_logger, null);
            return new ReporterResponse(true);
        }

        Log.Discarded(_logger, status, null);
        return new ReporterResponse(false);
    }

    private byte[] Payload(EventReport report)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("serverSdkKey", _serverSdkKey);

            json.WriteStartObject("metaContext");
            json.WriteString("sdkName", SdkIdentity.Name);
            json.WriteString("sdkVersion", SdkIdentity.Version);
            json.WriteEndObject();

            json.WriteStartObject("discreteEvents");
            json.WriteStartArray("capturedContexts");
            foreach (var context in report.Contexts)
            {
                WriteContext(json, context);
            }

            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteStartObject("aggregatedEvents");
            json.WriteStartArray("evaluatedConfig");
            foreach (var aggregated in report.Evaluations)
            {
                WriteAggregated(json, aggregated);
            }

            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteStartObject("droppedEvents");
            json.WriteNumber("evaluatedConfig", report.DroppedEvaluations);
            json.WriteNumber("capturedContexts", report.DroppedContexts);
            json.WriteEndObject();

            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static void WriteAggregated(Utf8JsonWriter json, AggregatedEvent aggregated)
    {
        json.WriteStartObject();
        json.WriteString("startTime", Timestamp(aggregated.StartTime));
        json.WriteString("endTime", Timestamp(aggregated.EndTime));
        json.WriteNumber("count", aggregated.Count);
        json.WritePropertyName("event");
        JsonSerializer.Serialize(json, aggregated.Event, TelemetryWire.Options);
        json.WriteEndObject();
    }

    // Only identified, non-anonymous contexts are ever captured, so `anonymous` is left out rather
    // than sent as a constant false.
    private static void WriteContext(Utf8JsonWriter json, Context context)
    {
        json.WriteStartObject();
        json.WriteString("id", context.Id);

        if (context.Name is not null)
        {
            json.WriteString("name", context.Name);
        }

        if (context.Traits.Count > 0)
        {
            json.WriteStartObject("traits");
            foreach (var trait in context.Traits)
            {
                json.WritePropertyName(trait.Key);
                WriteTrait(json, trait.Value);
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
    }

    private static void WriteTrait(Utf8JsonWriter json, TraitValue trait)
    {
        switch (trait.Kind)
        {
            case TraitValueKind.String:
                json.WriteStringValue(trait.StringValue);
                break;
            case TraitValueKind.Boolean:
                json.WriteBooleanValue(trait.BooleanValue);
                break;
            case TraitValueKind.Number:
                WriteNumber(json, trait);
                break;
            case TraitValueKind.Array:
                json.WriteStartArray();
                foreach (var element in trait.Elements)
                {
                    WriteTrait(json, element);
                }

                json.WriteEndArray();
                break;
            case TraitValueKind.Object:
                json.WriteStartObject();
                foreach (var member in trait.Members ?? new Dictionary<string, TraitValue>())
                {
                    json.WritePropertyName(member.Key);
                    WriteTrait(json, member.Value);
                }

                json.WriteEndObject();
                break;
            default:
                json.WriteNullValue();
                break;
        }
    }

    private static void WriteNumber(Utf8JsonWriter json, TraitValue trait)
    {
        if (trait.IsIntegral)
        {
            json.WriteNumberValue(trait.IntegerValue);
            return;
        }

        var number = trait.DoubleValue;
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            json.WriteNullValue();
            return;
        }

        json.WriteNumberValue(number);
    }

    // RFC 3339 with a trailing Z, the spelling the other SDKs send and the server parses.
    private static string Timestamp(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static class Log
    {
        internal static readonly Action<ILogger, Exception?> Sent =
            LoggerMessage.Define(
                LogLevel.Debug, new EventId(1, "Sent"), "Telemetry report successfully sent.");

        internal static readonly Action<ILogger, Exception?> SendFailed =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(2, "SendFailed"),
                "Error attempting to send telemetry data.");

        internal static readonly Action<ILogger, int, Exception?> Rejected =
            LoggerMessage.Define<int>(
                LogLevel.Warning,
                new EventId(3, "Rejected"),
                "The telemetry endpoint rejected the report with status {Status}. No more telemetry "
                    + "data will be sent.");

        internal static readonly Action<ILogger, int, Exception?> Discarded =
            LoggerMessage.Define<int>(
                LogLevel.Warning,
                new EventId(4, "Discarded"),
                "The telemetry endpoint responded with status {Status}. The events in this report "
                    + "were discarded.");
    }
}
