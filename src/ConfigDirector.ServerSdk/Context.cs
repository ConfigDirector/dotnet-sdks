namespace ConfigDirector;

/// <summary>
/// Who a config is being evaluated for. Targeting rules are matched against it.
/// </summary>
/// <remarks>
/// <see cref="Id"/> is what segments users in a percentage rollout, so changing it can move a user
/// into a different percentile.
/// <code>
/// new Context
/// {
///     Id = "user-1",
///     Traits =
///     {
///         ["plan"] = "pro",
///         ["tags"] = new[] { "beta" },
///     },
/// }
/// </code>
/// </remarks>
public sealed record Context
{
    private readonly TraitCollection _traits = [];

    /// <summary>
    /// The user's identifier, which decides their bucket in a percentage rollout. Null leaves the
    /// bucket unstable, assigned afresh on every evaluation.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>The user's display name.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The user's traits, matched against targeting rules by name. Never null; a context with no
    /// traits carries an empty collection. Assigning a collection copies it.
    /// </summary>
    public TraitCollection Traits
    {
        get => _traits;
        init => _traits = value is null ? [] : new TraitCollection(value);
    }

    /// <summary>
    /// Whether the context stays out of the dashboard: evaluated as usual, but never persisted, and
    /// telemetry reports neither the context nor its id.
    /// </summary>
    public bool Anonymous { get; init; }

    internal TraitValue TraitsValue => _traits;
}
