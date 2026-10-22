using System.Text.Json;
using ConfigDirector.Evaluation;

namespace ConfigDirector.Transport;

internal abstract record OfflineSource
{
    internal abstract IReadOnlyDictionary<string, Config> LoadConfigs();

    private static Dictionary<string, Config> ConfigsOf(
        IEnumerable<KeyValuePair<string, object?>> values, string sourceDescription)
    {
        var configs = new Dictionary<string, Config>(StringComparer.Ordinal);
        foreach (var entry in values)
        {
            configs[entry.Key] = OfflineConfig(entry.Key, entry.Value, sourceDescription);
        }

        return configs;
    }

    private static Config OfflineConfig(string key, object? value, string sourceDescription)
    {
        try
        {
            return NativeValueEncoder.Encode(key, value, "offline:" + key, null);
        }
        catch (ArgumentException refused)
        {
            throw new ConfigDirectorInitializationException(
                $"{sourceDescription} could not be loaded. {refused.Message}", refused);
        }
        catch (Exception unreadable)
        {
            throw new ConfigDirectorInitializationException(
                $"{sourceDescription} could not be loaded. The value for '{key}' could not be read: {unreadable.Message}",
                unreadable);
        }
    }

    internal sealed record InCodeDefaults : OfflineSource
    {
        internal override IReadOnlyDictionary<string, Config> LoadConfigs() =>
            new Dictionary<string, Config>(StringComparer.Ordinal);
    }

    internal sealed record ValuesFile(string Path) : OfflineSource
    {
        private const double FirstWholeNumberPastLong = 9223372036854775808d;

        private string Description => $"The offline values file '{Path}'";

        internal override IReadOnlyDictionary<string, Config> LoadConfigs()
        {
            using var document = ReadDocument();
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigDirectorInitializationException(
                    $"{Description} must hold a JSON object with one value per config key.");
            }

            return ConfigsOf(
                document.RootElement.EnumerateObject()
                    .Select(property => new KeyValuePair<string, object?>(property.Name, NativeValueOf(property))),
                Description);
        }

        private JsonDocument ReadDocument()
        {
            var text = ReadText();
            try
            {
                return JsonDocument.Parse(text);
            }
            catch (JsonException error)
            {
                throw new ConfigDirectorInitializationException($"{Description} is not valid JSON. {error.Message}", error);
            }
        }

        private string ReadText()
        {
            try
            {
                return File.ReadAllText(Path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                throw new ConfigDirectorInitializationException($"{Description} could not be read. {error.Message}", error);
            }
        }

        private object? NativeValueOf(JsonProperty property) =>
            property.Value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => NumberOf(property),
                JsonValueKind.Null => null,
                _ => property.Value,
            };

        private object NumberOf(JsonProperty property)
        {
            if (property.Value.TryGetInt64(out var whole))
            {
                return whole;
            }

            if (!property.Value.TryGetDouble(out var value) || double.IsInfinity(value))
            {
                throw new ConfigDirectorInitializationException(
                    $"{Description} could not be loaded. The value for '{property.Name}' is {property.Value.GetRawText()}, "
                        + "which is too large a number to read.");
            }

            if (Math.Floor(value) == value && Math.Abs(value) < FirstWholeNumberPastLong)
            {
                return (long)value;
            }

            return value;
        }
    }

    internal sealed record Rejected(string Reason) : OfflineSource
    {
        internal override IReadOnlyDictionary<string, Config> LoadConfigs() =>
            throw new ConfigDirectorInitializationException(Reason);
    }

    internal sealed record InlineValues(IReadOnlyDictionary<string, object?> Values) : OfflineSource
    {
        internal override IReadOnlyDictionary<string, Config> LoadConfigs() => ConfigsOf(Values, "The offline values");
    }
}
