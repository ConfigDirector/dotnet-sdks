using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ConfigDirector.Telemetry;

// How a telemetry report is written for the server to read. The metadata is source generated, so
// nothing here needs reflection over the SDK's own types and a trimmed application keeps working.
internal static class TelemetryWire
{
    // The encoder cannot be set through JsonSourceGenerationOptions, so the options are built here
    // and the generated context is bound to them.
    private static readonly TelemetryWireContext Context = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    internal static JsonTypeInfo<EvaluatedConfigEvent> Event => Context.EvaluatedConfigEvent;
}

[JsonSerializable(typeof(EvaluatedConfigEvent))]
internal sealed partial class TelemetryWireContext : JsonSerializerContext;
