using Microsoft.Extensions.Logging;

namespace ConfigDirector.Telemetry;

internal sealed record TelemetryCollectorOptions(
    string ServerSdkKey,
    Uri BaseUrl,
    ILoggerFactory LoggerFactory)
{
    internal int EventQueueLimit { get; init; } = TelemetryOptions.DefaultEventQueueLimit;

    internal TimeSpan FlushInterval { get; init; } = TelemetryOptions.DefaultFlushInterval;
}
