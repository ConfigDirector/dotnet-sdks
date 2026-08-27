using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigDirector;

/// <summary>
/// Reads and writes <see cref="ConfigType"/> as the name ConfigDirector spells it on the wire,
/// rather than as its ordinal.
/// </summary>
/// <remarks>
/// Applied to the enum itself, so a <see cref="ConfigState"/> handed to a client SDK to hydrate
/// with carries a type that SDK can read.
/// </remarks>
public sealed class ConfigTypeJsonConverter : JsonConverter<ConfigType>
{
    /// <inheritdoc/>
    public override ConfigType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ConfigTypes.FromWireName(reader.GetString())
        ?? throw new JsonException($"'{reader.GetString()}' is not a known config type.");

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, ConfigType value, JsonSerializerOptions options)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        writer.WriteStringValue(ConfigTypes.WireName(value));
    }
}
