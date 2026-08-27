using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigDirector.Telemetry;

// How a telemetry report is written for the server to read.
internal static class TelemetryWire
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed class EvaluationReasonJsonConverter : JsonConverter<EvaluationReason>
{
    public override EvaluationReason Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Telemetry is only ever written.");

    public override void Write(
        Utf8JsonWriter writer, EvaluationReason value, JsonSerializerOptions options) =>
        writer.WriteStringValue(EvaluationReasons.WireName(value));
}
