namespace ConfigDirector.EventSource;

// The server ended a stream it had been serving. Not an error in itself -- the stream is simply
// over, and worth resuming.
internal sealed class SseStreamClosedException : Exception
{
    internal SseStreamClosedException()
        : base("The event stream was closed by the server.")
    {
    }
}
