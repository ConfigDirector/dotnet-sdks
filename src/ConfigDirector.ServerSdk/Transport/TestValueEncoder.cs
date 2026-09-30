using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigDirector.Evaluation;

namespace ConfigDirector.Transport;

internal static class TestValueEncoder
{
    private static readonly JsonWriterOptions CompactJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static Config Encode(string key, object value)
    {
        ValidateKey(key);
        if (value is null)
        {
            throw new ArgumentNullException(
                nameof(value), $"The test value for '{key}' must not be null. Use RemoveValue to remove a value.");
        }

        return value switch
        {
            bool flag => Definition(key, ConfigType.Boolean, flag ? "true" : "false"),
            sbyte or byte or short or ushort or int or uint or long =>
                Definition(key, ConfigType.Integer, ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)),
            float single => Definition(key, ConfigType.Float, PlainDecimal(key, single, single.ToString("R", CultureInfo.InvariantCulture))),
            double number => Definition(key, ConfigType.Float, PlainDecimal(key, number, number.ToString("R", CultureInfo.InvariantCulture))),
            decimal exact => Definition(key, ConfigType.Float, exact.ToString(CultureInfo.InvariantCulture)),
            string text => Definition(key, ConfigType.String, text),
            _ => Definition(key, ConfigType.Json, JsonText(key, value)),
        };
    }

    private static Config Definition(string key, ConfigType type, string text) =>
        new()
        {
            Id = "test-config:" + key,
            Key = key,
            Type = type,
            Target = new TargetingRules { DefaultValue = text, DefaultValueId = "test-value:" + key },
        };

    private static void ValidateKey(string key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("The key of a test value must not be empty.", nameof(key));
        }
    }

    private static string PlainDecimal(string key, double value, string roundTrip)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException($"The test value for '{key}' is {roundTrip}, which is not a finite number.", nameof(value));
        }

        var exponentAt = roundTrip.IndexOfAny(['E', 'e']);
        if (exponentAt < 0)
        {
            return roundTrip;
        }

        var mantissa = roundTrip.Substring(0, exponentAt);
        var exponentText = roundTrip.Substring(exponentAt + 1);
        var exponent = int.Parse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var negative = mantissa[0] == '-';
        if (negative)
        {
            mantissa = mantissa.Substring(1);
        }

        var point = mantissa.IndexOf('.');
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var pointPosition = (point < 0 ? mantissa.Length : point) + exponent;
        string plain;
        if (pointPosition <= 0)
        {
            plain = "0." + new string('0', -pointPosition) + digits;
        }
        else if (pointPosition >= digits.Length)
        {
            plain = digits + new string('0', pointPosition - digits.Length);
        }
        else
        {
            plain = digits.Insert(pointPosition, ".");
        }

        return negative ? "-" + plain : plain;
    }

    private static string JsonText(string key, object value)
    {
        var isDocument = value switch
        {
            JsonElement element => element.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
            JsonNode node => node is JsonObject or JsonArray,
            IDictionary or IEnumerable => true,
            _ => false,
        };
        if (!isDocument)
        {
            throw new ArgumentException(
                $"The test value for '{key}' is {Describe(value)}, which is not a supported value. Use a bool, an integer, "
                    + "a float, double or decimal, a string, a dictionary with string keys, a list, or a JsonElement or "
                    + "JsonNode holding an object or an array.",
                nameof(value));
        }

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, CompactJson))
        {
            WriteContents(json, key, value, string.Empty);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteContents(Utf8JsonWriter json, string key, object? value, string path)
    {
        switch (value)
        {
            case null:
                json.WriteNullValue();
                break;
            case bool flag:
                json.WriteBooleanValue(flag);
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                json.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case float single:
                RequireFinite(key, path, single, value);
                json.WriteNumberValue(single);
                break;
            case double number:
                RequireFinite(key, path, number, value);
                json.WriteNumberValue(number);
                break;
            case decimal exact:
                json.WriteNumberValue(exact);
                break;
            case string text:
                json.WriteStringValue(text);
                break;
            case JsonElement element:
                element.WriteTo(json);
                break;
            case JsonNode node:
                node.WriteTo(json);
                break;
            case IDictionary dictionary:
                WriteDictionary(json, key, dictionary, path);
                break;
            case IEnumerable items:
                WriteList(json, key, items, path);
                break;
            default:
                throw InvalidContents(key, path, $"{Describe(value)} cannot be encoded as JSON");
        }
    }

    private static void WriteDictionary(Utf8JsonWriter json, string key, IDictionary dictionary, string path)
    {
        var entries = new List<KeyValuePair<string, object?>>(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string property)
            {
                throw InvalidContents(key, path, $"the key {Describe(entry.Key)} is not a string");
            }

            entries.Add(new KeyValuePair<string, object?>(property, entry.Value));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

        json.WriteStartObject();
        foreach (var entry in entries)
        {
            json.WritePropertyName(entry.Key);
            WriteContents(json, key, entry.Value, path.Length == 0 ? entry.Key : path + "." + entry.Key);
        }

        json.WriteEndObject();
    }

    private static void WriteList(Utf8JsonWriter json, string key, IEnumerable items, string path)
    {
        json.WriteStartArray();
        var index = 0;
        foreach (var item in items)
        {
            WriteContents(json, key, item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
            index++;
        }

        json.WriteEndArray();
    }

    private static void RequireFinite(string key, string path, double number, object value)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw InvalidContents(key, path, $"{value} is not a finite number");
        }
    }

    private static ArgumentException InvalidContents(string key, string path, string problem)
    {
        var location = path.Length == 0 ? string.Empty : $" at '{path}'";
        return new ArgumentException(
            $"Invalid test value for '{key}'{location}: {problem}. JSON contents can hold bools, finite numbers, "
                + "strings, null, lists, dictionaries with string keys, and JsonElement or JsonNode values.");
    }

    private static string Describe(object? value) =>
        value is null ? "null" : $"a {value.GetType().FullName} instance";
}
