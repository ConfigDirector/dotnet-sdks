using System.Globalization;

namespace ConfigDirector.Value;

// How a finite number is spelled as JSON. Shared by trait rendering and by telemetry, which agree
// on every number they can both be given and differ only on the ones JSON cannot spell at all.
internal static class JsonNumberText
{
    private const double LargestPlainInteger = 1e21;

    internal static string? RenderFinite(object? number) =>
        number switch
        {
            long whole => whole.ToString(CultureInfo.InvariantCulture),
            decimal exact => exact.ToString(CultureInfo.InvariantCulture),
            double value when !double.IsNaN(value) && !double.IsInfinity(value) => Render(value),
            float value when !float.IsNaN(value) && !float.IsInfinity(value) => Render(value),
            _ => null,
        };

    internal static string Render(double value)
    {
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

        return Exponent(value.ToString("R", CultureInfo.InvariantCulture));
    }

    // Widening to a double first would spell 0.1f as 0.10000000149011612.
    internal static string Render(float value)
    {
        if (value == 0)
        {
            return "0";
        }

        if (value == Math.Round(value) && Math.Abs(value) < LargestPlainInteger)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }

        return Exponent(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static string Exponent(string text) =>
        text.IndexOf('E') < 0 ? text : text.Replace('E', 'e');
}
