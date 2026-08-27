namespace ConfigDirector.Telemetry;

// One evaluation and how many times it was made over the window a snapshot covers.
internal sealed record AggregatedEvent(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int Count,
    EvaluatedConfigEvent Event);

internal static class Aggregation
{
    // Identical evaluations collapse into one entry with a count, which is what keeps a report
    // small for an application that evaluates the same config on every request.
    internal static IReadOnlyList<AggregatedEvent> Aggregate(
        IReadOnlyList<EvaluatedConfigEvent> events, DateTimeOffset startTime, DateTimeOffset endTime)
    {
        var counts = new Dictionary<EvaluatedConfigEvent, int>();
        var order = new List<EvaluatedConfigEvent>();

        foreach (var evaluation in events)
        {
            if (counts.TryGetValue(evaluation, out var seen))
            {
                counts[evaluation] = seen + 1;
            }
            else
            {
                counts[evaluation] = 1;
                order.Add(evaluation);
            }
        }

        return [.. order.Select(evaluation =>
            new AggregatedEvent(startTime, endTime, counts[evaluation], evaluation))];
    }
}
