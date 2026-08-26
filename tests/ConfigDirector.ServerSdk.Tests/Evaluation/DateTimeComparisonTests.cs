using ConfigDirector.Evaluation;

namespace ConfigDirector.Tests.Evaluation;

public class DateTimeComparisonTests
{
    [Theory]
    [InlineData("2026-01-28", "is before", "2026-01-29", true)]
    [InlineData("2026-01-29", "is before", "2026-01-28", false)]
    [InlineData("2026-01-28", "is before", "2026-01-28", false)]
    [InlineData("2026-01-29", "is after", "2026-01-28", true)]
    [InlineData("2026-01-28", "is after", "2026-01-29", false)]
    [InlineData("2026-01-28", "is after", "2026-01-28", false)]
    public void ComparesInstants(string value, string op, string target, bool expected) =>
        Compare(value, op, target).ShouldBe(expected);

    // SEMANTICS.md 5. The ECMAScript Date Time String Format, with every offsetless form read
    // as UTC rather than as the evaluating machine's local time.
    [Theory]
    [InlineData("2026")]
    [InlineData("2026-01")]
    [InlineData("2026-01-28")]
    [InlineData("2026-01-28T01:24")]
    [InlineData("2026-01-28T01:24:59")]
    [InlineData("2026-01-28T01:24:59.999")]
    [InlineData("2026-01-28T01:24:59.999Z")]
    [InlineData("2026-01-28T01:24:59.999z")]
    [InlineData("2026-01-28T01:24:59+02:00")]
    [InlineData("2026-01-28T01:24:59-05:30")]
    [InlineData("+002026-01-28")]
    public void ParsesEveryAcceptedForm(string value) =>
        Compare(value, "is before", "9999-12-31").ShouldBeTrue();

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("2026-13-01")]
    [InlineData("2026-01-32")]
    [InlineData("2026-02-30")]
    [InlineData("26-01-28")]
    [InlineData("2026/01/28")]
    [InlineData("2026-01-28 01:24")]
    [InlineData("2026-01-28T01")]
    [InlineData("2026-01-28T01:24:59.999+0200")]
    [InlineData("2026-01-28T01:24:59.999 Z")]
    [InlineData("-002026-01-28")]
    public void RefusesAnythingElse(string value)
    {
        Compare(value, "is before", "2026-06-01").ShouldBeFalse();
        Compare(value, "is after", "2026-06-01").ShouldBeFalse();
        Compare("2026-06-01", "is before", value).ShouldBeFalse();
        Compare("2026-06-01", "is after", value).ShouldBeFalse();
    }

    [Fact]
    public void AcceptsALeapDayAndRejectsANonLeapDay()
    {
        Compare("2024-02-29", "is before", "2026-01-01").ShouldBeTrue();
        Compare("2026-02-29", "is before", "2026-06-01").ShouldBeFalse();
    }

    [Fact]
    public void ReadsAnOffsetlessDateTimeAsUtc()
    {
        ShouldBeTheSameInstant("2026-01-28T01:25:00", "2026-01-28T01:25:00Z");
        ShouldBeTheSameInstant("2026-01-28T01:25", "2026-01-28T01:25:00Z");
        ShouldBeTheSameInstant("2026-01-28T01:25:00.123", "2026-01-28T01:25:00.123Z");
    }

    [Fact]
    public void AppliesTheOffsetItWasGiven()
    {
        ShouldBeTheSameInstant("2026-01-28T01:25:00+02:00", "2026-01-27T23:25:00Z");
        ShouldBeTheSameInstant("2026-01-28T01:25:00-05:00", "2026-01-28T06:25:00Z");
        ShouldBeTheSameInstant("2026-01-28T01:25:00-05:30", "2026-01-28T06:55:00Z");
    }

    [Fact]
    public void ReadsALowercaseZoneMarkerAsUtc() =>
        ShouldBeTheSameInstant("2026-01-28T01:25:00z", "2026-01-28T01:25:00Z");

    [Fact]
    public void FillsInMissingComponentsFromTheStartOfTheYear()
    {
        ShouldBeTheSameInstant("2026", "2026-01-01T00:00:00Z");
        ShouldBeTheSameInstant("2026-03", "2026-03-01T00:00:00Z");
        ShouldBeTheSameInstant("2026-03-04", "2026-03-04T00:00:00Z");
        ShouldBeTheSameInstant("+002026-03-04", "2026-03-04T00:00:00Z");
    }

    // SEMANTICS.md 5 -- precision is milliseconds, and further digits are truncated, not rounded.
    [Fact]
    public void TruncatesBelowMilliseconds()
    {
        ShouldBeTheSameInstant("2026-01-28T01:25:00.123999Z", "2026-01-28T01:25:00.123Z");
        ShouldBeTheSameInstant("2026-01-28T01:25:00.1Z", "2026-01-28T01:25:00.100Z");
        Compare("2026-01-28T01:25:00.123999Z", "is before", "2026-01-28T01:25:00.124Z").ShouldBeTrue();
    }

    [Theory]
    [InlineData("is before")]
    [InlineData("is after")]
    public void HandlesAnEmptyTargetList(string op) => Compare("2026-01-28", op).ShouldBeFalse();

    [Fact]
    public void MatchesOperatorsCaseInsensitively() =>
        Compare("2026-01-28", "IS BEFORE", "2026-01-29").ShouldBeTrue();

    [Theory]
    [InlineData("<")]
    [InlineData("=")]
    [InlineData("is one of")]
    [InlineData("")]
    public void TreatsAnUnknownOperatorAsNoMatch(string op)
    {
        DateTimeComparison.Parse(op).ShouldBe(DateTimeOperator.Unknown);
        Compare("2026-01-28", op, "2026-01-29").ShouldBeFalse();
    }

    // Neither side ordering before the other, in both directions, is the only assertion that pins
    // an offset exactly: "is before" alone still passes when a value is off by hours.
    private static void ShouldBeTheSameInstant(string left, string right)
    {
        Compare(left, "is before", right).ShouldBeFalse();
        Compare(left, "is after", right).ShouldBeFalse();
        Compare(right, "is before", left).ShouldBeFalse();
        Compare(right, "is after", left).ShouldBeFalse();
    }

    private static bool Compare(string value, string op, params string[] targets) =>
        DateTimeComparison.Compare(value, DateTimeComparison.Parse(op), targets);
}
