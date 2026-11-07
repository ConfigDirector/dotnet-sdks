using Microsoft.Extensions.Logging;

namespace ConfigDirector.Evaluation;

internal sealed class ConfigEvaluator
{
    private const int Last = int.MaxValue;

    private static readonly RuleServing NotMatched = new RuleNotMatched();

    private static readonly RuleServing SkippedNoIdentifier = new RolloutSkippedNoIdentifier();

    internal static readonly IReadOnlyDictionary<string, Segment> NoSegments =
        new Dictionary<string, Segment>(StringComparer.Ordinal);

    private static readonly Action<ILogger, string, string, Exception?> ReportDisregardedRule =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, "TargetingRuleDisregarded"),
            "There was an error while evaluating targeting rule {RuleId} for config {ConfigKey}. "
                + "The rule will be disregarded.");

    private readonly ILogger _logger;

    internal ConfigEvaluator(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    internal ConfigState Evaluate(Config config, Context? context, Metadata? metadata) =>
        Evaluate(config, context, metadata, NoSegments);

    internal ConfigState Evaluate(
        Config config,
        Context? context,
        Metadata? metadata,
        IReadOnlyDictionary<string, Segment> segments) =>
        EvaluateWithReason(config, context, metadata, segments).State;

    internal ServedConfig EvaluateWithReason(
        Config config,
        Context? context,
        Metadata? metadata,
        IReadOnlyDictionary<string, Segment> segments)
    {
        var aRolloutWasSkipped = false;
        foreach (var rule in config.Target.Rules.OrderBy(rule => rule.Order ?? Last))
        {
            switch (EvaluateRule(rule, config, context, metadata, segments))
            {
                case ServedValue servedValue:
                    return ServedConfigOf(config, servedValue, EvaluationReason.FoundMatch);
                case RolloutSkippedNoIdentifier:
                    aRolloutWasSkipped = true;
                    break;
                case RuleNotMatched:
                    break;
            }
        }

        var reason = aRolloutWasSkipped ? EvaluationReason.RolloutSkippedNoIdentifier : EvaluationReason.FoundMatch;
        return ServedConfigOf(config, DefaultTargetingRuleValue(config), reason);
    }

    private static ServedValue DefaultTargetingRuleValue(Config config) =>
        new(config.Target.DefaultValue, config.Target.DefaultValueId, config.Target.DefaultVariationId);

    private static ServedConfig ServedConfigOf(Config config, ServedValue servedValue, EvaluationReason reason) =>
        new(
            new ConfigState
            {
                Id = config.Id,
                Key = config.Key,
                Type = config.Type,
                Value = servedValue.Value,
                ValueId = servedValue.ValueId,
                Variation = VariationWithId(config, servedValue.VariationId),
                RuleId = servedValue.RuleId,
                ShareId = servedValue.ShareId,
            },
            reason);

    private static Variation? VariationWithId(Config config, string? variationId) =>
        variationId is null
            ? null
            : config.Variations.FirstOrDefault(variation => variation.Id == variationId);

    private RuleServing EvaluateRule(
        Rule rule,
        Config config,
        Context? context,
        Metadata? metadata,
        IReadOnlyDictionary<string, Segment> segments)
    {
        try
        {
            return rule switch
            {
                PercentageRule percentageRule => EvaluateRollout(percentageRule.Id, percentageRule.Percentages, config, context),
                ConditionalRule conditionalRule => EvaluateConditionals(conditionalRule, config, context, metadata, segments),
                _ => NotMatched,
            };
        }
        catch (Exception error)
        {
            // Malformed rule data must not take the whole evaluation down with it, nor discard the
            // sibling rules that would have matched.
            ReportDisregardedRule(_logger, rule.Id, config.Key, error);
            return NotMatched;
        }
    }

    private static RuleServing EvaluateConditionals(
        ConditionalRule rule,
        Config config,
        Context? context,
        Metadata? metadata,
        IReadOnlyDictionary<string, Segment> segments)
    {
        if (!ConditionsMet(rule, context, metadata, segments))
        {
            return NotMatched;
        }

        if (rule.Target == "percentage")
        {
            return EvaluateRollout(rule.Id, rule.Percentages, config, context);
        }

        return rule.Target == "value" ? ServedValueOrNotMatched(rule.Value, rule.ValueId, rule.VariationId) : NotMatched;
    }

    private static bool ConditionsMet(
        ConditionalRule rule,
        Context? context,
        Metadata? metadata,
        IReadOnlyDictionary<string, Segment> segments)
    {
        foreach (var condition in rule.Conditions)
        {
            if (!ConditionHolds(condition, context, metadata, segments))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConditionHolds(
        Condition condition,
        Context? context,
        Metadata? metadata,
        IReadOnlyDictionary<string, Segment> segments) =>
        condition switch
        {
            AttributeCondition attributeCondition => ConditionEvaluator.Evaluate(attributeCondition, context, metadata),
            SegmentCondition segmentCondition => SegmentEvaluator.Evaluate(segmentCondition, segments, context, metadata),
            _ => false,
        };

    private static RuleServing EvaluateRollout(
        string ruleId,
        IReadOnlyList<PercentageBucket>? buckets,
        Config config,
        Context? context)
    {
        if (buckets is null)
        {
            return NotMatched;
        }

        if (context?.Id is not { } identifier)
        {
            return SkippedNoIdentifier;
        }

        var assigned = PercentHashing.AssignPercentage(config.Id, identifier, ContextKinds.EffectiveKind(context));

        var total = 0.0;
        foreach (var bucket in buckets)
        {
            if (assigned < bucket.Percentage + total)
            {
                return ServedValueOrNotMatched(bucket.Value, bucket.ValueId, bucket.VariationId, ruleId, bucket.Id);
            }

            total += bucket.Percentage;
        }

        return NotMatched;
    }

    // A rule or bucket carrying no value selects nothing, and the next rule is tried.
    private static RuleServing ServedValueOrNotMatched(
        TraitValue value,
        string? valueId,
        string? variationId,
        string? ruleId = null,
        string? shareId = null) =>
        value.Kind == TraitValueKind.Null
            ? NotMatched
            : new ServedValue(TraitText.Render(value), valueId, variationId, ruleId, shareId);

    private abstract record RuleServing;

    private sealed record ServedValue(
        string? Value,
        string? ValueId,
        string? VariationId,
        string? RuleId = null,
        string? ShareId = null) : RuleServing;

    private sealed record RuleNotMatched : RuleServing;

    private sealed record RolloutSkippedNoIdentifier : RuleServing;
}
