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
            if (!TryUnescape(pointer.Substring(from, end - from), out var token) || !TryStep(current, token, out current))
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
                return TryParseArrayIndex(token, current.Elements.Count, out var index)
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

    private static bool TryParseArrayIndex(string token, int arrayLength, out int index)
    {
        index = 0;
        if (token.Length == 0 || (token[0] == '0' && token.Length > 1))
        {
            return false;
        }

        long value = 0;
        foreach (var character in token)
        {
            if (character < '0' || character > '9')
            {
                return false;
            }

            value = (value * 10) + (character - '0');
            if (value >= arrayLength)
            {
                return false;
            }
        }

        index = (int)value;
        return true;
    }

    private static bool TryUnescape(string escaped, out string token)
    {
        var tilde = escaped.IndexOf('~');
        while (tilde >= 0)
        {
            if (tilde + 1 == escaped.Length || (escaped[tilde + 1] != '0' && escaped[tilde + 1] != '1'))
            {
                token = escaped;
                return false;
            }

            tilde = escaped.IndexOf('~', tilde + 2);
        }

        // "~1" before "~0", so that "~01" unescapes to "~1" rather than to "/".
        token = escaped.Replace("~1", "/").Replace("~0", "~");
        return true;
    }
}
