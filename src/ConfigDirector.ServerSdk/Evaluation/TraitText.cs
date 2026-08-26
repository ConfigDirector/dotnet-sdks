using System.Globalization;

namespace ConfigDirector.Evaluation;

internal static class TraitText
{
    private const double LargestPlainInteger = 1e21;

    // Every SDK must spell a value the same way, or the same trait would match a text rule in one
    // and not in another. See SEMANTICS.md 1.1 in targeting-rules-contract.
    internal static string Render(in TraitValue value) =>
        value.Kind switch
        {
            TraitValueKind.String => value.StringValue,
            TraitValueKind.Boolean => value.BooleanValue ? "true" : "false",
            TraitValueKind.Number => RenderNumber(value),
            _ => string.Empty,
        };

    private static string RenderNumber(in TraitValue value)
    {
        if (value.IsIntegral)
        {
            return value.IntegerValue.ToString(CultureInfo.InvariantCulture);
        }

        return RenderDouble(value.DoubleValue);
    }

    private static string RenderDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "NaN";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if (value == 0)
        {
            return "0";
        }

        // JSON draws no line between whole and fractional numbers, so 26.0 renders as "26". "F0"
        // rather than the round-trip format, which switches to exponent notation well before the
        // other SDKs do.
        if (value == Math.Round(value) && Math.Abs(value) < LargestPlainInteger)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.IndexOf('E') < 0 ? text : text.Replace('E', 'e');
    }
}
