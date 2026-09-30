namespace ConfigDirector.Telemetry;

internal interface ITelemetryCollector : IAsyncDisposable
{
    void Record<T>(
        string key,
        T defaultValue,
        T value,
        bool usedDefault,
        EvaluationReason reason,
        Context? context,
        ConfigType? configType,
        string? valueId);
}
