using System.Globalization;

namespace ConfigDirector.Evaluation;

internal static class JsonPointer
{
    // RFC 6901. A pointer that addresses nothing resolves to null, which the evaluator treats the
    // same as a trait the context never carried.
    internal static TraitValue Resolve(string? pointer, in TraitValue document)
    {
        if (string.IsNullOrEmpty(pointer) || pointer![0] != '/')
        {
            return TraitValue.Null;
        }

        var current = document;
        var from = 1;
        while (true)
        {
            var separator = pointer.IndexOf('/', from);
            var end = separator < 0 ? pointer.Length : separator;
            if (!TryStep(current, Unescape(pointer.Substring(from, end - from)), out current))
            {
                return TraitValue.Null;
            }

            if (separator < 0)
            {
                return current;
            }

            from = separator + 1;
        }
    }

    private static bool TryStep(in TraitValue current, string token, out TraitValue next)
    {
        switch (current.Kind)
        {
            case TraitValueKind.Object:
                return current.TryGetMember(token, out next);
            case TraitValueKind.Array:
                // RFC 6901 indexes are unsigned decimals, so a sign or padding addresses nothing.
                return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    ? current.TryGetElement(index, out next)
                    : Missing(out next);
            default:
                return Missing(out next);
        }
    }

    private static bool Missing(out TraitValue next)
    {
        next = TraitValue.Null;
        return false;
    }

    private static string Unescape(string token) =>
        // "~1" before "~0", so that "~01" unescapes to "~1" rather than to "/".
        token.IndexOf('~') < 0 ? token : token.Replace("~1", "/").Replace("~0", "~");
}
