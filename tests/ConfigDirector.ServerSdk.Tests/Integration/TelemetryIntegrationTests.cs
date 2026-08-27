using System.Text.Json;

namespace ConfigDirector.Tests.Integration;

// What an application's evaluations tell ConfigDirector, driven through the public API against a
// stubbed server. Every layer inside the SDK runs for real.
public sealed class TelemetryIntegrationTests : IDisposable
{
    private readonly SdkServer _server = new();

    [Fact]
    public async Task ReportsWhatAnApplicationEvaluated()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("integer-config", 0);
        await FlushAsync(client);

        var evaluated = Evaluations()[0].GetProperty("event");
        evaluated.GetProperty("key").GetString().ShouldBe("integer-config");
        evaluated.GetProperty("evaluatedValue").GetProperty("value").GetString().ShouldBe("25");
        evaluated.GetProperty("defaultValue").GetProperty("value").GetString().ShouldBe("0");
        evaluated.GetProperty("requestedType").GetString().ShouldBe("Int32");
        evaluated.GetProperty("usedDefault").GetBoolean().ShouldBeFalse();
        evaluated.GetProperty("evaluationReason").GetString().ShouldBe("found-match");
    }

    [Fact]
    public async Task ReportsTheTypeTheConfigWasDeclaredWithAndTheServersValueId()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("integer-config", 0);
        await FlushAsync(client);

        var evaluated = Evaluations()[0].GetProperty("event");
        evaluated.GetProperty("type").GetString().ShouldBe("integer");
        evaluated.GetProperty("evaluatedValueId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReportsThatADefaultWasUsedForAKeyTheServerDoesNotKnow()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("no-such-config", "fallback");
        await FlushAsync(client);

        var evaluated = Evaluations()[0].GetProperty("event");
        evaluated.GetProperty("usedDefault").GetBoolean().ShouldBeTrue();
        evaluated.GetProperty("evaluationReason").GetString().ShouldBe("config-state-missing");
        evaluated.GetProperty("evaluatedValue").GetProperty("value").GetString().ShouldBe("fallback");
    }

    [Fact]
    public async Task CapturesTheContextAConfigWasEvaluatedAgainst()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue(
            "temporary-feature-flag",
            false,
            new Context { Id = "user-1", Name = "Ada", Traits = { ["plan"] = "pro" } });
        await FlushAsync(client);

        var captured = Payload().GetProperty("discreteEvents").GetProperty("capturedContexts")[0];
        captured.GetProperty("id").GetString().ShouldBe("user-1");
        captured.GetProperty("traits").GetProperty("plan").GetString().ShouldBe("pro");
        Evaluations()[0].GetProperty("event").GetProperty("contextId").GetString().ShouldBe("user-1");
    }

    [Fact]
    public async Task CollapsesTheSameEvaluationMadeOverAndOver()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        for (var index = 0; index < 5; index++)
        {
            client.GetValue("integer-config", 0);
        }

        await FlushAsync(client);

        Evaluations().Length.ShouldBe(1);
        Evaluations()[0].GetProperty("count").GetInt32().ShouldBe(5);
    }

    // A JSON config is always reported by ID: the document itself is too large to be worth
    // sending on every evaluation.
    [Fact]
    public async Task ReportsAJsonConfigByItsId()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("json-value-config", default(JsonElement));
        await FlushAsync(client);

        var evaluated = Evaluations()[0].GetProperty("event").GetProperty("evaluatedValue");
        evaluated.TryGetProperty("value", out _).ShouldBeFalse();
        evaluated.GetProperty("valueId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReportsAnEvaluationAWatchWasNotifiedOf()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.Watch("day-of-the-week-config", "unused", _ => { });

        _server.Push(SampleConfigs.DayOfTheWeek("Friday"));
        await WaitAsync(() => client.GetValue("day-of-the-week-config", "unused") == "Friday");
        await FlushAsync(client);

        Evaluations().ShouldContain(entry =>
            entry.GetProperty("event").GetProperty("key").GetString() == "day-of-the-week-config");
    }

    [Fact]
    public async Task SendsNothingWhenTheApplicationEvaluatedNothing()
    {
        var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        await client.DisposeAsync();

        _server.Paths.ShouldNotContain("/server/telemetry/v1");
    }

    [Fact]
    public async Task IdentifiesTheSdkAndTheKeyItReportsFor()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("integer-config", 0);
        await FlushAsync(client);

        Payload().GetProperty("serverSdkKey").GetString().ShouldBe("sdk-key");
        Payload().GetProperty("metaContext").GetProperty("sdkName").GetString()
            .ShouldBe("dotnet-server-sdk");
    }

    // Nothing closes the client here: the report has to arrive because the interval came round.
    [Fact]
    public async Task ReportsOnTheIntervalWithoutWaitingToBeClosed()
    {
        await using var client = Client(flushInterval: TimeSpan.FromMilliseconds(50));
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetValue("integer-config", 0);

        // Sooner than the default interval's first report could arrive, so a report this quick can
        // only be the configured one.
        await WaitAsync(
            () => _server.Paths.Contains("/server/telemetry/v1"), TimeSpan.FromSeconds(2));
    }

    // The interval is far away, so only closing can produce a report.
    [Fact]
    public async Task ReportsWhatIsLeftWhenTheClientIsClosed()
    {
        var client = Client(flushInterval: TimeSpan.FromMinutes(5));
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.GetValue("integer-config", 0);

        await client.DisposeAsync();

        await WaitAsync(() => _server.Paths.Contains("/server/telemetry/v1"));
        Evaluations().Length.ShouldBe(1);
    }

    // A container that disposes synchronously has to report too, or shutting down quietly drops
    // everything the application evaluated since the last interval.
    [Fact]
    public async Task ReportsWhatIsLeftWhenTheClientIsDisposedSynchronously()
    {
        var client = Client(flushInterval: TimeSpan.FromMinutes(5));
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.GetValue("integer-config", 0);

        client.Dispose();

        // Sooner than the collector's own first report would arrive, so only the close can have
        // produced it.
        await WaitAsync(
            () => _server.Paths.Contains("/server/telemetry/v1"), TimeSpan.FromSeconds(2));
        Evaluations().Length.ShouldBe(1);
    }

    // Closing is a single transition however many callers race for it: two of them getting past
    // the check would each tear the same connection down.
    [Fact]
    public async Task ClosesOnceWhenSeveralThreadsDisposeAtOnce()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var client = Client(flushInterval: TimeSpan.FromMinutes(5), loggerFactory: loggerFactory);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.GetValue("integer-config", 0);

        using var start = new Barrier(8);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.SignalAndWait();
            await client.DisposeAsync();
        })));

        loggerFactory.Logger.Entries
            .Count(entry => entry.Message.Contains("has been closed", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task DropsTheOldestEvaluationsOnceTheQueueLimitIsReached()
    {
        var client = Client(eventQueueLimit: TelemetryOptions.MinEventQueueLimit);
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Seventy of the hundred are kept for evaluations, so ten of these are dropped.
        for (var index = 0; index < 80; index++)
        {
            client.GetValue("integer-config", index);
        }

        await client.DisposeAsync();

        await WaitAsync(() => _server.Paths.Contains("/server/telemetry/v1"));
        Payload().GetProperty("droppedEvents").GetProperty("evaluatedConfig").GetInt32().ShouldBe(10);
    }

    public void Dispose() => _server.Dispose();

    private ConfigDirectorClient Client(
        TimeSpan? flushInterval = null,
        int eventQueueLimit = TelemetryOptions.DefaultEventQueueLimit,
        CapturingLoggerFactory? loggerFactory = null)
    {
        var options = new ConfigDirectorClientOptions();
        _server.Attach(options);
        if (loggerFactory is not null)
        {
            options.LoggerFactory = loggerFactory;
        }

        options.Telemetry.FlushInterval = flushInterval ?? TimeSpan.FromMinutes(5);
        options.Telemetry.EventQueueLimit = eventQueueLimit;
        return new ConfigDirectorClient("sdk-key", options);
    }

    // Closing reports whatever is left, which is how a test asks for a report without waiting on
    // the interval.
    private async Task FlushAsync(ConfigDirectorClient client)
    {
        await client.DisposeAsync();
        await WaitAsync(() => _server.Paths.Contains("/server/telemetry/v1"));
    }

    private static async Task WaitAsync(Func<bool> until, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow.Add(within ?? TimeSpan.FromSeconds(5));
        while (!until() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue("the telemetry report never arrived");
    }

    private JsonElement Payload() =>
        JsonDocument.Parse(_server.Bodies[_server.Paths.IndexOf("/server/telemetry/v1")])
            .RootElement.Clone();

    private JsonElement[] Evaluations() =>
        [.. Payload().GetProperty("aggregatedEvents").GetProperty("evaluatedConfig").EnumerateArray()];
}
