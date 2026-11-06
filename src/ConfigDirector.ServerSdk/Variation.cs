namespace ConfigDirector;

/// <summary>One of the values a config can serve, such as the control or a treatment of an experiment.</summary>
public sealed record Variation
{
    /// <summary>
    /// The identifier ConfigDirector assigned to the variation, which stays the same when it is renamed.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The variation's name, or null when it has none.</summary>
    public string? Name { get; init; }
}
