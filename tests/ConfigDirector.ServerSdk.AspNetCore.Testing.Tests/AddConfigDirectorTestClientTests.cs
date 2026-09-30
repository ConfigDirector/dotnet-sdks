using ConfigDirector.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ConfigDirector.AspNetCore.Testing.Tests;

public sealed class AddConfigDirectorTestClientTests : IAsyncDisposable
{
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongerThanTheTestWaits = TimeSpan.FromSeconds(30);

    private readonly List<TestClient> _testClients = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var testClient in _testClients)
        {
            await testClient.DisposeAsync();
        }
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private TestClient Create(TimeSpan? timeout = null)
    {
        var testClient = ConfigDirectorTesting.CreateTestClient(
            new Dictionary<string, object>(StringComparer.Ordinal) { ["new-checkout"] = true },
            new TestClientOptions { Timeout = timeout });
        _testClients.Add(testClient);
        return testClient;
    }

    [Fact]
    public async Task S27AWebApplicationFactoryHostServesTheTestClientsValues()
    {
        var testClient = Create();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.ConfigureTestServices(services => services.AddConfigDirectorTestClient(testClient)));

        using var http = factory.CreateClient();
        var response = await http.GetStringAsync(new Uri("/checkout", UriKind.Relative), Cancellation);

        response.ShouldBe("new");
        var client = factory.Services.GetRequiredService<IConfigDirectorClient>();
        client.ShouldBeSameAs(testClient.Client);
        client.IsReady.ShouldBeTrue();

        testClient.SetValue("new-checkout", false);

        (await http.GetStringAsync(new Uri("/checkout", UriKind.Relative), Cancellation)).ShouldBe("classic");
    }

    [Fact]
    public async Task ReplacesTheClientAddConfigDirectorRegisteredBeforeIt()
    {
        var testClient = Create();
        using var host = BuildHost(services =>
        {
            services.AddConfigDirector();
            services.AddConfigDirectorTestClient(testClient);
        });

        await host.StartAsync(Cancellation);

        host.Services.GetRequiredService<IConfigDirectorClient>().ShouldBeSameAs(testClient.Client);
        host.Services.GetServices<IConfigDirectorClient>().Count().ShouldBe(1);
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task KeepsTheTestClientWhenAddConfigDirectorIsCalledAfterIt()
    {
        var testClient = Create();
        using var host = BuildHost(services =>
        {
            services.AddConfigDirectorTestClient(testClient);
            services.AddConfigDirector();
        });

        await host.StartAsync(Cancellation);

        host.Services.GetRequiredService<IConfigDirectorClient>().ShouldBeSameAs(testClient.Client);
        host.Services.GetServices<IConfigDirectorClient>().Count().ShouldBe(1);
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task HostStartupInitializesTheTestClient()
    {
        var testClient = Create();
        using var host = BuildHost(services =>
        {
            services.AddConfigDirector();
            services.AddConfigDirectorTestClient(testClient);
        });
        testClient.Client.IsReady.ShouldBeFalse();

        await host.StartAsync(Cancellation);

        testClient.Client.IsReady.ShouldBeTrue();
        testClient.Client.GetValue("new-checkout", false).ShouldBeTrue();
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task TheHostDoesNotDisposeTheTestClient()
    {
        var testClient = Create();
        var host = BuildHost(services =>
        {
            services.AddConfigDirector();
            services.AddConfigDirectorTestClient(testClient);
        });
        await host.StartAsync(Cancellation);
        await host.StopAsync(Cancellation);

        host.Dispose();

        testClient.Client.IsClosed.ShouldBeFalse();
        testClient.SetValue("new-checkout", false);
        testClient.Client.GetValue("new-checkout", true).ShouldBeFalse();
    }

    [Fact]
    public void SuppliesAPlaceholderSdkKeyWhenNoneIsConfigured()
    {
        var testClient = Create();
        using var host = BuildHost(services =>
        {
            services.AddConfigDirector();
            services.AddConfigDirectorTestClient(testClient);
        });

        var settings = host.Services.GetRequiredService<IOptions<ConfigDirectorOptions>>().Value;

        settings.ServerSdkKey.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LeavesAConfiguredSdkKeyAlone()
    {
        var testClient = Create();
        using var host = BuildHost(
            services =>
            {
                services.AddConfigDirector();
                services.AddConfigDirectorTestClient(testClient);
            },
            ("ConfigDirector:ServerSdkKey", "a-key"));

        var settings = host.Services.GetRequiredService<IOptions<ConfigDirectorOptions>>().Value;

        settings.ServerSdkKey.ShouldBe("a-key");
    }

    [Fact]
    public async Task AHeldInitializationHoldsHostStartupUntilItIsCompleted()
    {
        var testClient = Create(LongerThanTheTestWaits);
        using var host = BuildHost(services =>
        {
            services.AddConfigDirector();
            services.AddConfigDirectorTestClient(testClient);
        });
        testClient.HoldInitialization();

        var starting = host.StartAsync(Cancellation);
        await Task.Delay(100, Cancellation);
        starting.IsCompleted.ShouldBeFalse();

        await Task.Run(testClient.CompleteInitialization, Cancellation);

        await starting.WaitAsync(Promptly, Cancellation);
        testClient.Client.IsReady.ShouldBeTrue();
        await host.StopAsync(Cancellation);
    }

    [Fact]
    public async Task AFailedInitializationFailsHostStartupWhenReadinessIsRequired()
    {
        var testClient = Create();
        using var host = BuildHost(
            services =>
            {
                services.AddConfigDirector();
                services.AddConfigDirectorTestClient(testClient);
            },
            ("ConfigDirector:RequireReadyOnStartup", "true"));
        testClient.FailInitialization();

        await Should.ThrowAsync<ConfigDirectorConnectionException>(() => host.StartAsync(Cancellation));
    }

    [Fact]
    public void RegistersTheTestClientWithoutAddConfigDirector()
    {
        var testClient = Create();
        using var host = BuildHost(services => services.AddConfigDirectorTestClient(testClient));

        host.Services.GetRequiredService<IConfigDirectorClient>().ShouldBeSameAs(testClient.Client);
    }

    [Fact]
    public void RejectsNullArguments()
    {
        var testClient = Create();

        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddConfigDirectorTestClient(null!));
        Should.Throw<ArgumentNullException>(() => ((IServiceCollection)null!).AddConfigDirectorTestClient(testClient));
    }

    private static IHost BuildHost(Action<IServiceCollection> add, params (string Key, string Value)[] configuration)
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = "checkout" });

        builder.Configuration.AddInMemoryCollection(
            configuration.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)));

        add(builder.Services);

        return builder.Build();
    }
}
