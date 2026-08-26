namespace ConfigDirector.Samples.AspNetCore;

/// The shape of `json-value-config`, a config declared as JSON in the dashboard and read straight
/// into a type of this application's own. Property names are matched case-insensitively, so
/// camelCase JSON binds to these.
internal sealed record RetrySettings
{
    public int Retries { get; init; }

    public int TimeoutMs { get; init; }
}
