using System.Text.Json;
using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

public class EvaluatedConfigEventTests
{
    [Fact]
    public void ReportsBothValuesAndTheRequestedType()
    {
        var built = EvaluatedConfigEvent.Create(
            "my-config", false, true, false, EvaluationReason.FoundMatch, configType: ConfigType.Boolean);

        built.DefaultValue.Value.ShouldBe("false");
        built.EvaluatedValue.Value.ShouldBe("true");
        built.RequestedType.ShouldBe("Boolean");
        built.Type.ShouldBe(ConfigType.Boolean);
    }

    // The type a caller asked the config to be returned as, named the way .NET names it.
    [Fact]
    public void NamesTheRequestedTypeTheWayDotnetDoes()
    {
        RequestedTypeOf(26).ShouldBe("Int32");
        RequestedTypeOf(26L).ShouldBe("Int64");
        RequestedTypeOf(1.5).ShouldBe("Double");
        RequestedTypeOf(1.5f).ShouldBe("Single");
        RequestedTypeOf(1.5m).ShouldBe("Decimal");
        RequestedTypeOf("text").ShouldBe("String");
        RequestedTypeOf(true).ShouldBe("Boolean");
        RequestedTypeOf(default(JsonElement)).ShouldBe("JsonElement");
    }

    // Otherwise a bound dictionary would report itself as "Dictionary`2".
    [Fact]
    public void NamesAGenericRequestedTypeWithoutItsArity() =>
        RequestedTypeOf(new Dictionary<string, int>()).ShouldBe("Dictionary");

    // A default is the caller's own literal, so the server has never seen it.
    [Fact]
    public void OnlyTheEvaluatedValueCarriesTheServerValueId()
    {
        var built = EvaluatedConfigEvent.Create(
            "my-config",
            Parse("{\"a\":1}"),
            Parse("{\"b\":2}"),
            false,
            EvaluationReason.FoundMatch,
            configType: ConfigType.Json,
            valueId: "server-id");

        built.EvaluatedValue.ValueId.ShouldBe("server-id");
        built.DefaultValue.Value.ShouldBe("{\"a\":1}");
        built.EvaluatedValueId.ShouldBe("server-id");
    }

    // Equality is what decides which events collapse together when they are aggregated.
    [Fact]
    public void IdenticalEvaluationsCompareEqual()
    {
        Event().ShouldBe(Event());
        Event().GetHashCode().ShouldBe(Event().GetHashCode());
    }

    [Fact]
    public void EventsThatDifferDoNotCompareEqual()
    {
        Event(key: "other-config").ShouldNotBe(Event());
        Event(value: "other").ShouldNotBe(Event());
        Event(defaultValue: "other").ShouldNotBe(Event());
        Event(usedDefault: true).ShouldNotBe(Event());
        Event(reason: EvaluationReason.ValueMissing).ShouldNotBe(Event());
        Event(contextId: "user-1").ShouldNotBe(Event());
        Event(configType: ConfigType.Enum).ShouldNotBe(Event());
    }

    [Fact]
    public void CompactingReducesBothValues()
    {
        var oversized = new string('x', TelemetryValue.ConfigValueMaxLength + 1);

        var compacted = Event(defaultValue: oversized, value: oversized).Compacted();

        compacted.DefaultValue.ValueId.ShouldNotBeNull();
        compacted.EvaluatedValue.ValueId.ShouldNotBeNull();
    }

    [Fact]
    public void CompactingLeavesTheRestOfTheEventAlone()
    {
        var built = Event(contextId: "user-1", configType: ConfigType.String);

        var compacted = built.Compacted();

        compacted.Key.ShouldBe(built.Key);
        compacted.ContextId.ShouldBe("user-1");
        compacted.Type.ShouldBe(ConfigType.String);
        compacted.RequestedType.ShouldBe(built.RequestedType);
    }

    [Fact]
    public void WritesTheFieldNamesTheServerReads()
    {
        var wire = Serialize(Event(contextId: "user-1", configType: ConfigType.String).Compacted());

        wire.ShouldBe(
            "{\"contextId\":\"user-1\",\"key\":\"my-config\",\"type\":\"string\","
            + "\"defaultValue\":{\"value\":\"default\"},\"requestedType\":\"String\","
            + "\"evaluatedValue\":{\"value\":\"hello\"},\"usedDefault\":false,"
            + "\"evaluationReason\":\"found-match\"}");
    }

    [Fact]
    public void OmitsTheContextAndTypeWhenThereAreNone()
    {
        var wire = Serialize(Event().Compacted());

        wire.ShouldNotContain("contextId");
        wire.ShouldNotContain("\"type\"");
        wire.ShouldNotContain("evaluatedValueId");
    }

    [Fact]
    public void PassesThroughTheValueIdTheServerSent() =>
        Serialize(Event(valueId: "server-id").Compacted())
            .ShouldContain("\"evaluatedValueId\":\"server-id\"");

    [Fact]
    public void ReportsAJsonValueById()
    {
        var built = EvaluatedConfigEvent.Create(
            "my-config", Parse("{\"a\":1}"), Parse("{\"b\":2}"), false, EvaluationReason.FoundMatch)
            .Compacted();

        built.DefaultValue.ValueId!.Length.ShouldBe(ValueIds.ValueIdLength);
        built.EvaluatedValue.ValueId!.Length.ShouldBe(ValueIds.ValueIdLength);
        built.EvaluatedValue.ValueId.ShouldNotBe(built.DefaultValue.ValueId);
    }

    [Theory]
    [InlineData(EvaluationReason.FoundMatch, "found-match")]
    [InlineData(EvaluationReason.ConfigStateMissing, "config-state-missing")]
    [InlineData(EvaluationReason.ClientNotReady, "client-not-ready")]
    [InlineData(EvaluationReason.InvalidBoolean, "invalid-boolean")]
    public void SpellsTheReasonTheWayTheServerReadsIt(EvaluationReason reason, string expected) =>
        Serialize(Event(reason: reason).Compacted()).ShouldContain($"\"evaluationReason\":\"{expected}\"");

    private static string RequestedTypeOf<T>(T defaultValue) =>
        EvaluatedConfigEvent.Create("my-config", defaultValue, defaultValue, true, EvaluationReason.FoundMatch)
            .RequestedType;

    private static EvaluatedConfigEvent Event(
        string key = "my-config",
        string defaultValue = "default",
        string value = "hello",
        bool usedDefault = false,
        EvaluationReason reason = EvaluationReason.FoundMatch,
        string? contextId = null,
        ConfigType? configType = null,
        string? valueId = null) =>
        EvaluatedConfigEvent.Create(key, defaultValue, value, usedDefault, reason, contextId, configType, valueId);

    private static string Serialize(EvaluatedConfigEvent built) =>
        JsonSerializer.Serialize(built, TelemetryWire.Event);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
