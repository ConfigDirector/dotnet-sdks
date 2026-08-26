using System.Globalization;
using System.Text.RegularExpressions;

namespace ConfigDirector.Evaluation;

internal enum DateTimeOperator
{
    Unknown = 0,
    IsBefore,
    IsAfter,
}

internal static class DateTimeComparison
{
    // The ECMAScript Date Time String Format, which is what the ConfigDirector dashboard emits.
    // Anchored, and every repetition but the fraction is a fixed width, so the scan stays linear.
    private static readonly Regex Format = new(
        @"^(?<year>[+-]\d{6}|\d{4})(?:-(?<month>\d{2})(?:-(?<day>\d{2}))?)?" +
        @"(?:T(?<hour>\d{2}):(?<minute>\d{2})(?::(?<second>\d{2})(?:\.(?<fraction>\d+))?)?" +
        @"(?<offset>[Zz]|[+-]\d{2}:\d{2})?)?$",
        RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, DateTimeOperator> Operators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["is before"] = DateTimeOperator.IsBefore,
            ["is after"] = DateTimeOperator.IsAfter,
        };

    internal static DateTimeOperator Parse(string? name) =>
        name is not null && Operators.TryGetValue(name, out var parsed) ? parsed : DateTimeOperator.Unknown;

    // SEMANTICS.md 5. Either side being unparseable makes the comparison false.
    internal static bool Compare(string value, DateTimeOperator comparison, IReadOnlyList<string> targets)
    {
        if (targets.Count == 0 || TryParse(value) is not { } left || TryParse(targets[0]) is not { } right)
        {
            return false;
        }

        return comparison switch
        {
            DateTimeOperator.IsBefore => left < right,
            DateTimeOperator.IsAfter => left > right,
            _ => false,
        };
    }

    private static DateTimeOffset? TryParse(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = Format.Match(value);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(
                int.Parse(match.Groups["year"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                Component(match, "month", 1),
                Component(match, "day", 1),
                Component(match, "hour", 0),
                Component(match, "minute", 0),
                Component(match, "second", 0),
                Milliseconds(match.Groups["fraction"].Value),
                Offset(match.Groups["offset"].Value));
        }
        catch (ArgumentException)
        {
            // A component the format allows but the calendar does not: month 13, February 30, an
            // offset beyond 14 hours, a year outside the representable range.
            return null;
        }
    }

    private static int Component(Match match, string name, int fallback) =>
        match.Groups[name].Success
            ? int.Parse(match.Groups[name].Value, NumberStyles.None, CultureInfo.InvariantCulture)
            : fallback;

    // SEMANTICS.md 5. Precision is milliseconds, and further digits are truncated, not rounded.
    private static int Milliseconds(string fraction)
    {
        var milliseconds = 0;
        for (var index = 0; index < 3; index++)
        {
            milliseconds = (milliseconds * 10) + (index < fraction.Length ? Digit(fraction, index) : 0);
        }

        return milliseconds;
    }

    // An offsetless date-time is UTC, not local: an evaluation must not depend on the timezone of
    // whichever machine happens to be running it.
    private static TimeSpan Offset(string offset)
    {
        if (offset.Length == 0 || offset is "Z" or "z")
        {
            return TimeSpan.Zero;
        }

        var magnitude = new TimeSpan(TwoDigits(offset, 1), TwoDigits(offset, 4), 0);
        return offset[0] == '-' ? magnitude.Negate() : magnitude;
    }

    // Every digit here was matched by the format, so no validation is left to do.
    private static int TwoDigits(string text, int index) => (Digit(text, index) * 10) + Digit(text, index + 1);

    private static int Digit(string text, int index) => text[index] - '0';
}
