namespace ConfigDirector.Telemetry;

internal sealed class DiscardingTelemetryCollector : ITelemetryCollector
{
    private DiscardingTelemetryCollector()
    {
    }

    internal static DiscardingTelemetryCollector Instance { get; } = new();

    public void Record<T>(
        string key,
        T defaultValue,
        T value,
        bool usedDefault,
        EvaluationReason reason,
        Context? context,
        ConfigType? configType,
        string? valueId)
    {
    }

    public ValueTask DisposeAsync() => default;
}
