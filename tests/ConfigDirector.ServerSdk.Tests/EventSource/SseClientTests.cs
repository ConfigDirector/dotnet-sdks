using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using ConfigDirector.EventSource;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigDirector.Tests.EventSource;

public class SseClientTests
{
    private static readonly Uri Endpoint = new("https://stream.example.com/configs");

    [Fact]
    public async Task ReadsTheEventsAStreamCarries()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Sse(
            "event: update\ndata: first\nid: 1\n\ndata: second\n\n"));

        var items = await ReadAsync(handler, 2);

        items[0].EventType.ShouldBe("update");
        items[0].Data.ShouldBe("first");
        items[0].EventId.ShouldBe("1");
        items[1].EventType.ShouldBe("message");
        items[1].Data.ShouldBe("second");
    }

    [Fact]
    public async Task JoinsTheDataLinesOfOneEvent()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Sse("data: one\ndata: two\n\n"));

        var items = await ReadAsync(handler, 1);

        items[0].Data.ShouldBe("one\ntwo");
    }

    [Fact]
    public async Task ReconnectsWhenTheServerClosesTheStream()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        var items = await ReadAsync(handler, 2);

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
        handler.Requests.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ResumesFromTheLastEventIdItSaw()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\nid: 42\n\ndata: unnamed\n\n"),
            () => ScriptedHandler.Sse("data: third\n\n"));

        await ReadAsync(handler, 3);

        handler.Requests[0].Headers.Contains("Last-Event-ID").ShouldBeFalse();
        // The id sticks to the stream, not to the event that carried it.
        handler.Requests[1].Headers.GetValues("Last-Event-ID").ShouldBe(["42"]);
    }

    [Fact]
    public async Task StopsWithoutReconnectingWhenTheServerAnswers204()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Status(HttpStatusCode.NoContent));

        var items = await ReadToEndAsync(handler);

        items.Select(item => item.Data).ShouldBe(["first"]);
    }

    [Fact]
    public async Task EndsTheStreamOnAStatusItCannotRecoverFrom()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Status(HttpStatusCode.Unauthorized));

        var failure = await Should.ThrowAsync<SseStatusException>(ReadToEndAsync(handler));

        failure.Status.ShouldBe(401);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RetriesAStatusThatMightRecover()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Status(HttpStatusCode.ServiceUnavailable),
            () => ScriptedHandler.Sse("data: recovered\n\n"));

        var items = await ReadAsync(handler, 1);

        items[0].Data.ShouldBe("recovered");
    }

    [Fact]
    public async Task RetriesAConnectionThatNeverAnswered()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Throws(),
            () => ScriptedHandler.Sse("data: recovered\n\n"));

        var items = await ReadAsync(handler, 1);

        items[0].Data.ShouldBe("recovered");
    }

    [Fact]
    public async Task CountsConsecutiveFailuresAndStartsOverAfterAStreamOpens()
    {
        var attempts = new List<int>();
        var handler = new ScriptedHandler(
            ScriptedHandler.Throws(),
            ScriptedHandler.Throws(),
            () => ScriptedHandler.Sse("data: opened\n\n"),
            ScriptedHandler.Throws(),
            () => ScriptedHandler.Sse("data: again\n\n"));

        await ReadAsync(handler, 2, options => options with
        {
            ReconnectDelay = reconnect =>
            {
                attempts.Add(reconnect.Attempt);
                return TimeSpan.FromMilliseconds(1);
            },
        });

        // Two consecutive failures; then a stream that opened, which puts the next failure back at
        // one; then a failure after that one, which counts from there.
        attempts.ShouldBe([1, 2, 1, 2]);
    }

    [Fact]
    public async Task OffersTheServersOwnRetryIntervalToThePolicy()
    {
        TimeSpan? offered = null;
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("retry: 4500\ndata: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        await ReadAsync(handler, 2, options => options with
        {
            ReconnectDelay = reconnect =>
            {
                offered ??= reconnect.ServerInterval;
                return TimeSpan.FromMilliseconds(1);
            },
        });

        offered.ShouldBe(TimeSpan.FromMilliseconds(4500));
    }

    [Fact]
    public async Task OffersNoIntervalWhenTheStreamCarriedNoRetryField()
    {
        var offered = new List<TimeSpan?>();
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        await ReadAsync(handler, 2, options => options with
        {
            ReconnectDelay = reconnect =>
            {
                offered.Add(reconnect.ServerInterval);
                return TimeSpan.FromMilliseconds(1);
            },
        });

        offered[0].ShouldBeNull();
    }

    [Fact]
    public async Task ReconnectsWhenAnOpenStreamGoesQuiet()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Silent("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        var items = await ReadAsync(handler, 2, options => options with
        {
            IdleTimeout = TimeSpan.FromMilliseconds(150),
        });

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task KeepsReadingWhileTheStreamStaysBusy()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Silent("data: a\n\ndata: b\n\ndata: c\n\n"));

        var items = await ReadAsync(handler, 3, options => options with
        {
            IdleTimeout = TimeSpan.FromSeconds(30),
        });

        items.Select(item => item.Data).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task StaysOnAStreamThatKeepsDeliveringSlowly()
    {
        // Three events, each arriving inside the idle timeout but together outstripping it, so a
        // timer that is armed once and never reset would tear this stream down mid-read.
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(
            TimeSpan.FromMilliseconds(200),
            "data: a\n\n",
            "data: b\n\n",
            "data: c\n\n",
            "data: d\n\n"));

        var items = await ReadAsync(handler, 4, options => options with
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        });

        items.Select(item => item.Data).ShouldBe(["a", "b", "c", "d"]);
        handler.Requests.Count.ShouldBe(1);
    }

    // ConfigDirector keeps a quiet stream alive with SSE comments, and the parser does not surface
    // those. Timed between events rather than between bytes, this would reconnect every timeout.
    [Fact]
    public async Task ReconnectsWhenAStreamSaysNothingFromTheStart()
    {
        // Open, and silent from the first byte, so the timer has to be armed before any read
        // rather than by one.
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Trickle(TimeSpan.Zero),
            () => ScriptedHandler.Sse("data: eventually\n\n"));

        var items = await ReadAsync(handler, 1, options => options with
        {
            IdleTimeout = TimeSpan.FromMilliseconds(150),
        });

        items[0].Data.ShouldBe("eventually");
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task StaysOnAStreamThatSendsOnlyKeepaliveComments()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(
            TimeSpan.FromMilliseconds(200),
            "data: first\n\n",
            ": keepalive\n\n",
            ": keepalive\n\n",
            ": keepalive\n\n",
            "data: second\n\n"));

        var items = await ReadAsync(handler, 2, options => options with
        {
            IdleTimeout = TimeSpan.FromMilliseconds(500),
        });

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TellsThePolicyWhyTheStreamEnded()
    {
        Exception? reported = null;
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        await ReadAsync(handler, 2, options => options with
        {
            ReconnectDelay = reconnect =>
            {
                reported ??= reconnect.Error;
                return TimeSpan.FromMilliseconds(1);
            },
        });

        reported.ShouldBeOfType<SseStreamClosedException>();
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(90_000)]
    public async Task IgnoresADelayItCouldNotSensiblyWaitOut(int seconds)
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        var items = await ReadAsync(handler, 2, options => options with
        {
            ReconnectDelay = _ => TimeSpan.FromSeconds(seconds),
        });

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task SendsTheHeadersAndBodyEveryAttemptNeeds()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        await ReadAsync(handler, 2, options => options with
        {
            Method = HttpMethod.Post,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["User-Agent"] = "configdirector-tests",
            },
            Body = () => new StringContent("""{"serverSdkKey":"key"}""", Encoding.UTF8, "application/json"),
        });

        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].Headers.GetValues("Accept").ShouldBe(["text/event-stream"]);
        handler.Requests[0].Headers.GetValues("User-Agent").ShouldBe(["configdirector-tests"]);

        // A fresh body per attempt: HttpContent cannot be sent twice.
        handler.Bodies.Take(2).ShouldAllBe(body => body == """{"serverSdkKey":"key"}""");
    }

    [Fact]
    public async Task AnnouncesEveryTimeTheStreamOpens()
    {
        var opens = 0;
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        await ReadAsync(handler, 2, options => options with { Connected = () => opens++ });

        opens.ShouldBe(2);
    }

    [Fact]
    public async Task KeepsStreamingWhenTheDelayPolicyThrows()
    {
        var handler = new ScriptedHandler(
            () => ScriptedHandler.Sse("data: first\n\n"),
            () => ScriptedHandler.Sse("data: second\n\n"));

        var items = await ReadAsync(handler, 2, options => options with
        {
            ReconnectDelay = _ => throw new InvalidOperationException("faulty policy"),
        });

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task StopsWhenTheCallerCancels()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Silent("data: first\n\n"));
        using var caller = new CancellationTokenSource();

        var reading = Task.Run(async () =>
        {
            var seen = new List<SseItem<string>>();
            await foreach (var item in Client(handler).ReadAsync(caller.Token))
            {
                seen.Add(item);
                await caller.CancelAsync();
            }

            return seen;
        });

        await Should.ThrowAsync<OperationCanceledException>(reading);
    }

    private static SseClient Client(ScriptedHandler handler, Func<SseClientOptions, SseClientOptions>? configure = null)
    {
        var options = new SseClientOptions(Endpoint)
        {
            ReconnectDelay = _ => TimeSpan.FromMilliseconds(1),
            IdleTimeout = TimeSpan.FromSeconds(30),
        };

        return new SseClient(new HttpClient(handler), configure is null ? options : configure(options), NullLogger.Instance);
    }

    [Fact]
    public async Task ReconnectsWhenTheServerNeverAnswersTheRequest()
    {
        var handler = new ScriptedHandler(
            ScriptedHandler.Stalls(),
            ScriptedHandler.Then(() => ScriptedHandler.Sse("data: arrived\n\n")));

        var items = await ReadAsync(handler, 1, options => options with
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(50),
            ReconnectDelay = _ => TimeSpan.FromMilliseconds(1),
        });

        items[0].Data.ShouldBe("arrived");
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task StaysOnAStreamThatOutlivesTheTimeAllowedToOpenIt()
    {
        var handler = new ScriptedHandler(() => ScriptedHandler.Trickle(
            TimeSpan.FromMilliseconds(150), "data: first\n\n", "data: second\n\n"));

        var items = await ReadAsync(handler, 2, options => options with
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(40),
        });

        items.Select(item => item.Data).ShouldBe(["first", "second"]);
        handler.Requests.Count.ShouldBe(1);
    }

    private static async Task<List<SseItem<string>>> ReadAsync(
        ScriptedHandler handler,
        int count,
        Func<SseClientOptions, SseClientOptions>? configure = null)
    {
        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var items = new List<SseItem<string>>();

        await foreach (var item in Client(handler, configure).ReadAsync(giveUp.Token))
        {
            items.Add(item);
            if (items.Count == count)
            {
                break;
            }
        }

        return items;
    }

    private static async Task<List<SseItem<string>>> ReadToEndAsync(ScriptedHandler handler)
    {
        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var items = new List<SseItem<string>>();

        await foreach (var item in Client(handler).ReadAsync(giveUp.Token))
        {
            items.Add(item);
        }

        return items;
    }
}
