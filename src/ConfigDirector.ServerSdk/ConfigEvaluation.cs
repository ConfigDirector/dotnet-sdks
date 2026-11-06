namespace ConfigDirector;

/// <summary>The result of evaluating a single config key.</summary>
public sealed record ConfigEvaluation
{
    /// <summary>The config that was evaluated.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>What the evaluation returned, in the type the in-code default value asked for.</summary>
    public object Value { get; init; } = string.Empty;

    /// <summary>
    /// Whether the in-code default value was returned rather than a value from ConfigDirector.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>Why the evaluation produced the value that it did.</summary>
    public EvaluationReason Reason { get; init; }

    /// <summary>
    /// The server's identifier for the value that was returned, or null when the in-code default
    /// value was returned.
    /// </summary>
    public string? ValueId { get; init; }

    /// <summary>The context the config was evaluated against, or null.</summary>
    public Context? Context { get; init; }

    /// <summary>
    /// The variation that was served, or null when the config has no variations or the in-code default
    /// value was returned.
    /// </summary>
    public Variation? Variation { get; init; }
}
