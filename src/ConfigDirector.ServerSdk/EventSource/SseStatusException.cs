namespace ConfigDirector.EventSource;

// Thrown out of the stream when the server answered with a status it cannot continue from, and
// carried into a retry when the status is one worth trying again.
internal sealed class SseStatusException : Exception
{
    internal SseStatusException(int status)
        : base($"The event stream request failed with status {status}.") => Status = status;

    internal int Status { get; }
}
