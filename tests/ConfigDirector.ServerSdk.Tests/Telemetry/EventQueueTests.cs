using ConfigDirector.Telemetry;

namespace ConfigDirector.Tests.Telemetry;

public class EventQueueTests
{
    [Fact]
    public void TakesASnapshotOfWhatWasPushed()
    {
        var queue = new EventQueue(10);
        queue.Push(Event("a"));
        queue.Push(Event("b"));

        var snapshot = queue.TakeSnapshot();

        snapshot.Events.Select(entry => entry.Key).ShouldBe(["a", "b"]);
        snapshot.DroppedCount.ShouldBe(0);
    }

    [Fact]
    public void ASnapshotEmptiesTheQueue()
    {
        var queue = new EventQueue(10);
        queue.Push(Event());

        queue.TakeSnapshot();

        queue.TakeSnapshot().IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void DropsTheOldestEventsOnceFull()
    {
        var queue = new EventQueue(2);
        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            queue.Push(Event(key));
        }

        var snapshot = queue.TakeSnapshot();

        snapshot.Events.Select(entry => entry.Key).ShouldBe(["c", "d"]);
        snapshot.DroppedCount.ShouldBe(2);
    }

    [Fact]
    public void TheDroppedCountStartsOverAfterASnapshot()
    {
        var queue = new EventQueue(1);
        queue.Push(Event("a"));
        queue.Push(Event("b"));
        queue.TakeSnapshot();

        queue.Push(Event("c"));

        queue.TakeSnapshot().DroppedCount.ShouldBe(0);
    }

    [Fact]
    public void TheWindowStartsAtTheFirstEventAndEndsAtTheSnapshot()
    {
        var queue = new EventQueue(10);
        var before = DateTimeOffset.UtcNow;
        queue.Push(Event());

        var snapshot = queue.TakeSnapshot();

        snapshot.StartTime.ShouldBeGreaterThanOrEqualTo(before);
        snapshot.EndTime.ShouldBeGreaterThanOrEqualTo(snapshot.StartTime);
        snapshot.EndTime.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
    }

    // The window has to cover every event in it, so it opens at the first one rather than sliding
    // forward to whichever arrived last.
    [Fact]
    public async Task TheWindowStartsAtTheFirstEventNotTheLast()
    {
        var queue = new EventQueue(10);
        queue.Push(Event("a"));
        await Task.Delay(25, TestContext.Current.CancellationToken);
        var beforeTheSecond = DateTimeOffset.UtcNow;

        queue.Push(Event("b"));

        queue.TakeSnapshot().StartTime.ShouldBeLessThan(beforeTheSecond);
    }

    // Reachable only by the reporter, which is handed a snapshot rather than a queue: events are
    // never dropped without one being kept, but a report still has to send the count.
    [Fact]
    public void ASnapshotThatOnlyCountsDropsIsNotEmpty() =>
        new EventQueueSnapshot(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], 3)
            .IsEmpty.ShouldBeFalse();

    [Fact]
    public void AnEmptySnapshotIsNotAZeroLengthWindow()
    {
        var snapshot = new EventQueue(10).TakeSnapshot();

        snapshot.IsEmpty.ShouldBeTrue();
        snapshot.StartTime.ShouldBe(snapshot.EndTime);
    }

    [Fact]
    public void ASnapshotHoldingOnlyDropsIsNotEmpty()
    {
        var queue = new EventQueue(1);
        queue.Push(Event("a"));
        queue.Push(Event("b"));

        queue.TakeSnapshot().IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ClearingDiscardsTheEventsAndTheDroppedCount()
    {
        var queue = new EventQueue(1);
        queue.Push(Event("a"));
        queue.Push(Event("b"));

        queue.Clear();

        queue.TakeSnapshot().IsEmpty.ShouldBeTrue();
    }

    // The queue is written to from every thread that evaluates a config.
    [Fact]
    public async Task ConcurrentPushesAllLand()
    {
        var queue = new EventQueue(1_000);
        using var start = new Barrier(4);

        var pushers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            for (var index = 0; index < 50; index++)
            {
                queue.Push(Event($"config-{index}"));
            }
        })).ToArray();

        await Task.WhenAll(pushers);

        var snapshot = queue.TakeSnapshot();
        snapshot.Events.Count.ShouldBe(200);
        snapshot.DroppedCount.ShouldBe(0);
    }

    private static EvaluatedConfigEvent Event(string key = "my-config") =>
        EvaluatedConfigEvent.Of(key, "default", "hello", false, EvaluationReason.FoundMatch);
}
