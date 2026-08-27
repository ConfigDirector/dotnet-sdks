using System.Net;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Transport;

// Fetches config state once, then again on every interval. A zero interval fetches once and stops,
// which is what one-time mode is.
internal class PollingTransport : ITransport
{
    private const string Path = "server/polling/v1";

    private readonly TransportOptions _options;
    private readonly ILogger _logger;
    private readonly Uri _url;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();

    private string? _lastUpdateTimestamp;
    private Task _polling = Task.CompletedTask;
    private volatile bool _fatal;

    internal PollingTransport(TransportOptions options, TimeSpan interval)
    {
        _options = options;
        _logger = options.LoggerFactory.CreateLogger<PollingTransport>();
        _url = Transports.Resolve(options.BaseUrl, Path);
        _interval = interval > TimeSpan.Zero ? interval : TimeSpan.Zero;
    }

    internal PollingTransport(TransportOptions options)
        : this(options, options.PollingInterval)
    {
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A transient failure on the first fetch must not leave the SDK without a connection,
            // so polling starts either way. An unrecoverable one has already cancelled the loop.
            if (_interval > TimeSpan.Zero && !_stop.IsCancellationRequested)
            {
                _polling = PollAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }

        try
        {
            await _polling.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The loop stopping is what was asked for.
        }

        _stop.Dispose();
    }

    private async Task PollAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, _stop.Token).ConfigureAwait(false);
                await FetchAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ConfigDirectorConnectionException error)
            {
                Log.PollFailed(_logger, error);
            }

            if (_fatal)
            {
                return;
            }
        }
    }

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        if (_fatal)
        {
            Log.IgnoringRetry(_logger, null);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = Transports.JsonBody(Transports.RequestPayload(_options, _lastUpdateTimestamp)),
        };

        foreach (var header in Transports.RequestHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;

        if (status == (int)HttpStatusCode.NoContent)
        {
            // The server has nothing newer than the timestamp that was sent.
            return;
        }

        var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Transports.IsFatalStatus(status)
                ? StopFor(Transports.FatalStatusError(status, body))
                : new ConfigDirectorConnectionException($"Connection failed with status: {status}", status);
        }

        ConfigBundle bundle;
        try
        {
            bundle = BundleParser.Parse(body, _logger);
        }
        catch (Exception error) when (error is BundleFormatException or NotAConfigBundleException)
        {
            throw new ConfigDirectorConnectionException(
                $"Failed to parse the response from the server: {error.Message}", error);
        }

        if (bundle.Timestamp is not null)
        {
            _lastUpdateTimestamp = bundle.Timestamp;
        }

        _options.OnBundle(bundle);
    }

    private static Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken) =>
#if NET8_0_OR_GREATER
        content.ReadAsStringAsync(cancellationToken);
#else
        content.ReadAsStringAsync();
#endif

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _options.Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error)
        {
            // Refused, unresolved, reset -- all worth retrying on the next interval.
            throw new ConfigDirectorConnectionException($"Connection to {_url} failed.", error);
        }
    }

    // Returns rather than throws so a caller can `throw StopFor(...)` and have the compiler see
    // that path terminate.
    private ConfigDirectorConnectionException StopFor(ConfigDirectorConnectionException error)
    {
        _fatal = true;
        Log.Fatal(_logger, error.Message, null);
        _stop.Cancel();
        return error;
    }

    private static class Log
    {
        internal static readonly Action<ILogger, Exception?> PollFailed =
            LoggerMessage.Define(LogLevel.Error, new EventId(1, "PollFailed"), "A poll for config state failed.");

        internal static readonly Action<ILogger, Exception?> IgnoringRetry =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(2, "IgnoringRetry"),
                "There was a prior unrecoverable error. Ignoring the attempt to reconnect.");

        internal static readonly Action<ILogger, string, Exception?> Fatal =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "Fatal"), "{Reason}");
    }
}
