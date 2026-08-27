using Microsoft.Extensions.Logging;

namespace ConfigDirector.Transport;

internal sealed record TransportOptions
{
    internal TransportOptions(
        string serverSdkKey,
        Uri baseUrl,
        HttpClient http,
        Action<ConfigBundle> onBundle,
        ILoggerFactory loggerFactory)
    {
        ServerSdkKey = serverSdkKey;
        BaseUrl = baseUrl;
        Http = http;
        OnBundle = onBundle;
        LoggerFactory = loggerFactory;
    }

    internal string ServerSdkKey { get; }

    internal Uri BaseUrl { get; }

    internal HttpClient Http { get; }

    internal Action<ConfigBundle> OnBundle { get; }

    internal ILoggerFactory LoggerFactory { get; }

    internal Metadata? Metadata { get; init; }

    internal TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(60);
}
