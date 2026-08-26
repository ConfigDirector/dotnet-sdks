using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class TraitTextTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    [InlineData(" abc ", " abc ")]
    public void RendersStringsAsThemselves(string value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void RendersBooleansAsJsonSpellsThem(bool value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Theory]
    [InlineData(26L, "26")]
    [InlineData(-5L, "-5")]
    [InlineData(0L, "0")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    [InlineData(long.MinValue, "-9223372036854775808")]
    public void RendersIntegersExactly(long value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Theory]
    [InlineData(26.5, "26.5")]
    [InlineData(-5.25, "-5.25")]
    [InlineData(0.1, "0.1")]
    public void RendersFractionalNumbers(double value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Theory]
    [InlineData(26.0, "26")]
    [InlineData(0.0, "0")]
    [InlineData(1e20, "100000000000000000000")]
    public void RendersWholeNumbersWithoutATrailingFraction(double value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Fact]
    public void RendersNegativeZeroAsZero() => TraitText.Render(-0.0).ShouldBe("0");

    [Theory]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e-7, "1e-07")]
    public void RendersNumbersOutsidePlainRangeWithALowercaseExponent(double value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void RendersNonFiniteNumbersTheWayTheOtherSdksDo(double value, string expected) =>
        TraitText.Render(value).ShouldBe(expected);

    [Fact]
    public void RendersValuesWithNoTextFormAsEmpty()
    {
        TraitText.Render(TraitValue.Null).ShouldBe("");
        TraitText.Render(TraitValue.FromArray(["a"])).ShouldBe("");
        TraitText.Render(TraitValue.FromObject(new Dictionary<string, TraitValue> { ["a"] = 1 })).ShouldBe("");
    }
}
