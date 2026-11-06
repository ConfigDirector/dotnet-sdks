using System.Globalization;
using ConfigDirector.Value;

namespace ConfigDirector;

/// <summary>
/// A number a tracked event measures, such as an amount of money, a count or a duration.
/// </summary>
/// <remarks>
/// Whole numbers, floats, doubles and decimals convert implicitly, so a value can be written as
/// <c>Value = 3</c> or <c>Value = 49.90m</c>. ConfigDirector stores it as an exact decimal, and a
/// <see cref="decimal"/> is sent with every digit it was written with.
/// </remarks>
public readonly struct TrackedEventValue : IEquatable<TrackedEventValue>
{
    private readonly object? _number;

    private TrackedEventValue(object number) => _number = number;

    internal string? JsonNumber => JsonNumberText.RenderFinite(_number);

    /// <summary>Wraps a whole number.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventValue(long value) => new(value);

    /// <summary>Wraps a number.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventValue(float value) => new(value);

    /// <summary>Wraps a number.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventValue(double value) => new(value);

    /// <summary>Wraps an exact number, which is sent with every digit it was written with.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventValue(decimal value) => new(value);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The value on the left.</param>
    /// <param name="right">The value on the right.</param>
    /// <returns><see langword="true"/> when both hold the same number in the same type.</returns>
    public static bool operator ==(TrackedEventValue left, TrackedEventValue right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The value on the left.</param>
    /// <param name="right">The value on the right.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(TrackedEventValue left, TrackedEventValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(TrackedEventValue other) => Equals(_number, other._number);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrackedEventValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _number?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    public override string ToString() => Convert.ToString(_number, CultureInfo.InvariantCulture) ?? string.Empty;
}
