using System.Text;
using System.Text.Json;

namespace ConfigDirector.Transport;

internal static class Transports
{
    // 2^9 = 512 seconds, which caps the backoff just under 10 minutes.
    private const int LongestBackoffExponent = 9;

    // How much of each delay is fixed; the rest is drawn at random. Half and half keeps the delay
    // growing with every attempt while spreading a fleet that all lost the connection at once.
    private const double BackoffFixedShare = 0.5;

    internal static Uri DefaultBaseUrl { get; } = new("https://server-sdk-api.configdirector.com/");

    // Long enough not to churn connections, short enough that a failover is picked up without a
    // restart. Matches what IHttpClientFactory rotates handlers on.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    // HttpClient.Timeout bounds the whole exchange on .NET Framework, the response body included,
    // so a streaming connection there is severed the moment it outlives it. Measured on .NET 10 it
    // stops at the response headers and leaves the body alone, which makes this insurance for the
    // netstandard2.0 target rather than something the modern runtimes need. Either way a transport
    // bounds its own requests: one deadline per request while polling, one for opening a stream.
    internal static HttpClient BuildHttpClient() => BuildHttpClient(Timeout.InfiniteTimeSpan);

    internal static HttpClient BuildHttpClient(TimeSpan timeout)
    {
#if NET8_0_OR_GREATER
        // A client is built once and kept for the life of the process, so without a lifetime its
        // pooled connections pin whatever address DNS resolved at startup and a ConfigDirector
        // failover stays invisible until a restart. Expiry is applied when a connection returns to
        // the pool, so it never severs a stream that is still being read.
        return new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime })
        {
            Timeout = timeout,
        };
#else
        // .NET Framework pools through ServicePointManager, whose equivalent setting is global to
        // the host process rather than to this client, so it is left to the application.
        return new HttpClient { Timeout = timeout };
#endif
    }

    internal static bool IsFatalStatus(int status) => status is >= 400 and < 500;

    internal static Uri Resolve(Uri baseUrl, string path)
    {
        var text = baseUrl.AbsoluteUri;
        var root = text[text.Length - 1] == '/' ? text : text + "/";
        return new Uri(new Uri(root), path);
    }

    internal static ConfigDirectorConnectionException FatalStatusError(int status, string? detail)
    {
        var body = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" ({detail!.Trim()})";
        return new ConfigDirectorConnectionException(
            $"Connection failed with status: {status}{body}. This is an unrecoverable error, "
                + "retry attempts will be ignored.",
            status);
    }

    internal static byte[] RequestPayload(TransportOptions options, string? lastUpdateTimestamp, string sessionId)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("serverSdkKey", options.ServerSdkKey);

            json.WriteStartObject("metaContext");
            json.WriteString("sdkName", SdkIdentity.Name);
            json.WriteString("sdkVersion", SdkIdentity.Version);
            WriteIfPresent(json, "appName", options.Metadata?.AppName);
            WriteIfPresent(json, "appVersion", options.Metadata?.AppVersion);
            json.WriteEndObject();

            WriteIfPresent(json, "lastUpdateTimestamp", lastUpdateTimestamp);
            json.WriteString("sessionId", sessionId);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static IReadOnlyDictionary<string, string> RequestHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Left to itself HttpClient sends no user agent at all, and bot-protection layers in
            // front of the API reject that before the request reaches the origin -- surfacing as a
            // 403 that looks exactly like a rejected SDK key.
            ["User-Agent"] = SdkIdentity.UserAgent,
        };

    internal static TimeSpan BackoffDelay(int attempt, Random random)
    {
        var exponent = Math.Min(Math.Max(attempt, 1), LongestBackoffExponent);
        var ceiling = TimeSpan.FromSeconds(1L << exponent).TotalMilliseconds;
        var fixedPart = ceiling * BackoffFixedShare;
        return TimeSpan.FromMilliseconds(fixedPart + (random.NextDouble() * (ceiling - fixedPart)));
    }

    internal static HttpContent JsonBody(byte[] payload) =>
        new ByteArrayContent(payload)
        {
            Headers = { { "Content-Type", "application/json" } },
        };


    private static void WriteIfPresent(Utf8JsonWriter json, string name, string? value)
    {
        if (value is not null)
        {
            json.WriteString(name, value);
        }
    }
}
