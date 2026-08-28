using System.Text.Json;
using ConfigDirector.Tests;
using ConfigDirector.Tests.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.AspNetCore.Tests;

// Drives the package the way a host does, against the same stub server the SDK's own integration
// tests use: the transports, the bundle parser and the telemetry reporter all run as shipped.
public sealed class InitializationTests : IDisposable
{
    private readonly SdkServer _server = new();

    [Fact]
    public async Task ConnectsWhileTheHostIsStarting()
    {
        using var host = BuildHost();
        var client = host.Services.GetRequiredService<IConfigDirectorClient>();

        client.IsReady.ShouldBeFalse();
        await host.StartAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        client.GetAllConfigs().ShouldNotBeEmpty();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IsReadyBeforeTheFirstHostedServiceStarts()
    {
        bool? readyWhenServicesStarted = null;

        using var host = BuildHost(add: services =>
            // Registered ahead of AddConfigDirector, which is where the web host's own hosted
            // service sits: ASP.NET Core adds it while the builder is constructed, and hosted
            // services start in registration order. Anything that depends on config being present
            // by the time it starts has to see a ready client here.
            services.AddHostedService(provider => new StartupProbe(
                provider.GetRequiredService<IConfigDirectorClient>(),
                ready => readyWhenServicesStarted = ready)));

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        readyWhenServicesStarted.ShouldBe(true);
    }

    [Fact]
    public async Task StartsTheHostWhenConfigDirectorCannotBeReached()
    {
        using var host = BuildHost(url: SdkServer.UnreachableUrl);

        await host.StartAsync(TestContext.Current.CancellationToken);

        host.Services.GetRequiredService<IConfigDirectorClient>().IsReady.ShouldBeFalse();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WarnsThroughTheHostsOwnLoggingWhenConfigStateNeverArrives()
    {
        var logging = new CapturingLoggerFactory();
        using var host = BuildHost(url: SdkServer.UnreachableUrl, loggerFactory: logging);

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        // "Configs will return their default" alone would match the SDK's own initialization
        // warning, which this factory captures too: it hands the same logger to every category.
        logging.Logger.Entries
            .Count(entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains(
                    "No config state arrived from ConfigDirector during startup",
                    StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task FailsTheHostWhenReadinessIsRequiredAndNoConfigStateArrives()
    {
        using var host = BuildHost(url: SdkServer.UnreachableUrl, requireReady: true);

        await Should.ThrowAsync<ConfigDirectorConnectionException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartsTheHostWhenReadinessIsRequiredAndConfigStateArrives()
    {
        using var host = BuildHost(requireReady: true);

        await host.StartAsync(TestContext.Current.CancellationToken);

        host.Services.GetRequiredService<IConfigDirectorClient>().IsReady.ShouldBeTrue();

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AppliesTheBoundTelemetryLimitToTheClient()
    {
        using var host = BuildHost(eventQueueLimit: TelemetryOptions.MinEventQueueLimit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var client = host.Services.GetRequiredService<IConfigDirectorClient>();

        // Seventy of the hundred are kept for evaluations, so ten of these are dropped. At the
        // default limit of five thousand, none would be.
        for (var index = 0; index < 80; index++)
        {
            client.GetValue("integer-config", index);
        }

        await host.StopAsync(TestContext.Current.CancellationToken);
        await client.DisposeAsync();

        await WaitAsync(() => _server.Paths.Contains("/server/telemetry/v1"));
        Payload().GetProperty("droppedEvents").GetProperty("evaluatedConfig").GetInt32().ShouldBe(10);
    }

    public void Dispose() => _server.Dispose();

    private IHost BuildHost(
        Action<IServiceCollection>? add = null,
        Uri? url = null,
        bool requireReady = false,
        int eventQueueLimit = TelemetryOptions.DefaultEventQueueLimit,
        ILoggerFactory? loggerFactory = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = "checkout" });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConfigDirector:ServerSdkKey"] = "a-key",
            ["ConfigDirector:RequireReadyOnStartup"] = requireReady ? "true" : "false",
            ["ConfigDirector:Connection:Url"] = (url ?? _server.BaseUrl).ToString(),
            ["ConfigDirector:Connection:Mode"] = "OneTime",
            ["ConfigDirector:Connection:Timeout"] = "00:00:05",
            ["ConfigDirector:Telemetry:FlushInterval"] = "00:05:00",
            ["ConfigDirector:Telemetry:EventQueueLimit"] =
                eventQueueLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

        add?.Invoke(builder.Services);
        builder.Services.AddConfigDirector();

        if (loggerFactory is not null)
        {
            builder.Services.AddSingleton(loggerFactory);
        }

        return builder.Build();
    }

    private JsonElement Payload() =>
        JsonDocument.Parse(_server.Bodies[_server.Paths.IndexOf("/server/telemetry/v1")])
            .RootElement.Clone();

    private static async Task WaitAsync(Func<bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!until() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue("the telemetry report never arrived");
    }

    private sealed class StartupProbe : IHostedService
    {
        private readonly IConfigDirectorClient _client;
        private readonly Action<bool> _record;

        internal StartupProbe(IConfigDirectorClient client, Action<bool> record)
        {
            _client = client;
            _record = record;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _record(_client.IsReady);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
