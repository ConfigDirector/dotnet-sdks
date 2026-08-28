using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConfigDirector.Value;

// Coerces an evaluated value into the type the caller asked for. The requested type comes from the
// default, not from how the config was declared in the dashboard: a caller who passes a bool gets a
// bool or their default back, never a string that happens to read as one.
internal static class ValueParser
{
    // No NumberStyles.AllowLeadingWhite or AllowTrailingWhite, so " 42 " is not a number, and no
    // AllowThousands, so "1,000" is not either.
    private const NumberStyles DecimalOnly =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private const NumberStyles IntegerOnly = NumberStyles.AllowLeadingSign;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // The caller has asked for binding explicitly, so System.Text.Json's rules are theirs to own.
    [RequiresUnreferencedCode(Reflective.BindingNeedsReflection)]
    [RequiresDynamicCode(Reflective.BindingNeedsReflection)]
    internal static ParseResult<T> Bind<T>(ConfigState state, T defaultValue) =>
        string.IsNullOrEmpty(state.Value)
            ? UsedDefault(defaultValue, EvaluationReason.ValueMissing)
            : ParseJson(state.Value!, state, defaultValue);

    internal static ParseResult<T> Parse<T>(ConfigState state, T defaultValue)
    {
        var raw = state.Value;
        if (string.IsNullOrEmpty(raw))
        {
            return UsedDefault(defaultValue, EvaluationReason.ValueMissing);
        }

        if (typeof(T) == typeof(string))
        {
            return Matched((T)(object)raw!, state);
        }

        if (typeof(T) == typeof(bool))
        {
            return TryParseBoolean(raw!, out var parsed)
                ? Matched((T)(object)parsed, state)
                : UsedDefault(defaultValue, EvaluationReason.InvalidBoolean);
        }

        if (typeof(T) == typeof(int) || typeof(T) == typeof(long))
        {
            return ParseWholeNumber(raw!, state, defaultValue);
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(decimal))
        {
            return ParseNumber(raw!, state, defaultValue);
        }

        if (typeof(T) == typeof(JsonElement))
        {
            return ParseElement(raw!, state, defaultValue);
        }

        // Every type the getters can be called with is handled above. Binding to a type of the
        // caller's own is what Bind is for, and it is reached only through GetJsonValue.
        return UsedDefault(defaultValue, EvaluationReason.InvalidJson);
    }

    private static bool TryParseBoolean(string raw, out bool parsed)
    {
        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;
            return true;
        }

        parsed = false;
        return string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static ParseResult<T> ParseWholeNumber<T>(string raw, ConfigState state, T defaultValue)
    {
        if (TryParseInt64(raw, out var whole))
        {
            if (typeof(T) == typeof(long))
            {
                return Matched((T)(object)whole, state);
            }

            if (whole is >= int.MinValue and <= int.MaxValue)
            {
                return Matched((T)(object)(int)whole, state);
            }
        }

        return UsedDefault(defaultValue, EvaluationReason.InvalidNumber);
    }

    private static bool TryParseInt64(string raw, out long whole)
    {
        if (long.TryParse(raw, IntegerOnly, CultureInfo.InvariantCulture, out whole))
        {
            return true;
        }

        // A whole number the server happened to write with a decimal point or an exponent, such as
        // "26.0" or "1e3".
        if (TryParseDouble(raw, out var parsed) && parsed == Math.Round(parsed) && parsed is >= -9.2e18 and <= 9.2e18)
        {
            whole = (long)parsed;
            return true;
        }

        return false;
    }

    private static ParseResult<T> ParseNumber<T>(string raw, ConfigState state, T defaultValue)
    {
        if (typeof(T) == typeof(decimal))
        {
            return decimal.TryParse(raw, DecimalOnly, CultureInfo.InvariantCulture, out var number)
                ? Matched((T)(object)number, state)
                : UsedDefault(defaultValue, EvaluationReason.InvalidNumber);
        }

        if (!TryParseDouble(raw, out var parsed))
        {
            return UsedDefault(defaultValue, EvaluationReason.InvalidNumber);
        }

        if (typeof(T) == typeof(double))
        {
            return Matched((T)(object)parsed, state);
        }

        // A double the server can hold but a float cannot, such as 1e300, overflows to infinity
        // rather than to a number the caller can use.
        var single = (float)parsed;
        return float.IsInfinity(single)
            ? UsedDefault(defaultValue, EvaluationReason.InvalidNumber)
            : Matched((T)(object)single, state);
    }

    // TryParse still admits "Infinity" and "NaN", and turns an overflow such as "1e400" into an
    // infinity rather than a failure, so finiteness is checked rather than assumed.
    private static bool TryParseDouble(string raw, out double parsed) =>
        double.TryParse(raw, DecimalOnly, CultureInfo.InvariantCulture, out parsed)
        && !double.IsNaN(parsed)
        && !double.IsInfinity(parsed);

    // JsonDocument rather than the serializer, so reading a config as JsonElement stays available to
    // a trimmed or AOT-compiled application.
    private static ParseResult<T> ParseElement<T>(string raw, ConfigState state, T defaultValue)
    {
        try
        {
            var element = JsonDocument.Parse(raw).RootElement.Clone();
            return Matched((T)(object)element, state);
        }
        catch (JsonException)
        {
            return UsedDefault(defaultValue, EvaluationReason.InvalidJson);
        }
    }

    [RequiresUnreferencedCode(Reflective.BindingNeedsReflection)]
    [RequiresDynamicCode(Reflective.BindingNeedsReflection)]
    private static ParseResult<T> ParseJson<T>(string raw, ConfigState state, T defaultValue)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(raw, JsonOptions);
            return parsed is null ? UsedDefault(defaultValue, EvaluationReason.InvalidJson) : Matched(parsed, state);
        }
        catch (JsonException)
        {
            return UsedDefault(defaultValue, EvaluationReason.InvalidJson);
        }
        catch (NotSupportedException)
        {
            // A type System.Text.Json cannot construct from JSON at all.
            return UsedDefault(defaultValue, EvaluationReason.InvalidJson);
        }
    }

    private static ParseResult<T> Matched<T>(T value, ConfigState state) =>
        new(value, EvaluationReason.FoundMatch, state.ValueId);

    private static ParseResult<T> UsedDefault<T>(T defaultValue, EvaluationReason reason) =>
        new(defaultValue, reason, null);
}
