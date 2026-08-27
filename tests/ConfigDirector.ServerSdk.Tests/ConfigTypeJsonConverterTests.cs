using System.Text.Json;

namespace ConfigDirector.Tests;

public class ConfigTypeJsonConverterTests
{
    [Theory]
    [InlineData(ConfigType.Custom, "custom")]
    [InlineData(ConfigType.Boolean, "boolean")]
    [InlineData(ConfigType.String, "string")]
    [InlineData(ConfigType.Integer, "integer")]
    [InlineData(ConfigType.Float, "float")]
    [InlineData(ConfigType.Enum, "enum")]
    [InlineData(ConfigType.Url, "url")]
    [InlineData(ConfigType.Json, "json")]
    public void WritesTheNameConfigDirectorSpellsOnTheWire(ConfigType type, string wire)
    {
        JsonSerializer.Serialize(type).ShouldBe($"\"{wire}\"");
        JsonSerializer.Deserialize<ConfigType>($"\"{wire}\"").ShouldBe(type);
    }

    [Fact]
    public void ReadsAWireNameWhateverItsCase() =>
        JsonSerializer.Deserialize<ConfigType>("\"BOOLEAN\"").ShouldBe(ConfigType.Boolean);

    [Fact]
    public void RejectsANameThatIsNotAConfigType() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<ConfigType>("\"quantum\""));

    // The state handed to a client SDK to hydrate with has to name the type, not number it.
    [Fact]
    public void SerialisesTheTypeOnAnEvaluatedConfigState()
    {
        var state = new ConfigState
        {
            Id = "id",
            Key = "json-value-config",
            Type = ConfigType.Json,
            Value = """{"a":1}""",
            ValueId = "value-1",
        };

        JsonSerializer.Serialize(state).ShouldContain("\"Type\":\"json\"");
    }

    [Fact]
    public void LeavesAnUnsetTypeNull() =>
        JsonSerializer.Serialize(new ConfigState { Type = null }).ShouldContain("\"Type\":null");
}
