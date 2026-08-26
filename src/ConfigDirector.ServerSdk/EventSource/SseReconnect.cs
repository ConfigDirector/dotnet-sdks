namespace ConfigDirector.EventSource;

// Why the last attempt ended, and what the server asked for. Status is null when the request
// failed before a response arrived.
internal readonly record struct SseReconnect(
    int Attempt,
    TimeSpan? ServerInterval,
    int? Status,
    Exception? Error);
