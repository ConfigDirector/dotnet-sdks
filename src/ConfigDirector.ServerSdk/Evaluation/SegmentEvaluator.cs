namespace ConfigDirector.Evaluation;

internal static class SegmentEvaluator
{
    internal static bool Evaluate(
        SegmentCondition condition,
        IReadOnlyDictionary<string, Segment> segments,
        Context? context,
        Metadata? metadata)
    {
        if (!segments.TryGetValue(condition.SegmentId, out var segment))
        {
            return false;
        }

        var member = Contains(segment, context, metadata);

        return condition.Operator.ToUpperInvariant() switch
        {
            "IN" => member,
            "NOT IN" => !member,
            _ => false,
        };
    }

    private static bool Contains(Segment segment, Context? context, Metadata? metadata)
    {
        foreach (var group in segment.Groups)
        {
            if (GroupMatches(group, context, metadata))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GroupMatches(IReadOnlyList<AttributeCondition> group, Context? context, Metadata? metadata)
    {
        foreach (var condition in group)
        {
            if (!ConditionEvaluator.Evaluate(condition, context, metadata))
            {
                return false;
            }
        }

        return true;
    }
}
