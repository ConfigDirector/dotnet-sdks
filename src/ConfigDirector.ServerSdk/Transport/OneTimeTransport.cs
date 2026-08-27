namespace ConfigDirector.Transport;

// One fetch and no polling loop, which a zero interval already means.
internal sealed class OneTimeTransport : PollingTransport
{
    internal OneTimeTransport(TransportOptions options)
        : base(options, TimeSpan.Zero)
    {
    }
}
