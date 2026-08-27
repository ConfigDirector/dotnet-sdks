using System.Text.Json;
using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

public class TelemetryValueTests
{
    // A scalar is reported as the text the config holds, not as a JSON literal: a string config
    // reads "hello" on the dashboard rather than "\"hello\"".
    [Fact]
    public void ReportsAStringWithoutQuotingIt() =>
        TelemetryValue.From("hello").Value.ShouldBe("hello");

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ReportsABooleanTheWayJsonSpellsIt(bool value, string expected) =>
        TelemetryValue.From(value).Value.ShouldBe(expected);

    [Fact]
    public void ReportsAWholeNumberWithoutAFractionalPart() =>
        TelemetryValue.From(26.0).Value.ShouldBe("26");

    [Fact]
    public void SerializesAJsonValueCompactly()
    {
        var reported = TelemetryValue.From(Parse("{ \"b\": 1, \"a\": [true] }"), ConfigType.Json);

        reported.Value.ShouldBe("{\"b\":1,\"a\":[true]}");
        reported.Type.ShouldBe(ConfigType.Json);
    }

    // An evaluation that found no config state has no declared type to go on, so the shape of the
    // value is all there is.
    [Fact]
    public void TreatsAnUntypedDocumentAsJson()
    {
        TelemetryValue.From(Parse("{\"a\":1}")).Type.ShouldBe(ConfigType.Json);
        TelemetryValue.From(Parse("[1,2]")).Type.ShouldBe(ConfigType.Json);
        TelemetryValue.From(new Dictionary<string, int> { ["a"] = 1 }).Type.ShouldBe(ConfigType.Json);
    }

    [Fact]
    public void DoesNotTreatAScalarAsJson() =>
        TelemetryValue.From("hello").Type.ShouldBeNull();

    [Fact]
    public void PrefersTheValueIdTheServerSentForAJsonValue()
    {
        var reported = TelemetryValue.From(Parse("{\"a\":1}"), ConfigType.Json, "server-id");

        reported.ValueId.ShouldBe("server-id");
        reported.Value.ShouldBeNull();
    }

    [Fact]
    public void ReportsAnOversizedValueByTheIdTheServerSent()
    {
        var reported = TelemetryValue.From(Oversized, valueId: "server-id");

        reported.ValueId.ShouldBe("server-id");
        reported.Value.ShouldBeNull();
    }

    // It is compacted into an ID at flush time instead; the hashing does not belong on the
    // caller's thread.
    [Fact]
    public void KeepsAnOversizedValueWhenTheServerSentNoId() =>
        TelemetryValue.From(Oversized).Value.ShouldBe(Oversized);

    // Every SDK reports inline up to the same length, or the dashboard would show one SDK's
    // values by ID and another's in full for the same config.
    [Fact]
    public void ReportsInlineUpToTheLengthEverySdkAgreesOn()
    {
        TelemetryValue.ConfigValueMaxLength.ShouldBe(500);

        new TelemetryValue { Value = new string('x', 500) }.Compacted().Value.ShouldNotBeNull();
        new TelemetryValue { Value = new string('x', 501) }.Compacted().ValueId.ShouldNotBeNull();
    }

    [Fact]
    public void LeavesASmallValueInlineWhenCompacted() =>
        new TelemetryValue { Value = "hello" }.Compacted().Value.ShouldBe("hello");

    [Fact]
    public void KeepsAValueOfExactlyTheMaximumLengthInline()
    {
        var atLimit = new string('x', TelemetryValue.ConfigValueMaxLength);

        new TelemetryValue { Value = atLimit }.Compacted().Value.ShouldBe(atLimit);
    }

    [Fact]
    public void ReplacesAnOversizedValueWithItsId()
    {
        var compacted = new TelemetryValue { Value = Oversized }.Compacted();

        compacted.ValueId.ShouldBe(ValueIds.Generate(Oversized));
        compacted.Value.ShouldBeNull();
    }

    [Fact]
    public void ReplacesEveryJsonDocumentWithItsId()
    {
        var compacted = new TelemetryValue { Value = "{\"a\":1}", Type = ConfigType.Json }.Compacted();

        compacted.ValueId.ShouldBe(ValueIds.Generate("{\"a\":1}"));
        compacted.Value.ShouldBeNull();
    }

    [Fact]
    public void KeepsAnIdItAlreadyHas() =>
        new TelemetryValue { ValueId = "server-id", Type = ConfigType.Json }.Compacted()
            .ValueId.ShouldBe("server-id");

    // The type is only carried so that compaction can recognise a JSON document; the server reads
    // it from the event instead.
    [Fact]
    public void DropsTheDeclaredTypeWhenCompacted()
    {
        new TelemetryValue { Value = "{\"a\":1}", Type = ConfigType.Json }.Compacted().Type.ShouldBeNull();
        new TelemetryValue { Value = "hello", Type = ConfigType.String }.Compacted().Type.ShouldBeNull();
    }

    [Fact]
    public void LeavesAnEmptyValueAlone() =>
        new TelemetryValue { Value = string.Empty }.Compacted().Value.ShouldBe(string.Empty);

    [Fact]
    public void ComparesEqualToTheSameReportedValue() =>
        TelemetryValue.From("hello").ShouldBe(TelemetryValue.From("hello"));

    private static string Oversized => new('x', TelemetryValue.ConfigValueMaxLength + 1);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
