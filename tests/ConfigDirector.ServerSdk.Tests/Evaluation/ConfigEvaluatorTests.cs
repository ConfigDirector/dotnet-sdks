using ConfigDirector.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.Evaluation;

public class ConfigEvaluatorTests
{
    private const string BucketingConfigId = "11111111-1111-4111-8111-111111111111";

    private static readonly Context User = new() { Id = "user-1", Traits = { ["plan"] = "pro" } };

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

    // SEMANTICS.md 7 -- a conditional rule applies when any of its conditions match.
    [Fact]
    public void MatchesAConditionalRuleOnAnyOfItsConditions()
    {
        var rule = new ConditionalRule
        {
            Id = "r",
            Order = 1,
            Value = "matched",
            Conditions = [Condition("identifier", "never"), Condition("identifier", "user-1")],
        };

        Evaluate(Config(rule)).Value.ShouldBe("matched");
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
            Conditions = [Condition("identifier", "user-1")],
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
            Conditions = [Condition("identifier", "somebody-else")],
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

    // SEMANTICS.md 7.1 -- a context with no id is assigned a random identifier, so it still lands
    // in a bucket, just not a stable one.
    [Fact]
    public void AssignsAnUnstableBucketWithoutAnIdentifier()
    {
        var config = Config("config-1", new PercentageRule
        {
            Id = "r",
            Order = 1,
            Percentages = [Bucket(50, "under"), Bucket(50, "over")],
        });

        var served = Enumerable.Range(0, 200)
            .Select(_ => Evaluate(config, new Context()).Value)
            .Distinct()
            .ToList();

        served.ShouldBe(["under", "over"], ignoreOrder: true);
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
                new Condition
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
            Conditions = [Condition("identifier", "user-1")],
        };

    private static ConditionalRule NotMatching(string value) =>
        new()
        {
            Id = $"rule-{value}",
            Order = 1,
            Value = value,
            Conditions = [Condition("identifier", "somebody-else")],
        };

    private static Condition Condition(string attribute, string target) =>
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
}
