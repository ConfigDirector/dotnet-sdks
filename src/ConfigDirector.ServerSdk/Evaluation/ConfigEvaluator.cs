using Microsoft.Extensions.Logging;

namespace ConfigDirector.Evaluation;

internal sealed class ConfigEvaluator
{
    // SEMANTICS.md 7 -- rules with no order evaluate last, keeping the order the server sent them.
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

    // The value and the server's id for it travel together: which rule produced the value is the
    // only thing that says which id belongs to it.
    private Selection SelectValue(Config config, Context? context, Metadata? metadata)
    {
        // OrderBy is a stable sort, which is what keeps rules sharing an order in the order the
        // server sent them.
        foreach (var rule in config.Target.Rules.OrderBy(rule => rule.Order ?? Last))
        {
            var selected = Apply(rule, config, context, metadata);
            if (selected.Matched)
            {
                return selected;
            }
        }

        return Selection.From(config.Target.DefaultValue, config.Target.DefaultValueId);
    }

    private Selection Apply(Rule rule, Config config, Context? context, Metadata? metadata)
    {
        try
        {
            return rule switch
            {
                PercentageRule percentage => SelectBucket(percentage.Percentages, config, context),
                ConditionalRule conditional => Apply(conditional, config, context, metadata),
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

    private static Selection Apply(ConditionalRule rule, Config config, Context? context, Metadata? metadata)
    {
        if (!Matches(rule, context, metadata))
        {
            return Selection.None;
        }

        if (rule.Target == "percentage")
        {
            return SelectBucket(rule.Percentages, config, context);
        }

        return rule.Target == "value" ? Selection.From(rule.Value, rule.ValueId) : Selection.None;
    }

    private static bool Matches(ConditionalRule rule, Context? context, Metadata? metadata)
    {
        foreach (var condition in rule.Conditions)
        {
            if (ConditionEvaluator.Evaluate(condition, context, metadata))
            {
                return true;
            }
        }

        return false;
    }

    // SEMANTICS.md 7.1 -- a bucket spans [total, total + width). The comparison is strict, so a
    // context landing exactly on a boundary belongs to the bucket that starts there, which is what
    // keeps a 0% bucket unreachable and each bucket's share exact.
    private static Selection SelectBucket(
        IReadOnlyList<PercentageBucket> buckets,
        Config config,
        Context? context)
    {
        // A caller with no identifier still gets a bucket, just not a stable one.
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
