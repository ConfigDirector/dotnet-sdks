namespace ConfigDirector;

internal static class ContextKinds
{
    internal const string User = "user";

    private const int MaxLength = 64;

    private const int SampleLength = 128;

    internal static string EffectiveKind(Context? context) => context?.Kind ?? User;

    internal static bool IsValid(string kind)
    {
        if (kind.Length is 0 or > MaxLength)
        {
            return false;
        }

        foreach (var character in kind)
        {
            if (!IsAllowed(character))
            {
                return false;
            }
        }

        return true;
    }

    internal static string Sample(string kind)
    {
        var end = 0;
        for (var taken = 0; taken < SampleLength && end < kind.Length; taken++)
        {
            end += IsSurrogatePairAt(kind, end) ? 2 : 1;
        }

        return end == kind.Length ? kind : kind.Substring(0, end);
    }

    private static bool IsSurrogatePairAt(string text, int index) =>
        index + 1 < text.Length && char.IsSurrogatePair(text[index], text[index + 1]);

    private static bool IsAllowed(char character) =>
        character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
}
