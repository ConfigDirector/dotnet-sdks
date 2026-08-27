namespace ConfigDirector.Telemetry;

// The contexts configs were evaluated against, one entry per identifier.
internal sealed class ContextRegistry(int limit)
{
    private readonly Dictionary<string, Context> _contexts = [];

    // The order they were first seen in, which is the order they are evicted in. Kept alongside
    // the dictionary because removing an entry would otherwise lose it.
    private readonly Queue<string> _order = new();
    private readonly object _lock = new();

    private int _droppedCount;

    internal void Add(string contextId, Context context)
    {
        lock (_lock)
        {
            // Seeing a context again leaves it where it was, so one that keeps being evaluated is
            // no safer from eviction than one seen once. That matches the other SDKs, whose maps
            // behave the same way.
            if (!_contexts.ContainsKey(contextId))
            {
                _order.Enqueue(contextId);
            }

            _contexts[contextId] = context;

            while (_contexts.Count > limit)
            {
                _contexts.Remove(_order.Dequeue());
                _droppedCount++;
            }
        }
    }

    // Returns the contexts collected so far and how many were dropped, then starts over.
    internal (IReadOnlyList<Context> Contexts, int DroppedCount) TakeSnapshot()
    {
        lock (_lock)
        {
            var snapshot = (Contexts: (IReadOnlyList<Context>)[.. _order.Select(id => _contexts[id])], _droppedCount);
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
        _contexts.Clear();
        _order.Clear();
        _droppedCount = 0;
    }
}
