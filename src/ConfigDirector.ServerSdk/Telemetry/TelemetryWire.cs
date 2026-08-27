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
