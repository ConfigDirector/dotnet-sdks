namespace ConfigDirector.Telemetry;

internal abstract record TrackedEventAdmission(IReadOnlyList<TrackedEventWarning> Warnings)
{
    internal static TrackedEventAdmission Of(
        string? name, Context? context, TrackOptions options, DateTimeOffset happenedAt)
    {
        if (name is null || !TrackedEventLimits.IsNameWithinTheCharacterLimit(name))
        {
            return new Dropped([TrackedEventWarning.NameOutsideTheCharacterLimit]);
        }

        var value = ReportableValueOf(options.Value);
        var details = ReportableDetailsOf(options);

        return new Admitted(
            new TrackedEventEntry(name, ReportedContext.Of(context), value.Reportable, details.Json, happenedAt),
            context,
            details.Reportable,
            [.. value.Warnings, .. details.Warnings]);
    }

    private static (TrackedEventValue? Reportable, TrackedEventWarning[] Warnings) ReportableValueOf(
        TrackedEventValue? value) =>
        value is { JsonNumber: null }
            ? (null, [TrackedEventWarning.ValueNotAFiniteNumber])
            : (value, []);

    private static (TrackedEventDetails? Reportable, byte[]? Json, TrackedEventWarning[] Warnings) ReportableDetailsOf(
        TrackOptions options)
    {
        if (!options.HasDetails)
        {
            return (null, null, []);
        }

        var copy = new TrackedEventDetails(options.Details);
        return TrackedEventDetailsJson.WithinTheLimits(copy) is { } json
            ? (copy, json, [])
            : (null, null, [TrackedEventWarning.DetailsNotReportable]);
    }

    internal sealed record Admitted(
        TrackedEventEntry Entry,
        Context? Context,
        TrackedEventDetails? Details,
        IReadOnlyList<TrackedEventWarning> Warnings)
        : TrackedEventAdmission(Warnings);

    internal sealed record Dropped(IReadOnlyList<TrackedEventWarning> Warnings) : TrackedEventAdmission(Warnings);
}
