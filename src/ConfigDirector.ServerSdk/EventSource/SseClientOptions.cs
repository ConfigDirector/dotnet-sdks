namespace ConfigDirector.EventSource;

internal sealed record SseClientOptions
{
    // Long enough that a stream the server is simply quiet on is not torn down, short enough that
    // a connection dead below the application is noticed.
    private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);

    internal SseClientOptions(Uri url) => Url = url ?? throw new ArgumentNullException(nameof(url));

    internal Uri Url { get; }

    internal HttpMethod Method { get; init; } = HttpMethod.Get;

    // A factory rather than a body: HttpContent cannot be sent twice, and every reconnect is a
    // new request.
    internal Func<HttpContent>? Body { get; init; }

    internal IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Resumes a stream the caller has already read part of.
    internal string? LastEventId { get; init; }

    // How long an open stream may go without delivering an event before it counts as dead.
    // TimeSpan.Zero waits forever.
    internal TimeSpan IdleTimeout { get; init; } = DefaultIdleTimeout;

    // How long to wait for the response headers. Separate from the idle timeout, which only starts
    // once the stream is open, and needed because the HttpClient a stream runs on cannot carry a
    // timeout of its own without severing the stream it just opened. TimeSpan.Zero waits forever.
    internal TimeSpan ConnectTimeout { get; init; } = DefaultConnectTimeout;

    // A status the stream cannot recover from, which ends the read rather than retrying it.
    internal Func<int, bool> IsFatalStatus { get; init; } = status => status is >= 400 and < 500;

    internal Func<SseReconnect, TimeSpan> ReconnectDelay { get; init; } = DefaultDelay;

    // Raised once each time the stream opens, including after a reconnect.
    internal Action? Connected { get; init; }

    // Raised when an open stream drops, before any reconnect attempt.
    internal Action? Disconnected { get; init; }

    // The server's own retry interval when it sent one, otherwise a doubling backoff capped at
    // 30 seconds.
    private static TimeSpan DefaultDelay(SseReconnect reconnect) =>
        reconnect.ServerInterval
        ?? TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(reconnect.Attempt, 5)));
}
