namespace ConfigDirector.Transport;

internal interface ITransport : IAsyncDisposable
{
    // Completes once the first config state has been handed to the client, or once the token is
    // cancelled. A transient failure leaves the transport retrying rather than completing.
    Task ConnectAsync(CancellationToken cancellationToken);
}
