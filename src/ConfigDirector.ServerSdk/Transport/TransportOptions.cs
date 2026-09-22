using Microsoft.Extensions.Logging;

namespace ConfigDirector.Transport;

internal sealed record TransportOptions
{
    internal TransportOptions(
        string serverSdkKey,
        Uri baseUrl,
        Action<ConfigBundle> onBundle,
        SdkIdentity identity,
        ILoggerFactory loggerFactory)
    {
        ServerSdkKey = serverSdkKey;
        BaseUrl = baseUrl;
        OnBundle = onBundle;
        Identity = identity;
        LoggerFactory = loggerFactory;
    }

    internal string ServerSdkKey { get; }

    internal Uri BaseUrl { get; }

    internal SdkIdentity Identity { get; }

    internal Action<ConfigBundle> OnBundle { get; }

    internal ILoggerFactory LoggerFactory { get; }

    internal Metadata? Metadata { get; init; }

    internal TimeSpan PollingInterval { get; init; } = ConnectionOptions.DefaultPollingInterval;

    // Bounds a single request, since the HttpClient underneath carries no timeout of its own.
    internal TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);
}
