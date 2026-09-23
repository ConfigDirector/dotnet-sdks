using Microsoft.Extensions.Logging;

namespace ConfigDirector.Evaluation;

internal sealed class ConfigEvaluator
{
    private const int Last = int.MaxValue;

    private static readonly Action<ILogger, string, string, Exception?> ReportDisregardedRule =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, "TargetingRuleDisregarded"),
            "There was an error while evaluating targeting rule {RuleId} for config {ConfigKey}. "
                + "The rule will be disregarded.");

    private readonly ILogger _logger;

    internal ConfigEvaluator(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    internal ConfigState Evaluate(Config config, Context? context, Metadata? metadata)
    {
        var selected = SelectValue(config, context, metadata);

        return new ConfigState
        {
            Id = config.Id,
            Key = config.Key,
            Type = config.Type,
            Value = selected.Value,
            ValueId = selected.ValueId,
        };
    }

    private Selection SelectValue(Config config, Context? context, Metadata? metadata)
    {
        foreach (var rule in config.Target.Rules.OrderBy(rule => rule.Order ?? Last))
        {
            var selected = EvaluateRule(rule, config, context, metadata);
            if (selected.Matched)
            {
                return selected;
            }
        }

        return Selection.From(config.Target.DefaultValue, config.Target.DefaultValueId);
    }

    private Selection EvaluateRule(Rule rule, Config config, Context? context, Metadata? metadata)
    {
        try
        {
            return rule switch
            {
                PercentageRule percentageRule => SelectBucket(percentageRule.Percentages, config, context),
                ConditionalRule conditionalRule => EvaluateConditionals(conditionalRule, config, context, metadata),
                _ => Selection.None,
            };
        }
        catch (Exception error)
        {
            // Malformed rule data must not take the whole evaluation down with it, nor discard the
            // sibling rules that would have matched.
            ReportDisregardedRule(_logger, rule.Id, config.Key, error);
            return Selection.None;
        }
    }

    private static Selection EvaluateConditionals(ConditionalRule rule, Config config, Context? context, Metadata? metadata)
    {
        if (!ConditionsMet(rule, context, metadata))
        {
            return Selection.None;
        }

        if (rule.Target == "percentage")
        {
            return SelectBucket(rule.Percentages, config, context);
        }

        return rule.Target == "value" ? Selection.From(rule.Value, rule.ValueId) : Selection.None;
    }

    private static bool ConditionsMet(ConditionalRule rule, Context? context, Metadata? metadata)
    {
        foreach (var condition in rule.Conditions)
        {
            if (!ConditionEvaluator.Evaluate(condition, context, metadata))
            {
                return false;
            }
        }

        return true;
    }

    private static Selection SelectBucket(
        IReadOnlyList<PercentageBucket> buckets,
        Config config,
        Context? context)
    {
        var identifier = context?.Id ?? Guid.NewGuid().ToString();
        var assigned = PercentHashing.AssignPercentage(config.Id, identifier);

        var total = 0.0;
        foreach (var bucket in buckets)
        {
            if (assigned < bucket.Percentage + total)
            {
                return Selection.From(bucket.Value, bucket.ValueId);
            }

            total += bucket.Percentage;
        }

        return Selection.None;
    }

    private readonly record struct Selection(bool Matched, string? Value, string? ValueId)
    {
        internal static Selection None => default;

        internal static Selection From(string? value, string? valueId) => new(true, value, valueId);

        // A rule or bucket carrying no value selects nothing, and the next rule is tried.
        internal static Selection From(TraitValue value, string? valueId) =>
            value.Kind == TraitValueKind.Null ? None : new Selection(true, TraitText.Render(value), valueId);
    }
}
