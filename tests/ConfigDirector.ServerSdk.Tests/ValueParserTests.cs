using ConfigDirector.Value;

namespace ConfigDirector.Tests;

public class ValueParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReportsAMissingValue(string? raw)
    {
        var result = Parse(raw, "fallback");

        result.Value.ShouldBe("fallback");
        result.Reason.ShouldBe(EvaluationReason.ValueMissing);
        result.UsedDefault.ShouldBeTrue();
        result.ValueId.ShouldBeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void ParsesBooleans(string raw, bool expected) => Parse(raw, !expected).Value.ShouldBe(expected);

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData(" true")]
    [InlineData("true ")]
    public void RefusesAnythingElseAsABoolean(string raw)
    {
        var result = Parse(raw, false);

        result.Value.ShouldBeFalse();
        result.Reason.ShouldBe(EvaluationReason.InvalidBoolean);
    }

    // The requested type comes from the default, not from how the config was declared: a caller
    // asking for text gets the raw value, whatever it looks like.
    [Theory]
    [InlineData("plain")]
    [InlineData("true")]
    [InlineData("26")]
    [InlineData("{\"a\": 1}")]
    [InlineData("null")]
    public void TakesAnyValueAsText(string raw) => Parse(raw, "fallback").Value.ShouldBe(raw);

    [Theory]
    [InlineData("26", 26)]
    [InlineData("-5", -5)]
    [InlineData("+5", 5)]
    [InlineData("26.0", 26)]
    [InlineData("1e3", 1000)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void ParsesWholeNumbers(string raw, int expected) => Parse(raw, 0).Value.ShouldBe(expected);

    [Theory]
    [InlineData("26.5")]
    [InlineData("abc")]
    [InlineData("26abc")]
    [InlineData(" 42 ")]
    [InlineData("1_000")]
    [InlineData("1,000")]
    [InlineData("0x10")]
    [InlineData("Infinity")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("9999999999999999999999")]
    public void RefusesAnythingElseAsAWholeNumber(string raw)
    {
        var result = Parse(raw, 7);

        result.Value.ShouldBe(7);
        result.Reason.ShouldBe(EvaluationReason.InvalidNumber);
    }

    [Fact]
    public void ParsesWholeNumbersTooLargeForAnInt()
    {
        Parse("2147483648", 0L).Value.ShouldBe(2147483648L);
        Parse("9223372036854775807", 0L).Value.ShouldBe(long.MaxValue);
        Parse("9223372036854775808", 0L).Reason.ShouldBe(EvaluationReason.InvalidNumber);
    }

    [Theory]
    [InlineData("26.5", 26.5)]
    [InlineData("26", 26.0)]
    [InlineData("-5.25", -5.25)]
    [InlineData("1e3", 1000.0)]
    public void ParsesNumbers(string raw, double expected) => Parse(raw, 0.0).Value.ShouldBe(expected);

    [Theory]
    [InlineData("abc")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("1e400")]
    [InlineData(" 42 ")]
    public void RefusesAnythingElseAsANumber(string raw)
    {
        var result = Parse(raw, 1.5);

        result.Value.ShouldBe(1.5);
        result.Reason.ShouldBe(EvaluationReason.InvalidNumber);
    }

    [Fact]
    public void ParsesTheOtherNumericTypes()
    {
        Parse("26.5", 0f).Value.ShouldBe(26.5f);
        Parse("26.5", 0m).Value.ShouldBe(26.5m);
        Parse("1e300", 0f).Reason.ShouldBe(EvaluationReason.InvalidNumber);
        Parse("1e300", 0.0).Value.ShouldBe(1e300);
    }

    [Fact]
    public void ParsesJsonIntoTheShapeTheDefaultAsksFor()
    {
        var result = Bind("""{"name": "checkout", "retries": 3}""", new Settings());

        result.Value.Name.ShouldBe("checkout");
        result.Value.Retries.ShouldBe(3);
        result.Reason.ShouldBe(EvaluationReason.FoundMatch);
    }

    [Fact]
    public void MatchesJsonPropertiesRegardlessOfCase()
    {
        var result = Bind("""{"Name": "checkout", "RETRIES": 3}""", new Settings());

        result.Value.Name.ShouldBe("checkout");
        result.Value.Retries.ShouldBe(3);
    }

    [Fact]
    public void ParsesJsonCollections()
    {
        Bind("[1, 2, 3]", new List<int>()).Value.ShouldBe([1, 2, 3]);
        Bind("""{"a": 1}""", new Dictionary<string, int>()).Value["a"].ShouldBe(1);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"unclosed\": ")]
    [InlineData("[1, 2]")]
    [InlineData("26")]
    [InlineData("null")]
    public void RefusesJsonThatDoesNotFitTheDefault(string raw)
    {
        var fallback = new Settings { Name = "fallback" };

        var result = Bind(raw, fallback);

        result.Value.ShouldBeSameAs(fallback);
        result.Reason.ShouldBe(EvaluationReason.InvalidJson);
    }

    [Fact]
    public void CarriesTheValueIdOfWhatItMatched()
    {
        var state = new ConfigState { Key = "k", Value = "26", ValueId = "value-id" };

        ValueParser.Parse(state, 0).ValueId.ShouldBe("value-id");
        ValueParser.Parse(state, false).ValueId.ShouldBeNull();
    }

    private sealed record Settings
    {
        public string? Name { get; init; }

        public int Retries { get; init; }
    }

    private static ParseResult<T> Parse<T>(string? raw, T defaultValue) =>
        ValueParser.Parse(new ConfigState { Key = "the-key", Value = raw }, defaultValue);

    private static ParseResult<T> Bind<T>(string? raw, T defaultValue) =>
        ValueParser.Bind(new ConfigState { Key = "the-key", Value = raw }, defaultValue);
}
