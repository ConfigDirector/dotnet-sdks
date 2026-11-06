namespace ConfigDirector.Telemetry;

internal enum TrackedEventWarning
{
    NameOutsideTheCharacterLimit,
    ValueNotAFiniteNumber,
    DetailsNotReportable,
    BatchAlreadyFull,
}

internal static class TrackedEventWarnings
{
    internal static string MessageOf(TrackedEventWarning warning) =>
        warning switch
        {
            TrackedEventWarning.NameOutsideTheCharacterLimit =>
                $"A tracked event was not reported because its name does not have 1 to {TrackedEventLimits.NameMaximumCharacters} characters.",
            TrackedEventWarning.ValueNotAFiniteNumber =>
                "A tracked event was reported without its value because the value is not a finite number.",
            TrackedEventWarning.DetailsNotReportable =>
                $"A tracked event was reported without its details because they have more than {TrackedEventLimits.DetailsMaximumTopLevelKeys} top-level keys or are larger than {TrackedEventLimits.DetailsMaximumJsonBytes} bytes as JSON.",
            TrackedEventWarning.BatchAlreadyFull =>
                $"A tracked event was not reported because {TrackedEventLimits.EventsPerBatch} tracked events are already waiting for the next telemetry batch.",
            _ => throw new InvalidOperationException("The tracked event warning is not one the SDK knows."),
        };
}
