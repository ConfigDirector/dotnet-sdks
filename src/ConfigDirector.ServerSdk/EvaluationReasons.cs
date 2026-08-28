using System.Text;

namespace ConfigDirector;

// ConfigDirector spells an evaluation reason in kebab-case, and every SDK has to agree on that
// spelling. Built by hand rather than by a regular expression, which would be a slower way to
// walk the same characters.
internal static class EvaluationReasons
{
    private static readonly Dictionary<EvaluationReason, string> ByReason =
        Values().ToDictionary(reason => reason, reason => ToKebabCase(reason.ToString()));

    // The generic overload is the one AOT can honour; the non-generic has to build the array at
    // runtime, which a trimmed application may not be able to do.
    private static EvaluationReason[] Values() =>
#if NET8_0_OR_GREATER
        Enum.GetValues<EvaluationReason>();
#else
        ((EvaluationReason[])Enum.GetValues(typeof(EvaluationReason)));
#endif

    internal static string WireName(EvaluationReason reason) =>
        ByReason.TryGetValue(reason, out var name) ? name : ToKebabCase(reason.ToString());

    private static string ToKebabCase(string name)
    {
        var kebab = new StringBuilder(name.Length + 4);
        foreach (var character in name)
        {
            if (char.IsUpper(character) && kebab.Length > 0)
            {
                kebab.Append('-');
            }

            kebab.Append(char.ToLowerInvariant(character));
        }

        return kebab.ToString();
    }
}
