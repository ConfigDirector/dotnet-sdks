namespace ConfigDirector.Tests.Integration;

// Drives the whole SDK through its public API against the config state the transport supplies,
// with nothing inside the SDK substituted.
public sealed class ClientIntegrationTests : IDisposable
{
    private readonly SdkServer _server = new();

    private static readonly Context ProUser = new()
    {
        Id = "user-1",
        Traits = { ["plan"] = "pro" },
    };

    private static readonly Context FreeUser = new()
    {
        Id = "user-3",
        Traits = { ["plan"] = "free" },
    };

    [Fact]
    public async Task ReportsItselfReadyOnceConfigStateHasArrived()
    {
        await using var client = Client();

        client.IsReady.ShouldBeFalse();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        client.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task ReturnsTheDefaultBeforeItIsInitialized()
    {
        await using var client = Client();

        client.GetValue("temporary-feature-flag", true).ShouldBeTrue();
        client.GetAllConfigs().ShouldBeEmpty();
    }

    [Fact]
    public async Task EvaluatesABooleanAgainstTheContextsTraits()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("temporary-feature-flag", false, ProUser).ShouldBeTrue();
        client.GetValue("temporary-feature-flag", true, FreeUser).ShouldBeFalse();
    }

    [Fact]
    public async Task FallsBackToTheConfigsDefaultWhenNoRuleMatches()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("day-of-the-week-config", "unused").ShouldBe("Monday");
    }

    [Fact]
    public async Task PrefersTheEarlierRuleWhenSeveralMatch()
    {
        await using var client = await ReadyClientAsync(Version("2.5.0"));
        var betaUser = new Context { Id = "user-1", Traits = { ["tags"] = new[] { "beta" } } };

        client.GetValue("day-of-the-week-config", "unused", betaUser).ShouldBe("Caturday");
    }

    [Fact]
    public async Task EvaluatesAgainstTheApplicationMetadataItWasBuiltWith()
    {
        await using var modern = await ReadyClientAsync(Version("2.5.0"));
        await using var legacy = await ReadyClientAsync(Version("1.9.0"));

        modern.GetValue("day-of-the-week-config", "unused").ShouldBe("Sunday");
        legacy.GetValue("day-of-the-week-config", "unused").ShouldBe("Monday");
    }

    [Fact]
    public async Task ReadsWholeAndFractionalNumbers()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("integer-config", 0).ShouldBe(25);
        client.GetValue("integer-config", 0L).ShouldBe(25L);
        client.GetValue("integer-config", 0d).ShouldBe(25d);
    }

    [Fact]
    public async Task ReadsAJsonConfigIntoTheCallersOwnType()
    {
        await using var client = await ReadyClientAsync();

        var settings = client.GetValue("json-value-config", new RetrySettings());

        settings.Retries.ShouldBe(3);
        settings.TimeoutMs.ShouldBe(1500);
    }

    [Fact]
    public async Task PutsTheSameContextInTheSameBucketEveryTime()
    {
        await using var client = await ReadyClientAsync();

        // Each default is the opposite of what the rollout selects, so a bucket that stopped
        // being found would read as the default and fail here.
        client.GetValue("permanent-kill-switch", false, ProUser).ShouldBeTrue();
        client.GetValue("permanent-kill-switch", false, ProUser).ShouldBeTrue();
        client.GetValue("permanent-kill-switch", true, FreeUser).ShouldBeFalse();
    }

    [Fact]
    public async Task ReturnsTheDefaultForAKeyItDoesNotHold()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("never-heard-of-it", "fallback").ShouldBe("fallback");
    }

    [Fact]
    public async Task ReturnsTheDefaultWhenTheValueWillNotCoerceToTheTypeAsked()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("day-of-the-week-config", -1).ShouldBe(-1);
        client.GetValue("day-of-the-week-config", false).ShouldBeFalse();
        client.GetValue("integer-config", new RetrySettings { Retries = 9 }).Retries.ShouldBe(9);
    }

    [Fact]
    public async Task EvaluatesEveryConfigItHoldsAtOnce()
    {
        await using var client = await ReadyClientAsync();

        var all = client.GetAllConfigs(ProUser);

        all.Keys.ShouldBe(
            [
                "temporary-feature-flag",
                "permanent-kill-switch",
                "integer-config",
                "day-of-the-week-config",
                "json-value-config",
            ],
            ignoreOrder: true);
        all["temporary-feature-flag"].Value.ShouldBe("true");
        all["temporary-feature-flag"].ValueId.ShouldBe("temporary-feature-flag-on");
        all["temporary-feature-flag"].Type.ShouldBe(ConfigType.Boolean);
        all["integer-config"].Value.ShouldBe("25");
    }

    [Fact]
    public async Task NarrowsGetAllConfigsToTheKeysItWasAskedFor()
    {
        await using var client = await ReadyClientAsync();

        var some = client.GetAllConfigs(ProUser, ["integer-config", "never-heard-of-it", "integer-config"]);

        some.Keys.ShouldBe(["integer-config"]);
    }

    [Fact]
    public async Task RejectsABlankConfigKey()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentNullException>(() => client.GetValue(null!, "fallback"));
        Should.Throw<ArgumentException>(() => client.GetValue("  ", "fallback"));
    }

    [Fact]
    public async Task RejectsAMissingDefaultValue()
    {
        await using var client = await ReadyClientAsync();

        Should.Throw<ArgumentNullException>(() => client.GetValue<string>("day-of-the-week-config", null!));
    }

    [Fact]
    public async Task ServesDefaultsOnceItIsDisposed()
    {
        var client = await ReadyClientAsync();
        await client.DisposeAsync();

        client.IsClosed.ShouldBeTrue();
        client.IsReady.ShouldBeFalse();
        client.GetValue("integer-config", 7).ShouldBe(7);
        client.GetAllConfigs().ShouldBeEmpty();
        await Should.ThrowAsync<ObjectDisposedException>(client.InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ToleratesBeingDisposedTwice()
    {
        var client = await ReadyClientAsync();

        await client.DisposeAsync();
        await client.DisposeAsync();
        client.Dispose();

        client.IsClosed.ShouldBeTrue();
    }

    private async Task<ConfigDirectorClient> ReadyClientAsync(ConfigDirectorClientOptions? options = null)
    {
        var client = Client(options);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        return client;
    }

    private ConfigDirectorClient Client(ConfigDirectorClientOptions? options = null)
    {
        var settings = options ?? new ConfigDirectorClientOptions();
        _server.Attach(settings);
        return new ConfigDirectorClient("server-sdk-key", settings);
    }

    public void Dispose() => _server.Dispose();

    private static ConfigDirectorClientOptions Version(string appVersion) =>
        new() { Metadata = new Metadata { AppName = "sample", AppVersion = appVersion } };

    private sealed record RetrySettings
    {
        public int Retries { get; init; }

        public int TimeoutMs { get; init; }
    }
}
