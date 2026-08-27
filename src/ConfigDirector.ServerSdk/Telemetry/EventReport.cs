namespace ConfigDirector.Telemetry;

// Everything one flush has to say. A report holding nothing but drop counts is still worth
// sending: the server counts what an application could not report.
internal sealed record EventReport(
    IReadOnlyList<AggregatedEvent> Evaluations,
    int DroppedEvaluations,
    IReadOnlyList<Context> Contexts,
    int DroppedContexts)
{
    internal bool IsEmpty =>
        Evaluations.Count == 0 && Contexts.Count == 0 && DroppedEvaluations == 0 && DroppedContexts == 0;
}

// The outcome of reporting a batch of events. A fatal outcome is one no later report would
// survive either, so collecting stops altogether.
internal readonly record struct ReporterResponse(bool Success, bool Fatal = false);
