using System.Net;
using System.Text.Json;
using ConfigDirector.Telemetry;
using ConfigDirector.Tests.Integration;

namespace ConfigDirector.Tests.Telemetry;

// Driven against a real server over a loopback socket, so the payload the server would read is
// the one being asserted.
public sealed class HttpEventReporterTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 1, 30, TimeSpan.Zero);

    private readonly SdkServer _server = new();
    private readonly CapturingLoggerFactory _loggerFactory = new();

    [Fact]
    public async Task PostsToTheTelemetryEndpoint()
    {
        await using var reporter = Reporter();

        await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        _server.Paths.ShouldBe(["/server/telemetry/v1"]);
    }

    [Fact]
    public async Task IdentifiesTheSdkAndTheKeyItReportsFor()
    {
        await using var reporter = Reporter();

        await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        var payload = Payload();
        payload.GetProperty("serverSdkKey").GetString().ShouldBe("sdk-key");
        payload.GetProperty("metaContext").GetProperty("sdkName").GetString().ShouldBe("dotnet-server-sdk");
        payload.GetProperty("metaContext").GetProperty("sdkVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CarriesEachAggregatedEvaluationWithItsWindowAndCount()
    {
        await using var reporter = Reporter();

        await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        var evaluated = Payload().GetProperty("aggregatedEvents").GetProperty("evaluatedConfig");
        evaluated.GetArrayLength().ShouldBe(1);
        var entry = evaluated[0];
        entry.GetProperty("count").GetInt32().ShouldBe(3);
        entry.GetProperty("startTime").GetString().ShouldBe("2026-01-01T00:00:00.000Z");
        entry.GetProperty("endTime").GetString().ShouldBe("2026-01-01T00:01:30.000Z");
        entry.GetProperty("event").GetProperty("key").GetString().ShouldBe("my-config");
    }

    [Fact]
    public async Task CarriesTheContextsConfigsWereEvaluatedAgainst()
    {
        var context = new Context
        {
            Id = "user-1",
            Name = "Ada",
            Traits = { ["plan"] = "pro", ["age"] = 41, ["beta"] = true, ["score"] = 1.5 },
        };
        await using var reporter = Reporter();

        await reporter.ReportAsync(Report(contexts: [context]), TestContext.Current.CancellationToken);

        var captured = Payload().GetProperty("discreteEvents").GetProperty("capturedContexts")[0];
        captured.GetProperty("id").GetString().ShouldBe("user-1");
        captured.GetProperty("name").GetString().ShouldBe("Ada");
        var traits = captured.GetProperty("traits");
        traits.GetProperty("plan").GetString().ShouldBe("pro");
        traits.GetProperty("age").GetInt32().ShouldBe(41);
        traits.GetProperty("beta").GetBoolean().ShouldBeTrue();
        traits.GetProperty("score").GetDouble().ShouldBe(1.5);
    }

    // Only identified, non-anonymous contexts are ever captured, so there is nothing to say.
    [Fact]
    public async Task LeavesOutAContextsNameAndTraitsWhenThereAreNone()
    {
        await using var reporter = Reporter();

        await reporter.ReportAsync(
            Report(contexts: [new Context { Id = "user-1" }]), TestContext.Current.CancellationToken);

        var captured = Payload().GetProperty("discreteEvents").GetProperty("capturedContexts")[0];
        captured.TryGetProperty("name", out _).ShouldBeFalse();
        captured.TryGetProperty("anonymous", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task ReportsWhatItCouldNotHold()
    {
        await using var reporter = Reporter();

        await reporter.ReportAsync(
            Report(droppedEvaluations: 7, droppedContexts: 2), TestContext.Current.CancellationToken);

        var dropped = Payload().GetProperty("droppedEvents");
        dropped.GetProperty("evaluatedConfig").GetInt32().ShouldBe(7);
        dropped.GetProperty("capturedContexts").GetInt32().ShouldBe(2);
    }

    // Reachable only through the collector, which builds a report from a snapshot: nothing is
    // ever dropped without something being kept, but the count still has to reach the server.
    [Fact]
    public void AReportThatOnlyCountsDropsIsNotEmpty()
    {
        new EventReport([], 1, [], 0).IsEmpty.ShouldBeFalse();
        new EventReport([], 0, [], 1).IsEmpty.ShouldBeFalse();
        new EventReport([], 0, [], 0).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public async Task SendsNothingWhenThereIsNothingToReport()
    {
        await using var reporter = Reporter();

        var response = await reporter.ReportAsync(
            new EventReport([], 0, [], 0), TestContext.Current.CancellationToken);

        response.Success.ShouldBeTrue();
        _server.Paths.ShouldBeEmpty();
    }

    [Fact]
    public async Task IdentifiesItselfSoBotProtectionDoesNotRejectIt()
    {
        await using var reporter = Reporter();

        await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        _server.UserAgents[0].ShouldStartWith("dotnet-server-sdk/");
    }

    [Fact]
    public async Task StopsReportingAfterAStatusThatWillNotPass()
    {
        _server.Replies(HttpStatusCode.Forbidden);
        await using var reporter = Reporter();

        var rejected = await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);
        var afterwards = await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        rejected.Fatal.ShouldBeTrue();
        afterwards.Fatal.ShouldBeTrue();
        _server.Paths.Count.ShouldBe(1);
        _loggerFactory.Logger.Entries.ShouldContain(entry => entry.Message.Contains("No more telemetry"));
    }

    // The events in this report are lost, but a server that is merely struggling is worth
    // reporting to again on the next flush.
    [Fact]
    public async Task KeepsReportingAfterAStatusThatMightPass()
    {
        _server.Replies(HttpStatusCode.InternalServerError);
        await using var reporter = Reporter();

        var failed = await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);
        var afterwards = await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        failed.Success.ShouldBeFalse();
        failed.Fatal.ShouldBeFalse();
        afterwards.Success.ShouldBeTrue();
        _server.Paths.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ReportsAFailureToReachTheServerWithoutThrowing()
    {
        await using var reporter = Reporter(SdkServer.UnreachableUrl);

        var response = await reporter.ReportAsync(Report(), TestContext.Current.CancellationToken);

        response.Success.ShouldBeFalse();
        response.Fatal.ShouldBeFalse();
    }

    public void Dispose() => _server.Dispose();

    private HttpEventReporter Reporter(Uri? url = null) =>
        new("sdk-key", url ?? _server.BaseUrl, _loggerFactory);

    private static EventReport Report(
        IReadOnlyList<Context>? contexts = null,
        int droppedEvaluations = 0,
        int droppedContexts = 0) =>
        new(
            [new AggregatedEvent(Start, End, 3, EvaluatedConfigEvent.Of(
                "my-config", "default", "hello", false, EvaluationReason.FoundMatch))],
            droppedEvaluations,
            contexts ?? [],
            droppedContexts);

    private JsonElement Payload() => JsonDocument.Parse(_server.Bodies[0]).RootElement.Clone();
}
