namespace ConfigDirector;

/// <summary>The evaluated state of a single config, as text, before it is parsed to its type.</summary>
public sealed record ConfigState
{
    /// <summary>The config's identifier in ConfigDirector.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The config's key.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The type the config was declared with, or null for a type this SDK version does not
    /// recognise. A type added to ConfigDirector after this SDK was released must not break an
    /// evaluation.
    /// </summary>
    public ConfigType? Type { get; init; }

    /// <summary>
    /// The selected value rendered as text, or null when the config had no default to fall back to.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>The server's identifier for whichever value was selected, carried for telemetry.</summary>
    public string? ValueId { get; init; }
}
