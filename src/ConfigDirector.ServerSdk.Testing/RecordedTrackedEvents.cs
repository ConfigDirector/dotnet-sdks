using System.Collections;
using ConfigDirector.Telemetry;

namespace ConfigDirector.Testing;

internal sealed class RecordedTrackedEvents : IList<TrackedEvent>
{
    private readonly object _writing = new();
    private volatile TrackedEvent[] _events = [];

    public int Count => _events.Length;

    public bool IsReadOnly => false;

    public TrackedEvent this[int index]
    {
        get => _events[index];
        set => WriteToACopy(events => events[index] = value);
    }

    public void Add(TrackedEvent item) => WriteToACopy(events => events.Add(item));

    public void Clear() => WriteToACopy(events => events.Clear());

    public bool Contains(TrackedEvent item) => IndexOf(item) >= 0;

    public void CopyTo(TrackedEvent[] array, int arrayIndex)
    {
        var events = _events;
        var eventsThatFit = Math.Min(events.Length, array.Length - arrayIndex);
        Array.Copy(events, 0, array, arrayIndex, eventsThatFit);
    }

    public IEnumerator<TrackedEvent> GetEnumerator() => ((IEnumerable<TrackedEvent>)_events).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(TrackedEvent item) => Array.IndexOf(_events, item);

    public void Insert(int index, TrackedEvent item) => WriteToACopy(events => events.Insert(index, item));

    public bool Remove(TrackedEvent item)
    {
        var removed = false;
        WriteToACopy(events => removed = events.Remove(item));
        return removed;
    }

    public void RemoveAt(int index) => WriteToACopy(events => events.RemoveAt(index));

    internal void Record(TrackedEventAdmission.Admitted admitted) =>
        Add(new TrackedEvent(admitted.Entry.Name, admitted.Context, admitted.Entry.Value, admitted.Details));

    private void WriteToACopy(Action<List<TrackedEvent>> write)
    {
        lock (_writing)
        {
            var events = new List<TrackedEvent>(_events);
            write(events);
            _events = [.. events];
        }
    }
}
