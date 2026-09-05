namespace ConfigDirector.Transport;

internal static class TransportFactory
{
    internal static ITransport Create(ConnectionMode mode, TransportOptions options) =>
        mode switch
        {
            ConnectionMode.Polling => new PollingTransport(options),
            _ => new StreamingTransport(options),
        };
}
