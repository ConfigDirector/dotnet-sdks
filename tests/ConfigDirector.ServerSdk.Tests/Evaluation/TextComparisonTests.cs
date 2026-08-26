using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class TextComparisonTests
{
    [Theory]
    [InlineData("abc", "=", "abc", true)]
    [InlineData("abc", "equals", "abc", true)]
    [InlineData("abc", "=", "abd", false)]
    [InlineData("abc", "!=", "abd", true)]
    [InlineData("abc", "does not equal", "abc", false)]
    [InlineData("", "=", "", true)]
    [InlineData("ABC", "=", "abc", false)]
    public void ComparesAgainstTheFirstTarget(string value, string op, string target, bool expected) =>
        Compare(value, op, target).ShouldBe(expected);

    [Theory]
    [InlineData("is one of", true)]
    [InlineData("is NOT one of", false)]
    public void ComparesMembership(string op, bool expected) =>
        Compare("b", op, "a", "b", "c").ShouldBe(expected);

    [Theory]
    [InlineData("starts with any of", "ab", true)]
    [InlineData("starts with any of", "bc", false)]
    [InlineData("does NOT start with any of", "ab", false)]
    [InlineData("does NOT start with any of", "bc", true)]
    [InlineData("ends with any of", "bc", true)]
    [InlineData("ends with any of", "ab", false)]
    [InlineData("does NOT end with any of", "bc", false)]
    [InlineData("does NOT end with any of", "ab", true)]
    public void ComparesAffixes(string op, string target, bool expected) =>
        Compare("abc", op, target).ShouldBe(expected);

    [Fact]
    public void MatchesAnyOfSeveralAffixes()
    {
        Compare("abc", "starts with any of", "x", "a").ShouldBeTrue();
        Compare("abc", "ends with any of", "x", "c").ShouldBeTrue();
    }

    // SEMANTICS.md 2. An empty target list makes the negative "any of" operators vacuously true,
    // and every operator needing a t[0] false.
    [Theory]
    [InlineData("=", false)]
    [InlineData("equals", false)]
    [InlineData("!=", false)]
    [InlineData("does not equal", false)]
    [InlineData("is one of", false)]
    [InlineData("is NOT one of", true)]
    [InlineData("starts with any of", false)]
    [InlineData("does NOT start with any of", true)]
    [InlineData("ends with any of", false)]
    [InlineData("does NOT end with any of", true)]
    public void HandlesAnEmptyTargetList(string op, bool expected) =>
        Compare("abc", op).ShouldBe(expected);

    [Theory]
    [InlineData("IS ONE OF")]
    [InlineData("is one of")]
    [InlineData("Is One Of")]
    public void MatchesOperatorsCaseInsensitively(string op) =>
        Compare("a", op, "a").ShouldBeTrue();

    [Theory]
    [InlineData("matches regex")]
    [InlineData("does NOT match regex")]
    [InlineData("is before")]
    [InlineData("contains any of")]
    [InlineData("")]
    [InlineData("<")]
    public void TreatsAnUnknownOperatorAsNoMatch(string op)
    {
        TextComparison.Parse(op).ShouldBe(TextOperator.Unknown);
        Compare("abc", op, "abc").ShouldBeFalse();
        Compare("abc", op).ShouldBeFalse();
    }

    [Fact]
    public void TreatsAMissingOperatorAsNoMatch() => TextComparison.Parse(null).ShouldBe(TextOperator.Unknown);

    private static bool Compare(string value, string? op, params string[] targets) =>
        TextComparison.Compare(value, TextComparison.Parse(op), targets);
}
