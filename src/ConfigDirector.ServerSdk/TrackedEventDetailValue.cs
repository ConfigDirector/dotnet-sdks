using System.Globalization;
using ConfigDirector.Telemetry;

namespace ConfigDirector;

/// <summary>
/// A value in the details of a tracked event: a string, a number, a boolean, a date, or a nested
/// object or list of such values.
/// </summary>
/// <remarks>
/// Strings, numbers, booleans, arrays of those, dates and <see cref="TrackedEventDetails"/> convert
/// implicitly, so a value can be written as <c>["seats"] = 3</c>. Build an array of anything else with
/// <see cref="FromArray"/>. A date is sent as ISO 8601 text and counts toward the 1 KB limit as that
/// text. A number that is not finite becomes <see cref="Null"/>, since JSON has no way to write it.
/// </remarks>
public readonly struct TrackedEventDetailValue : IEquatable<TrackedEventDetailValue>
{
    private TrackedEventDetailValue(object? content) => Content = content;

    /// <summary>The absence of a value, which is also what <see langword="default"/> produces.</summary>
    public static TrackedEventDetailValue Null => default;

    internal object? Content { get; }

    /// <summary>Wraps text, or <see cref="Null"/> when the text is <see langword="null"/>.</summary>
    /// <param name="value">The text to wrap.</param>
    public static implicit operator TrackedEventDetailValue(string? value) => new(value);

    /// <summary>Wraps a whole number.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventDetailValue(long value) => new(value);

    /// <summary>Wraps a number, or <see cref="Null"/> when it is not finite.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventDetailValue(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? Null : new(value);

    /// <summary>Wraps a number, or <see cref="Null"/> when it is not finite.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventDetailValue(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? Null : new(value);

    /// <summary>Wraps an exact number, which is sent with every digit it was written with.</summary>
    /// <param name="value">The number to wrap.</param>
    public static implicit operator TrackedEventDetailValue(decimal value) => new(value);

    /// <summary>Wraps a boolean.</summary>
    /// <param name="value">The boolean to wrap.</param>
    public static implicit operator TrackedEventDetailValue(bool value) => new(value);

    /// <summary>
    /// Wraps a date as ISO 8601 text, such as <c>2026-12-01T00:00:00.0000000Z</c>. A date of
    /// <see cref="DateTimeKind.Local"/> kind is sent with this machine's offset, and one of
    /// <see cref="DateTimeKind.Unspecified"/> kind with none.
    /// </summary>
    /// <param name="value">The date to wrap.</param>
    public static implicit operator TrackedEventDetailValue(DateTime value) =>
        new(value.ToString("o", CultureInfo.InvariantCulture));

    /// <summary>
    /// Wraps a date as ISO 8601 text that keeps its offset, such as
    /// <c>2026-12-01T00:00:00.0000000+02:00</c>.
    /// </summary>
    /// <param name="value">The date to wrap.</param>
    public static implicit operator TrackedEventDetailValue(DateTimeOffset value) =>
        new(value.ToString("o", CultureInfo.InvariantCulture));

    /// <summary>
    /// Wraps a copy of a nested object, or <see cref="Null"/> when it is <see langword="null"/>. A
    /// later change to <paramref name="value"/> does not reach the copy.
    /// </summary>
    /// <param name="value">The object to wrap.</param>
    public static implicit operator TrackedEventDetailValue(TrackedEventDetails? value) =>
        value is null ? Null : new(new TrackedEventDetails(value));

    /// <summary>Wraps an array, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(TrackedEventDetailValue[]? values) =>
        values is null ? Null : FromArray(values);

    /// <summary>Wraps an array of text, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(string?[]? values) =>
        FromValues(values, value => value);

    /// <summary>Wraps an array of whole numbers, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(int[]? values) =>
        FromValues(values, value => value);

    /// <summary>Wraps an array of whole numbers, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(long[]? values) =>
        FromValues(values, value => value);

    /// <summary>Wraps an array of numbers, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(double[]? values) =>
        FromValues(values, value => value);

    /// <summary>Wraps an array of exact numbers, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(decimal[]? values) =>
        FromValues(values, value => value);

    /// <summary>Wraps an array of booleans, or <see cref="Null"/> when it is <see langword="null"/>.</summary>
    /// <param name="values">The values to wrap.</param>
    public static implicit operator TrackedEventDetailValue(bool[]? values) =>
        FromValues(values, value => value);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The value on the left.</param>
    /// <param name="right">The value on the right.</param>
    /// <returns><see langword="true"/> when both carry the same content.</returns>
    public static bool operator ==(TrackedEventDetailValue left, TrackedEventDetailValue right) => left.Equals(right);

    /// <summary>Compares two values.</summary>
    /// <param name="left">The value on the left.</param>
    /// <param name="right">The value on the right.</param>
    /// <returns><see langword="true"/> when they differ.</returns>
    public static bool operator !=(TrackedEventDetailValue left, TrackedEventDetailValue right) => !left.Equals(right);

    /// <summary>Builds an array value, copying the values given.</summary>
    /// <param name="values">The values the array holds.</param>
    /// <returns>An array value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public static TrackedEventDetailValue FromArray(IEnumerable<TrackedEventDetailValue> values)
    {
        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        return new(values.ToArray());
    }

    /// <inheritdoc/>
    public bool Equals(TrackedEventDetailValue other) =>
        Content is TrackedEventDetailValue[] items && other.Content is TrackedEventDetailValue[] otherItems
            ? items.SequenceEqual(otherItems)
            : Equals(Content, other.Content);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TrackedEventDetailValue other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        Content is TrackedEventDetailValue[] items
            ? items.Aggregate(items.Length, (hash, item) => unchecked((hash * 31) + item.GetHashCode()))
            : Content?.GetHashCode() ?? 0;

    /// <summary>The value as JSON.</summary>
    /// <returns>The JSON the value is reported as.</returns>
    public override string ToString() => TrackedEventDetailsJson.Text(this);

    private static TrackedEventDetailValue FromValues<T>(T[]? values, Func<T, TrackedEventDetailValue> wrap) =>
        values is null ? Null : new(values.Select(wrap).ToArray());
}
