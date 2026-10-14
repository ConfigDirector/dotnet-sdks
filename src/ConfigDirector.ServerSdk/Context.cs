namespace ConfigDirector;

/// <summary>
/// Who a config is being evaluated for. Targeting rules are matched against it.
/// </summary>
/// <remarks>
/// <see cref="Id"/> and <see cref="Kind"/> are what segment contexts in a percentage rollout, so
/// changing either can move a context into a different percentile.
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
    /// The user's identifier, which decides their bucket in a percentage rollout. Without one, a
    /// context always lands in the rollout's first non-empty bucket.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// What kind of entity the context describes, such as <c>user</c>, <c>device</c> or
    /// <c>tenant</c>. Null means <c>user</c>. A context of one kind is a different context from one
    /// of another kind with the same <see cref="Id"/>, and a percentage rollout buckets it by both,
    /// so the two can land in different buckets.
    /// </summary>
    /// <remarks>
    /// A kind has 1 to 64 characters, each a lowercase ASCII letter, a digit, <c>-</c> or <c>_</c>,
    /// and is used exactly as written. Setting any other kind never throws: evaluating with it logs
    /// an error and evaluates as if no context had been supplied.
    /// </remarks>
    public string? Kind { get; init; }

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
