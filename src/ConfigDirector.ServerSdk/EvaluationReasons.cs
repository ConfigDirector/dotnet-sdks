using System.Text;

namespace ConfigDirector;

// ConfigDirector spells an evaluation reason in kebab-case, and every SDK has to agree on that
// spelling. Built by hand rather than by a regular expression, which would be a slower way to
// walk the same characters.
internal static class EvaluationReasons
{
    private static readonly Dictionary<EvaluationReason, string> ByReason =
        Enum.GetValues(typeof(EvaluationReason))
            .Cast<EvaluationReason>()
            .ToDictionary(reason => reason, reason => ToKebabCase(reason.ToString()));

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
