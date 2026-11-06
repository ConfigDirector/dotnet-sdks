using Microsoft.Extensions.Logging;

namespace ConfigDirector.Telemetry;

internal sealed class TrackedEventRecordingTelemetryCollector(
    ILogger logger, Action<TrackedEventAdmission.Admitted> recordTrackedEvent)
    : ITelemetryCollector
{
    private static readonly Action<ILogger, string, Exception?> LogWarning =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "TrackedEventWarning"), "{Warning}");

    public void Record<T>(
        string key,
        T defaultValue,
        T value,
        bool usedDefault,
        EvaluationReason reason,
        Context? context,
        ConfigType? configType,
        string? valueId,
        string? variationId,
        string? ruleId,
        string? shareId)
    {
    }

    public InvalidContextLogging RecordInvalidContext(string kindSample) => InvalidContextLogging.LogKind;

    public void RecordTrackedEvent(TrackedEventAdmission admission)
    {
        foreach (var warning in admission.Warnings)
        {
            LogWarning(logger, TrackedEventWarnings.MessageOf(warning), null);
        }

        switch (admission)
        {
            case TrackedEventAdmission.Dropped:
                break;
            case TrackedEventAdmission.Admitted admitted:
                recordTrackedEvent(admitted);
                break;
            default:
                throw new InvalidOperationException("The tracked event admission is not one the test client knows.");
        }
    }

    public ValueTask DisposeAsync() => default;
}
