using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

public class AggregationTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);

    [Fact]
    public void CollapsesIdenticalEventsIntoOneEntryWithACount()
    {
        var aggregated = Aggregation.Aggregate([Event(), Event(), Event()], Start, End);

        aggregated.ShouldHaveSingleItem();
        aggregated[0].Count.ShouldBe(3);
        aggregated[0].Event.ShouldBe(Event());
    }

    [Fact]
    public void KeepsEventsThatDifferApart()
    {
        var aggregated = Aggregation.Aggregate(
            [Event("config-a"), Event("config-b"), Event("config-a")], Start, End);

        aggregated.Select(entry => (entry.Event.Key, entry.Count))
            .OrderBy(entry => entry.Key)
            .ShouldBe([("config-a", 2), ("config-b", 1)]);
    }

    [Fact]
    public void KeepsEventsThatDifferOnlyInTheirRuleApart()
    {
        var aggregated = Aggregation.Aggregate(
            [ServedBy(ruleId: "rule-a"), ServedBy(ruleId: "rule-b"), ServedBy(ruleId: "rule-a")], Start, End);

        aggregated.Select(entry => (entry.Event.RuleId, entry.Count))
            .OrderBy(entry => entry.RuleId)
            .ShouldBe([("rule-a", 2), ("rule-b", 1)]);
    }

    [Fact]
    public void KeepsEventsThatDifferOnlyInTheirShareApart()
    {
        var aggregated = Aggregation.Aggregate(
            [ServedBy(shareId: "share-a"), ServedBy(shareId: "share-b"), ServedBy(shareId: "share-a")], Start, End);

        aggregated.Select(entry => (entry.Event.ShareId, entry.Count))
            .OrderBy(entry => entry.ShareId)
            .ShouldBe([("share-a", 2), ("share-b", 1)]);
    }

    [Fact]
    public void KeepsEventsThatDifferOnlyInTheirVariationApart()
    {
        var aggregated = Aggregation.Aggregate(
            [ServedBy(variationId: "variation-a"), ServedBy(variationId: "variation-b"), ServedBy(variationId: "variation-a")], Start, End);

        aggregated.Select(entry => (entry.Event.VariationId, entry.Count))
            .OrderBy(entry => entry.VariationId)
            .ShouldBe([("variation-a", 2), ("variation-b", 1)]);
    }

    [Fact]
    public void EveryEntryCarriesTheWindowTheSnapshotCovers() =>
        Aggregation.Aggregate([Event("a"), Event("b")], Start, End)
            .ShouldAllBe(entry => entry.StartTime == Start && entry.EndTime == End);

    [Fact]
    public void AggregatingNothingProducesNothing() =>
        Aggregation.Aggregate([], Start, End).ShouldBeEmpty();

    private static EvaluatedConfigEvent Event(string key = "my-config") =>
        EvaluatedConfigEvent.Create(key, "default", "hello", false, EvaluationReason.FoundMatch);

    private static EvaluatedConfigEvent ServedBy(string? variationId = null, string? ruleId = null, string? shareId = null) =>
        EvaluatedConfigEvent.Create(
            "my-config",
            "default",
            "hello",
            false,
            EvaluationReason.FoundMatch,
            variationId: variationId,
            ruleId: ruleId,
            shareId: shareId);
}
