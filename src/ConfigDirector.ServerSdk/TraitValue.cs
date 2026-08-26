namespace ConfigDirector;

/// <summary>
/// A JSON-shaped value a context carries as a trait, and that targeting rules are evaluated
/// against.
/// </summary>
/// <remarks>
/// Strings, numbers, and booleans convert implicitly, so a trait can be written as
/// <c>["age"] = 26</c>. Lists and nested objects are built with <see cref="FromArray"/> and
/// <see cref="FromObject"/>. A value with no text form -- null, a list, a nested object -- simply
/// does not match a rule that compares text.
/// </remarks>
public readonly struct TraitValue : IEquatable<TraitValue>
{
    private readonly object? _reference;
    private readonly double _double;
    private readonly long _integer;
    private readonly bool _boolean;
    private readonly bool _isIntegral;
    private readonly TraitValueKind _kind;

    private TraitValue(TraitValueKind kind, object? reference)
    {
        _kind = kind;
        _reference = reference;
    }

    private TraitValue(long value)
    {
        _kind = TraitValueKind.Number;
        _integer = value;
        _isIntegral = true;
    }

    private TraitValue(double value)
    {
        _kind = TraitValueKind.Number;
        _double = value;
    }

    private TraitValue(bool value)
    {
        _kind = TraitValueKind.Boolean;
        _boolean = value;
    }

    /// <summary>The absence of a value, which is also what <see langword="default"/> produces.</summary>
    public static TraitValue Null => default;

    /// <summary>The JSON shape this value carries.</summary>
    public TraitValueKind Kind => _kind;

    /// <summary>
    /// The values of an array, or an empty list for anything else.
    /// </summary>
    public IReadOnlyList<TraitValue> Elements => _reference as TraitValue[] ?? [];

    internal IReadOnlyDictionary<string, TraitValue>? Members =>
        _reference as IReadOnlyDictionary<string, TraitValue>;

    internal string StringValue => (string)_reference!;

    internal bool BooleanValue => _boolean;

    internal bool IsIntegral => _isIntegral;

    internal long IntegerValue => _integer;

    internal double DoubleValue => _double;

    /// <summary>Builds an array value, copying the values given.</summary>
    /// <param name="values">The values the array holds.</param>
    /// <returns>An array value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public static TraitValue FromArray(IEnumerable<TraitValue> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return new TraitValue(TraitValueKind.Array, values.ToArray());
    }

    /// <summary>Builds an object value, copying the members given.</summary>
    /// <param name="members">The named values the object holds. A repeated name keeps the last value.</param>
    /// <returns>An object value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is <see langword="null"/>.</exception>
    public static TraitValue FromObject(IEnumerable<KeyValuePair<string, TraitValue>> members)
    {
        if (members is null)
        {
            throw new ArgumentNullException(nameof(members));
        }

        var copy = new Dictionary<string, TraitValue>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            copy[member.Key] = member.Value;
        }

        return new TraitValue(TraitValueKind.Object, copy);
    }

    /// <summary>Reads a member of an object value.</summary>
    /// <param name="name">The member's name, matched exactly.</param>
    /// <param name="value">The member's value, or <see cref="Null"/> when there is no such member.</param>
    /// <returns><see langword="true"/> when the value is an object carrying that member.</returns>
    public bool TryGetMember(string name, out TraitValue value)
    {
        if (Members is { } members && members.TryGetValue(name, out value))
        {
            return true;
        }

        value = Null;
        return false;
    }

    /// <summary>Reads an element of an array value.</summary>
    /// <param name="index">The element's position, counted from zero.</param>
    /// <param name="value">The element, or <see cref="Null"/> when there is no element there.</param>
    /// <returns><see langword="true"/> when the value is an array holding that many elements.</returns>
    public bool TryGetElement(int index, out TraitValue value)
    {
        var elements = Elements;
        if (index >= 0 && index < elements.Count)
        {
            value = elements[index];
            return true;
        }

        value = Null;
        return false;
    }

    /// <summary>Wraps text, or <see cref="Null"/> when the text is <see langword="null"/>.</summary>
    /// <param name="value">The text to wrap.</param>
    public static implicit operator TraitValue(string? value) =>
        value is null ? Null : new TraitValue(TraitValueKind.String, value);

    /// <summary>Wraps a whole number.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TraitValue(long value) => new(value);

    /// <summary>Wraps a number.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TraitValue(double value) => new(value);

    /// <summary>Wraps a boolean.</summary>
    /// <param name="value">The boolean to wrap.</param>
    public static implicit operator TraitValue(bool value) => new(value);

    /// <summary>Wraps an array, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TraitValue(TraitValue[]? values) =>
        values is null ? Null : FromArray(values);

    /// <inheritdoc/>
    public bool Equals(TraitValue other)
    {
        if (_kind != other._kind)
        {
            return false;
        }

        return _kind switch
        {
            TraitValueKind.Null => true,
            TraitValueKind.String => string.Equals(StringValue, other.StringValue, StringComparison.Ordinal),
            TraitValueKind.Boolean => _boolean == other._boolean,
            TraitValueKind.Number => NumbersEqual(this, other),
            TraitValueKind.Array => ElementsEqual(Elements, other.Elements),
            TraitValueKind.Object => MembersEqual(Members!, other.Members!),
            _ => false,
        };
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TraitValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = (int)_kind;
        switch (_kind)
        {
            case TraitValueKind.String:
                return Combine(hash, StringComparer.Ordinal.GetHashCode(StringValue));
            case TraitValueKind.Boolean:
                return Combine(hash, _boolean.GetHashCode());
            case TraitValueKind.Number:
                return Combine(hash, AsDouble().GetHashCode());
            case TraitValueKind.Array:
                foreach (var element in Elements)
                {
                    hash = Combine(hash, element.GetHashCode());
                }

                return hash;
            case TraitValueKind.Object:
                // Members have no order, so each one's contribution must not depend on where it sits.
                var members = 0;
                foreach (var member in Members!)
                {
                    members ^= Combine(StringComparer.Ordinal.GetHashCode(member.Key), member.Value.GetHashCode());
                }

                return Combine(hash, members);
            default:
                return hash;
        }
    }

    /// <summary>Compares two values.</summary>
    /// <param name="left">The value on the left.</param>
    /// <param name="right">The value on the right.</param>
    /// <returns><see langword="true"/> when both carry the same JSON shape and content.</returns>
    public static bool operator ==(TraitValue left, TraitValue right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The value on the left.</param>
    /// <param name="right">The value on the right.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(TraitValue left, TraitValue right) => !left.Equals(right);

    internal double AsDouble() => _isIntegral ? _integer : _double;

    private static bool NumbersEqual(in TraitValue left, in TraitValue right) =>
        left._isIntegral && right._isIntegral
            ? left._integer == right._integer
            : left.AsDouble().Equals(right.AsDouble());

    private static bool ElementsEqual(IReadOnlyList<TraitValue> left, IReadOnlyList<TraitValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MembersEqual(
        IReadOnlyDictionary<string, TraitValue> left,
        IReadOnlyDictionary<string, TraitValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var member in left)
        {
            if (!right.TryGetValue(member.Key, out var other) || !member.Value.Equals(other))
            {
                return false;
            }
        }

        return true;
    }

    private static int Combine(int hash, int value) => unchecked((hash * 31) + value);
}
