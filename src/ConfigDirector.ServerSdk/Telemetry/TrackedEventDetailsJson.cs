using System.Globalization;
using System.Text;
using ConfigDirector.Value;

namespace ConfigDirector.Telemetry;

internal static class TrackedEventDetailsJson
{
    internal static byte[]? WithinTheLimits(TrackedEventDetails details)
    {
        if (details.Count > TrackedEventLimits.DetailsMaximumTopLevelKeys)
        {
            return null;
        }

        var json = new StringBuilder();
        WriteObject(json, details, TrackedEventLimits.DetailsMaximumJsonBytes);
        if (json.Length > TrackedEventLimits.DetailsMaximumJsonBytes)
        {
            return null;
        }

        var utf8 = Encoding.UTF8.GetBytes(json.ToString());
        return utf8.Length > TrackedEventLimits.DetailsMaximumJsonBytes ? null : utf8;
    }

    internal static string Text(TrackedEventDetails details)
    {
        var json = new StringBuilder();
        WriteObject(json, details, int.MaxValue);
        return json.ToString();
    }

    internal static string Text(TrackedEventDetailValue value)
    {
        var json = new StringBuilder();
        Write(json, value, int.MaxValue);
        return json.ToString();
    }

    private static void Write(StringBuilder json, TrackedEventDetailValue value, int characterLimit)
    {
        if (json.Length > characterLimit)
        {
            return;
        }

        switch (value.Content)
        {
            case string text:
                WriteText(json, text);
                break;
            case bool flag:
                json.Append(flag ? "true" : "false");
                break;
            case TrackedEventDetails members:
                WriteObject(json, members, characterLimit);
                break;
            case TrackedEventDetailValue[] items:
                WriteArray(json, items, characterLimit);
                break;
            default:
                json.Append(JsonNumberText.RenderFinite(value.Content) ?? "null");
                break;
        }
    }

    private static void WriteObject(StringBuilder json, TrackedEventDetails details, int characterLimit)
    {
        json.Append('{');
        var first = true;
        foreach (var member in details)
        {
            if (json.Length > characterLimit)
            {
                return;
            }

            if (!first)
            {
                json.Append(',');
            }

            first = false;
            WriteText(json, member.Key);
            json.Append(':');
            Write(json, member.Value, characterLimit);
        }

        json.Append('}');
    }

    private static void WriteArray(StringBuilder json, TrackedEventDetailValue[] items, int characterLimit)
    {
        json.Append('[');
        var first = true;
        foreach (var item in items)
        {
            if (json.Length > characterLimit)
            {
                return;
            }

            if (!first)
            {
                json.Append(',');
            }

            first = false;
            Write(json, item, characterLimit);
        }

        json.Append(']');
    }

    private static void WriteText(StringBuilder json, string text)
    {
        json.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '"':
                    json.Append("\\\"");
                    break;
                case '\\':
                    json.Append("\\\\");
                    break;
                case '\b':
                    json.Append("\\b");
                    break;
                case '\f':
                    json.Append("\\f");
                    break;
                case '\n':
                    json.Append("\\n");
                    break;
                case '\r':
                    json.Append("\\r");
                    break;
                case '\t':
                    json.Append("\\t");
                    break;
                case < ' ':
                    json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    json.Append(character);
                    break;
            }
        }

        json.Append('"');
    }
}
