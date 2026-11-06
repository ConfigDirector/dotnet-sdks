namespace ConfigDirector.Telemetry;

internal sealed record TrackedEventEntry(
    string Name,
    ReportedContext Context,
    TrackedEventValue? Value,
    byte[]? DetailsJson,
    DateTimeOffset HappenedAt);
