using ConfigDirector.Evaluation;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging;

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

        var condition = rule.Conditions.ShouldHaveSingleItem().ShouldBeOfType<AttributeCondition>();
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

    [Fact]
    public void AFullSetWithConditionKindsAndAPayloadVersionServesAsBefore()
    {
        var bundle = Parse(SetWithConditionKindsAndPayloadVersion("full"));
        var evaluator = new ConfigEvaluator(_logger);

        bundle.Kind.ShouldBe(BundleKind.Full);
        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
        var config = bundle.Configs.ShouldHaveSingleItem().Value;
        var matched = evaluator.Evaluate(config, new Context { Id = "10", Traits = { ["plan"] = "pro" } }, null);
        matched.Value.ShouldBe("bonjour");
        matched.ValueId.ShouldBe("value-id-2");
        var unmatched = evaluator.Evaluate(config, new Context { Id = "10", Traits = { ["plan"] = "free" } }, null);
        unmatched.Value.ShouldBe("hello");
        unmatched.ValueId.ShouldBe("value-id-1");
    }

    [Fact]
    public void ADeltaWithConditionKindsAndAPayloadVersionServesAsBefore()
    {
        var bundle = Parse(SetWithConditionKindsAndPayloadVersion("delta"));
        var evaluator = new ConfigEvaluator(_logger);

        bundle.Kind.ShouldBe(BundleKind.Delta);
        _logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Warning);
        var config = bundle.Configs.ShouldHaveSingleItem().Value;
        var matched = evaluator.Evaluate(config, new Context { Id = "10", Traits = { ["plan"] = "pro" } }, null);
        matched.Value.ShouldBe("bonjour");
        matched.ValueId.ShouldBe("value-id-2");
        var unmatched = evaluator.Evaluate(config, new Context { Id = "10", Traits = { ["plan"] = "free" } }, null);
        unmatched.Value.ShouldBe("hello");
        unmatched.ValueId.ShouldBe("value-id-1");
    }

    private static string SetWithConditionKindsAndPayloadVersion(string kind) =>
        $$$"""
            {
              "payloadVersion": 1,
              "kind": "{{{kind}}}",
              "environmentId": "env-1",
              "projectId": "proj-1",
              "configs": {
                "greeting": {
                  "id": "c1",
                  "key": "greeting",
                  "type": "string",
                  "variations": [],
                  "target": {
                    "environmentId": "env-1",
                    "defaultValue": "hello",
                    "defaultValueId": "value-id-1",
                    "rules": [
                      {
                        "id": "r1",
                        "type": "conditional",
                        "order": 0,
                        "target": "value",
                        "value": "bonjour",
                        "valueId": "value-id-2",
                        "conditions": [
                          {
                            "id": "cond-1",
                            "kind": "attribute",
                            "attribute": "identifier",
                            "trait": null,
                            "operator": "=",
                            "targetType": "text",
                            "targetValues": ["10"]
                          },
                          {
                            "id": "cond-2",
                            "kind": "attribute",
                            "attribute": "traits",
                            "trait": "/plan",
                            "operator": "is one of",
                            "targetType": "text",
                            "targetValues": ["pro", "enterprise"]
                          }
                        ]
                      }
                    ]
                  }
                }
              }
            }
            """;

    [Fact]
    public void ReadsTheSegmentsSectionIntoGroupsOfAttributeConditions()
    {
        var bundle = Parse($$$$"""
            {"configs": {}, "segments": {"segment-1": {"groups": [
              [{{{{GroupCondition("@acme.com", "attribute")}}}}],
              [{{{{GroupCondition("@beta.com", "attribute")}}}}]
            ]}}}
            """);

        bundle.Segments.Keys.ShouldBe(["segment-1"]);
        var groups = bundle.Segments["segment-1"].Groups;
        groups.Count.ShouldBe(2);
        var first = groups[0].ShouldHaveSingleItem();
        first.Id.ShouldBe("g0c0");
        first.Attribute.ShouldBe("traits");
        first.Trait.ShouldBe("/email");
        first.Operator.ShouldBe("ends with any of");
        first.TargetType.ShouldBe("text");
        first.TargetValues.ShouldBe(["@acme.com"]);
        groups[1].ShouldHaveSingleItem().TargetValues.ShouldBe(["@beta.com"]);
    }

    [Fact]
    public void APayloadWithoutASegmentsSectionCarriesNoSegments()
    {
        Parse("""{"configs": {}}""").Segments.ShouldBeEmpty();
    }

    [Fact]
    public void AGroupConditionWithoutAKindIsAnAttributeCondition()
    {
        var bundle = Parse($$$$"""{"configs": {}, "segments": {"segment-1": {"groups": [[{{{{GroupCondition("@acme.com", null)}}}}]]}}}""");

        bundle.Segments["segment-1"].Groups[0][0].Attribute.ShouldBe("traits");
    }

    [Fact]
    public void ReadsASegmentConditionInARule()
    {
        var rule = RuleOf("""
            {"id": "r1", "type": "conditional", "order": 0, "target": "value", "value": "members",
             "conditions": [{"id": "c-1", "kind": "segment", "operator": "in", "segmentId": "segment-1"}]}
            """).ShouldBeOfType<ConditionalRule>();

        rule.Conditions.ShouldHaveSingleItem().ShouldBe(new SegmentCondition { Id = "c-1", Operator = "in", SegmentId = "segment-1" });
    }

    [Fact]
    public void AnUnreadableSegmentIsSkippedAndLoggedAndTheRestKept()
    {
        var bundle = Parse($$$$"""
            {"configs": {}, "segments": {
              "broken": {"groups": [[{"id": "g0c0", "kind": "attribute", "operator": "equals"}]]},
              "segment-1": {"groups": [[{{{{GroupCondition("@acme.com", "attribute")}}}}]]}
            }}
            """);

        bundle.Segments.Keys.ShouldBe(["segment-1"]);
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("broken"));
    }

    [Fact]
    public void ASegmentConditionInsideAGroupMakesTheSegmentUnreadable()
    {
        var bundle = Parse("""
            {"configs": {}, "segments": {"segment-1": {"groups": [[
              {"id": "g0c0", "kind": "segment", "operator": "in", "segmentId": "other"}
            ]]}}}
            """);

        bundle.Segments.ShouldBeEmpty();
    }

    [Fact]
    public void AConditionOfAnUnknownKindMakesTheConfigUnreadable()
    {
        var bundle = Parse("""
            {"configs": {"greeting": {"id": "c1", "key": "greeting", "type": "string", "target": {"defaultValue": "hello",
              "rules": [{"id": "r1", "type": "conditional", "order": 0, "target": "value", "value": "members",
                "conditions": [{"id": "c-1", "kind": "made-up", "operator": "in", "segmentId": "segment-1"}]}]}}}}
            """);

        bundle.Configs.ShouldBeEmpty();
    }

    private static string GroupCondition(string domain, string? kind)
    {
        var kindField = kind is null ? string.Empty : $"\"kind\": \"{kind}\", ";
        return $$$$"""{"id": "g0c0", {{{{kindField}}}}"attribute": "traits", "trait": "/email", "operator": "ends with any of", "targetType": "text", "targetValues": ["{{{{domain}}}}"]}""";
    }

    private ConfigBundle Parse(string payload) => BundleParser.Parse(payload, _logger);

    private Config ConfigOf(string config) =>
        Parse($$$"""{"configs": {"k": {{{config}}} }}""").Configs["k"];

    private Rule RuleOf(string rule) =>
        ConfigOf($$$"""{"id": "i", "key": "k", "target": {"rules": [{{{rule}}}]}}""").Target.Rules[0];
}
