using System.Text.Json.Serialization;

namespace ConfigDirector.Telemetry;

// A single config evaluation, as the server reads it. Equality across every reported field is what
// decides which evaluations collapse together when a report is aggregated.
internal sealed record EvaluatedConfigEvent
{
    [JsonPropertyName("contextId")]
    public string? ContextId { get; init; }

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public ConfigType? Type { get; init; }

    [JsonPropertyName("defaultValue")]
    public TelemetryValue DefaultValue { get; init; } = new();

    [JsonPropertyName("requestedType")]
    public string RequestedType { get; init; } = string.Empty;

    [JsonPropertyName("evaluatedValue")]
    public TelemetryValue EvaluatedValue { get; init; } = new();

    [JsonPropertyName("evaluatedValueId")]
    public string? EvaluatedValueId { get; init; }

    [JsonPropertyName("usedDefault")]
    public bool UsedDefault { get; init; }

    [JsonPropertyName("evaluationReason")]
    [JsonConverter(typeof(EvaluationReasonJsonConverter))]
    public EvaluationReason Reason { get; init; }

    internal static EvaluatedConfigEvent Create<T>(
        string key,
        T defaultValue,
        T value,
        bool usedDefault,
        EvaluationReason reason,
        string? contextId = null,
        ConfigType? configType = null,
        string? valueId = null) =>
        new()
        {
            Key = key,
            DefaultValue = TelemetryValue.From(defaultValue, configType),
            EvaluatedValue = TelemetryValue.From(value, configType, valueId),
            RequestedType = TypeName<T>.Name,
            UsedDefault = usedDefault,
            Reason = reason,
            ContextId = contextId,
            Type = configType,
            EvaluatedValueId = valueId,
        };

    internal EvaluatedConfigEvent Compacted() =>
        this with { DefaultValue = DefaultValue.Compacted(), EvaluatedValue = EvaluatedValue.Compacted() };

    // The type the caller asked the config to be returned as, named the way .NET names it -- the
    // JavaScript SDK reports string/number/Object for the same three. The arity a generic type
    // carries in its name is dropped, so a bound dictionary does not report "Dictionary`2".
    // Worked out once per T rather than on every evaluation, which is a hot path.
    private static class TypeName<T>
    {
        internal static readonly string Name = Resolve();

        private static string Resolve()
        {
            var name = typeof(T).Name;
            var arity = name.IndexOf('`');
            return arity < 0 ? name : name.Substring(0, arity);
        }
    }
}
