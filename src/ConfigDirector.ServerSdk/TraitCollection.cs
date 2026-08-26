using System.Collections;

namespace ConfigDirector;

/// <summary>
/// The traits a <see cref="Context"/> carries, keyed by the names targeting rules reference.
/// </summary>
/// <remarks>
/// Written inline when a context is built, so a trait can be spelled the way JSON would spell it:
/// <code>
/// new Context
/// {
///     Id = "user-1",
///     Traits =
///     {
///         ["plan"] = "pro",
///         ["age"] = 26,
///         ["tags"] = new[] { "beta", "internal" },
///     },
/// }
/// </code>
/// Names are matched exactly, as JSON member names are.
/// </remarks>
public sealed class TraitCollection : IReadOnlyDictionary<string, TraitValue>, IEquatable<TraitCollection>
{
    private readonly Dictionary<string, TraitValue> _members;

    /// <summary>Starts an empty collection.</summary>
    public TraitCollection() => _members = new Dictionary<string, TraitValue>(StringComparer.Ordinal);

    /// <summary>Starts a collection holding a copy of the members given.</summary>
    /// <param name="members">The traits to copy. A repeated name keeps the last value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is <see langword="null"/>.</exception>
    public TraitCollection(IEnumerable<KeyValuePair<string, TraitValue>> members)
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

    /// <summary>How many traits the collection carries.</summary>
    public int Count => _members.Count;

    /// <summary>The names of every trait carried.</summary>
    public IEnumerable<string> Keys => _members.Keys;

    /// <summary>The value of every trait carried.</summary>
    public IEnumerable<TraitValue> Values => _members.Values;

    /// <summary>Reads or writes one trait.</summary>
    /// <param name="name">The trait's name, as targeting rules reference it.</param>
    /// <returns>The trait's value.</returns>
    /// <exception cref="KeyNotFoundException">Reading a name the collection does not carry.</exception>
    public TraitValue this[string name]
    {
        get => _members[name];
        set => _members[name] = value;
    }

    /// <summary>Adds one trait.</summary>
    /// <param name="name">The trait's name, as targeting rules reference it.</param>
    /// <param name="value">A JSON-shaped value. Strings, numbers, and booleans convert implicitly.</param>
    /// <exception cref="ArgumentException">The collection already carries that name.</exception>
    public void Add(string name, TraitValue value) => _members.Add(name, value);

    /// <summary>Whether the collection carries a trait by that name.</summary>
    /// <param name="name">The name to look for, matched exactly.</param>
    /// <returns><see langword="true"/> when the trait is present.</returns>
    public bool ContainsKey(string name) => _members.ContainsKey(name);

    /// <summary>Reads one trait, if it is there.</summary>
    /// <param name="name">The trait's name, matched exactly.</param>
    /// <param name="value">The trait's value, or null when there is no such trait.</param>
    /// <returns><see langword="true"/> when the trait is present.</returns>
    public bool TryGetValue(string name, out TraitValue value) => _members.TryGetValue(name, out value);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, TraitValue>> GetEnumerator() => _members.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Wraps traits as a value, so an object can be nested inside another trait.</summary>
    /// <param name="members">The traits to wrap.</param>
    public static implicit operator TraitValue(TraitCollection? members) =>
        members is null ? TraitValue.Null : TraitValue.WrapObject(members);

    /// <summary>Copies a dictionary of traits into a collection.</summary>
    /// <param name="members">The traits to copy.</param>
    public static implicit operator TraitCollection(Dictionary<string, TraitValue>? members) =>
        members is null ? [] : new TraitCollection(members);

    /// <inheritdoc/>
    public bool Equals(TraitCollection? other)
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
    public override bool Equals(object? obj) => Equals(obj as TraitCollection);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // Traits have no order, so each one's contribution must not depend on where it sits.
        var hash = 0;
        foreach (var member in _members)
        {
            hash ^= unchecked((StringComparer.Ordinal.GetHashCode(member.Key) * 31) + member.Value.GetHashCode());
        }

        return hash;
    }
}
