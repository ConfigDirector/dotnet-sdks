using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class JsonPointerTests
{
    private static readonly TraitValue Document = Object(
        ("plan", "pro"),
        ("", "empty key"),
        ("a/b", "slash key"),
        ("m~n", "tilde key"),
        ("account", Object(("tier", 2), ("tags", TraitValue.FromArray(["x", "y"])))),
        ("nothing", TraitValue.Null));

    [Theory]
    [InlineData("/plan", "pro")]
    [InlineData("/account/tier", 2L)]
    [InlineData("/account/tags/0", "x")]
    [InlineData("/account/tags/1", "y")]
    [InlineData("/", "empty key")]
    [InlineData("/a~1b", "slash key")]
    [InlineData("/m~0n", "tilde key")]
    public void ResolvesAPointerToItsValue(string pointer, object expected) =>
        JsonPointer.Resolve(pointer, Document).ShouldBe(ToTraitValue(expected));

    [Theory]
    [InlineData("/missing")]
    [InlineData("/account/missing")]
    [InlineData("/nothing")]
    [InlineData("/account/tags/2")]
    [InlineData("/account/tags/-1")]
    [InlineData("/account/tags/+0")]
    [InlineData("/account/tags/ 0")]
    [InlineData("/account/tags/last")]
    [InlineData("/plan/deeper")]
    [InlineData("/nothing/deeper")]
    [InlineData("")]
    [InlineData("plan")]
    [InlineData("#/plan")]
    [InlineData(null)]
    public void ResolvesAnythingElseToNull(string? pointer) =>
        JsonPointer.Resolve(pointer, Document).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void UnescapesTildeBeforeSlash()
    {
        var document = Object(("~1", "literal tilde one"));

        JsonPointer.Resolve("/~01", document).ShouldBe((TraitValue)"literal tilde one");
    }

    [Fact]
    public void ResolvesATrailingEmptyToken()
    {
        var document = Object(("account", Object(("", "unnamed"))));

        JsonPointer.Resolve("/account/", document).ShouldBe((TraitValue)"unnamed");
    }

    [Fact]
    public void ResolvesAgainstANonObjectDocumentToNull() =>
        JsonPointer.Resolve("/plan", "not an object").Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesALoneTildeToNull() =>
        JsonPointer.Resolve("/~", Object(("~", "literal tilde"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesATildeFollowedByAnotherDigitToNull() =>
        JsonPointer.Resolve("/~2", Object(("~2", "literal tilde two"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesATildeFollowedByALetterToNull() =>
        JsonPointer.Resolve("/~a", Object(("~a", "literal tilde a"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesATrailingTildeToNull() =>
        JsonPointer.Resolve("/a~", Object(("a~", "literal trailing tilde"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesTwoConsecutiveTildesToNull() =>
        JsonPointer.Resolve("/~~", Object(("~~", "literal double tilde"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesATrailingTildeBeforeAnotherTokenToNull()
    {
        var document = Object(("a~", Object(("b", "nested under trailing tilde"))));

        JsonPointer.Resolve("/a~/b", document).Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ResolvesAnInvalidEscapeInALaterTokenToNull()
    {
        var document = Object(("a", Object(("~2", "nested tilde two"))));

        JsonPointer.Resolve("/a/~2", document).Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ResolvesAnInvalidEscapeInAnEarlierTokenToNull()
    {
        var document = Object(("~2", Object(("b", "under tilde two"))));

        JsonPointer.Resolve("/~2/b", document).Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ResolvesAnInvalidEscapeAfterAValidOneToNull() =>
        JsonPointer.Resolve("/~0~2", Object(("~~2", "tilde then tilde two"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnInvalidEscapeBeforeAValidOneToNull() =>
        JsonPointer.Resolve("/~2~1", Object(("~2/", "tilde two then slash"))).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void UnescapesTildeZeroToTilde() =>
        JsonPointer.Resolve("/~0", Object(("~", "tilde"))).ShouldBe((TraitValue)"tilde");

    [Fact]
    public void UnescapesTildeOneToSlash() =>
        JsonPointer.Resolve("/~1", Object(("/", "slash"))).ShouldBe((TraitValue)"slash");

    [Fact]
    public void UnescapesTildeOneZeroToSlashZero()
    {
        var document = Object(("/0", "slash zero"), ("~0", "tilde zero"));

        JsonPointer.Resolve("/~10", document).ShouldBe((TraitValue)"slash zero");
    }

    [Fact]
    public void UnescapesRepeatedTildeEscapes() =>
        JsonPointer.Resolve("/~0~0", Object(("~~", "two tildes"))).ShouldBe((TraitValue)"two tildes");

    [Fact]
    public void UnescapesRepeatedSlashEscapes() =>
        JsonPointer.Resolve("/~1~1", Object(("//", "two slashes"))).ShouldBe((TraitValue)"two slashes");

    [Fact]
    public void UnescapesMixedEscapesInOrder() =>
        JsonPointer.Resolve("/~0~1", Object(("~/", "tilde slash"))).ShouldBe((TraitValue)"tilde slash");

    [Fact]
    public void UnescapesEachTokenOfANestedPointer()
    {
        var document = Object(("a/b", Object(("m~n", "nested escapes"))));

        JsonPointer.Resolve("/a~1b/m~0n", document).ShouldBe((TraitValue)"nested escapes");
    }

    [Fact]
    public void ResolvesIndexZero() =>
        JsonPointer.Resolve("/list/0", ElevenElements).ShouldBe((TraitValue)"element 0");

    [Fact]
    public void ResolvesAMultiDigitIndex() =>
        JsonPointer.Resolve("/list/10", ElevenElements).ShouldBe((TraitValue)"element 10");

    [Fact]
    public void ResolvesAnIndexWithALeadingZeroToNull() =>
        JsonPointer.Resolve("/list/01", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesARepeatedZeroIndexToNull() =>
        JsonPointer.Resolve("/list/00", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAMultiDigitIndexWithALeadingZeroToNull() =>
        JsonPointer.Resolve("/list/010", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexEqualToTheLengthToNull() =>
        JsonPointer.Resolve("/list/11", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesTheLargestIntIndexToNull() =>
        JsonPointer.Resolve("/list/2147483647", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexJustBeyondIntToNull() =>
        JsonPointer.Resolve("/list/2147483648", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexThatWrapsAroundIntToOneToNull() =>
        JsonPointer.Resolve("/list/4294967297", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexThatWrapsAroundLongToOneToNull() =>
        JsonPointer.Resolve("/list/18446744073709551617", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexBeyondLongToNull() =>
        JsonPointer.Resolve("/list/99999999999999999999999", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnArabicIndicDigitIndexToNull() =>
        JsonPointer.Resolve("/list/\u0661", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnArabicIndicDigitIndexInALongArrayToNull()
    {
        var document = Object(("list", TraitValue.FromArray(Enumerable.Range(0, 2000).Select(position => (TraitValue)(long)position))));

        JsonPointer.Resolve("/list/\u0661", document).Kind.ShouldBe(TraitValueKind.Null);
    }

    [Fact]
    public void ResolvesAFullwidthDigitIndexToNull() =>
        JsonPointer.Resolve("/list/\uFF11", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnEmptyIndexToNull() =>
        JsonPointer.Resolve("/list/", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesTheEndOfArrayMarkerToNull() =>
        JsonPointer.Resolve("/list/-", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesANegativeZeroIndexToNull() =>
        JsonPointer.Resolve("/list/-0", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesADecimalIndexToNull() =>
        JsonPointer.Resolve("/list/1.0", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnExponentIndexToNull() =>
        JsonPointer.Resolve("/list/1e0", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexWithTrailingWhitespaceToNull() =>
        JsonPointer.Resolve("/list/1 ", ElevenElements).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexIntoAStringToNull() =>
        JsonPointer.Resolve("/plan/0", Document).Kind.ShouldBe(TraitValueKind.Null);

    [Fact]
    public void ResolvesAnIndexThroughAnEscapedMemberName()
    {
        var document = Object(("a/b", TraitValue.FromArray(["first", "second"])));

        JsonPointer.Resolve("/a~1b/1", document).ShouldBe((TraitValue)"second");
    }

    private static TraitValue ElevenElements =>
        Object(("list", TraitValue.FromArray(Enumerable.Range(0, 11).Select(position => (TraitValue)$"element {position}"))));

    private static TraitValue Object(params (string Key, TraitValue Value)[] members) =>
        TraitValue.FromObject(members.Select(member => new KeyValuePair<string, TraitValue>(member.Key, member.Value)));

    private static TraitValue ToTraitValue(object expected) =>
        expected switch
        {
            string text => text,
            long number => number,
            _ => throw new ArgumentOutOfRangeException(nameof(expected)),
        };
}
