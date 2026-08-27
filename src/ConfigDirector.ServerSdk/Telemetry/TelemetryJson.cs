using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ConfigDirector.Value;

namespace ConfigDirector.Telemetry;

// Renders a value the way JSON.stringify does in the other ConfigDirector SDKs: nothing between
// the punctuation, keys in the order they were written, and non-ASCII left alone. A value too
// large to report inline is identified by the digest of this text, so the same value has to
// render identically everywhere or it would be counted as two.
internal static class TelemetryJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static string Serialize<T>(T value)
    {
        switch (value)
        {
            case null:
                return "null";
            case bool flag:
                return flag ? "true" : "false";
            case string text:
                return Quote(text);
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case long number:
                return number.ToString(CultureInfo.InvariantCulture);
            case double number:
                return double.IsNaN(number) || double.IsInfinity(number)
                    ? "null"
                    : JsonNumberText.Render(number);
            case float number:
                return float.IsNaN(number) || float.IsInfinity(number)
                    ? "null"
                    : JsonNumberText.Render(number);

            // A decimal keeps however many trailing zeros it was written with, where every other
            // SDK holds the same value as a double and renders it without them.
            case decimal number:
                return number.ToString("G29", CultureInfo.InvariantCulture);
            case JsonElement element:
                return Write(element);
            default:
                return Write(JsonSerializer.SerializeToElement(value, Options));
        }
    }

    private static string Write(JsonElement element)
    {
        var json = new StringBuilder();
        Write(json, element);
        return json.ToString();
    }

    // Recursion is bounded by the serializer's own depth limit, which rejects a document deeper
    // than 64 levels before it ever reaches here.
    private static void Write(StringBuilder json, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(json, element);
                break;
            case JsonValueKind.Array:
                WriteArray(json, element);
                break;
            case JsonValueKind.String:
                json.Append(Quote(element.GetString()!));
                break;
            case JsonValueKind.Number:
                json.Append(Number(element));
                break;
            case JsonValueKind.True:
                json.Append("true");
                break;
            case JsonValueKind.False:
                json.Append("false");
                break;
            default:
                json.Append("null");
                break;
        }
    }

    private static void WriteObject(StringBuilder json, JsonElement element)
    {
        json.Append('{');
        var first = true;
        foreach (var member in element.EnumerateObject())
        {
            if (!first)
            {
                json.Append(',');
            }

            first = false;
            json.Append(Quote(member.Name)).Append(':');
            Write(json, member.Value);
        }

        json.Append('}');
    }

    private static void WriteArray(StringBuilder json, JsonElement element)
    {
        json.Append('[');
        var first = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!first)
            {
                json.Append(',');
            }

            first = false;
            Write(json, item);
        }

        json.Append(']');
    }

    // System.Text.Json echoes a number back exactly as it was written, so 26.0 and 2.6e1 would
    // each be counted separately from the 26 they both mean.
    private static string Number(JsonElement element)
    {
        if (element.TryGetInt64(out var whole))
        {
            return whole.ToString(CultureInfo.InvariantCulture);
        }

        return element.TryGetDouble(out var number) && !double.IsNaN(number) && !double.IsInfinity(number)
            ? JsonNumberText.Render(number)
            : "null";
    }

    private static string Quote(string text) => JsonSerializer.Serialize(text, Options);
}
