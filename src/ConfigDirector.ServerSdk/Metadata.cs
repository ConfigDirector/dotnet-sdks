namespace ConfigDirector;

/// <summary>
/// The calling application, which targeting rules can match on alongside the user's own details.
/// </summary>
public sealed record Metadata
{
    /// <summary>The application's name.</summary>
    public string? AppName { get; init; }

    /// <summary>The version the application is running, matched by semver rules.</summary>
    public string? AppVersion { get; init; }
}
