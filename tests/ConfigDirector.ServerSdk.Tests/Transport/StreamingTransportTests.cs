using System.Net;
using ConfigDirector.Tests.EventSource;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.Transport;

public class StreamingTransportTests
{
    private static readonly Uri BaseUrl = new("https://server-sdk-api.example.com/");

    private const string Bundle = """
        {"kind":"full","configs":{"integer-config":{"id":"i","key":"integer-config","target":{"defaultValue":25}}}}
        """;

    private static string Event(string data) => $"data: {data}\n\n";

    [Fact]
    public async Task ConnectsToTheStreamingEndpointAndSettlesOnTheFirstConfigState()
    {
        var bundles = new List<ConfigBundle>();
        var handler = new ScriptedHandler(() => ScriptedHandler.Silent(Event(Bundle)));
        await using var transport = Streaming(handler, bundles.Add);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].RequestUri.ShouldBe(new Uri("https://server-sdk-api.example.com/server/sse/v1"));
        handler.Requests[0].Headers.GetValues("Accept").ShouldBe(["text/event-stream"]);
        bundles.ShouldHaveSingleItem().Configs["integer-config"].Target.DefaultValue.ShouldBe("25");
    }

    [Fact]
    public async Task AppliesEveryUpdateTheStreamCarries()
    {
        var bundles = new List<ConfigBundle>();
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(
            TimeSpan.FromMilliseconds(30),
            Event(Bundle),
            Event("""{"kind":"delta","configs":{"other":{"id":"o","key":"other","target":{"defaultValue":"x"}}}}""")));
        await using var transport = Streaming(handler, bundles.Add);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await WaitForAsync(() => bundles.Count == 2);

        bundles[1].Kind.ShouldBe(BundleKind.Delta);
    }

    [Fact]
    public async Task IgnoresAFrameThatCarriesNoConfigState()
    {
        var bundles = new List<ConfigBundle>();
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(
            TimeSpan.FromMilliseconds(30),
            "event: heartbeat\ndata: {\"alive\":true}\n\n",
            Event(Bundle)));
        await using var transport = Streaming(handler, bundles.Add);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        // The heartbeat neither settled the connection nor cleared config state.
        bundles.ShouldHaveSingleItem().Configs.Keys.ShouldContain("integer-config");
    }

    [Fact]
    public async Task ReportsAStatusItCannotRecoverFrom()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Status(HttpStatusCode.Forbidden));
        await using var transport = Streaming(handler, _ => { });

        var failure = await Should.ThrowAsync<ConfigDirectorConnectionException>(
            transport.ConnectAsync(TestContext.Current.CancellationToken));

        failure.Status.ShouldBe(403);
        failure.Message.ShouldContain("unrecoverable");
    }

    [Fact]
    public async Task LeavesTheCallerWaitingWhileItRetriesAStatusThatMightRecover()
    {
        var bundles = new List<ConfigBundle>();
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Status(HttpStatusCode.BadGateway),
            () => ScriptedHandler.Silent(Event(Bundle)));
        await using var transport = Streaming(handler, bundles.Add);

        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        bundles.ShouldHaveSingleItem();
        handler.Requests.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ReturnsWithoutConfigStateWhenTheCallerStopsWaiting()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(TimeSpan.Zero));
        await using var transport = Streaming(handler, _ => { });
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(transport.ConnectAsync(giveUp.Token));
    }

    [Fact]
    public async Task StopsReadingOnceDisposed()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Silent(Event(Bundle)));
        var transport = Streaming(handler, _ => { });

        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await transport.DisposeAsync();

        var settled = handler.Requests.Count;
        await Task.Delay(150, TestContext.Current.CancellationToken);
        handler.Requests.Count.ShouldBe(settled);
    }

    // Long enough to ride out three missed keepalives, since the server sends one every 15
    // seconds and a quiet stream is not a dead one.
    [Fact]
    public void WaitsOutThreeMissedKeepalivesBeforeGivingUpOnAStream() =>
        StreamingTransport.DefaultReadTimeout.ShouldBe(TimeSpan.FromSeconds(45));

    private static StreamingTransport Streaming(ScriptedHandler handler, Action<ConfigBundle> onBundle) =>
        new(
            new TransportOptions("sdk-key", BaseUrl, new HttpClient(handler), onBundle, NullLoggerFactory.Instance),
            TimeSpan.FromSeconds(30));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition was never met.");
    }
}
