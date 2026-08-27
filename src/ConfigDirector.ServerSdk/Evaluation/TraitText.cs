using System.Globalization;
using ConfigDirector.Value;

namespace ConfigDirector.Evaluation;

internal static class TraitText
{
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

    // A trait spells these out where JSON has no way to, so they are handled here rather than by
    // the shared number rendering.
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

        return JsonNumberText.Render(value);
    }
}
