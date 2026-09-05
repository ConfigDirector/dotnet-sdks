using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ConfigDirector.AspNetCore.Tests;

public class AddConfigDirectorTests
{
    // Nothing in these tests should be able to reach the real service, so every client is pointed
    // at a port nothing listens on.
    private const string Nowhere = "http://127.0.0.1:1/";

    [Fact]
    public void BindsTheConfigDirectorSectionByConvention()
    {
        var settings = Settings(
            services => services.AddConfigDirector(),
            ("ConfigDirector:ServerSdkKey", "a-key"),
            ("ConfigDirector:AppVersion", "1.2.3"),
            ("ConfigDirector:Connection:Mode", "Polling"),
            ("ConfigDirector:Connection:PollingInterval", "00:00:30"),
            ("ConfigDirector:Connection:Timeout", "00:00:05"),
            ("ConfigDirector:Connection:Url", "https://proxy.example/"),
            ("ConfigDirector:Telemetry:FlushInterval", "00:00:10"),
            ("ConfigDirector:Telemetry:EventQueueLimit", "250"));

        settings.ServerSdkKey.ShouldBe("a-key");
        settings.AppVersion.ShouldBe("1.2.3");
        settings.Connection.Mode.ShouldBe(ConnectionMode.Polling);
        settings.Connection.PollingInterval.ShouldBe(TimeSpan.FromSeconds(30));
        settings.Connection.Timeout.ShouldBe(TimeSpan.FromSeconds(5));
        settings.Connection.Url.ShouldBe(new Uri("https://proxy.example/"));
        settings.Telemetry.FlushInterval.ShouldBe(TimeSpan.FromSeconds(10));
        settings.Telemetry.EventQueueLimit.ShouldBe(250);
    }

    [Fact]
    public void LeavesUnconfiguredSettingsAtTheSdkDefaults()
    {
        var settings = Settings(
            services => services.AddConfigDirector(),
            ("ConfigDirector:ServerSdkKey", "a-key"));

        settings.Connection.Mode.ShouldBe(ConnectionMode.Streaming);
        settings.Connection.Timeout.ShouldBe(TimeSpan.FromSeconds(3));
        settings.Connection.Url.ShouldBeNull();
        settings.Telemetry.EventQueueLimit.ShouldBe(TelemetryOptions.DefaultEventQueueLimit);
    }

    [Fact]
    public void BindsASectionNamedByTheCaller()
    {
        using var host = BuildHost(
            builder => builder.Services.AddConfigDirector(builder.Configuration.GetSection("Flags")),
            ("Flags:ServerSdkKey", "a-key"),
            ("Flags:Connection:Mode", "Polling"));

        var settings = host.Services.GetRequiredService<IOptions<ConfigDirectorOptions>>().Value;

        settings.ServerSdkKey.ShouldBe("a-key");
        settings.Connection.Mode.ShouldBe(ConnectionMode.Polling);
    }

    [Fact]
    public void AppliesCodeConfigurationOverTheBoundSection()
    {
        var settings = Settings(
            services => services.AddConfigDirector(options => options.ServerSdkKey = "from-code"),
            ("ConfigDirector:ServerSdkKey", "from-configuration"));

        settings.ServerSdkKey.ShouldBe("from-code");
    }

    [Fact]
    public void RejectsAMissingServerSdkKey()
    {
        var failure = Should.Throw<OptionsValidationException>(
            () => Settings(services => services.AddConfigDirector()));

        failure.Message.ShouldContain("ConfigDirector:ServerSdkKey");
    }

    [Fact]
    public void NamesTheCallersOwnSectionWhenTheKeyIsMissing()
    {
        var failure = Should.Throw<OptionsValidationException>(() =>
        {
            using var host = BuildHost(builder =>
                builder.Services.AddConfigDirector(builder.Configuration.GetSection("Flags")));

            return host.Services.GetRequiredService<IOptions<ConfigDirectorOptions>>().Value;
        });

        failure.Message.ShouldContain("Flags:ServerSdkKey");
    }

    [Fact]
    public void DescribesTheApplicationFromTheHostWhenNothingIsConfigured()
    {
        var settings = Settings(
            services => services.AddConfigDirector(),
            ("ConfigDirector:ServerSdkKey", "a-key"));

        settings.AppName.ShouldBe("checkout");
        settings.AppVersion.ShouldBe("9.9.9");
    }

    [Fact]
    public void KeepsAConfiguredApplicationNameOverTheHostDefault()
    {
        var settings = Settings(
            services => services.AddConfigDirector(),
            ("ConfigDirector:ServerSdkKey", "a-key"),
            ("ConfigDirector:AppName", "billing"));

        settings.AppName.ShouldBe("billing");
    }

    [Fact]
    public void ReportsAVersionWithoutItsBuildMetadata()
    {
        var settings = Settings(
            services => services.AddConfigDirector(),
            ("ConfigDirector:ServerSdkKey", "a-key"));

        // This assembly's InformationalVersion is 9.9.9+9f4c1a; the build metadata matches no
        // semver targeting rule, so it must not reach the SDK.
        settings.AppVersion.ShouldBe("9.9.9");
    }

    [Fact]
    public void RegistersOneClientForTheWholeApplication()
    {
        using var host = BuildHost(
            builder => builder.Services.AddConfigDirector(),
            ("ConfigDirector:ServerSdkKey", "a-key"),
            ("ConfigDirector:Connection:Url", Nowhere));

        var client = host.Services.GetRequiredService<IConfigDirectorClient>();

        client.ShouldBeSameAs(host.Services.GetRequiredService<IConfigDirectorClient>());
        client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public void LeavesAClientRegisteredBeforeItAlone()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfigDirectorClient>(_ => throw new InvalidOperationException("unused"));

        services.AddConfigDirector(options => options.ServerSdkKey = "a-key");

        services.Count(service => service.ServiceType == typeof(IConfigDirectorClient)).ShouldBe(1);
    }

    [Fact]
    public void RejectsAMissingConfigureDelegate() =>
        Should.Throw<ArgumentNullException>(
            () => new ServiceCollection().AddConfigDirector((Action<ConfigDirectorOptions>)null!));

    private static ConfigDirectorOptions Settings(
        Action<IServiceCollection> add, params (string Key, string Value)[] configuration)
    {
        using var host = BuildHost(builder => add(builder.Services), configuration);

        return host.Services.GetRequiredService<IOptions<ConfigDirectorOptions>>().Value;
    }

    private static IHost BuildHost(
        Action<HostApplicationBuilder> add, params (string Key, string Value)[] configuration)
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = "checkout" });

        builder.Configuration.AddInMemoryCollection(
            configuration.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)));

        add(builder);

        return builder.Build();
    }
}
