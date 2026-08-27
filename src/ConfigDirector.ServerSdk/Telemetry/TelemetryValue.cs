using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigDirector.Telemetry;

// One side of an evaluation, either the caller's default or what the evaluation returned. Held as
// text so that identical evaluations compare equal and collapse together when they are aggregated.
internal sealed record TelemetryValue
{
    // Longer values are reported by ID rather than inline, to keep telemetry payloads small.
    internal const int ConfigValueMaxLength = 500;

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("valueId")]
    public string? ValueId { get; init; }

    [JsonPropertyName("type")]
    public ConfigType? Type { get; init; }

    // `valueId` is the ID the server sent along with the config state, when there was one.
    internal static TelemetryValue From<T>(T value, ConfigType? configType = null, string? valueId = null)
    {
        if (IsJson(value, configType))
        {
            return valueId is null
                ? new TelemetryValue { Value = TelemetryJson.Serialize(value), Type = ConfigType.Json }
                : new TelemetryValue { ValueId = valueId, Type = ConfigType.Json };
        }

        var rendered = Render(value);
        if (rendered.Length <= ConfigValueMaxLength)
        {
            return new TelemetryValue { Value = rendered };
        }

        return valueId is null
            ? new TelemetryValue { Value = rendered }
            : new TelemetryValue { ValueId = valueId };
    }

    // The form that is sent to the server: values too large to report inline, and every JSON
    // document, are replaced by their ID. This is the only step that hashes, which is why it runs
    // on the flush thread rather than on the caller's.
    internal TelemetryValue Compacted()
    {
        if (ValueId is not null)
        {
            return new TelemetryValue { ValueId = ValueId };
        }

        return !string.IsNullOrEmpty(Value) && (Type == ConfigType.Json || Value!.Length > ConfigValueMaxLength)
            ? new TelemetryValue { ValueId = ValueIds.Generate(Value!) }
            : new TelemetryValue { Value = Value };
    }

    // A scalar is reported as the text the config holds, so a string config reads "hello" on the
    // dashboard rather than as the JSON literal it would be inside a document.
    private static string Render<T>(T value) =>
        value is string text ? text : TelemetryJson.Serialize(value);

    // An evaluation that found no config state has no declared type, so the shape of the value is
    // all there is to go on.
    private static bool IsJson<T>(T value, ConfigType? configType) =>
        configType == ConfigType.Json || (configType is null && IsDocument(value));

    private static bool IsDocument<T>(T value) => value switch
    {
        null => false,
        string or bool or int or long or double or float or decimal => false,
        JsonElement element => element.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
        _ => true,
    };
}
