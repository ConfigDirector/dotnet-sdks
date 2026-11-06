using ConfigDirector.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.Evaluation;

public class ConfigEvaluatorTests
{
    private const string BucketingConfigId = "11111111-1111-4111-8111-111111111111";

    private static readonly Context User = new() { Id = "user-1", Traits = { ["plan"] = "pro" } };

    private static readonly Variation UnderVariation = new() { Id = "under-variation", Name = "Under" };

    private static readonly Variation OverVariation = new() { Id = "over-variation", Name = "Over" };

    // SEMANTICS.md 7 -- rules evaluate in ascending order, and the first that applies wins.
    [Fact]
    public void EvaluatesRulesInAscendingOrder()
    {
        var config = Config(Matching("second", order: 2), Matching("first", order: 1));

        Evaluate(config).Value.ShouldBe("first");
    }

    // SEMANTICS.md 7 -- rules with no order evaluate last, keeping the order the server sent them.
    [Fact]
    public void EvaluatesRulesWithNoOrderLast()
    {
        var config = Config(Matching("unordered", order: null), Matching("ordered", order: 5));

        Evaluate(config).Value.ShouldBe("ordered");
    }

    [Fact]
    public void KeepsTheServerOrderForRulesSharingAnOrder()
    {
        var config = Config(Matching("first", order: 1), Matching("second", order: 1));

        Evaluate(config).Value.ShouldBe("first");

        var unordered = Config(Matching("a", order: null), Matching("b", order: null));

        Evaluate(unordered).Value.ShouldBe("a");
    }

    [Fact]
    public void FallsBackToTheDefaultValue()
    {
        var config = Config(NotMatching("never"));

        var state = Evaluate(config);

        state.Value.ShouldBe("the default");
        state.ValueId.ShouldBe("default-value-id");
    }

    [Fact]
    public void ServesTheValueOfTheRuleThatMatched()
    {
        var state = Evaluate(Config(Matching("matched", order: 1)));

        state.Value.ShouldBe("matched");
        state.ValueId.ShouldBe("matched-value-id");
    }

    // SEMANTICS.md 1.1 -- a rule value is spelled the way JSON spells it.
    [Theory]
    [InlineData(true, "true")]
    [InlineData(26L, "26")]
    [InlineData(26.5, "26.5")]
    [InlineData(26.0, "26")]
    [InlineData("text", "text")]
    public void RendersRuleValuesAsJsonSpellsThem(object value, string expected)
    {
        var rule = Matching("ignored", order: 1) with { Value = ToTraitValue(value) };

        Evaluate(Config(rule)).Value.ShouldBe(expected);
    }

    [Fact]
    public void SkipsAConditionalRuleCarryingNoValue()
    {
        var rule = Matching("ignored", order: 1) with { Value = TraitValue.Null };

        Evaluate(Config(rule)).Value.ShouldBe("the default");
    }

    [Fact]
    public void SkipsARuleWhoseConditionsDoNotMatch() =>
        Evaluate(Config(NotMatching("never"))).Value.ShouldBe("the default");

    // SEMANTICS.md 7 -- a conditional rule applies only when every one of its conditions matches.
    [Fact]
    public void MatchesARuleWhenEveryConditionMatches()
    {
        var config = Config(Requiring(AttributeCondition("identifier", "user-1"), TraitCondition("/plan", "pro")));

        Evaluate(config, ContextWith("user-1", plan: "pro")).Value.ShouldBe("matched");
    }

    [Fact]
    public void DoesNotMatchARuleWhenOnlyTheFirstConditionMatches()
    {
        var config = Config(Requiring(AttributeCondition("identifier", "user-1"), TraitCondition("/plan", "pro")));

        Evaluate(config, ContextWith("user-1", plan: "free")).Value.ShouldBe("the default");
    }

    [Fact]
    public void DoesNotMatchARuleWhenOnlyTheSecondConditionMatches()
    {
        var config = Config(Requiring(AttributeCondition("identifier", "user-1"), TraitCondition("/plan", "pro")));

        Evaluate(config, ContextWith("user-2", plan: "pro")).Value.ShouldBe("the default");
    }

    [Fact]
    public void DoesNotMatchARuleWhenNoConditionMatches()
    {
        var config = Config(Requiring(AttributeCondition("identifier", "user-1"), TraitCondition("/plan", "pro")));

        Evaluate(config, ContextWith("user-2", plan: "free")).Value.ShouldBe("the default");
    }

