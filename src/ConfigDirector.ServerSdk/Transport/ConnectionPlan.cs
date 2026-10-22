namespace ConfigDirector.Transport;

internal abstract record ConnectionPlan
{
    private const string MissingServerSdkKey = "The client cannot be built without a server SDK key.";

    internal static ConnectionPlan Of(string? serverSdkKey, ConnectionOptions connection)
    {
        var file = string.IsNullOrWhiteSpace(connection.File) ? null : connection.File;

        return connection.Mode switch
        {
            ConnectionMode.Offline => new StayOffline(OfflineSourceOf(file, connection.Values)),
            ConnectionMode.Polling => LiveModePlan(LiveConnectionMode.Polling, serverSdkKey, file, connection),
            _ => LiveModePlan(LiveConnectionMode.Streaming, serverSdkKey, file, connection),
        };
    }

    private static ConnectionPlan LiveModePlan(
        LiveConnectionMode mode, string? serverSdkKey, string? file, ConnectionOptions connection)
    {
        if (serverSdkKey is null)
        {
            throw new ArgumentNullException(nameof(serverSdkKey), MissingServerSdkKey);
        }

        if (string.IsNullOrWhiteSpace(serverSdkKey))
        {
            throw new ArgumentException(MissingServerSdkKey, nameof(serverSdkKey));
        }

        if (file is not null || connection.Values is not null)
        {
            return new StayOffline(new OfflineSource.Rejected(
                $"{nameof(ConnectionOptions.File)} and {nameof(ConnectionOptions.Values)} apply only when the connection "
                    + $"mode is {nameof(ConnectionMode.Offline)}, but it is {mode}."));
        }

        return new Connect(mode, serverSdkKey, connection.Url ?? Transports.DefaultBaseUrl);
    }

    private static OfflineSource OfflineSourceOf(string? file, IReadOnlyDictionary<string, object>? values) =>
        (file, values) switch
        {
            (null, null) => new OfflineSource.InCodeDefaults(),
            ({ } path, null) => new OfflineSource.ValuesFile(path),
            (null, { } given) => new OfflineSource.InlineValues(
                given.ToDictionary(entry => entry.Key, entry => (object?)entry.Value, StringComparer.Ordinal)),
            ({ }, { }) => new OfflineSource.Rejected(
                $"An offline connection takes either {nameof(ConnectionOptions.File)} or {nameof(ConnectionOptions.Values)}, not both."),
        };

    internal sealed record Connect : ConnectionPlan
    {
        internal Connect(LiveConnectionMode mode, string serverSdkKey, Uri baseUrl)
        {
            Mode = mode;
            ServerSdkKey = serverSdkKey;
            BaseUrl = baseUrl;
        }

        internal LiveConnectionMode Mode { get; }

        internal string ServerSdkKey { get; }

        internal Uri BaseUrl { get; }
    }

    internal sealed record StayOffline(OfflineSource Source) : ConnectionPlan;
}
