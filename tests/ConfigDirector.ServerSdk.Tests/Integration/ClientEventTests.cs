using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests.Integration;

// Events and watches driven entirely through the public API. Handlers are registered before
// initialization, so the first config state is what triggers them.
public class ClientEventTests
{
    private static readonly Context ProUser = new()
    {
        Id = "user-1",
        Traits = { ["plan"] = "pro" },
    };

    [Fact]
    public async Task AnnouncesItselfReadyWhenTheFirstConfigStateArrives()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var announced = 0;
        client.ClientReady += (_, _) => announced++;

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        announced.ShouldBe(1);
    }

    [Fact]
    public async Task AnnouncesTheKeysAnUpdateCarried()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        IReadOnlyList<string> keys = [];
        client.ConfigsUpdated += (_, updated) => keys = updated.Keys;

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        keys.ShouldBe(
            [
                "day-of-the-week-config",
                "integer-config",
                "json-value-config",
                "permanent-kill-switch",
                "temporary-feature-flag",
            ]);
    }

    [Fact]
    public async Task ReportsAnEvaluationThatFoundAValue()
    {
        await using var client = await ReadyClientAsync();
        var evaluations = Collect(client);

        client.GetValue("temporary-feature-flag", false, ProUser).ShouldBeTrue();

        var evaluation = evaluations.ShouldHaveSingleItem();
        evaluation.Key.ShouldBe("temporary-feature-flag");
        evaluation.Value.ShouldBe(true);
        evaluation.IsDefault.ShouldBeFalse();
        evaluation.Reason.ShouldBe(EvaluationReason.FoundMatch);
        evaluation.ValueId.ShouldBe("temporary-feature-flag-on");
        evaluation.Context.ShouldBe(ProUser);
    }

    [Fact]
    public async Task ReportsAnEvaluationOfAKeyItDoesNotHold()
    {
        await using var client = await ReadyClientAsync();
        var evaluations = Collect(client);

        client.GetValue("never-heard-of-it", "fallback");

        var evaluation = evaluations.ShouldHaveSingleItem();
        evaluation.Value.ShouldBe("fallback");
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.ConfigStateMissing);
        evaluation.ValueId.ShouldBeNull();
        evaluation.Context.ShouldBeNull();
    }

    [Fact]
    public async Task SeparatesAnUnknownKeyFromNotBeingReadyYet()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var evaluations = Collect(client);

        client.GetValue("temporary-feature-flag", false);

        evaluations.ShouldHaveSingleItem().Reason.ShouldBe(EvaluationReason.ClientNotReady);
    }

    [Fact]
    public async Task ReportsWhyAValueCouldNotBeUsed()
    {
        await using var client = await ReadyClientAsync();
        var evaluations = Collect(client);

        client.GetValue("day-of-the-week-config", -1).ShouldBe(-1);

        var evaluation = evaluations.ShouldHaveSingleItem();
        evaluation.IsDefault.ShouldBeTrue();
        evaluation.Reason.ShouldBe(EvaluationReason.InvalidNumber);
        evaluation.ValueId.ShouldBeNull();
    }

    [Fact]
    public async Task NotifiesAWatchWhenConfigStateArrives()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var seen = new List<bool>();
        client.Watch("temporary-feature-flag", false, seen.Add, ProUser);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        seen.ShouldBe([true]);
    }

    [Fact]
    public async Task NotifiesEachWatchOnAKeyWithItsOwnDefaultAndContext()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var enabled = new List<bool>();
        var banner = new List<string>();
        client.Watch("temporary-feature-flag", false, enabled.Add, ProUser);
        client.Watch("temporary-feature-flag", false, enabled.Add, new Context { Id = "user-3" });
        client.Watch("day-of-the-week-config", "unused", banner.Add);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        enabled.ShouldBe([true, false]);
        banner.ShouldBe(["Monday"]);
    }

    [Fact]
    public async Task StopsNotifyingAWatchThatWasCancelled()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var seen = new List<bool>();
        var watch = client.Watch("temporary-feature-flag", false, seen.Add, ProUser);
        watch.Dispose();
        watch.Dispose();

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancellingOneWatchLeavesTheOthersInPlace()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var kept = new List<bool>();
        var cancelled = client.Watch("temporary-feature-flag", false, _ => throw new InvalidOperationException("cancelled"));
        client.Watch("temporary-feature-flag", false, kept.Add, ProUser);
        cancelled.Dispose();

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        kept.ShouldBe([true]);
    }

    [Fact]
    public async Task LetsAWatchCancelItselfWhileItIsBeingNotified()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var seen = new List<bool>();
        IDisposable? once = null;
        once = client.Watch("temporary-feature-flag", false, value =>
        {
            seen.Add(value);
            once!.Dispose();
        });
        client.Watch("temporary-feature-flag", false, seen.Add, ProUser);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        seen.ShouldBe([false, true]);
        client.IsReady.ShouldBeTrue();
    }

    [Fact]
    public async Task UnwatchRemovesEveryWatchOnOneKey()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var enabled = new List<bool>();
        var banner = new List<string>();
        client.Watch("temporary-feature-flag", false, enabled.Add, ProUser);
        client.Watch("temporary-feature-flag", false, enabled.Add, ProUser);
        client.Watch("day-of-the-week-config", "unused", banner.Add);

        client.Unwatch("temporary-feature-flag");
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        enabled.ShouldBeEmpty();
        banner.ShouldBe(["Monday"]);
    }

    [Fact]
    public async Task UnwatchAllRemovesEveryWatch()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var seen = new List<string>();
        client.Watch("temporary-feature-flag", "unused", seen.Add);
        client.Watch("day-of-the-week-config", "unused", seen.Add);

        client.UnwatchAll();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        seen.ShouldBeEmpty();
    }

    [Fact]
    public async Task AWatchIsEvaluatedLikeAnyOtherRead()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");
        var evaluations = Collect(client);
        client.Watch("temporary-feature-flag", false, _ => { }, ProUser);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        var evaluation = evaluations.ShouldHaveSingleItem();
        evaluation.Key.ShouldBe("temporary-feature-flag");
        evaluation.ValueId.ShouldBe("temporary-feature-flag-on");
    }

    [Fact]
    public async Task AFaultyHandlerCostsNeitherTheCallerNorTheHandlersAfterIt()
    {
        var loggerFactory = new CapturingLoggerFactory();
        await using var client = await ReadyClientAsync(new ConfigDirectorClientOptions { LoggerFactory = loggerFactory });
        var reached = false;
        client.ConfigEvaluated += (_, _) => throw new InvalidOperationException("faulty handler");
        client.ConfigEvaluated += (_, _) => reached = true;

        client.GetValue("integer-config", 0).ShouldBe(25);

        reached.ShouldBeTrue();
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Error is InvalidOperationException);
    }

    [Fact]
    public async Task AFaultyWatchCostsNeitherTheUpdateNorTheWatchesAfterIt()
    {
        var loggerFactory = new CapturingLoggerFactory();
        await using var client = new ConfigDirectorClient(
            "server-sdk-key",
            new ConfigDirectorClientOptions { LoggerFactory = loggerFactory });
        var reached = false;
        client.Watch("temporary-feature-flag", false, _ => throw new InvalidOperationException("faulty watch"));
        client.Watch("temporary-feature-flag", false, _ => reached = true);

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        reached.ShouldBeTrue();
        client.IsReady.ShouldBeTrue();
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Error is InvalidOperationException);
    }

    [Fact]
    public async Task DropsHandlersAndWatchesWhenItIsDisposed()
    {
        var client = await ReadyClientAsync();
        var evaluations = Collect(client);

        await client.DisposeAsync();
        client.GetValue("integer-config", 7).ShouldBe(7);

        evaluations.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectsAWatchWithNothingToCallBack()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentNullException>(() => client.Watch("temporary-feature-flag", false, null!));
        Should.Throw<ArgumentNullException>(() => client.Watch<string>("temporary-feature-flag", null!, _ => { }));
        Should.Throw<ArgumentException>(() => client.Watch(" ", false, _ => { }));
    }

    private static List<ConfigEvaluation> Collect(IConfigDirectorClient client)
    {
        var evaluations = new List<ConfigEvaluation>();
        client.ConfigEvaluated += (_, evaluated) => evaluations.Add(evaluated.Evaluation);
        return evaluations;
    }

    private static async Task<ConfigDirectorClient> ReadyClientAsync(ConfigDirectorClientOptions? options = null)
    {
        var client = new ConfigDirectorClient("server-sdk-key", options);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        return client;
    }
}
