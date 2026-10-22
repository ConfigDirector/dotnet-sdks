namespace ConfigDirector.Transport;

internal sealed class OfflineTransport : ITransport
{
    private const string EnvironmentId = "offline";
    private const string ProjectId = "offline";

    private readonly OfflineSource _source;
    private readonly Action<ConfigBundle> _onBundle;
    private readonly object _deliveryLock = new();

    internal OfflineTransport(OfflineSource source, Action<ConfigBundle> onBundle)
    {
        _source = source;
        _onBundle = onBundle;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        lock (_deliveryLock)
        {
            _onBundle(new ConfigBundle
            {
                Configs = _source.LoadConfigs(),
                Kind = BundleKind.Full,
                EnvironmentId = EnvironmentId,
                ProjectId = ProjectId,
            });
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => default;
}
