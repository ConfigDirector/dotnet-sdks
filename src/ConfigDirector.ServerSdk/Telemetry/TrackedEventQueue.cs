namespace ConfigDirector.Telemetry;

internal sealed class TrackedEventQueue
{
    private readonly List<TrackedEventEntry> _entries = [];
    private readonly HashSet<TrackedEventWarning> _warningsRaisedThisBatch = [];
    private readonly object _lock = new();

    internal IReadOnlyList<TrackedEventWarning> Add(TrackedEventAdmission admission)
    {
        lock (_lock)
        {
            return [.. WarningsOfKeeping(admission).Where(_warningsRaisedThisBatch.Add)];
        }
    }

    internal IReadOnlyList<TrackedEventEntry> TakeBatch()
    {
        lock (_lock)
        {
            var batch = _entries.ToArray();
            Reset();
            return batch;
        }
    }

    internal void Clear()
    {
        lock (_lock)
        {
            Reset();
        }
    }

    private IReadOnlyList<TrackedEventWarning> WarningsOfKeeping(TrackedEventAdmission admission)
    {
        switch (admission)
        {
            case TrackedEventAdmission.Dropped:
                return admission.Warnings;
            case TrackedEventAdmission.Admitted when _entries.Count >= TrackedEventLimits.EventsPerBatch:
                return [TrackedEventWarning.BatchAlreadyFull];
            case TrackedEventAdmission.Admitted admitted:
                _entries.Add(admitted.Entry);
                return admission.Warnings;
            default:
                throw new InvalidOperationException("The tracked event admission is not one the queue knows.");
        }
    }

    private void Reset()
    {
        _entries.Clear();
        _warningsRaisedThisBatch.Clear();
    }
}
