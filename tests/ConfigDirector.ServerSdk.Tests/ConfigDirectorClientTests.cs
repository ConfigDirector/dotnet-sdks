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
        var client = new ConfigDirectorClient("server-sdk-key", null, onBundle =>
        {
            transport.Publish = onBundle;
            return transport;
        });

        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.IsReady.ShouldBeTrue();

        await client.DisposeAsync();
        transport.Publish!(LateBundle);

        client.IsReady.ShouldBeFalse();
        client.GetAllConfigs().ShouldBeEmpty();
    }

    private static ConfigDirectorClient StalledClient(ConfigDirectorClientOptions options) =>
        new("server-sdk-key", options, _ => new StalledTransport());

    private static readonly ConfigBundle LateBundle = new()
    {
        Configs = new Dictionary<string, Config>(StringComparer.Ordinal)
        {
            ["arrived-too-late"] = new Config { Id = "late", Key = "arrived-too-late" },
        },
    };

    private sealed class DeferredTransport : ITransport
    {
        internal Action<ConfigBundle>? Publish { get; set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            Publish!(new ConfigBundle());
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
