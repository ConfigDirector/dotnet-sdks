using System.Globalization;
using System.Text.Json;
using ConfigDirector.Evaluation;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Transport;

// Reads the JSON a transport received into the config definitions the evaluator works on.
internal static class BundleParser
{
    internal static ConfigBundle Parse(string payload, ILogger logger)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException malformed)
        {
            throw new BundleFormatException("The config bundle is not valid JSON.", malformed);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new BundleFormatException("Expected the config bundle to be a JSON object.");
            }

            if (!root.TryGetProperty("configs", out var configs) || configs.ValueKind != JsonValueKind.Object)
            {
                throw new NotAConfigBundleException("The payload carries no configs object.");
            }

            return new ConfigBundle
            {
                Configs = ParseConfigs(configs, logger),
                Kind = Text(root, "kind") == "delta" ? BundleKind.Delta : BundleKind.Full,
                EnvironmentId = Text(root, "environmentId"),
                ProjectId = Text(root, "projectId"),
                Timestamp = Text(root, "timestamp"),
            };
        }
    }

    private static Dictionary<string, Config> ParseConfigs(JsonElement raw, ILogger logger)
    {
        var configs = new Dictionary<string, Config>(StringComparer.Ordinal);
        foreach (var entry in raw.EnumerateObject())
        {
            try
            {
                configs[entry.Name] = ParseConfig(Object(entry.Value));
            }
            catch (BundleFormatException error)
            {
                // One unreadable config must not cost the application every other config in the
                // bundle. It keeps whatever definition it already had, or falls back to defaults.
                Log.SkippedConfig(logger, entry.Name, error);
            }
        }

        return configs;
    }

    private static Config ParseConfig(JsonElement raw)
    {
        var target = raw.TryGetProperty("target", out var found) ? Object(found) : default;

        return new Config
        {
            Id = Required(raw, "id"),
            Key = Required(raw, "key"),
            Type = ConfigTypes.FromWireName(Text(raw, "type")),
            Target = new TargetingRules
            {
                DefaultValue = AsText(Property(target, "defaultValue")),
                DefaultValueId = Text(target, "defaultValueId"),
                Rules = Map(Property(target, "rules"), element => ParseRule(Object(element))),
            },
        };
    }

    private static Rule ParseRule(JsonElement raw)
    {
        var percentages = Map(Property(raw, "percentages"), element => ParseBucket(Object(element)));

        if (Text(raw, "type") == "percentage")
        {
            return new PercentageRule
            {
                Id = Required(raw, "id"),
                Order = Order(raw),
                Percentages = percentages,
            };
        }

        // Anything that is not a percentage rule is carried as a conditional rule. A kind this SDK
        // version predates then matches nothing, rather than being discarded before evaluation.
        return new ConditionalRule
        {
            Id = Required(raw, "id"),
            Order = Order(raw),
            Conditions = Map(Property(raw, "conditions"), element => ParseCondition(Object(element))),
            Target = Text(raw, "target") ?? "value",
            Value = AsValue(Property(raw, "value")),
            ValueId = Text(raw, "valueId"),
        };
    }

    private static Condition ParseCondition(JsonElement raw) =>
        new()
        {
            Id = Required(raw, "id"),
            Attribute = Required(raw, "attribute"),
            Operator = Required(raw, "operator"),
            TargetType = Required(raw, "targetType"),
            TargetValues = Map(Property(raw, "targetValues"), element => AsText(element) ?? string.Empty),
            Trait = Text(raw, "trait"),
        };

    private static PercentageBucket ParseBucket(JsonElement raw) =>
        new()
        {
            Id = Required(raw, "id"),
            Percentage = RequiredNumber(raw, "percentage"),
            Value = AsValue(Property(raw, "value")),
            ValueId = Text(raw, "valueId"),
        };

    private static JsonElement Object(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object
            ? raw
            : throw new BundleFormatException("Expected a JSON object.");

    private static JsonElement Property(JsonElement raw, string name) =>
        raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty(name, out var found) ? found : default;

    private static string? Text(JsonElement raw, string name) =>
        Property(raw, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static string Required(JsonElement raw, string name) =>
        Text(raw, name) ?? throw new BundleFormatException($"Missing the required field '{name}'.");

    private static double RequiredNumber(JsonElement raw, string name) =>
        Property(raw, name) is { ValueKind: JsonValueKind.Number } value
            ? value.GetDouble()
            : throw new BundleFormatException($"Missing the required numeric field '{name}'.");

    // Rules without a usable order evaluate last, in the order the server sent them.
    private static int? Order(JsonElement raw) =>
        Property(raw, "order") is { ValueKind: JsonValueKind.Number } value ? (int)value.GetDouble() : null;

    private static List<T> Map<T>(JsonElement raw, Func<JsonElement, T> parse)
    {
        if (raw.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<T>(raw.GetArrayLength());
        foreach (var element in raw.EnumerateArray())
        {
            parsed.Add(parse(element));
        }

        return parsed;
    }

    // A value a rule selects. A structured one reaches the application as the JSON text it was
    // sent as, which is what every other ConfigDirector SDK does with it.
    private static TraitValue AsValue(JsonElement raw) =>
        raw.ValueKind switch
        {
            JsonValueKind.String => raw.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => Number(raw),
            JsonValueKind.Object or JsonValueKind.Array => raw.GetRawText(),
            _ => TraitValue.Null,
        };

    private static string? AsText(JsonElement raw) =>
        raw.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => raw.GetString(),
            _ => TraitText.Render(AsValue(raw)),
        };

    // JSON has one number type. A whole number is carried as an integer so it renders as "26"
    // rather than "26.0", which is how every other ConfigDirector SDK spells it.
    //
    // Written out rather than as a conditional: the two branches would unify to double, widening
    // the long back to the precision this is here to preserve.
    private static TraitValue Number(JsonElement raw)
    {
        if (raw.TryGetInt64(out var whole))
        {
            return whole;
        }

        return raw.GetDouble();
    }

    private static class Log
    {
        internal static readonly Action<ILogger, string, Exception?> SkippedConfig =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(1, "SkippedConfig"),
                "Skipping the config {ConfigKey}, its definition could not be read.");
    }
}
