using ConfigDirector.Tests.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace ConfigDirector.AspNetCore.Tests;

public sealed class HealthCheckTests : IDisposable
{
    private readonly SdkServer _server = new();

    [Fact]
    public async Task ReportsHealthyOnceConfigStateHasArrived()
    {
        using var host = BuildHost();
        await host.StartAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(host)).Status.ShouldBe(HealthStatus.Healthy);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportsDegradedWhileNoConfigStateHasArrived()
    {
        using var host = BuildHost(url: SdkServer.UnreachableUrl);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var report = await CheckAsync(host);

        report.Status.ShouldBe(HealthStatus.Degraded);
        report.Entries["configdirector"].Description!.ShouldContain("default its caller supplied");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportsTheFailureStatusTheCallerAskedFor()
    {
        using var host = BuildHost(url: SdkServer.UnreachableUrl, failureStatus: HealthStatus.Unhealthy);
        await host.StartAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(host)).Status.ShouldBe(HealthStatus.Unhealthy);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReportsUnhealthyOnceTheClientIsClosedWhateverTheFailureStatus()
    {
        using var host = BuildHost(failureStatus: HealthStatus.Degraded);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.Services.GetRequiredService<IConfigDirectorClient>().DisposeAsync();

        var report = await CheckAsync(host);

        report.Status.ShouldBe(HealthStatus.Unhealthy);
        report.Entries["configdirector"].Description!.ShouldContain("cannot be reopened");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RegistersUnderTheNameTheCallerAskedFor()
    {
        using var host = BuildHost(name: "flags");
        await host.StartAsync(TestContext.Current.CancellationToken);

        (await CheckAsync(host)).Entries.Keys.ShouldContain("flags");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose() => _server.Dispose();

    private static Task<HealthReport> CheckAsync(IHost host) =>
        host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

    private IHost BuildHost(
        Uri? url = null,
        string name = "configdirector",
        HealthStatus failureStatus = HealthStatus.Degraded)
    {
        var builder = Host.CreateEmptyApplicationBuilder(
            new HostApplicationBuilderSettings { ApplicationName = "checkout" });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConfigDirector:ServerSdkKey"] = "a-key",
            ["ConfigDirector:Connection:Url"] = (url ?? _server.BaseUrl).ToString(),
            ["ConfigDirector:Connection:Mode"] = "Polling",
            ["ConfigDirector:Connection:Timeout"] = "00:00:05",
        });

        builder.Services.AddConfigDirector();
        builder.Services.AddHealthChecks().AddConfigDirector(name, failureStatus);

        return builder.Build();
    }
}
