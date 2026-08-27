using ConfigDirector.Evaluation;
using ConfigDirector.Transport;

namespace ConfigDirector.Tests.Transport;

public class BundleParserTests
{
    private readonly CapturingLogger _logger = new();

    [Fact]
    public void ReadsTheEnvelopeAroundTheConfigs()
    {
        var bundle = Parse("""
            {
              "environmentId": "env-1",
              "projectId": "proj-1",
              "kind": "delta",
              "timestamp": "2024-01-01T00:00:00.000Z",
              "configs": {}
            }
            """);

        bundle.EnvironmentId.ShouldBe("env-1");
        bundle.ProjectId.ShouldBe("proj-1");
        bundle.Kind.ShouldBe(BundleKind.Delta);
        bundle.Timestamp.ShouldBe("2024-01-01T00:00:00.000Z");
        bundle.Configs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"full\"")]
    [InlineData("\"unheard-of\"")]
    [InlineData("null")]
    public void TreatsAnythingButDeltaAsAFullBundle(string kind)
    {
        Parse($$$"""{"kind": {{{kind}}}, "configs": {}}""").Kind.ShouldBe(BundleKind.Full);
    }

    [Fact]
    public void ReadsAConfigAndItsDefaultValue()
    {
        var config = Parse("""
            {
              "configs": {
                "example-config": {
                  "id": "00000000-0000-0000-0000-0000000003e8",
                  "key": "example-config",
                  "type": "string",
                  "variations": [],
                  "target": { "environmentId": "env-1", "rules": [], "defaultValue": "Hello" }
                }
              }
            }
            """).Configs["example-config"];

        config.Id.ShouldBe("00000000-0000-0000-0000-0000000003e8");
        config.Key.ShouldBe("example-config");
        config.Type.ShouldBe(ConfigType.String);
        config.Target.DefaultValue.ShouldBe("Hello");
        config.Target.Rules.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("\"boolean\"", ConfigType.Boolean)]
    [InlineData("\"integer\"", ConfigType.Integer)]
    [InlineData("\"json\"", ConfigType.Json)]
    [InlineData("\"custom\"", ConfigType.Custom)]
    public void ReadsTheTypeByItsWireName(string wire, ConfigType expected) =>
        ConfigOf($$$"""{"id": "i", "key": "k", "type": {{{wire}}}}""").Type.ShouldBe(expected);

    [Theory]
    [InlineData("\"quantum\"")]
    [InlineData("null")]
    [InlineData("7")]
    public void LeavesATypeItDoesNotKnowUnset(string wire) =>
        ConfigOf($$$"""{"id": "i", "key": "k", "type": {{{wire}}}}""").Type.ShouldBeNull();

    [Theory]
    [InlineData("26", "26")]
    [InlineData("26.5", "26.5")]
    [InlineData("true", "true")]
    [InlineData("\"text\"", "text")]
    public void RendersAScalarDefaultValueAsText(string json, string expected) =>
        ConfigOf($$$"""{"id": "i", "key": "k", "target": {"defaultValue": {{{json}}}}}""")
            .Target.DefaultValue.ShouldBe(expected);

    [Fact]
    public void CarriesAStructuredDefaultValueAsTheJsonItWasSent() =>
        ConfigOf("""{"id": "i", "key": "k", "target": {"defaultValue": {"a":1}}}""")
            .Target.DefaultValue.ShouldBe("""{"a":1}""");

    [Fact]
    public void LeavesAnAbsentDefaultValueUnset() =>
        ConfigOf("""{"id": "i", "key": "k", "target": {}}""").Target.DefaultValue.ShouldBeNull();

    [Fact]
    public void ReadsAConditionalRule()
    {
        var rule = (ConditionalRule)RuleOf("""
            {
              "id": "rule-1",
              "order": 2,
              "target": "value",
              "value": true,
              "valueId": "value-1",
              "conditions": [
                {
                  "id": "cond-1",
                  "attribute": "traits",
                  "trait": "/plan",
                  "operator": "is one of",
                  "targetType": "text",
                  "targetValues": ["pro", "enterprise"]
                }
              ]
            }
            """);

        rule.Id.ShouldBe("rule-1");
        rule.Order.ShouldBe(2);
        rule.Target.ShouldBe("value");
        rule.ValueId.ShouldBe("value-1");
        TraitText.Render(rule.Value).ShouldBe("true");

        var condition = rule.Conditions.ShouldHaveSingleItem();
        condition.Attribute.ShouldBe("traits");
        condition.Trait.ShouldBe("/plan");
        condition.Operator.ShouldBe("is one of");
        condition.TargetType.ShouldBe("text");
        condition.TargetValues.ShouldBe(["pro", "enterprise"]);
    }

    [Fact]
    public void ReadsAPercentageRule()
    {
        var rule = (PercentageRule)RuleOf("""
            {
              "id": "rule-1",
              "type": "percentage",
              "percentages": [
                { "id": "b0", "percentage": 40.5, "value": "control", "valueId": "v0" },
                { "id": "b1", "percentage": 59.5, "value": "variant", "valueId": "v1" }
              ]
            }
            """);

        rule.Percentages.Count.ShouldBe(2);
        rule.Percentages[0].Percentage.ShouldBe(40.5);
        rule.Percentages[0].ValueId.ShouldBe("v0");
        TraitText.Render(rule.Percentages[0].Value).ShouldBe("control");
    }

    [Fact]
    public void TreatsARuleKindItDoesNotKnowAsConditional() =>
        RuleOf("""{"id": "rule-1", "type": "from-the-future"}""").ShouldBeOfType<ConditionalRule>();

    [Fact]
    public void DefaultsARuleWithNoTargetToSelectingAValue() =>
        ((ConditionalRule)RuleOf("""{"id": "rule-1"}""")).Target.ShouldBe("value");

    [Fact]
    public void LeavesARuleWithNoUsableOrderUnordered() =>
        RuleOf("""{"id": "rule-1", "order": "second"}""").Order.ShouldBeNull();

    [Fact]
    public void RendersAWholeNumberRuleValueWithoutADecimalPoint() =>
        TraitText.Render(((ConditionalRule)RuleOf("""{"id": "r", "value": 26}""")).Value).ShouldBe("26");

    // Beyond what a double holds exactly, so a number read as one would come back a digit short.
    [Fact]
    public void KeepsALargeWholeNumberExact()
    {
        TraitText.Render(((ConditionalRule)RuleOf("""{"id": "r", "value": 9007199254740993}""")).Value)
            .ShouldBe("9007199254740993");

        ConfigOf("""{"id": "i", "key": "k", "target": {"defaultValue": 9007199254740993}}""")
            .Target.DefaultValue.ShouldBe("9007199254740993");
    }

    [Fact]
    public void CarriesAStructuredRuleValueAsTheJsonItWasSent() =>
        TraitText.Render(((ConditionalRule)RuleOf("""{"id": "r", "value": {"a":[1,2]}}""")).Value)
            .ShouldBe("""{"a":[1,2]}""");

    [Fact]
    public void SkipsOneUnreadableConfigAndKeepsTheRest()
    {
        var bundle = Parse("""
            {
              "configs": {
                "broken": { "key": "broken" },
                "sound": { "id": "i", "key": "sound", "target": { "defaultValue": "kept" } }
              }
            }
            """);

        bundle.Configs.Keys.ShouldBe(["sound"]);
        bundle.Configs["sound"].Target.DefaultValue.ShouldBe("kept");
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("broken", StringComparison.Ordinal));
    }

    [Fact]
    public void SkipsAConfigWhoseRulesCannotBeRead()
    {
        var bundle = Parse("""
            {
              "configs": {
                "broken": {
                  "id": "i", "key": "broken",
                  "target": { "rules": [ { "order": 1 } ] }
                }
              }
            }
            """);

        bundle.Configs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    public void RejectsAPayloadThatIsNotAJsonObject(string payload) =>
        Should.Throw<BundleFormatException>(() => Parse(payload));

    [Theory]
    [InlineData("""{"kind": "full"}""")]
    [InlineData("""{"configs": null}""")]
    [InlineData("""{"configs": []}""")]
    public void RejectsAPayloadCarryingNoConfigs(string payload) =>
        Should.Throw<NotAConfigBundleException>(() => Parse(payload));

    private ConfigBundle Parse(string payload) => BundleParser.Parse(payload, _logger);

    private Config ConfigOf(string config) =>
        Parse($$$"""{"configs": {"k": {{{config}}} }}""").Configs["k"];

    private Rule RuleOf(string rule) =>
        ConfigOf($$$"""{"id": "i", "key": "k", "target": {"rules": [{{{rule}}}]}}""").Target.Rules[0];
}
