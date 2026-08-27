using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests.Integration;

// How the client reaches ConfigDirector, driven through the public API against a stubbed server.
// Every layer inside the SDK -- transport, event stream, bundle parser -- runs for real.
public sealed class ConnectionIntegrationTests : IDisposable
{
    private readonly SdkServer _server = new();

    [Fact]
    public async Task StreamsFromTheEventEndpointByDefault()
    {
        var loggerFactory = new CapturingLoggerFactory();
        await using var client = Client(new ConfigDirectorClientOptions { LoggerFactory = loggerFactory });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Reported with what the SDK and the server each saw: a stream that fails to deliver says
        // nothing useful through IsReady alone, and this is the first test to notice.
        client.IsReady.ShouldBeTrue(Diagnose(loggerFactory));
        _server.Paths.ShouldBe(["/server/sse/v1"]);
    }

    private string Diagnose(CapturingLoggerFactory loggerFactory)
    {
        var log = string.Join(
            Environment.NewLine,
            loggerFactory.Logger.Entries.Select(entry =>
                $"  [{entry.Level}] {entry.Message}"
                + (entry.Error is null ? string.Empty : $" -> {entry.Error.GetType().Name}: {entry.Error.Message}")));

        var seen = string.Join(", ", _server.Paths);
        return $"config state never arrived.{Environment.NewLine}"
            + $"requests the server saw: [{seen}]{Environment.NewLine}"
            + $"bytes written to the stream: {_server.StreamedBytes}{Environment.NewLine}"
            + $"server-side failures: [{string.Join("; ", _server.Failures)}]{Environment.NewLine}"
            + $"SDK log:{Environment.NewLine}{log}";
    }

    [Fact]
    public async Task PollsTheFetchEndpointWhenAskedTo()
    {
        await using var client = Client(Polling());

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        _server.Paths[0].ShouldBe("/server/polling/v1");
    }

    [Fact]
    public async Task IdentifiesItselfAndItsKeyOnEveryRequest()
    {
        await using var client = Client(Polling());

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.Bodies[0].ShouldContain("\"serverSdkKey\":\"server-sdk-key\"");
        _server.Bodies[0].ShouldContain("\"sdkName\":\"dotnet-server-sdk\"");
        _server.UserAgents[0].ShouldStartWith("dotnet-server-sdk/");
    }

    [Fact]
    public async Task CarriesTheApplicationMetadataToTheServer()
    {
        var options = Polling();
        options.Metadata = new Metadata { AppName = "checkout", AppVersion = "1.2.3" };
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.Bodies[0].ShouldContain("\"appName\":\"checkout\"");
        _server.Bodies[0].ShouldContain("\"appVersion\":\"1.2.3\"");
    }

    [Fact]
    public async Task FetchesOnceAndStopsInOneTimeMode()
    {
        var options = new ConfigDirectorClientOptions { Connection = { Mode = ConnectionMode.OneTime } };
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        _server.Paths.ShouldBe(["/server/polling/v1"]);
    }

    [Fact]
    public async Task EchoesTheTimestampBackSoTheServerCanAnswerWithADelta()
    {
        await using var client = Client(Polling());

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => _server.Bodies.Count >= 2);

