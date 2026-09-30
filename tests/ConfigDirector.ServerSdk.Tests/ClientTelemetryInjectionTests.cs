using ConfigDirector.Telemetry;
using ConfigDirector.Transport;

namespace ConfigDirector.Tests;

public sealed class ClientTelemetryInjectionTests : IDisposable
{
    private readonly CapturingLoggerFactory _loggerFactory = new();
    private readonly RecordingTelemetryCollector _collector = new();
    private readonly List<TelemetryCollectorOptions> _receivedOptions = [];

    public void Dispose() => _loggerFactory.Dispose();

    [Fact]
    public async Task EvaluationsAreRecordedThroughTheCollectorTheFactoryReturned()
    {
        await using var client = Client(new ConfigDirectorClientOptions { LoggerFactory = _loggerFactory });

        var value = client.GetValue("flag", true);

        value.ShouldBeTrue();
        _collector.Recorded.Count.ShouldBe(1);
        var recorded = _collector.Recorded[0];
        recorded.Key.ShouldBe("flag");
        recorded.DefaultValue.ShouldBe(true);
        recorded.UsedDefault.ShouldBeTrue();
        recorded.Reason.ShouldBe(EvaluationReason.ClientNotReady);
        recorded.ConfigType.ShouldBeNull();
    }

    [Fact]
    public async Task TheFactoryReceivesTheSettingsTheClientWasBuiltWith()
    {
        await using var client = Client(new ConfigDirectorClientOptions
        {
            LoggerFactory = _loggerFactory,
            Metadata = new Metadata { AppName = "checkout", AppVersion = "1.2.3" },
            Telemetry = { EventQueueLimit = 123, FlushInterval = TimeSpan.FromSeconds(42) },
        });

        _receivedOptions.Count.ShouldBe(1);
        var options = _receivedOptions[0];
        options.ServerSdkKey.ShouldBe("server-sdk-key");
        options.BaseUrl.ShouldBe(Transports.DefaultBaseUrl);
        options.Identity.ShouldBeSameAs(SdkIdentity.ServerSdk);
        options.LoggerFactory.ShouldBeSameAs(_loggerFactory);
        options.Metadata!.AppName.ShouldBe("checkout");
        options.Metadata.AppVersion.ShouldBe("1.2.3");
        options.EventQueueLimit.ShouldBe(123);
        options.FlushInterval.ShouldBe(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task DisposingTheClientDisposesTheCollector()
    {
        var client = Client(new ConfigDirectorClientOptions { LoggerFactory = _loggerFactory });

        await client.DisposeAsync();

        _collector.Disposed.ShouldBeTrue();
    }

    private ConfigDirectorClient Client(ConfigDirectorClientOptions options) =>
        new(
            "server-sdk-key",
            options,
            SdkIdentity.ServerSdk,
            (_, _) => new IdleTransport(),
            telemetryOptions =>
            {
                _receivedOptions.Add(telemetryOptions);
                return _collector;
            });

    private sealed record RecordedEvaluation(
        string Key, object? DefaultValue, bool UsedDefault, EvaluationReason Reason, ConfigType? ConfigType);

    private sealed class RecordingTelemetryCollector : ITelemetryCollector
    {
        internal List<RecordedEvaluation> Recorded { get; } = [];

        internal bool Disposed { get; private set; }

        public void Record<T>(
            string key,
            T defaultValue,
            T value,
            bool usedDefault,
            EvaluationReason reason,
            Context? context,
            ConfigType? configType,
            string? valueId) =>
            Recorded.Add(new RecordedEvaluation(key, defaultValue, usedDefault, reason, configType));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }

    private sealed class IdleTransport : ITransport
    {
        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }
}
