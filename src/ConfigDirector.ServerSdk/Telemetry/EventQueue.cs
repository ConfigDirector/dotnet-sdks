namespace ConfigDirector.Telemetry;

// What a flush reports, and the window it covers. A snapshot holding nothing but drops is still
// worth sending: the server counts what an application could not report.
internal sealed record EventQueueSnapshot(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<EvaluatedConfigEvent> Events,
    int DroppedCount)
{
    internal bool IsEmpty => Events.Count == 0 && DroppedCount == 0;
}

// Holds evaluations until they are flushed. Written to by every thread that evaluates a config
// and drained by the one that reports them.
internal sealed class EventQueue(int limit)
{
    private readonly Queue<EvaluatedConfigEvent> _events = new();
    private readonly object _lock = new();

    private DateTimeOffset? _startTime;
    private int _droppedCount;

    internal void Push(EvaluatedConfigEvent evaluation)
    {
        lock (_lock)
        {
            _startTime ??= DateTimeOffset.UtcNow;

            if (_events.Count == limit)
            {
                _events.Dequeue();
                _droppedCount++;
            }

            _events.Enqueue(evaluation);
        }
    }

    // Empties the queue, leaving it ready to collect the next batch.
    internal EventQueueSnapshot TakeSnapshot()
    {
        lock (_lock)
        {
            var endTime = DateTimeOffset.UtcNow;
            var snapshot = new EventQueueSnapshot(
                _startTime ?? endTime, endTime, [.. _events], _droppedCount);

            Reset();
            return snapshot;
        }
    }

    internal void Clear()
    {
        lock (_lock)
        {
            Reset();
        }
    }

    private void Reset()
    {
        _events.Clear();
        _startTime = null;
        _droppedCount = 0;
    }
}
