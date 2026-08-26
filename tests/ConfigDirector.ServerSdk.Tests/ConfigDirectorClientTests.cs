using ConfigDirector.Evaluation;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests;

public class ConfigDirectorClientTests
{
    [Fact]
    public void RejectsAMissingServerSdkKey() =>
        Should.Throw<ArgumentNullException>(() => new ConfigDirectorClient(null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankServerSdkKey(string key) =>
        Should.Throw<ArgumentException>(() => new ConfigDirectorClient(key));

    // Bounded, so a client that ignores its own timeout fails here rather than hanging the suite.
    [Fact(Timeout = 10_000)]
    public async Task GivesUpOnInitializationWhenTheTimeoutElapses()
    {
        var loggerFactory = new CapturingLoggerFactory();
        await using var client = StalledClient(new ConfigDirectorClientOptions
        {
            LoggerFactory = loggerFactory,
            Connection = { Timeout = TimeSpan.FromMilliseconds(20) },
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeFalse();
        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Timed out", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SurfacesTheCallersCancellationRatherThanTreatingItAsATimeout()
    {
        await using var client = StalledClient(new ConfigDirectorClientOptions
        {
            Connection = { Timeout = TimeSpan.FromMinutes(5) },
        });

        using var caller = new CancellationTokenSource();
        var initializing = client.InitializeAsync(caller.Token);
        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(initializing);
    }

    [Fact]
    public async Task IgnoresConfigStateThatArrivesAfterItIsDisposed()
    {
        var transport = new DeferredTransport();
        var client = ClientOn(transport);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.IsReady.ShouldBeTrue();

        await client.DisposeAsync();
        transport.Publish!(LateBundle);

        client.IsReady.ShouldBeFalse();
        client.GetAllConfigs().ShouldBeEmpty();
    }

    private static ConfigDirectorClient StalledClient(ConfigDirectorClientOptions options) =>
        new("server-sdk-key", options, _ => new StalledTransport());

    [Fact]
    public async Task AnnouncesItselfReadyOnlyForTheFirstConfigState()
    {
        var transport = new DeferredTransport();
        await using var client = ClientOn(transport);
        var announced = 0;
        var updates = 0;
        client.ClientReady += (_, _) => announced++;
        client.ConfigsUpdated += (_, _) => updates++;

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        transport.Publish!(BundleOf(("greeting", "hola")));

        announced.ShouldBe(1);
        updates.ShouldBe(2);
    }

    [Fact]
    public async Task NotifiesAWatchOnlyForTheKeysAnUpdateCarried()
    {
        var transport = new DeferredTransport(BundleOf(("greeting", "hello"), ("farewell", "bye")));
        await using var client = ClientOn(transport);
        var greetings = new List<string>();
        var farewells = new List<string>();
        client.Watch("greeting", "unused", greetings.Add);
        client.Watch("farewell", "unused", farewells.Add);

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        transport.Publish!(BundleOf(("greeting", "hola")));

        greetings.ShouldBe(["hello", "hola"]);
        farewells.ShouldBe(["bye"]);
    }

    [Fact]
    public async Task RaisesNothingForConfigStateThatArrivesAfterItIsDisposed()
    {
        var transport = new DeferredTransport();
        var client = ClientOn(transport);
        var updates = 0;
        client.ConfigsUpdated += (_, _) => updates++;

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await client.DisposeAsync();
        transport.Publish!(BundleOf(("arrived-too-late", "ignored")));

        updates.ShouldBe(1);
    }

    private static ConfigDirectorClient ClientOn(DeferredTransport transport) =>
        new("server-sdk-key", null, onBundle =>
        {
            transport.Publish = onBundle;
            return transport;
        });

    private static readonly ConfigBundle LateBundle = BundleOf(("arrived-too-late", "ignored"));

    private static ConfigBundle BundleOf(params (string Key, string Value)[] configs) =>
        new()
        {
            Configs = configs.ToDictionary(
                config => config.Key,
                config => new Config
                {
                    Id = config.Key,
                    Key = config.Key,
                    Target = new TargetingRules
                    {
                        DefaultValue = config.Value,
                        DefaultValueId = config.Key + "-default",
                    },
                },
                StringComparer.Ordinal),
        };

    private sealed class DeferredTransport(ConfigBundle? onConnect = null) : ITransport
    {
        internal Action<ConfigBundle>? Publish { get; set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Publish!(onConnect ?? new ConfigBundle());
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => default;
    }

    private sealed class StalledTransport : ITransport
    {
        public Task ConnectAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);

        public ValueTask DisposeAsync() => default;
    }
}
