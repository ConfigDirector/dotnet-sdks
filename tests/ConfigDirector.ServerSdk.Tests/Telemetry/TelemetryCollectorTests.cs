using System.Net;
using System.Text.Json;
using ConfigDirector.Telemetry;
using ConfigDirector.Tests.Integration;

namespace ConfigDirector.Tests.Telemetry;

public sealed class TelemetryCollectorTests : IDisposable
{
    private readonly SdkServer _server = new();
    private readonly CapturingLoggerFactory _loggerFactory = new();

    [Fact]
    public async Task ReportsWhatWasEvaluatedOnTheInterval()
    {
        await using var collector = Collector(flushInterval: TimeSpan.FromMilliseconds(50));

        Record(collector);

        await WaitForReportAsync();
        Evaluations()[0].GetProperty("event").GetProperty("key").GetString().ShouldBe("my-config");
    }

    [Fact]
    public async Task CollapsesRepeatedEvaluationsIntoACount()
    {
        await using var collector = Collector();
        for (var index = 0; index < 4; index++)
        {
            Record(collector);
        }

        await collector.FlushAsync(TestContext.Current.CancellationToken);

        Evaluations().Length.ShouldBe(1);
        Evaluations()[0].GetProperty("count").GetInt32().ShouldBe(4);
    }

    [Fact]
    public async Task CapturesTheContextAConfigWasEvaluatedAgainst()
    {
        await using var collector = Collector();

        Record(collector, context: new Context { Id = "user-1", Name = "Ada" });
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        var captured = Payload().GetProperty("discreteEvents").GetProperty("capturedContexts");
        captured.GetArrayLength().ShouldBe(1);
        captured[0].GetProperty("id").GetString().ShouldBe("user-1");
        Evaluations()[0].GetProperty("event").GetProperty("contextId").GetString().ShouldBe("user-1");
    }

    // An anonymous context still targets rules, but it is not persisted and must not be
    // identifiable in what is reported.
    [Fact]
    public async Task NeverIdentifiesAnAnonymousContext()
    {
        await using var collector = Collector();

        Record(collector, context: new Context { Id = "user-1", Anonymous = true });
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        Payload().GetProperty("discreteEvents").GetProperty("capturedContexts").GetArrayLength().ShouldBe(0);
        Evaluations()[0].GetProperty("event").TryGetProperty("contextId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task CapturesNothingForAContextWithNoIdentifier()
    {
        await using var collector = Collector();

        Record(collector, context: new Context { Name = "Ada" });
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        Payload().GetProperty("discreteEvents").GetProperty("capturedContexts").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task CapturesNothingForAContextWhoseIdentifierIsBlank()
    {
        await using var collector = Collector();

        Record(collector, context: new Context { Id = string.Empty, Name = "Ada" });
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        Payload().GetProperty("discreteEvents").GetProperty("capturedContexts").GetArrayLength().ShouldBe(0);
        Evaluations()[0].GetProperty("event").TryGetProperty("contextId", out _).ShouldBeFalse();
    }

    // Hashing an oversized value is the flush thread's work, so it has to happen on the way out
    // rather than being left for the server to receive in full.
    [Fact]
    public async Task ReportsAnOversizedValueByItsIdRatherThanInFull()
    {
        var oversized = new string('x', 600);
        await using var collector = Collector();

        collector.Record(
            "my-config", "default", oversized, false, EvaluationReason.FoundMatch, null, null, null);
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        var evaluated = Evaluations()[0].GetProperty("event").GetProperty("evaluatedValue");
        evaluated.TryGetProperty("value", out _).ShouldBeFalse();
        evaluated.GetProperty("valueId").GetString()!.Length.ShouldBe(22);
    }

    [Fact]
    public async Task SendsNothingWhenNothingWasEvaluated()
    {
        await using var collector = Collector();

        await collector.FlushAsync(TestContext.Current.CancellationToken);

        _server.Requests.ShouldBe(0);
    }

    // Evaluations outnumber the contexts they were made against by a wide margin, so they get the
    // larger share of the limit.
    [Fact]
    public async Task GivesEvaluationsTheLargerShareOfTheQueueLimit()
    {
        await using var collector = Collector(eventQueueLimit: 10);

        for (var index = 0; index < 9; index++)
        {
            Record(collector, key: $"config-{index}");
        }

        await collector.FlushAsync(TestContext.Current.CancellationToken);

        // Seven of the nine fit; the rest were dropped and counted.
        Evaluations().Length.ShouldBe(7);
        Payload().GetProperty("droppedEvents").GetProperty("evaluatedConfig").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task ReportsWhatIsLeftWhenItIsClosed()
    {
        var collector = Collector();
        Record(collector);

        await collector.DisposeAsync();

        _server.Requests.ShouldBe(1);
        Evaluations().Length.ShouldBe(1);
    }

    [Fact]
    public async Task ClosingTwiceReportsOnlyOnce()
    {
        var collector = Collector();
        Record(collector);

        await collector.DisposeAsync();
        await collector.DisposeAsync();

        _server.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task StopsCollectingAfterAStatusThatWillNotPass()
    {
        _server.Replies(HttpStatusCode.Forbidden);
        await using var collector = Collector();
        Record(collector);

        await collector.FlushAsync(TestContext.Current.CancellationToken);
        Record(collector);
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        _server.Requests.ShouldBe(1);
        _loggerFactory.Logger.Entries.ShouldContain(entry => entry.Message.Contains("No longer collecting"));
    }

    [Fact]
    public async Task IgnoresAnEvaluationRecordedAfterItIsClosed()
    {
        var collector = Collector();
        await collector.DisposeAsync();

        Record(collector);
        await collector.FlushAsync(TestContext.Current.CancellationToken);

        _server.Requests.ShouldBe(0);
    }

    // A report that cannot be sent must not take the flush loop down with it.
    [Fact]
    public async Task KeepsCollectingWhenTheServerCannotBeReached()
    {
        await using var collector = Collector(url: SdkServer.UnreachableUrl);
        Record(collector);

        await collector.FlushAsync(TestContext.Current.CancellationToken);
        Record(collector);

        await Should.NotThrowAsync(() => collector.FlushAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose() => _server.Dispose();

    private TelemetryCollector Collector(
        TimeSpan? flushInterval = null, int eventQueueLimit = 5_000, Uri? url = null) =>
        new(new TelemetryCollectorOptions(
            "sdk-key", url ?? _server.BaseUrl, _loggerFactory)
        {
            FlushInterval = flushInterval ?? TimeSpan.FromMinutes(5),
            EventQueueLimit = eventQueueLimit,
        });

    private static void Record(
        TelemetryCollector collector, string key = "my-config", Context? context = null) =>
        collector.Record(key, "default", "hello", false, EvaluationReason.FoundMatch, context, null, null);

    private async Task WaitForReportAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_server.Requests == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        _server.Requests.ShouldBeGreaterThan(0, "no telemetry report arrived");
    }

    private JsonElement Payload() => JsonDocument.Parse(_server.Bodies[0]).RootElement.Clone();

    private JsonElement[] Evaluations() =>
        [.. Payload().GetProperty("aggregatedEvents").GetProperty("evaluatedConfig").EnumerateArray()];
}