    // SEMANTICS.md 7 -- with nothing to check, every condition holds.
    [Fact]
    public void AppliesARuleWithNoConditionsToEveryContext()
    {
        var config = Config(Requiring());

        Evaluate(config, ContextWith("user-2", plan: "free")).Value.ShouldBe("matched");
        Evaluate(config, new Context()).Value.ShouldBe("matched");
    }

    [Fact]
    public void NeverMatchesARuleWhoseConditionsCannotAllHold()
    {
        var config = Config(Requiring(AttributeCondition("identifier", "never"), AttributeCondition("identifier", "user-1")));

        Evaluate(config, ContextWith("user-1", plan: "pro")).Value.ShouldBe("the default");
        Evaluate(config, ContextWith("never", plan: "pro")).Value.ShouldBe("the default");
    }

    [Fact]
    public void ServesAPercentageRule()
    {
        // "user-1" against "config-1" is assigned 67.8, so the second bucket serves it.
        PercentHashing.AssignPercentage("config-1", "user-1").ShouldBe(67.8);
        var config = Config("config-1", new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "under"), Bucket(50, "over")],
        });

        var state = Evaluate(config);

        state.Value.ShouldBe("over");
        state.ValueId.ShouldBe("over-value-id");
    }

    [Fact]
    public void ServesAConditionalRuleTargetingPercentages()
    {
        var rule = new ConditionalRule
        {
            Id = "r",
            Order = 1,
            Target = "percentage",
            Conditions = [AttributeCondition("identifier", "user-1")],
            Percentages = [Bucket(50, "under"), Bucket(50, "over")],
        };

        Evaluate(Config("config-1", rule)).Value.ShouldBe("over");
    }

    [Fact]
    public void SkipsAConditionalPercentageRuleWhoseConditionsDoNotMatch()
    {
        var rule = new ConditionalRule
        {
            Id = "r",
            Order = 1,
            Target = "percentage",
            Conditions = [AttributeCondition("identifier", "somebody-else")],
            Percentages = [Bucket(100, "everyone")],
        };

        Evaluate(Config("config-1", rule)).Value.ShouldBe("the default");
    }

    // SEMANTICS.md 7.1 -- a bucket spans [total, total + width). The comparison is strict, so a
    // context landing exactly on a boundary belongs to the bucket that starts there.
    [Fact]
    public void SelectsTheBucketThatStartsOnABoundary()
    {
        PercentHashing.AssignPercentage(BucketingConfigId, "u582").ShouldBe(50.0);
        var config = Config(BucketingConfigId, new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "under"), Bucket(50, "over")],
        });

        Evaluate(config, new Context { Id = "u582" }).Value.ShouldBe("over");
    }

    // SEMANTICS.md 7.1 -- width 0 makes the interval empty, so a bucket set to 0% serves nobody
    // wherever it sits in the list.
    [Fact]
    public void NeverSelectsAZeroWidthBucket()
    {
        var config = Config(BucketingConfigId, new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "first"), Bucket(0, "empty"), Bucket(50, "last")],
        });

        Evaluate(config, new Context { Id = "u582" }).Value.ShouldBe("last");
    }

    [Fact]
    public void SelectsTheFirstBucketForAContextAssignedZero()
    {
        PercentHashing.AssignPercentage(BucketingConfigId, "u562").ShouldBe(0.0);
        var config = Config(BucketingConfigId, new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "first"), Bucket(50, "last")],
        });

        Evaluate(config, new Context { Id = "u562" }).Value.ShouldBe("first");
    }

    [Fact]
    public void FallsBackWhenTheBucketsDoNotCoverTheAssignedPercentage()
    {
        var config = Config("config-1", new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(10, "narrow")],
        });

        Evaluate(config).Value.ShouldBe("the default");
    }

    [Fact]
    public void SkipsABucketCarryingNoValue()
    {
        var config = Config("config-1", new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "under"), Bucket(50, null)],
        });

        Evaluate(config).Value.ShouldBe("the default");
    }

    [Fact]
    public void ServesARolloutWithItsRuleShareAndVariation()
    {
        var rule = new PercentageRule
        {
            Id = "rollout-rule",
            Order = 1,
            Percentages =
            [
                Bucket(50, "under") with { Id = "under-share", VariationId = "under-variation" },
                Bucket(50, "over") with { Id = "over-share", VariationId = "over-variation" },
            ],
        };

        var state = Evaluate(Config("config-1", rule) with { Variations = [UnderVariation, OverVariation] });

        state.Value.ShouldBe("over");
        state.RuleId.ShouldBe("rollout-rule");
        state.ShareId.ShouldBe("over-share");
        state.Variation.ShouldBe(OverVariation);
    }

    [Fact]
    public void ServesAConditionalRolloutWithItsRuleShareAndVariation()
    {
        var rule = new ConditionalRule
        {
            Id = "conditional-rollout-rule",
            Order = 1,
            Target = "percentage",
            Conditions = [AttributeCondition("identifier", "user-1")],
            Percentages =
            [
                Bucket(50, "under") with { Id = "under-share", VariationId = "under-variation" },
                Bucket(50, "over") with { Id = "over-share", VariationId = "over-variation" },
            ],
        };

        var state = Evaluate(Config("config-1", rule) with { Variations = [UnderVariation, OverVariation] });

        state.RuleId.ShouldBe("conditional-rollout-rule");
        state.ShareId.ShouldBe("over-share");
        state.Variation.ShouldBe(OverVariation);
    }

    [Fact]
    public void ServesASingleValueRuleWithItsVariationAlone()
    {
        var rule = Matching("matched", order: 1) with { VariationId = "under-variation" };

        var state = Evaluate(Config(rule) with { Variations = [UnderVariation] });

        state.Variation.ShouldBe(UnderVariation);
        state.RuleId.ShouldBeNull();
        state.ShareId.ShouldBeNull();
    }

    [Fact]
    public void ServesTheDefaultTargetingRuleValueWithItsVariationAlone()
    {
        var config = Config() with { Variations = [UnderVariation] };
        config = config with { Target = config.Target with { DefaultVariationId = "under-variation" } };

        var state = Evaluate(config);

        state.Value.ShouldBe("the default");
        state.Variation.ShouldBe(UnderVariation);
        state.RuleId.ShouldBeNull();
        state.ShareId.ShouldBeNull();
    }

    [Fact]
    public void ServesAValueOfAConfigWithoutVariationsWithNoVariation()
    {
        var config = Config("config-1", new PercentageRule
        {
            Id = "rollout-rule",
            Order = 1,
            Percentages = [Bucket(100, "everyone") with { Id = "everyone-share" }],
        });

        var state = Evaluate(config);

        state.Variation.ShouldBeNull();
        state.RuleId.ShouldBe("rollout-rule");
        state.ShareId.ShouldBe("everyone-share");
    }

    [Fact]
    public void ServesNoVariationWhenTheConfigDoesNotListTheVariationServed()
    {
        var rule = Matching("matched", order: 1) with { VariationId = "unlisted-variation" };

        Evaluate(Config(rule) with { Variations = [UnderVariation] }).Variation.ShouldBeNull();
    }

    [Fact]
    public void ReportsAFoundMatchForARuleThatServedAValue()
    {
        var served = EvaluateWithReason(Config(Matching("matched", order: 1)), User);

        served.State.Value.ShouldBe("matched");
        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void ReportsAFoundMatchForTheDefaultTargetingRuleValueWhenNoRuleMatched()
    {
        var served = EvaluateWithReason(Config(NotMatching("never")), User);

        served.State.Value.ShouldBe("the default");
        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void SkipsAPercentageRuleForAContextWithoutAnIdentifier()
    {
        var served = EvaluateWithReason(Config("config-1", EvenRollout()), new Context());

        served.State.Value.ShouldBe("the default");
        served.State.ValueId.ShouldBe("default-value-id");
        served.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public void SkipsAPercentageRuleWithoutAContext()
    {
        var served = EvaluateWithReason(Config("config-1", EvenRollout()), null);

        served.State.Value.ShouldBe("the default");
        served.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public void SkipsAPercentageRuleWithoutAnIdentifierWhateverTheKind()
    {
        var served = EvaluateWithReason(Config(BucketingConfigId, TenEqualBuckets()), new Context { Kind = "device" });

        served.State.Value.ShouldBe("the default");
        served.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public void SkipsAConditionalRolloutForAContextWithoutAnIdentifier()
    {
        var rule = new ConditionalRule
        {
            Id = "r",
            Order = 1,
            Target = "percentage",
            Conditions = [TraitCondition("/plan", "pro")],
            Percentages = [Bucket(100, "everyone")],
        };

        var served = EvaluateWithReason(Config("config-1", rule), new Context { Traits = { ["plan"] = "pro" } });

        served.State.Value.ShouldBe("the default");
        served.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public void ServesASkippedRolloutWithTheVariationOfTheDefaultTargetingRuleValueAlone()
    {
        var config = Config("config-1", EvenRollout()) with { Variations = [UnderVariation] };
        config = config with { Target = config.Target with { DefaultVariationId = "under-variation" } };

        var served = EvaluateWithReason(config, new Context());

        served.State.Variation.ShouldBe(UnderVariation);
        served.State.RuleId.ShouldBeNull();
        served.State.ShareId.ShouldBeNull();
    }

    [Fact]
    public void TriesNoRuleAfterASkippedRollout()
    {
        var everyone = Requiring() with { Order = 2 };
        var config = Config("config-1", EvenRollout(), everyone);

        var served = EvaluateWithReason(config, new Context());

        served.State.Value.ShouldBe("the default");
        served.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public void AppliesASingleValueRuleToAContextWithoutAnIdentifier()
    {
        var everyone = Requiring() with { Order = 1 };
        var config = Config("config-1", everyone, EvenRollout() with { Order = 2 });

        var served = EvaluateWithReason(config, new Context());

        served.State.Value.ShouldBe("matched");
        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void TriesTheNextRuleWhenAConditionalRolloutsConditionsDoNotHoldWithoutAnIdentifier()
    {
        var rule = new ConditionalRule
        {
            Id = "r",
            Order = 1,
            Target = "percentage",
            Conditions = [TraitCondition("/plan", "pro")],
            Percentages = [Bucket(100, "everyone")],
        };
        var config = Config("config-1", rule, Requiring() with { Order = 2 });

        var served = EvaluateWithReason(config, new Context());

        served.State.Value.ShouldBe("matched");
        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void SkipsARolloutWithNoSharesForAContextWithoutAnIdentifier()
    {
        var served = EvaluateWithReason(
            Config("config-1", new PercentageRule { Id = "r", Order = 1, Percentages = [] }, Requiring() with { Order = 2 }),
            new Context());

        served.State.Value.ShouldBe("the default");
        served.Reason.ShouldBe(EvaluationReason.RolloutSkippedNoIdentifier);
    }

    [Fact]
    public void TriesTheNextRuleWhenARuleCarriesNoPercentagesForAContextWithoutAnIdentifier()
    {
        var config = Config(
            "config-1",
            new PercentageRule { Id = "r", Order = 1 },
            Requiring() with { Order = 2 });

        var served = EvaluateWithReason(config, new Context());

        served.State.Value.ShouldBe("matched");
        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void TriesTheNextRuleWhenARuleCarriesNoPercentagesForAContextWithAnIdentifier()
    {
        var config = Config(
            "config-1",
            new PercentageRule { Id = "r", Order = 1 },
            Requiring() with { Order = 2 });

        var served = EvaluateWithReason(config, User);

        served.State.Value.ShouldBe("matched");
        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void AssignsABucketToAnEmptyIdentifier()
    {
        var served = EvaluateWithReason(Config("config-1", EvenRollout()), new Context { Id = string.Empty });

        served.Reason.ShouldBe(EvaluationReason.FoundMatch);
        served.State.RuleId.ShouldBe("r");
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData("user", "0")]
    [InlineData("device", "5")]
    [InlineData("tenant", "0")]
    public void AssignsABucketFromTheIdentifierAndTheKind(string? kind, string expected)
    {
        var config = Config(BucketingConfigId, TenEqualBuckets());

        Evaluate(config, new Context { Id = "user-1", Kind = kind }).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "user", "matched")]
    [InlineData("user", "user", "matched")]
    [InlineData("device", "device", "matched")]
    [InlineData("device", "user", "the default")]
    [InlineData(null, "", "the default")]
    public void MatchesAKindConditionAgainstTheEffectiveKind(string? kind, string target, string expected)
    {
        var config = Config(Requiring(AttributeCondition("kind", target)));

        Evaluate(config, new Context { Id = "42", Kind = kind }).Value.ShouldBe(expected);
    }

    [Fact]
    public void MatchesAKindConditionForUserWithoutAContext()
    {
        var config = Config(Requiring(AttributeCondition("kind", "user")));

        new ConfigEvaluator(NullLogger.Instance).Evaluate(config, null, null).Value.ShouldBe("matched");
    }

    // SEMANTICS.md 7 -- a rule that cannot be evaluated is skipped, and the next rule is tried.
    [Fact]
    public void SkipsARuleThatThrowsAndReportsIt()
    {
        var logger = new CapturingLogger();
        var malformed = new ConditionalRule
        {
            Id = "broken-rule",
            Order = 1,
            Value = "never served",
            Conditions =
            [
                new AttributeCondition
                {
                    Attribute = "identifier",
                    Operator = "starts with any of",
                    TargetType = "text",
                    TargetValues = [null!],
                },
            ],
        };

        var state = Evaluate(Config(malformed, Matching("recovered", order: 2)), logger: logger);

        state.Value.ShouldBe("recovered");
        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldContain("broken-rule");
        entry.Message.ShouldContain("the-key");
        entry.Error.ShouldBeOfType<ArgumentNullException>();
    }

    [Fact]
    public void SkipsARuleKindItDoesNotKnow()
    {
        var config = Config(new UnknownRule { Id = "future", Order = 1 });

        Evaluate(config).Value.ShouldBe("the default");
    }

    [Fact]
    public void CarriesTheConfigIdentityIntoTheResult()
    {
        var state = Evaluate(Config(Matching("matched", order: 1)));

        state.Id.ShouldBe("the-id");
        state.Key.ShouldBe("the-key");
        state.Type.ShouldBe(ConfigType.String);
    }

    [Fact]
    public void EvaluatesAConfigWithNoRules()
    {
        var state = Evaluate(Config());

        state.Value.ShouldBe("the default");
        state.ValueId.ShouldBe("default-value-id");
    }

    private sealed record UnknownRule : Rule;

    private static ConditionalRule Matching(string value, int? order) =>
        new()
        {
            Id = $"rule-{value}",
            Order = order,
            Value = value,
            ValueId = $"{value}-value-id",
            Conditions = [AttributeCondition("identifier", "user-1")],
        };

    private static ConditionalRule NotMatching(string value) =>
        new()
        {
            Id = $"rule-{value}",
            Order = 1,
            Value = value,
            Conditions = [AttributeCondition("identifier", "somebody-else")],
        };

    private static ConditionalRule Requiring(params AttributeCondition[] conditions) =>
        new()
        {
            Id = "r",
            Order = 1,
            Value = "matched",
            Conditions = conditions,
        };

    private static AttributeCondition TraitCondition(string trait, string target) =>
        new()
        {
            Attribute = "traits",
            Trait = trait,
            Operator = "=",
            TargetType = "text",
            TargetValues = [target],
        };

    private static Context ContextWith(string id, string plan) =>
        new() { Id = id, Traits = { ["plan"] = plan } };

    private static AttributeCondition AttributeCondition(string attribute, string target) =>
        new()
        {
            Attribute = attribute,
            Operator = "=",
            TargetType = "text",
            TargetValues = [target],
        };

    private static PercentageBucket Bucket(double percentage, string? value) =>
        new()
        {
            Id = $"bucket-{value}",
            Percentage = percentage,
            Value = value,
            ValueId = value is null ? null : $"{value}-value-id",
        };

    private static PercentageRule TenEqualBuckets() =>
        new()
        {
            Id = "r",
            Order = 1,
            Percentages = [.. Enumerable.Range(0, 10).Select(index => Bucket(10, $"{index}"))],
        };

    private static PercentageRule EvenRollout() =>
        new()
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "under"), Bucket(50, "over")],
        };

    private static Config Config(params Rule[] rules) => Config("the-id", rules);

    private static Config Config(string id, params Rule[] rules) =>
        new()
        {
            Id = id,
            Key = "the-key",
            Type = ConfigType.String,
            Target = new TargetingRules
            {
                DefaultValue = "the default",
                DefaultValueId = "default-value-id",
                Rules = rules,
            },
        };

    private static TraitValue ToTraitValue(object value) =>
        value switch
        {
            string text => text,
            long number => number,
            double number => number,
            bool flag => flag,
            _ => TraitValue.Null,
        };

    private static ConfigState Evaluate(Config config, Context? context = null, ILogger? logger = null) =>
        new ConfigEvaluator(logger ?? NullLogger.Instance).Evaluate(config, context ?? User, null);

    private static ServedConfig EvaluateWithReason(Config config, Context? context) =>
        new ConfigEvaluator(NullLogger.Instance).EvaluateWithReason(config, context, null, ConfigEvaluator.NoSegments);
}
