using System.Text.Json.Serialization;

namespace ConfigDirector.Telemetry;

internal sealed record InvalidContextEvent
{
    internal const string InvalidContextKind = "invalid-context-kind";

    [JsonPropertyName("reason")]
    public string Reason { get; } = InvalidContextKind;

    [JsonPropertyName("kindSample")]
    public string? KindSample { get; init; }
}
