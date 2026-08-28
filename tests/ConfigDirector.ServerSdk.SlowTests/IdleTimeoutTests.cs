using ConfigDirector.EventSource;
using ConfigDirector.Tests.EventSource;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests;

// The idle timer resets on bytes rather than on events, so a stream that is quiet but healthy is
// not torn down. Proving that needs a stream whose frames together outlast the timeout, which
// makes these tests wall-clock bound: they live here rather than in the fast suite, where the
// delays they depend on get stretched by 1,400 tests running alongside them.
public class IdleTimeoutTests
{
    // Deliberately generous. Each gap has to survive a stalled machine, so it sits well inside the
    // timeout, and the frames together run far enough past the timeout that a timer which never
    // reset is still caught. Few frames, because every gap is another chance to be unlucky.
    private static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
    private static readonly Uri Endpoint = new("https://config.test/stream");

    [Fact]
    public async Task StaysOnAStreamThatKeepsDeliveringSlowly()
    {
        // Eight events, each well inside the timeout but together outlasting it, so a timer armed
        // once and never reset would tear the stream down mid-read.
        var frames = Enumerable.Range(0, 8).Select(index => $"data: event-{index}\n\n").ToArray();
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(Gap, frames));

        var items = await ReadAsync(handler, frames.Length);

        items.Select(item => item.Data)
            .ShouldBe(Enumerable.Range(0, 8).Select(index => $"event-{index}"));
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StaysOnAStreamThatSendsOnlyKeepaliveComments()
    {
        // The parser never surfaces a comment, so only a timer measured on bytes keeps this up.
        var frames = new List<string> { "data: first\n\n" };
        frames.AddRange(Enumerable.Repeat(": keepalive\n\n", 6));
        frames.Add("data: second\n\n");
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(Gap, [.. frames]));

        var items = await ReadAsync(handler, 2);

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
        handler.Requests.Count.ShouldBe(1);
    }

    private static async Task<List<System.Net.ServerSentEvents.SseItem<string>>> ReadAsync(
        ScriptedHandler handler, int count)
    {
        var client = new SseClient(
            new HttpClient(handler),
            new SseClientOptions(Endpoint)
            {
                ReconnectDelay = _ => TimeSpan.FromMilliseconds(1),
                IdleTimeout = Timeout,
            },
            NullLogger.Instance);

        var items = new List<System.Net.ServerSentEvents.SseItem<string>>();
        await foreach (var item in client.ReadAsync(TestContext.Current.CancellationToken))
        {
            items.Add(item);
            if (items.Count == count)
            {
                break;
            }
        }

        return items;
    }
}
