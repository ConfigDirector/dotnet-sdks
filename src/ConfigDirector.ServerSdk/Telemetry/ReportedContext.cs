namespace ConfigDirector.Telemetry;

internal readonly record struct ReportedContext(string? Kind, string? Identifier)
{
    internal static ReportedContext Of(Context? context) =>
        context is { Anonymous: false, Id: { Length: > 0 } identifier }
            ? new ReportedContext(ContextKinds.EffectiveKind(context), identifier)
            : default;
}
