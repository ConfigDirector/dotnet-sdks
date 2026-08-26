namespace ConfigDirector.Evaluation;

internal enum TextOperator
{
    Unknown = 0,
    Equal,
    NotEqual,
    IsOneOf,
    IsNotOneOf,
    StartsWithAnyOf,
    DoesNotStartWithAnyOf,
    EndsWithAnyOf,
    DoesNotEndWithAnyOf,
}

internal static class TextComparison
{
    private static readonly Dictionary<string, TextOperator> Operators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["="] = TextOperator.Equal,
            ["equals"] = TextOperator.Equal,
            ["!="] = TextOperator.NotEqual,
            ["does not equal"] = TextOperator.NotEqual,
            ["is one of"] = TextOperator.IsOneOf,
            ["is not one of"] = TextOperator.IsNotOneOf,
            ["starts with any of"] = TextOperator.StartsWithAnyOf,
            ["does not start with any of"] = TextOperator.DoesNotStartWithAnyOf,
            ["ends with any of"] = TextOperator.EndsWithAnyOf,
            ["does not end with any of"] = TextOperator.DoesNotEndWithAnyOf,
        };

    internal static TextOperator Parse(string? name) =>
        name is not null && Operators.TryGetValue(name, out var parsed) ? parsed : TextOperator.Unknown;

    // SEMANTICS.md 2. The value is already rendered, so an absent attribute arrives as "". An empty
    // target list leaves the "any of" negatives vacuously true and everything else false.
    internal static bool Compare(string value, TextOperator comparison, IReadOnlyList<string> targets)
    {
        var first = targets.Count > 0 ? targets[0] : null;

        return comparison switch
        {
            TextOperator.Equal => first is not null && string.Equals(value, first, StringComparison.Ordinal),
            TextOperator.NotEqual => first is not null && !string.Equals(value, first, StringComparison.Ordinal),
            TextOperator.IsOneOf => IsOneOf(value, targets),
            TextOperator.IsNotOneOf => !IsOneOf(value, targets),
            TextOperator.StartsWithAnyOf => StartsWithAny(value, targets),
            TextOperator.DoesNotStartWithAnyOf => !StartsWithAny(value, targets),
            TextOperator.EndsWithAnyOf => EndsWithAny(value, targets),
            TextOperator.DoesNotEndWithAnyOf => !EndsWithAny(value, targets),
            _ => false,
        };
    }

    private static bool IsOneOf(string value, IReadOnlyList<string> targets)
    {
        foreach (var target in targets)
        {
            if (string.Equals(value, target, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWithAny(string value, IReadOnlyList<string> targets)
    {
        foreach (var target in targets)
        {
            if (value.StartsWith(target, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EndsWithAny(string value, IReadOnlyList<string> targets)
    {
        foreach (var target in targets)
        {
            if (value.EndsWith(target, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
