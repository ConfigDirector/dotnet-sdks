using ConfigDirector.EventSource;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Transport;

// Keeps a connection open and applies config state as it arrives.
internal sealed class StreamingTransport : ITransport
{
    private const string Path = "server/sse/v1";

    // Past this many attempts a reconnect is no longer routine and deserves a louder log level.
    private const int QuietAttempts = 5;

    // The server sends a keepalive comment every 15 seconds, so three missed in a row means a dead
    // connection rather than a quiet one.
    internal static TimeSpan DefaultReadTimeout { get; } = TimeSpan.FromSeconds(45);

    private readonly TransportOptions _options;
    private readonly ILogger _logger;
    private readonly SseClient _stream;
    private readonly CancellationTokenSource _stop = new();
    private readonly Random _jitter = new();

    // Completed by the first config state, or by a failure the stream cannot recover from, so
    // whoever is waiting on ConnectAsync is released either way.
    private readonly TaskCompletionSource<bool> _settled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task _reading = Task.CompletedTask;

    internal StreamingTransport(TransportOptions options)
        : this(options, DefaultReadTimeout)
    {
    }

    // The timeout is a parameter so a test can stall a stream out in milliseconds.
    internal StreamingTransport(TransportOptions options, TimeSpan readTimeout)
    {
        _options = options;
        _logger = options.LoggerFactory.CreateLogger<StreamingTransport>();

        _stream = new SseClient(
            options.Http,
            new SseClientOptions(Transports.Resolve(options.BaseUrl, Path))
            {
                Method = HttpMethod.Post,
                Headers = Transports.RequestHeaders,
                Body = () => Transports.JsonBody(Transports.RequestPayload(options, null)),
                IdleTimeout = readTimeout,
                IsFatalStatus = Transports.IsFatalStatus,
                ReconnectDelay = ReconnectDelay,
                Connected = () => Log.Connected(_logger, null),
            },
            _logger);
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        _reading = ReadAsync();

        // Returning on the timeout is not a failure: the stream keeps retrying in the background,
        // and the client reports itself unready until config state arrives.
        return _settled.Task.WaitOrCancel(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }

        try
        {
            await _reading.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The stream stopping is what was asked for.
        }

        _stop.Dispose();
    }

    private async Task ReadAsync()
    {
        try
        {
            await foreach (var message in _stream.ReadAsync(_stop.Token).ConfigureAwait(false))
            {
                Apply(message.Data, message.EventType);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (SseStatusException fatal)
        {
            var error = Transports.FatalStatusError(fatal.Status, null);
            Log.Fatal(_logger, error.Message, null);

            // Whoever is still blocked in ConnectAsync is waiting for exactly this.
            _settled.TrySetException(error);
        }
        catch (Exception error)
        {
            // The read loop must not die without saying why.
            Log.ReadStopped(_logger, error);
            _settled.TrySetException(error);
        }
    }

    private void Apply(string data, string eventType)
    {
        ConfigBundle bundle;
        try
        {
            bundle = BundleParser.Parse(data, _logger);
        }
        catch (NotAConfigBundleException)
        {
            // A heartbeat, or any other frame the stream carries alongside config updates. Skipped
            // rather than applied: an empty bundle would read as a full one and clear config state.
            Log.IgnoredEvent(_logger, eventType, null);
            return;
        }
        catch (BundleFormatException error)
        {
            Log.UnreadableUpdate(_logger, error);
            return;
        }

        _options.OnBundle(bundle);
        _settled.TrySetResult(true);
    }

    private TimeSpan ReconnectDelay(SseReconnect reconnect)
    {
        var delay = Transports.BackoffDelay(reconnect.Attempt, _jitter);
        if (reconnect.Attempt <= QuietAttempts)
        {
            Log.Reconnecting(_logger, reconnect.Attempt, delay, null);
        }
        else
        {
            Log.ReconnectingLate(_logger, reconnect.Attempt, delay, null);
        }

        return delay;
    }

    private static class Log
    {
        internal static readonly Action<ILogger, Exception?> Connected =
            LoggerMessage.Define(LogLevel.Debug, new EventId(1, "Connected"), "Connected to the config stream.");

        internal static readonly Action<ILogger, string, Exception?> IgnoredEvent =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(2, "IgnoredEvent"),
                "Ignoring a '{EventType}' event, which carries no config state.");

        internal static readonly Action<ILogger, Exception?> UnreadableUpdate =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(3, "UnreadableUpdate"),
                "A config update could not be read.");

        internal static readonly Action<ILogger, int, TimeSpan, Exception?> Reconnecting =
            LoggerMessage.Define<int, TimeSpan>(
                LogLevel.Information,
                new EventId(4, "Reconnecting"),
                "Scheduling reconnect attempt #{Attempt} in {Delay}.");

        internal static readonly Action<ILogger, int, TimeSpan, Exception?> ReconnectingLate =
            LoggerMessage.Define<int, TimeSpan>(
                LogLevel.Warning,
                new EventId(5, "ReconnectingLate"),
                "Scheduling reconnect attempt #{Attempt} in {Delay}.");

        internal static readonly Action<ILogger, string, Exception?> Fatal =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(6, "Fatal"), "{Reason}");

        internal static readonly Action<ILogger, Exception?> ReadStopped =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(7, "ReadStopped"),
                "The config stream stopped unexpectedly.");
    }
}
