using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

public class ValueIdsTests
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // Taken from the JavaScript SDK's suite: every SDK has to agree on these, or the same config
    // value would be counted as two different ones in the dashboard.
    [Theory]
    [InlineData("hello", "1MoOW7eqAPjhZeoELVwO9G")]
    [InlineData("world", "2Cg0gndCS8p6nDE5aa6LcI")]
    [InlineData("42", "3VWjGpOwynZPh07ivDC56c")]
    [InlineData("", "6ve2WrOl3mnciB6WIL2fIa")]
    public void MatchesWhatTheOtherSdksProduce(string value, string expected) =>
        ValueIds.Generate(value).ShouldBe(expected);

    // These hash to a number small enough that its base62 form is shorter than the fixed width, so
    // the leading zeros have to be written rather than dropped.
    [Theory]
    [InlineData("seek-438", "01HIHOQ1EOGUUUxjw3XzTY")]
    [InlineData("seek-465", "00LlHyAvF0ZgWilmdRpxJb")]
    public void PadsADigestWithLeadingZeroBytes(string value, string expected) =>
        ValueIds.Generate(value).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("unicode ☂ café")]
    [InlineData("a much longer value repeated many times")]
    public void IsAlwaysTheSameLength(string value) =>
        ValueIds.Generate(value).Length.ShouldBe(ValueIds.ValueIdLength);

    [Fact]
    public void UsesOnlyBase62Characters() =>
        ValueIds.Generate("hello").ShouldAllBe(character => Base62.Contains(character));

    [Fact]
    public void IsDeterministic() =>
        ValueIds.Generate("my-value").ShouldBe(ValueIds.Generate("my-value"));

    [Fact]
    public void DifferentValuesProduceDifferentIds() =>
        ValueIds.Generate("value-a").ShouldNotBe(ValueIds.Generate("value-b"));

    // A digest taken over some other encoding would not match the other SDKs.
    [Fact]
    public void HashesTheUtf8Encoding() =>
        ValueIds.Generate("café").ShouldNotBe(ValueIds.Generate("cafe"));
}
