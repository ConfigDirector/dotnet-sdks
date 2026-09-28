using ConfigDirector.Transport;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Tests;

// What a client rejects or settles before it ever connects. Everything that needs a server is an
// integration test, driven through the public API against a stubbed ConfigDirector.
public sealed class ConfigDirectorClientTests : IDisposable
{
    private readonly CapturingLoggerFactory _loggerFactory = new();
    private TransportOptions? _transportOptions;

    public void Dispose() => _loggerFactory.Dispose();

    [Fact]
    public void RejectsAMissingServerSdkKey() =>
        Should.Throw<ArgumentNullException>(() => new ConfigDirectorClient(null!));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankServerSdkKey(string key) =>
        Should.Throw<ArgumentException>(() => new ConfigDirectorClient(key));

    [Fact]
    public async Task PollsEveryFiveMinutesWhenNoIntervalIsGiven()
    {
        await using var client = Client(ConnectionMode.Polling);

        _transportOptions!.PollingInterval.ShouldBe(TimeSpan.FromMinutes(5));
        Warnings().ShouldBeEmpty();
    }

    [Fact]
    public async Task RaisesAPollingIntervalBelowTheMinimumAndWarnsOnce()
    {
        var options = Options(ConnectionMode.Polling, TimeSpan.FromSeconds(10));

        await using var client = Client(options);

        _transportOptions!.PollingInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.Connection.PollingInterval.ShouldBe(TimeSpan.FromSeconds(10));
        Warnings().ShouldBe(
            ["PollingInterval of 00:00:10 is below the minimum of 00:01:00. Using 00:01:00."]);
    }

    [Fact]
    public async Task PollsOnExactlyTheMinimumWithoutComment()
    {
        await using var client = Client(ConnectionMode.Polling, TimeSpan.FromMinutes(1));

        _transportOptions!.PollingInterval.ShouldBe(TimeSpan.FromMinutes(1));
        Warnings().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RaisesAPollingIntervalThatIsNotPositiveToTheMinimum(int seconds)
    {
        await using var client = Client(ConnectionMode.Polling, TimeSpan.FromSeconds(seconds));

        _transportOptions!.PollingInterval.ShouldBe(TimeSpan.FromMinutes(1));
        Warnings().Count.ShouldBe(1);
        Warnings()[0].ShouldContain("below the minimum");
    }

    [Fact]
    public async Task SaysNothingAboutThePollingIntervalWhenStreaming()
    {
        await using var client = Client(ConnectionMode.Streaming, TimeSpan.FromSeconds(10));

        _loggerFactory.Logger.Entries.ShouldBeEmpty();
    }

    private ConfigDirectorClientOptions Options(ConnectionMode mode, TimeSpan? pollingInterval)
    {
        var options = new ConfigDirectorClientOptions
        {
            LoggerFactory = _loggerFactory,
            Connection = { Mode = mode },
        };
        if (pollingInterval is { } interval)
        {
            options.Connection.PollingInterval = interval;
        }

        return options;
    }

    private ConfigDirectorClient Client(ConnectionMode mode, TimeSpan? pollingInterval = null) =>
        Client(Options(mode, pollingInterval));

    private ConfigDirectorClient Client(ConfigDirectorClientOptions options) =>
        new("server-sdk-key", options, SdkIdentity.ServerSdk, CaptureTransportOptions);

    private IdleTransport CaptureTransportOptions(ConnectionMode mode, TransportOptions transportOptions)
    {
        _transportOptions = transportOptions;
        return new IdleTransport();
    }

    private List<string> Warnings() =>
        _loggerFactory.Logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => entry.Message)
            .ToList();

    private sealed class IdleTransport : ITransport
    {
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }
}
