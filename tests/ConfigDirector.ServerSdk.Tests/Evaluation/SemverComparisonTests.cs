using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class SemverComparisonTests
{
    [Theory]
    [InlineData("2.3.4", "=", "2.3.4", true)]
    [InlineData("2.3.4", "=", "2.3.5", false)]
    [InlineData("2.3.4", "<", "2.3.5", true)]
    [InlineData("2.3.4", "<", "2.3.4", false)]
    [InlineData("2.3.4", "<=", "2.3.4", true)]
    [InlineData("2.3.4", ">", "2.3.3", true)]
    [InlineData("2.3.4", ">", "2.3.4", false)]
    [InlineData("2.3.4", ">=", "2.3.4", true)]
    [InlineData("2.10.0", ">", "2.9.0", true)]
    [InlineData("3.0.0", ">", "2.99.99", true)]
    public void ComparesVersions(string value, string op, string target, bool expected) =>
        Compare(value, op, target).ShouldBe(expected);

    // SEMANTICS.md 4. Both sides are coerced, so partial and v-prefixed versions are usable and
    // any prerelease or build suffix is dropped.
    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("1", "1.0.0")]
    [InlineData("v2.3.4", "2.3.4")]
    [InlineData("1.2.3.4", "1.2.3")]
    [InlineData("0.1.645-a", "0.1.645")]
    [InlineData("1.2.3+build.5", "1.2.3")]
    [InlineData("release-9.8.7", "9.8.7")]
    public void CoercesBothSides(string value, string equivalent) =>
        Compare(value, "=", equivalent).ShouldBeTrue();

    [Theory]
    [InlineData("abc")]
    [InlineData("v")]
    [InlineData("...")]
    public void NeverMatchesAnUncoercibleOperand(string value)
    {
        Compare(value, "=", "1.0.0").ShouldBeFalse();
        Compare("1.0.0", "=", value).ShouldBeFalse();
        Compare(value, "<", "1.0.0").ShouldBeFalse();
        Compare(value, ">", "1.0.0").ShouldBeFalse();
        Compare("1.0.0", "<", value).ShouldBeFalse();
        Compare(value, "is one of", "1.0.0").ShouldBeFalse();
        Compare(value, "is NOT one of", "1.0.0").ShouldBeTrue();
    }

    [Fact]
    public void ComparesMembership()
    {
        Compare("1.2.3", "is one of", "1.0.0", "1.2.3").ShouldBeTrue();
        Compare("1.2.3", "is one of", "1.0.0", "9.9.9").ShouldBeFalse();
        Compare("1.2.3", "is NOT one of", "1.0.0", "1.2.3").ShouldBeFalse();
        Compare("1.2.3", "is NOT one of", "1.0.0", "9.9.9").ShouldBeTrue();
    }

    // SEMANTICS.md 4. An empty or blank value is true for "is NOT one of" and false everywhere
    // else. It coerces to nothing, which is what an uncoercible value does above.
    [Theory]
    [InlineData("", "is NOT one of", true)]
    [InlineData("   ", "is NOT one of", true)]
    [InlineData("\t", "is NOT one of", true)]
    [InlineData("", "=", false)]
    [InlineData("", "<", false)]
    [InlineData("", ">", false)]
    [InlineData("", "is one of", false)]
    public void TreatsABlankValueAsMatchingNothing(string value, string op, bool expected)
    {
        Compare(value, op, "1.0.0").ShouldBe(expected);
        Compare(value, op).ShouldBe(expected);
    }

    [Theory]
    [InlineData("=", false)]
    [InlineData("<", false)]
    [InlineData("<=", false)]
    [InlineData(">", false)]
    [InlineData(">=", false)]
    [InlineData("is one of", false)]
    [InlineData("is NOT one of", true)]
    public void HandlesAnEmptyTargetList(string op, bool expected) =>
        Compare("1.2.3", op).ShouldBe(expected);

    [Fact]
    public void CoercesComponentsTooLongForAnInt() =>
        Compare("9999999999999999.1.2", "=", "9999999999999999.1.2").ShouldBeTrue();

    [Fact]
    public void MatchesOperatorsCaseInsensitively() =>
        Compare("1.2.3", "IS ONE OF", "1.2.3").ShouldBeTrue();

    // SEMANTICS.md 4. Lists no "equals" spelling and no "!=" for semver, unlike text and number.
    [Theory]
    [InlineData("equals")]
    [InlineData("!=")]
    [InlineData("does not equal")]
    [InlineData("starts with any of")]
    [InlineData("is before")]
    [InlineData("")]
    public void TreatsAnUnknownOperatorAsNoMatch(string op)
    {
        SemverComparison.Parse(op).ShouldBe(SemverOperator.Unknown);
        Compare("1.2.3", op, "1.2.3").ShouldBeFalse();
        Compare("", op, "1.2.3").ShouldBeFalse();
    }

    private static bool Compare(string value, string op, params string[] targets) =>
        SemverComparison.Compare(value, SemverComparison.Parse(op), targets);
}
