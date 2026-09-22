using Microsoft.Extensions.Logging;

namespace ConfigDirector.Telemetry;

// The properties are internal deliberately: a record prints its public members from the generated
// ToString, and the server SDK key is a secret that must not reach a log or a debugger view.
internal sealed record TelemetryCollectorOptions
{
    internal TelemetryCollectorOptions(
        string serverSdkKey, Uri baseUrl, SdkIdentity identity, ILoggerFactory loggerFactory)
    {
        ServerSdkKey = serverSdkKey;
        BaseUrl = baseUrl;
        Identity = identity;
        LoggerFactory = loggerFactory;
    }

    internal string ServerSdkKey { get; }

    internal Uri BaseUrl { get; }

    internal SdkIdentity Identity { get; }

    internal ILoggerFactory LoggerFactory { get; }

    internal int EventQueueLimit { get; init; } = TelemetryOptions.DefaultEventQueueLimit;

    internal TimeSpan FlushInterval { get; init; } = TelemetryOptions.DefaultFlushInterval;
}
