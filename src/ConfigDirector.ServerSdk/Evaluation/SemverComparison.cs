using System.Globalization;
using System.Text.RegularExpressions;

namespace ConfigDirector.Evaluation;

internal enum SemverOperator
{
    Unknown = 0,
    Equal,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    IsOneOf,
    IsNotOneOf,
}

internal static class SemverComparison
{
    // node-semver's coerce: the first run of digits that can begin a version, then up to two more
    // dot-separated components. Every repetition is bounded, so the scan stays linear.
    private static readonly Regex Coercion =
        new(@"(^|[^\d])(\d{1,16})(?:\.(\d{1,16}))?(?:\.(\d{1,16}))?(?:$|[^\d])", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, SemverOperator> Operators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["="] = SemverOperator.Equal,
            ["<"] = SemverOperator.LessThan,
            ["<="] = SemverOperator.LessThanOrEqual,
            [">"] = SemverOperator.GreaterThan,
            [">="] = SemverOperator.GreaterThanOrEqual,
            ["is one of"] = SemverOperator.IsOneOf,
            ["is not one of"] = SemverOperator.IsNotOneOf,
        };

    internal static SemverOperator Parse(string? name) =>
        name is not null && Operators.TryGetValue(name, out var parsed) ? parsed : SemverOperator.Unknown;

    // SEMANTICS.md 4. Both sides are coerced, so "1.0" and "v2.3.4" are usable and any prerelease
    // or build suffix is dropped. An operand that will not coerce never compares equal and never
    // satisfies an ordering, which leaves "is NOT one of" the only operator it can make true.
    internal static bool Compare(string value, SemverOperator comparison, IReadOnlyList<string> targets)
    {
        var parsed = Coerce(value);

        switch (comparison)
        {
            case SemverOperator.IsOneOf:
                return IsOneOf(parsed, targets);
            case SemverOperator.IsNotOneOf:
                return !IsOneOf(parsed, targets);
            case SemverOperator.Unknown:
                return false;
        }

        if (parsed is null || targets.Count == 0 || Coerce(targets[0]) is not { } target)
        {
            return false;
        }

        var ordering = Compare(parsed.Value, target);

        return comparison switch
        {
            SemverOperator.Equal => ordering == 0,
            SemverOperator.LessThan => ordering < 0,
            SemverOperator.LessThanOrEqual => ordering <= 0,
            SemverOperator.GreaterThan => ordering > 0,
            SemverOperator.GreaterThanOrEqual => ordering >= 0,
            _ => false,
        };
    }

    private static bool IsOneOf(Version? value, IReadOnlyList<string> targets)
    {
        if (value is null)
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (Coerce(target) is { } coerced && Compare(value.Value, coerced) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static Version? Coerce(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var match = Coercion.Match(value);
        if (!match.Success)
        {
            return null;
        }

        return new Version(Component(match, 2), Component(match, 3), Component(match, 4));
    }

    // Components run to 16 digits, which overflows an int.
    private static long Component(Match match, int group) =>
        match.Groups[group].Success ? long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) : 0;

    private static int Compare(Version left, Version right)
    {
        var major = left.Major.CompareTo(right.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = left.Minor.CompareTo(right.Minor);
        return minor != 0 ? minor : left.Patch.CompareTo(right.Patch);
    }

    private readonly record struct Version(long Major, long Minor, long Patch);
}
