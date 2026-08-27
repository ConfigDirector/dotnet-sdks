using Microsoft.Extensions.Logging;

namespace ConfigDirector.Telemetry;

// Collects config evaluations and reports them on an interval.
internal sealed class TelemetryCollector : IAsyncDisposable
{
    // The first report comes early so that a process which runs briefly still reports what it
    // evaluated, without outrunning an interval shorter than that.
    private static readonly TimeSpan EarliestFirstFlush = TimeSpan.FromSeconds(5);

    // The queue limit is split between the two things a report carries. Evaluations outnumber the
    // distinct contexts they were evaluated against by a wide margin, so they get the larger share.
    private const int EvaluationShare = 7;

    private readonly ILogger _logger;
    private readonly TimeSpan _flushInterval;
    private readonly HttpEventReporter _reporter;
    private readonly EventQueue _events;
    private readonly ContextRegistry _contexts;

    // Held for the whole of a flush, so a report triggered by closing cannot overtake one the
    // interval already started.
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _closing = new();
    private readonly Task _reporting;

    private volatile bool _collecting = true;
    private volatile bool _disposed;
    private bool _closed;

    internal TelemetryCollector(TelemetryCollectorOptions options)
    {
        _logger = options.LoggerFactory.CreateLogger<TelemetryCollector>();
        _flushInterval = options.FlushInterval;
        _reporter = new HttpEventReporter(options.ServerSdkKey, options.BaseUrl, options.LoggerFactory);

        var evaluationLimit = options.EventQueueLimit * EvaluationShare / 10;
        _events = new EventQueue(evaluationLimit);
        _contexts = new ContextRegistry(options.EventQueueLimit - evaluationLimit);

        _reporting = ReportOnIntervalAsync();
    }

    // On the client's hot path, so this returns without doing any appreciable work.
    internal void Record<T>(
        string key,
        T defaultValue,
        T value,
        bool usedDefault,
        EvaluationReason reason,
        Context? context,
        ConfigType? configType,
        string? valueId)
    {
        if (!_collecting)
        {
            return;
        }

        // An anonymous context still targets rules, but it is not persisted and must not be
        // identifiable in what is reported.
        string? contextId = null;
        if (context is { Anonymous: false, Id: { Length: > 0 } identifier })
        {
            contextId = identifier;
            _contexts.Add(identifier, context);
        }

        _events.Push(EvaluatedConfigEvent.Create(
            key, defaultValue, value, usedDefault, reason, contextId, configType, valueId));
    }

    // Reports everything collected so far without waiting for the next interval. Nothing is sent
    // when there is nothing to report.
    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        // A closed collector has already reported whatever it held, so asking again is not an
        // error worth throwing over.
        if (_disposed)
        {
            return;
        }

        await _flushing.WaitAsync(cancellationToken).ConfigureAwait(false);
        ReporterResponse response;
        try
        {
            var snapshot = _events.TakeSnapshot();
            var (contexts, droppedContexts) = _contexts.TakeSnapshot();
            var report = new EventReport(
                Aggregation.Aggregate(
                    [.. snapshot.Events.Select(evaluation => evaluation.Compacted())],
                    snapshot.StartTime,
                    snapshot.EndTime),
                snapshot.DroppedCount,
                contexts,
                droppedContexts);

            if (report.IsEmpty)
            {
                return;
            }

            response = await _reporter.ReportAsync(report, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // A failed report must not take down the flush loop.
            Log.ReportFailed(_logger, error);
            return;
        }
        finally
        {
            _flushing.Release();
        }

        if (response.Fatal)
        {
            StopCollecting();
        }
    }

    // Reports whatever is left and stops collecting.
    public async ValueTask DisposeAsync()
    {
        bool wasCollecting;
        lock (_closing)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            wasCollecting = _collecting;
            _collecting = false;
        }

        _stop.Cancel();
        try
        {
            await _reporting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The loop stopping is what was asked for.
        }

        // Nothing left to say to a server that already rejected us.
        if (wasCollecting)
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _events.Clear();
        _contexts.Clear();
        _disposed = true;
        await _reporter.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
        _flushing.Dispose();
    }

    private async Task ReportOnIntervalAsync()
    {
        var delay = _flushInterval < EarliestFirstFlush ? _flushInterval : EarliestFirstFlush;
        try
        {
            while (true)
            {
                await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
                await FlushAsync(_stop.Token).ConfigureAwait(false);
                if (!_collecting)
                {
                    return;
                }

                delay = _flushInterval;
            }
        }
        catch (OperationCanceledException)
        {
            // Closed.
        }
    }

    private void StopCollecting()
    {
        _collecting = false;
        _stop.Cancel();
        _events.Clear();
        _contexts.Clear();
        Log.StoppedCollecting(_logger, null);
    }

    private static class Log
    {
        internal static readonly Action<ILogger, Exception?> ReportFailed =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(1, "ReportFailed"),
                "Error reporting telemetry data.");

        internal static readonly Action<ILogger, Exception?> StoppedCollecting =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(2, "StoppedCollecting"),
                "Received a fatal error while reporting telemetry. No longer collecting events.");
    }
}
