namespace ConfigDirector.Evaluation;

internal enum ArrayOperator
{
    Unknown = 0,
    ContainsAnyOf,
    DoesNotContainAnyOf,
}

internal static class ArrayComparison
{
    private static readonly Dictionary<string, ArrayOperator> Operators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contains any of"] = ArrayOperator.ContainsAnyOf,
            ["does not contain any of"] = ArrayOperator.DoesNotContainAnyOf,
        };

    internal static ArrayOperator Parse(string? name) =>
        name is not null && Operators.TryGetValue(name, out var parsed) ? parsed : ArrayOperator.Unknown;

    // SEMANTICS.md 6. A value that is not an array, a comma-separated string included, contains
    // nothing, so only the negative operator can be true for it.
    internal static bool Compare(in TraitValue value, ArrayOperator comparison, IReadOnlyList<string> targets)
    {
        if (value.Kind != TraitValueKind.Array)
        {
            return comparison == ArrayOperator.DoesNotContainAnyOf;
        }

        return comparison switch
        {
            ArrayOperator.ContainsAnyOf => ContainsAny(value, targets),
            ArrayOperator.DoesNotContainAnyOf => !ContainsAny(value, targets),
            _ => false,
        };
    }

    private static bool ContainsAny(in TraitValue value, IReadOnlyList<string> targets)
    {
        foreach (var element in value.Elements)
        {
            // SEMANTICS.md 1.3. Nested arrays, objects and nulls have no text form, so they are
            // dropped rather than matching an empty target value.
            if (element.Kind is not (TraitValueKind.String or TraitValueKind.Number or TraitValueKind.Boolean))
            {
                continue;
            }

            var text = TraitText.Render(element);
            foreach (var target in targets)
            {
                if (string.Equals(text, target, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
