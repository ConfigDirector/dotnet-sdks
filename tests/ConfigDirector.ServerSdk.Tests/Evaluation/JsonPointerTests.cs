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
