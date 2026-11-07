using System.Collections;
using System.Diagnostics.CodeAnalysis;
using ConfigDirector.Telemetry;

namespace ConfigDirector;

/// <summary>
/// Details of a tracked event, such as the plan a customer chose.
/// </summary>
/// <remarks>
/// Written inline, the way a context's traits are:
/// <code>
/// new TrackOptions
/// {
///     Details =
///     {
///         ["plan"] = "pro",
///         ["seats"] = 3,
///         ["tags"] = new[] { "beta", "internal" },
///         ["address"] = new TrackedEventDetails { ["city"] = "Lisbon" },
///     },
/// }
/// </code>
/// Details with more than 20 top-level keys, or larger than 1 KB as JSON, are left out and the event
/// is still reported.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "Every ConfigDirector SDK names this type TrackedEventDetails.")]
public sealed class TrackedEventDetails : IReadOnlyDictionary<string, TrackedEventDetailValue>, IEquatable<TrackedEventDetails>
{
    private readonly Dictionary<string, TrackedEventDetailValue> _members;

    /// <summary>Starts empty details.</summary>
    public TrackedEventDetails() => _members = new Dictionary<string, TrackedEventDetailValue>(StringComparer.Ordinal);

    /// <summary>Starts details holding a copy of the members given.</summary>
    /// <param name="members">The details to copy. A repeated name keeps the last value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is <see langword="null"/>.</exception>
    public TrackedEventDetails(IEnumerable<KeyValuePair<string, TrackedEventDetailValue>> members)
        : this()
    {
        if (members is null)
        {
            throw new ArgumentNullException(nameof(members));
        }

        foreach (var member in members)
        {
            _members[member.Key] = member.Value;
        }
    }

    /// <summary>How many top-level keys the details hold.</summary>
    public int Count => _members.Count;

    /// <summary>The top-level keys.</summary>
    public IEnumerable<string> Keys => _members.Keys;

    /// <summary>The value of every top-level key.</summary>
    public IEnumerable<TrackedEventDetailValue> Values => _members.Values;

    /// <summary>Reads or writes one top-level key.</summary>
    /// <param name="name">The key.</param>
    /// <returns>The key's value.</returns>
    /// <exception cref="KeyNotFoundException">Reading a key the details do not hold.</exception>
    public TrackedEventDetailValue this[string name]
    {
        get => _members[name];
        set => _members[name] = value;
    }

    /// <summary>Copies a dictionary of details.</summary>
    /// <param name="members">The details to copy.</param>
    public static implicit operator TrackedEventDetails(Dictionary<string, TrackedEventDetailValue>? members) =>
        members is null ? [] : new TrackedEventDetails(members);

    /// <summary>Adds one top-level key.</summary>
    /// <param name="name">The key.</param>
    /// <param name="value">The key's value. Strings, numbers, booleans, arrays of those, and dates convert implicitly.</param>
    /// <exception cref="ArgumentException">The details already hold that key.</exception>
    public void Add(string name, TrackedEventDetailValue value) => _members.Add(name, value);

    /// <summary>Whether the details hold a top-level key by that name.</summary>
    /// <param name="name">The key to look for, matched exactly.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool ContainsKey(string name) => _members.ContainsKey(name);

    /// <summary>Reads one top-level key, if it is there.</summary>
    /// <param name="name">The key, matched exactly.</param>
    /// <param name="value">The key's value, or null when there is no such key.</param>
    /// <returns><see langword="true"/> when the key is present.</returns>
    public bool TryGetValue(string name, out TrackedEventDetailValue value) => _members.TryGetValue(name, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, TrackedEventDetailValue>> GetEnumerator() => _members.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public bool Equals(TrackedEventDetails? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other.Count != Count)
        {
            return false;
        }

        foreach (var member in _members)
        {
            if (!other.TryGetValue(member.Key, out var value) || !member.Value.Equals(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TrackedEventDetails);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var member in _members)
        {
            hash ^= unchecked((StringComparer.Ordinal.GetHashCode(member.Key) * 31) + member.Value.GetHashCode());
        }

        return hash;
    }

    /// <summary>The details as JSON.</summary>
    /// <returns>The JSON the details are reported as.</returns>
    public override string ToString() => TrackedEventDetailsJson.Text(this);
}
