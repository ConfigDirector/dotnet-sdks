namespace ConfigDirector.Tests.Integration;

// Drives the whole SDK through its public API against the config state the transport supplies,
// with nothing inside the SDK substituted.
public class ClientIntegrationTests
{
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
        await using var client = new ConfigDirectorClient("server-sdk-key");

        client.IsReady.ShouldBeFalse();
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        client.IsClosed.ShouldBeFalse();
    }

    [Fact]
    public async Task ReturnsTheDefaultBeforeItIsInitialized()
    {
        await using var client = new ConfigDirectorClient("server-sdk-key");

        client.GetValue("new-checkout", true).ShouldBeTrue();
        client.GetAllConfigs().ShouldBeEmpty();
    }

    [Fact]
    public async Task EvaluatesABooleanAgainstTheContextsTraits()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("new-checkout", false, ProUser).ShouldBeTrue();
        client.GetValue("new-checkout", true, FreeUser).ShouldBeFalse();
    }

    [Fact]
    public async Task FallsBackToTheConfigsDefaultWhenNoRuleMatches()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("checkout-banner", "unused").ShouldBe("Welcome back");
    }

    [Fact]
    public async Task PrefersTheEarlierRuleWhenSeveralMatch()
    {
        await using var client = await ReadyClientAsync(Version("2.5.0"));
        var betaUser = new Context { Id = "user-1", Traits = { ["tags"] = new[] { "beta" } } };

        client.GetValue("checkout-banner", "unused", betaUser).ShouldBe("Welcome to the beta");
    }

    [Fact]
    public async Task EvaluatesAgainstTheApplicationMetadataItWasBuiltWith()
    {
        await using var modern = await ReadyClientAsync(Version("2.5.0"));
        await using var legacy = await ReadyClientAsync(Version("1.9.0"));

        modern.GetValue("checkout-banner", "unused").ShouldBe("Welcome to the new checkout");
        legacy.GetValue("checkout-banner", "unused").ShouldBe("Welcome back");
    }

    [Fact]
    public async Task ReadsWholeAndFractionalNumbers()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("max-cart-items", 0).ShouldBe(25);
        client.GetValue("max-cart-items", 0L).ShouldBe(25L);
        client.GetValue("discount-rate", 0d).ShouldBe(0.15);
    }

    [Fact]
    public async Task ReadsAJsonConfigIntoTheCallersOwnType()
    {
        await using var client = await ReadyClientAsync();

        var settings = client.GetValue("checkout-settings", new CheckoutSettings());

        settings.Retries.ShouldBe(3);
        settings.TimeoutMs.ShouldBe(1500);
    }

    [Fact]
    public async Task PutsTheSameContextInTheSameBucketEveryTime()
    {
        await using var client = await ReadyClientAsync();

        client.GetValue("checkout-experiment", "unused", ProUser).ShouldBe("variant");
        client.GetValue("checkout-experiment", "unused", ProUser).ShouldBe("variant");
        client.GetValue("checkout-experiment", "unused", FreeUser).ShouldBe("control");
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

        client.GetValue("checkout-banner", -1).ShouldBe(-1);
        client.GetValue("checkout-banner", false).ShouldBeFalse();
        client.GetValue("max-cart-items", new CheckoutSettings { Retries = 9 }).Retries.ShouldBe(9);
    }

    [Fact]
    public async Task EvaluatesEveryConfigItHoldsAtOnce()
    {
        await using var client = await ReadyClientAsync();

        var all = client.GetAllConfigs(ProUser);

        all.Keys.ShouldBe(
            [
                "new-checkout",
                "checkout-banner",
                "max-cart-items",
                "discount-rate",
                "checkout-settings",
                "checkout-experiment",
            ],
            ignoreOrder: true);
        all["new-checkout"].Value.ShouldBe("true");
        all["new-checkout"].ValueId.ShouldBe("new-checkout-on");
        all["new-checkout"].Type.ShouldBe(ConfigType.Boolean);
        all["max-cart-items"].Value.ShouldBe("25");
    }

    [Fact]
    public async Task NarrowsGetAllConfigsToTheKeysItWasAskedFor()
    {
        await using var client = await ReadyClientAsync();

        var some = client.GetAllConfigs(ProUser, ["max-cart-items", "never-heard-of-it", "max-cart-items"]);

        some.Keys.ShouldBe(["max-cart-items"]);
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

        Should.Throw<ArgumentNullException>(() => client.GetValue<string>("checkout-banner", null!));
    }

    [Fact]
    public async Task ServesDefaultsOnceItIsDisposed()
    {
        var client = await ReadyClientAsync();
        await client.DisposeAsync();

        client.IsClosed.ShouldBeTrue();
        client.IsReady.ShouldBeFalse();
        client.GetValue("max-cart-items", 7).ShouldBe(7);
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

    private static async Task<ConfigDirectorClient> ReadyClientAsync(ConfigDirectorClientOptions? options = null)
    {
        var client = new ConfigDirectorClient("server-sdk-key", options);
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        return client;
    }

    private static ConfigDirectorClientOptions Version(string appVersion) =>
        new() { Metadata = new Metadata { AppName = "checkout", AppVersion = appVersion } };

    private sealed record CheckoutSettings
    {
        public int Retries { get; init; }

        public int TimeoutMs { get; init; }
    }
}
