using System.Text.Json;
using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

// A value too large to report inline is identified by the digest of this text, so the same value
// has to render identically in every SDK or one value would be counted as two. The expectations
// are what JSON.stringify produces.
public class TelemetryJsonTests
{
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void SpellsBooleansTheWayJsonDoes(bool value, string expected) =>
        TelemetryJson.Serialize(value).ShouldBe(expected);

    [Theory]
    [InlineData(26, "26")]
    [InlineData(-3, "-3")]
    [InlineData(0, "0")]
    public void WritesIntegers(int value, string expected) =>
        TelemetryJson.Serialize(value).ShouldBe(expected);

    [Fact]
    public void WritesALongBeyondTheRangeOfADouble() =>
        TelemetryJson.Serialize(9_007_199_254_740_993L).ShouldBe("9007199254740993");

    // JSON draws no line between whole and fractional numbers, so 26.0 renders as "26".
    [Theory]
    [InlineData(26.0, "26")]
    [InlineData(1.5, "1.5")]
    [InlineData(-0.0, "0")]
    [InlineData(1e21, "1e+21")]
    public void WritesDoublesTheWayJsonStringifyDoes(double value, string expected) =>
        TelemetryJson.Serialize(value).ShouldBe(expected);

    // JSON has no way to spell either one, and JSON.stringify writes null for both.
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void WritesNullForANumberJsonCannotSpell(double value) =>
        TelemetryJson.Serialize(value).ShouldBe("null");

    // Widening the float to a double first would render 0.1f as 0.10000000149011612.
    [Fact]
    public void WritesAFloatAtItsOwnPrecision() =>
        TelemetryJson.Serialize(0.1f).ShouldBe("0.1");

    // A decimal keeps the trailing zeros it was written with, where every other SDK holds the
    // same value as a double and renders it without them.
    [Theory]
    [InlineData("1.50", "1.5")]
    [InlineData("26.000", "26")]
    [InlineData("0.10", "0.1")]
    public void WritesADecimalWithoutItsTrailingZeros(string value, string expected) =>
        TelemetryJson.Serialize(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .ShouldBe(expected);

    [Theory]
    [InlineData("text", "\"text\"")]
    [InlineData("quote \" and \\ and \n", "\"quote \\\" and \\\\ and \\n\"")]
    [InlineData("tab\there", "\"tab\\there\"")]
    public void EscapesAStringTheWayJsonDoes(string value, string expected) =>
        TelemetryJson.Serialize(value).ShouldBe(expected);

    // Escaping these would change the digest, and JSON.stringify leaves them alone.
    [Theory]
    [InlineData("café", "\"café\"")]
    [InlineData("unicode ☂", "\"unicode ☂\"")]
    [InlineData("<a>&'+", "\"<a>&'+\"")]
    public void LeavesCharactersJsonStringifyDoesNotEscape(string value, string expected) =>
        TelemetryJson.Serialize(value).ShouldBe(expected);

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    [InlineData("[1,\"two\",true,null]", "[1,\"two\",true,null]")]
    [InlineData("{ \"a\" : 1, \"b\" : 2 }", "{\"a\":1,\"b\":2}")]
    [InlineData("{\"nested\":{\"list\":[1.0,{\"deep\":false}]}}", "{\"nested\":{\"list\":[1,{\"deep\":false}]}}")]
    public void WritesJsonWithNothingBetweenThePunctuation(string json, string expected) =>
        TelemetryJson.Serialize(Parse(json)).ShouldBe(expected);

    // System.Text.Json echoes a number back exactly as it was written, so without normalising
    // them a config the server sent as 26.0 would not match the same value sent as 26.
    [Theory]
    [InlineData("{\"n\":26.0}", "{\"n\":26}")]
    [InlineData("{\"n\":2.6e1}", "{\"n\":26}")]
    public void NormalisesANumberTheServerSpelledDifferently(string json, string expected) =>
        TelemetryJson.Serialize(Parse(json)).ShouldBe(expected);

    [Fact]
    public void PreservesKeyOrderRatherThanSorting() =>
        TelemetryJson.Serialize(Parse("{\"b\":1,\"a\":2}")).ShouldBe("{\"b\":1,\"a\":2}");

    [Fact]
    public void WritesAnUnsetJsonElementAsNull() =>
        TelemetryJson.Serialize(default(JsonElement)).ShouldBe("null");

    [Fact]
    public void WritesAnObjectTheCallerAskedToBeBoundTo() =>
        TelemetryJson.Serialize(new Sample { Name = "a", Count = 2.0, Tags = ["x"] })
            .ShouldBe("{\"name\":\"a\",\"count\":2,\"tags\":[\"x\"]}");

    [Fact]
    public void WritesADictionaryDefault() =>
        TelemetryJson.Serialize(new Dictionary<string, int> { ["b"] = 1, ["a"] = 2 })
            .ShouldBe("{\"b\":1,\"a\":2}");

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class Sample
    {
        public string Name { get; set; } = string.Empty;

        public double Count { get; set; }

        public IReadOnlyList<string> Tags { get; set; } = [];
    }
}
