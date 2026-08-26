using System.Globalization;

namespace ConfigDirector.Evaluation;

internal enum NumberOperator
{
    Unknown = 0,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
}

internal static class NumberComparison
{
    // SEMANTICS.md 3. Wants strict parsing, and these styles are what make it strict: no
    // NumberStyles.AllowLeadingWhite or AllowTrailingWhite, so " 42 " is not a number, and no
    // AllowThousands, so "1,000" is not either. The invariant culture keeps the decimal point a
    // point wherever the SDK runs.
    private const NumberStyles DecimalOnly =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private static readonly Dictionary<string, NumberOperator> Operators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["="] = NumberOperator.Equal,
            ["equals"] = NumberOperator.Equal,
            ["!="] = NumberOperator.NotEqual,
            ["does not equal"] = NumberOperator.NotEqual,
            ["<"] = NumberOperator.LessThan,
            ["<="] = NumberOperator.LessThanOrEqual,
            [">"] = NumberOperator.GreaterThan,
            [">="] = NumberOperator.GreaterThanOrEqual,
        };

    internal static NumberOperator Parse(string? name) =>
        name is not null && Operators.TryGetValue(name, out var parsed) ? parsed : NumberOperator.Unknown;

    // SEMANTICS.md 3. A value that will not parse resolves before the target is looked at: true for
    // "does not equal", false for everything else. A rule targeting a numeric trait some users do
    // not have should skip those users rather than be discarded.
    internal static bool Compare(in TraitValue value, NumberOperator comparison, IReadOnlyList<string> targets)
    {
        if (!TryParse(value, out var parsed))
        {
            return comparison == NumberOperator.NotEqual;
        }

        if (targets.Count == 0 || !TryParse(targets[0], out var target))
        {
            return false;
        }

        return comparison switch
        {
            NumberOperator.Equal => parsed == target,
            NumberOperator.NotEqual => parsed != target,
            NumberOperator.LessThan => parsed < target,
            NumberOperator.LessThanOrEqual => parsed <= target,
            NumberOperator.GreaterThan => parsed > target,
            NumberOperator.GreaterThanOrEqual => parsed >= target,
            _ => false,
        };
    }

    private static bool TryParse(in TraitValue value, out double parsed)
    {
        switch (value.Kind)
        {
            case TraitValueKind.Number:
                parsed = value.AsDouble();
                return IsFinite(parsed);
            case TraitValueKind.String:
                return TryParse(value.StringValue, out parsed);
            default:
                parsed = 0;
                return false;
        }
    }

    // TryParse still admits "Infinity" and "NaN", and turns an overflow such as "1e400" into an
    // infinity rather than a failure, so finiteness is checked rather than assumed.
    private static bool TryParse(string? text, out double parsed) =>
        double.TryParse(text, DecimalOnly, CultureInfo.InvariantCulture, out parsed) && IsFinite(parsed);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
