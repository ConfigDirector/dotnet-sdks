using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class NumberComparisonTests
{
    [Theory]
    [InlineData("=", "26", true)]
    [InlineData("equals", "26", true)]
    [InlineData("=", "27", false)]
    [InlineData("!=", "27", true)]
    [InlineData("does not equal", "26", false)]
    [InlineData("<", "27", true)]
    [InlineData("<", "26", false)]
    [InlineData("<=", "26", true)]
    [InlineData(">", "25", true)]
    [InlineData(">", "26", false)]
    [InlineData(">=", "26", true)]
    public void ComparesNumbers(string op, string target, bool expected) =>
        Compare(26L, op, target).ShouldBe(expected);

    [Fact]
    public void ComparesANumberHeldAsText() => Compare("26", "=", "26").ShouldBeTrue();

    [Fact]
    public void ComparesFractionalNumbers()
    {
        Compare(26.5, ">", "26").ShouldBeTrue();
        Compare("26.5", "=", "26.5").ShouldBeTrue();
    }

    // SEMANTICS.md 3. Strict parsing: no surrounding whitespace, no trailing characters.
    [Theory]
    [InlineData("10", true)]
    [InlineData("10.5", true)]
    [InlineData("-5", true)]
    [InlineData("+5", true)]
    [InlineData("1e3", true)]
    [InlineData(".5", true)]
    [InlineData("26abc", false)]
    [InlineData(" 42 ", false)]
    [InlineData("42 ", false)]
    [InlineData(" 42", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("0x10", false)]
    [InlineData("Infinity", false)]
    [InlineData("-Infinity", false)]
    [InlineData("NaN", false)]
    [InlineData("1_000", false)]
    [InlineData("1.2.3", false)]
    [InlineData("1e", false)]
    [InlineData("-", false)]
    [InlineData("1e400", false)]
    public void ParsesTextStrictly(string value, bool parses)
    {
        Compare(value, "=", value).ShouldBe(parses);
        Compare(value, "!=", value).ShouldBe(!parses);
    }

    // SEMANTICS.md 3. An unparseable value is true for != and false for everything else, decided
    // before the target is even looked at.
    [Theory]
    [InlineData("=", false)]
    [InlineData("equals", false)]
    [InlineData("!=", true)]
    [InlineData("does not equal", true)]
    [InlineData("<", false)]
    [InlineData("<=", false)]
    [InlineData(">", false)]
    [InlineData(">=", false)]
    public void ResolvesAnUnparseableValueWithoutReadingTheTarget(string op, bool expected)
    {
        Compare("abc", op, "26").ShouldBe(expected);
        Compare("abc", op, "also not a number").ShouldBe(expected);
        Compare("abc", op).ShouldBe(expected);
        Compare(TraitValue.Null, op, "26").ShouldBe(expected);
        Compare(true, op, "26").ShouldBe(expected);
        Compare(TraitValue.FromArray([1]), op, "26").ShouldBe(expected);
    }

    [Fact]
    public void RejectsNonFiniteNumbers()
    {
        Compare(double.NaN, "=", "26").ShouldBeFalse();
        Compare(double.PositiveInfinity, ">", "26").ShouldBeFalse();
        Compare(double.PositiveInfinity, "!=", "26").ShouldBeTrue();
    }

    [Theory]
    [InlineData("=")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData(">")]
    public void HandlesAnEmptyTargetList(string op) => Compare(26L, op).ShouldBeFalse();

    [Theory]
    [InlineData("=")]
    [InlineData("!=")]
    [InlineData("<")]
    public void RejectsAnUnparseableTarget(string op) => Compare(26L, op, "abc").ShouldBeFalse();

    [Fact]
    public void MatchesOperatorsCaseInsensitively() => Compare(26L, "EQUALS", "26").ShouldBeTrue();

    [Theory]
    [InlineData("is one of")]
    [InlineData("starts with any of")]
    [InlineData("is before")]
    [InlineData("")]
    public void TreatsAnUnknownOperatorAsNoMatch(string op)
    {
        NumberComparison.Parse(op).ShouldBe(NumberOperator.Unknown);
        Compare(26L, op, "26").ShouldBeFalse();
        Compare("abc", op, "26").ShouldBeFalse();
    }

    private static bool Compare(TraitValue value, string op, params string[] targets) =>
        NumberComparison.Compare(value, NumberComparison.Parse(op), targets);
}
