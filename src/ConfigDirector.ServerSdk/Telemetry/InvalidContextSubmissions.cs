namespace ConfigDirector.Telemetry;

internal enum InvalidContextLogging
{
    LogKind,
    LogSampleLimitReached,
    Silent,
}

internal sealed record InvalidContextEntry(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int Count,
    InvalidContextEvent Event);

internal sealed class InvalidContextSubmissions
{
    internal const int SampleLimit = 20;

    private readonly Dictionary<string, int> _countsBySample = new(StringComparer.Ordinal);
    private readonly List<string> _samples = [];
    private readonly object _lock = new();

    private DateTimeOffset? _startTime;
    private int _unsampledCount;

    internal InvalidContextLogging Record(string kindSample)
    {
        lock (_lock)
        {
            _startTime ??= DateTimeOffset.UtcNow;

            if (_countsBySample.TryGetValue(kindSample, out var count))
            {
                _countsBySample[kindSample] = count + 1;
                return InvalidContextLogging.Silent;
            }

            if (_samples.Count < SampleLimit)
            {
                _countsBySample[kindSample] = 1;
                _samples.Add(kindSample);
                return InvalidContextLogging.LogKind;
            }

            _unsampledCount++;
            return _unsampledCount == 1 ? InvalidContextLogging.LogSampleLimitReached : InvalidContextLogging.Silent;
        }
    }

    internal IReadOnlyList<InvalidContextEntry> TakeSnapshot()
    {
        lock (_lock)
        {
            var endTime = DateTimeOffset.UtcNow;
            var startTime = _startTime ?? endTime;
            var entries = _samples
                .Select(sample => new InvalidContextEntry(
                    startTime, endTime, _countsBySample[sample], new InvalidContextEvent { KindSample = sample }))
                .ToList();

            if (_unsampledCount > 0)
            {
                entries.Add(new InvalidContextEntry(startTime, endTime, _unsampledCount, new InvalidContextEvent()));
            }

            Reset();
            return entries;
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
        _countsBySample.Clear();
        _samples.Clear();
        _startTime = null;
        _unsampledCount = 0;
    }
}
