using ConfigDirector.Tests.Integration;

namespace ConfigDirector.Tests;

// Each class holds one behavior, so xUnit runs them alongside each other rather than end to end.
//
// Two constants deliberately have no test here. Transports.BuildHttpClient's infinite timeout and
// SseClient's disarming of the connect deadline both guard .NET Framework behavior that reaches
// the SDK only through the netstandard2.0 target. Measured on .NET 10, neither the HttpClient
// timeout nor the request's own token severs a response body that is still being read, so a test
// on this runtime would pass whether the code was there or not.

// Mutant: StreamingTransport.ReadTimeout shortened from 45 seconds. The server sends a keepalive
// every 15 seconds, so a shorter idle timeout tears down a healthy connection and reconnects.
public sealed class AQuietStreamIsNotMistakenForADeadOne : IDisposable
{
    private readonly SdkServer _server = new();

    [Fact]
    public async Task HoldsOneConnectionThroughASilenceShorterThanTheReadTimeout()
    {
        await using var client = Timings.Client(_server);
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        // Nothing is sent at all: well inside the 45 second timeout, and past any shorter one.
        await Timings.WaitAsync(TimeSpan.FromSeconds(40));

        _server.Requests.ShouldBe(1, "the stream was torn down and reopened during the silence");
    }

    public void Dispose() => _server.Dispose();
}

// Mutant: TelemetryCollector.EarliestFirstFlush lengthened from 5 seconds. A process that runs
// briefly still has to report what it evaluated, without waiting out a whole flush interval.
public sealed class TelemetryReportsEarlyForAShortLivedProcess : IDisposable
{
    private readonly SdkServer _server = new();

    [Fact]
    public async Task ReportsWellBeforeTheFirstIntervalWouldComeRound()
    {
        await using var client = Timings.Client(_server, TimeSpan.FromMinutes(10));
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        client.GetValue("integer-config", 0);

        await Timings.UntilAsync(
            () => _server.Paths.Contains("/server/telemetry/v1"),
            TimeSpan.FromSeconds(10));
    }

    public void Dispose() => _server.Dispose();
}

internal static class Timings
{
    internal static ConfigDirectorClient Client(SdkServer server, TimeSpan? flushInterval = null)
    {
        var options = new ConfigDirectorClientOptions();
        server.Attach(options);

        // Far enough out that no interval report can be mistaken for the behavior under test.
        options.Telemetry.FlushInterval = flushInterval ?? TimeSpan.FromMinutes(10);
        return new ConfigDirectorClient("sdk-key", options);
    }

    internal static Task WaitAsync(TimeSpan duration) =>
        Task.Delay(duration, TestContext.Current.CancellationToken);

    internal static async Task UntilAsync(Func<bool> condition, TimeSpan within)
    {
        var deadline = DateTime.UtcNow.Add(within);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue($"the condition was still false after {within}");
    }
}
