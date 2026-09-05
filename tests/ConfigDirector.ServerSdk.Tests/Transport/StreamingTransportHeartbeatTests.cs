using System.Text.RegularExpressions;
using ConfigDirector.Tests.Integration;
using ConfigDirector.Transport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.Transport;

public sealed class StreamingTransportHeartbeatTests : IDisposable
{
    private static readonly TimeSpan FastHeartbeat = TimeSpan.FromMilliseconds(50);

    private readonly SdkServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task SendsTheCurrentSessionIdOnTheHeartbeatInterval()
    {
        await using var transport = new StreamingTransport(Options(), FastHeartbeat);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);

        await WaitAsync(() => HeartbeatBodies().Count > 0);

        var heartbeat = HeartbeatBodies()[0];
        heartbeat.ShouldContain("\"serverSdkKey\":\"server-sdk-key\"");
        SessionIdOf(heartbeat).ShouldBe(SessionIdOf(StreamBodies()[0]));
    }

    [Fact(Timeout = 10_000)]
    public async Task DisposeStopsTheHeartbeat()
    {
        var transport = new StreamingTransport(Options(), FastHeartbeat);
        await transport.ConnectAsync(TestContext.Current.CancellationToken);
        await WaitAsync(() => HeartbeatBodies().Count > 0);

        await transport.DisposeAsync();
        var settled = HeartbeatBodies().Count;

        await Task.Delay(300, TestContext.Current.CancellationToken);
        HeartbeatBodies().Count.ShouldBe(settled);
    }

    [Fact]
    public async Task BeatsEvery90Seconds()
    {
        await using var transport = new StreamingTransport(Options());

        transport.HeartbeatInterval.ShouldBe(TimeSpan.FromSeconds(90));
    }

    private TransportOptions Options() =>
        new("server-sdk-key", _server.BaseUrl, _ => { }, NullLoggerFactory.Instance);

    private List<string> HeartbeatBodies() => BodiesFor("/server/heartbeat/v1");

    private List<string> StreamBodies() => BodiesFor("/server/sse/v1");

    private List<string> BodiesFor(string path)
    {
        var bodies = new List<string>();
        for (var index = 0; index < Math.Min(_server.Paths.Count, _server.Bodies.Count); index++)
        {
            if (_server.Paths[index] == path)
            {
                bodies.Add(_server.Bodies[index]);
            }
        }

        return bodies;
    }

    private static string SessionIdOf(string body)
    {
        var match = Regex.Match(body, "\"sessionId\":\"([^\"]*)\"");
        match.Success.ShouldBeTrue($"expected a sessionId in: {body}");
        return match.Groups[1].Value;
    }

    private static async Task WaitAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 300 && !until(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        until().ShouldBeTrue();
    }
}
