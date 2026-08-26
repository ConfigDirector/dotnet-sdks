using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class ArrayComparisonTests
{
    [Fact]
    public void ComparesMembership()
    {
        Compare(Array("a", "b"), "contains any of", "b").ShouldBeTrue();
        Compare(Array("a", "b"), "contains any of", "c").ShouldBeFalse();
        Compare(Array("a", "b"), "does NOT contain any of", "b").ShouldBeFalse();
        Compare(Array("a", "b"), "does NOT contain any of", "c").ShouldBeTrue();
    }

    [Fact]
    public void MatchesAnyOfSeveralTargets() =>
        Compare(Array("a", "b"), "contains any of", "x", "b").ShouldBeTrue();

    // SEMANTICS.md 1.3. Elements are rendered to text first, so a list of numbers matches "1".
    [Fact]
    public void RendersElementsBeforeMatching()
    {
        Compare(TraitValue.FromArray([1, 2]), "contains any of", "1").ShouldBeTrue();
        Compare(TraitValue.FromArray([26.5]), "contains any of", "26.5").ShouldBeTrue();
        Compare(TraitValue.FromArray([26.0]), "contains any of", "26").ShouldBeTrue();
        Compare(TraitValue.FromArray([true]), "contains any of", "true").ShouldBeTrue();
    }

    // SEMANTICS.md 1.3. Nested arrays, objects and nulls are dropped rather than becoming "".
    [Fact]
    public void DropsElementsWithNoTextForm()
    {
        var value = TraitValue.FromArray([TraitValue.Null, TraitValue.FromArray(["a"]), Object()]);

        Compare(value, "contains any of", "").ShouldBeFalse();
        Compare(value, "does NOT contain any of", "").ShouldBeTrue();
    }

    [Fact]
    public void KeepsAnEmptyStringElement()
    {
        Compare(TraitValue.FromArray([""]), "contains any of", "").ShouldBeTrue();
    }

    // SEMANTICS.md 6. A value that is not an array contains nothing, so only the negative
    // operator can be true for it.
    [Theory]
    [InlineData("contains any of", false)]
    [InlineData("does NOT contain any of", true)]
    public void TreatsANonArrayAsContainingNothing(string op, bool expected)
    {
        Compare("a,b", op, "a").ShouldBe(expected);
        Compare(TraitValue.Null, op, "a").ShouldBe(expected);
        Compare(26L, op, "26").ShouldBe(expected);
        Compare(Object(), op, "a").ShouldBe(expected);
        Compare("a,b", op).ShouldBe(expected);
    }

    [Theory]
    [InlineData("contains any of", false)]
    [InlineData("does NOT contain any of", true)]
    public void HandlesAnEmptyTargetList(string op, bool expected) =>
        Compare(Array("a"), op).ShouldBe(expected);

    [Fact]
    public void HandlesAnEmptyArray()
    {
        Compare(TraitValue.FromArray([]), "contains any of", "a").ShouldBeFalse();
        Compare(TraitValue.FromArray([]), "does NOT contain any of", "a").ShouldBeTrue();
    }

    [Fact]
    public void MatchesOperatorsCaseInsensitively() =>
        Compare(Array("a"), "CONTAINS ANY OF", "a").ShouldBeTrue();

    [Theory]
    [InlineData("is one of")]
    [InlineData("=")]
    [InlineData("")]
    public void TreatsAnUnknownOperatorAsNoMatch(string op)
    {
        ArrayComparison.Parse(op).ShouldBe(ArrayOperator.Unknown);
        Compare(Array("a"), op, "a").ShouldBeFalse();
        Compare("not an array", op, "a").ShouldBeFalse();
    }

    private static TraitValue Array(params string[] values) =>
        TraitValue.FromArray(values.Select(value => (TraitValue)value));

    private static TraitValue Object() =>
        TraitValue.FromObject(new Dictionary<string, TraitValue> { ["a"] = "b" });

    private static bool Compare(TraitValue value, string op, params string[] targets) =>
        ArrayComparison.Compare(value, ArrayComparison.Parse(op), targets);
}
