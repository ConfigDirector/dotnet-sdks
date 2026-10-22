namespace ConfigDirector.Transport;

internal static class TransportFactory
{
    internal static ITransport Create(LiveConnectionMode mode, TransportOptions options) =>
        mode switch
        {
            LiveConnectionMode.Polling => new PollingTransport(options),
            _ => new StreamingTransport(options),
        };
}
