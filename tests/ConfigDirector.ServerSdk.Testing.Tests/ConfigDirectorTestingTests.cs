using System.Diagnostics;
using ConfigDirector.Tests;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.Testing.Tests;

public sealed class ConfigDirectorTestingTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatesAnEmptyUninitializedTestClientByDefault()
    {
        await using var testClient = ConfigDirectorTesting.CreateTestClient();

        testClient.Client.IsReady.ShouldBeFalse();
        testClient.Client.IsClosed.ShouldBeFalse();
        await testClient.Client.InitializeAsync(Cancellation);
        testClient.Client.IsReady.ShouldBeTrue();
        testClient.Client.GetAllConfigs().ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedsTheGivenValues()
    {
        await using var testClient = ConfigDirectorTesting.CreateTestClient(
            new Dictionary<string, object>(StringComparer.Ordinal) { ["new-checkout"] = true, ["max-items"] = 20 });

        await testClient.Client.InitializeAsync(Cancellation);

        testClient.Client.GetValue("new-checkout", false).ShouldBeTrue();
        testClient.Client.GetValue("max-items", 0).ShouldBe(20);
    }

    [Fact]
    public void OptionsDefaultToTheSdksOwnTimeoutAndLogging()
    {
        var options = new TestClientOptions();

        options.Timeout.ShouldBeNull();
        options.LoggerFactory.ShouldBeNull();
    }

    [Fact]
    public async Task TheTimeoutBoundsAHeldInitialize()
    {
        await using var testClient = ConfigDirectorTesting.CreateTestClient(
            options: new TestClientOptions { Timeout = TimeSpan.FromMilliseconds(200) });
        testClient.HoldInitialization();

        var stopwatch = Stopwatch.StartNew();
        await testClient.Client.InitializeAsync(Cancellation);

        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        testClient.Client.IsReady.ShouldBeFalse();
    }

    [Fact]
    public async Task TheLoggerFactoryReceivesTheClientsLogs()
    {
        using var loggerFactory = new CapturingLoggerFactory();
        await using var testClient = ConfigDirectorTesting.CreateTestClient(
            options: new TestClientOptions { LoggerFactory = loggerFactory });
        testClient.FailInitialization();

        await testClient.Client.InitializeAsync(Cancellation);

        loggerFactory.Logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("Connection failed with status: 401", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DisposingTheTestClientDisposesItsClient()
    {
        var testClient = ConfigDirectorTesting.CreateTestClient();

        await testClient.DisposeAsync();

        testClient.Client.IsClosed.ShouldBeTrue();
        await Should.NotThrowAsync(async () => await testClient.DisposeAsync());
    }
}