        _server.Bodies[0].ShouldNotContain("lastUpdateTimestamp");
        _server.Bodies[1].ShouldContain("\"lastUpdateTimestamp\":\"2026-08-01T12:00:00.000Z\"");
    }

    [Fact]
    public async Task AppliesAnUpdateTheStreamDeliversAfterInitialization()
    {
        await using var client = Client();
        var seen = new List<string>();
        client.Watch("day-of-the-week-config", "unused", seen.Add);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        _server.Push(SampleConfigs.DayOfTheWeek("Friday"));
        await WaitAsync(() => seen.Count == 2);

        seen.ShouldBe(["Monday", "Friday"]);
        client.GetValue("day-of-the-week-config", "unused").ShouldBe("Friday");
    }

    [Fact]
    public async Task ReadsABundleWhoseLinesEndTheWayWindowsEndsThem()
    {
        // A source checkout on Windows carries CRLF, so the sample bundles do too. The stream has
        // to survive that: a stray carriage return is whitespace to a JSON reader but a line
        // terminator to an event stream, which is enough to cut a frame in half.
        _server.Bundle = SampleConfigs.Bundle.Replace("\n", "\r\n", StringComparison.Ordinal);
        await using var client = Client();

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        client.GetValue("integer-config", 0).ShouldBe(25);
    }

    [Fact]
    public async Task KeepsTheConfigsADeltaDidNotCarry()
    {
        var proUser = new Context { Id = "user-1", Traits = { ["plan"] = "pro" } };
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.GetValue("integer-config", 0).ShouldBe(25);

        _server.Push(SampleConfigs.DayOfTheWeek("Friday"));
        await WaitAsync(() => client.GetValue("day-of-the-week-config", "unused") == "Friday");

        // A delta carries only what changed. Taking it as the whole config state drops every
        // config the server had no news about.
        client.GetValue("integer-config", 0).ShouldBe(25);
        client.GetValue("temporary-feature-flag", false, proUser).ShouldBeTrue();
    }

    [Fact]
    public async Task ReplacesTheConfigStateWhenAFullBundleArrives()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.GetValue("integer-config", 0).ShouldBe(25);

        _server.Push(SampleConfigs.DayOfTheWeek("Friday", kind: "full"));
        await WaitAsync(() => client.GetValue("day-of-the-week-config", "unused") == "Friday");

        // A full bundle is the whole config state, so a config it leaves out is one the server no
        // longer has. Merging it would keep serving a config that was deleted.
        client.GetValue("integer-config", 0).ShouldBe(0);
    }

    [Fact]
    public async Task RoutesThroughAProxyUrlWithAPathOfItsOwn()
    {
        var options = Polling();

        // A base URL with a path of its own, the way a proxy in front of ConfigDirector is given.
        await using var client = Client(options, new Uri(_server.BaseUrl, "configdirector"));

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.Paths[0].ShouldBe("/configdirector/server/polling/v1");
    }

    [Fact]
    public async Task StaysUnreadyRatherThanThrowingWhenTheKeyIsRejected()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = Polling();
        options.LoggerFactory = loggerFactory;
        _server.Replies(HttpStatusCode.Forbidden, "the server SDK key was revoked");
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeFalse();
        client.GetValue("integer-config", 7).ShouldBe(7);
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("403", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecoversOnTheNextPollFromAStatusThatMightPass()
    {
        var options = Polling();
        _server.Replies(HttpStatusCode.ServiceUnavailable, "try again");
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => client.IsReady);

        client.GetValue("integer-config", 0).ShouldBe(25);
    }

    [Fact]
    public async Task BoundsEachPollByTheConfiguredTimeout()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = Polling();
        options.LoggerFactory = loggerFactory;
        options.Connection.Timeout = TimeSpan.FromMilliseconds(150);
        _server.Stalls = true;
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => loggerFactory.Logger.Entries.Any(entry => entry.Error is not null));

        // Names the timeout it gave up on, which is what says the configured one reached the
        // transport rather than the transport falling back to a default of its own.
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Error != null
            && entry.Error.Message.Contains("00:00:00.1500000", StringComparison.Ordinal));
    }

    // Bounded, so a client that ignores its own timeout fails here rather than hanging the suite.
    [Fact(Timeout = 10_000)]
    public async Task GivesUpOnInitializationWhenTheTimeoutElapses()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = new ConfigDirectorClientOptions
        {
            LoggerFactory = loggerFactory,
            Connection = { Timeout = TimeSpan.FromMilliseconds(50) },
        };
        _server.Stalls = true;
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeFalse();
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Timed out", StringComparison.Ordinal));
    }

    [Fact(Timeout = 10_000)]
    public async Task SurfacesTheCallersCancellationRatherThanTreatingItAsATimeout()
    {
        var options = new ConfigDirectorClientOptions
        {
            Connection = { Timeout = TimeSpan.FromMinutes(5) },
        };
        _server.Stalls = true;
        await using var client = Client(options);

        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var initializing = client.InitializeAsync(caller.Token);
        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(initializing);
    }

    [Fact]
    public async Task LeavesOutMetadataItWasNotGiven()
    {
        await using var client = Client(Polling());

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.Bodies[0].ShouldNotContain("appName");
        _server.Bodies[0].ShouldNotContain("appVersion");
    }

    [Fact]
    public async Task KeepsTheConfigStateItHasWhenTheServerHasNothingNewer()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = Polling();
        options.LoggerFactory = loggerFactory;
        await using var client = Client(options);
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.Replies(HttpStatusCode.NoContent);
        await WaitAsync(() => _server.Requests >= 2);

        client.IsReady.ShouldBeTrue();
        client.GetValue("integer-config", 0).ShouldBe(25);

        // A 204 is the server saying nothing has changed, not a failure: config state that merely
        // survives an error being logged is not the same as the response being understood.
        loggerFactory.Logger.Entries.ShouldNotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task RefusesToReconnectAfterAnUnrecoverableError()
    {
        _server.Replies(HttpStatusCode.Forbidden, "the server SDK key was revoked");
        await using var client = Client(Polling());

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        var attempted = _server.Requests;
        await Task.Delay(200, TestContext.Current.CancellationToken);

        _server.Requests.ShouldBe(attempted);
    }

    [Fact]
    public async Task StaysUnreadyWhenTheServerCannotBeReachedAtAll()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = Polling();
        options.LoggerFactory = loggerFactory;
        await using var client = Client(options, SdkServer.UnreachableUrl);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeFalse();
        loggerFactory.Logger.Entries.ShouldContain(entry => entry.Error != null);
    }

    [Fact]
    public async Task ReportsAResponseItCouldNotRead()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = Polling();
        options.LoggerFactory = loggerFactory;
        _server.Replies(HttpStatusCode.OK, "this is not a config bundle");
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeFalse();
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Error != null && entry.Error.Message.Contains("parse", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StopsPollingOnceItIsDisposed()
    {
        var client = Client(Polling());
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        await client.DisposeAsync();
        var settled = _server.Requests;
        await Task.Delay(200, TestContext.Current.CancellationToken);

        _server.Requests.ShouldBe(settled);
    }

    [Fact]
    public async Task AsksForAnEventStream()
    {
        await using var client = Client();

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.Accepts[0].ShouldBe("text/event-stream");
    }

    [Fact]
    public async Task IgnoresAFrameThatCarriesNoConfigState()
    {
        await using var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        _server.PushRaw("event: heartbeat\ndata: {\"alive\":true}\n\n");
        await Task.Delay(150, TestContext.Current.CancellationToken);

        // A heartbeat is not an empty bundle: it must not clear the config state already held.
        client.IsReady.ShouldBeTrue();
        client.GetValue("integer-config", 0).ShouldBe(25);
    }

    [Fact]
    public async Task StaysUnreadyWhenTheStreamIsRejectedOutright()
    {
        var loggerFactory = new CapturingLoggerFactory();
        var options = new ConfigDirectorClientOptions { LoggerFactory = loggerFactory };
        _server.Replies(HttpStatusCode.Forbidden, "the server SDK key was revoked");
        await using var client = Client(options);

        var started = Stopwatch.StartNew();
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        started.Stop();

        client.IsReady.ShouldBeFalse();
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("403", StringComparison.Ordinal));

        // Timed, because a rejection the stream never reports leaves the caller waiting out the
        // whole initialization timeout instead of being released the moment it is known.
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 30_000)]
    public async Task KeepsWaitingWhileItRetriesAStreamThatMightRecover()
    {
        // Long enough to outlast one backoff, which starts at a second and is half jittered.
        var options = new ConfigDirectorClientOptions
        {
            Connection = { Timeout = TimeSpan.FromSeconds(10) },
        };
        _server.Replies(HttpStatusCode.BadGateway, "try again");
        await using var client = Client(options);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        _server.Requests.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task StopsStreamingOnceItIsDisposed()
    {
        var client = Client();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        await client.DisposeAsync();
        var settled = _server.Requests;
        await Task.Delay(200, TestContext.Current.CancellationToken);

        _server.Requests.ShouldBe(settled);
    }

    public void Dispose() => _server.Dispose();

    private static ConfigDirectorClientOptions Polling() =>
        new()
        {
            Connection =
            {
                Mode = ConnectionMode.Polling,
                PollingInterval = TimeSpan.FromMilliseconds(50),
            },
        };

    private ConfigDirectorClient Client(ConfigDirectorClientOptions? options = null, Uri? url = null)
    {
        var settings = options ?? new ConfigDirectorClientOptions();
        _server.Attach(settings);
        if (url is not null)
        {
            settings.Connection.Url = url;
        }

        return new ConfigDirectorClient("server-sdk-key", settings);
    }

    private static async Task WaitAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 300 && !until(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue();
    }
}
