using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ConfigDirector.EventSource;

// An SSE stream that reconnects itself. Parsing is System.Net.ServerSentEvents; what this adds is
// the connection lifecycle around it -- retrying, backing off, and resuming from the last event id.
//
// A transient failure is absorbed and retried, so a caller reading the sequence sees one
// uninterrupted run of events. Only a fatal status or cancellation ends it.
internal sealed class SseClient
{
    private const int NoContent = 204;
    private static readonly TimeSpan ShortestDelay = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan LongestDelay = TimeSpan.FromHours(1);

    private readonly HttpClient _http;
    private readonly SseClientOptions _options;
    private readonly ILogger _logger;

    internal SseClient(HttpClient http, SseClientOptions options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    internal async IAsyncEnumerable<SseItem<string>> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lastEventId = _options.LastEventId;
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var opened = await OpenAsync(lastEventId, cancellationToken).ConfigureAwait(false);
            if (opened.Response is null && opened.Failure is null)
            {
                // The server answered 204, which is how it says not to reconnect.
                yield break;
            }

            var failure = opened.Failure;
            TimeSpan? serverInterval = null;

            if (opened.Response is { } response)
            {
                // A stream that opened starts the count over, so a connection that comes back and
                // drops again is not punished for what happened before it.
                attempt = 0;
                _options.Connected?.Invoke();

                using (response)
                using (opened.Deadline)
                using (var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false))
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (var timed = new IdleTimeoutStream(body, idle, _options.IdleTimeout))
                {
                    var parser = SseParser.Create(timed);
                    await foreach (var item in ReadStreamAsync(parser, idle.Token, cancellationToken).ConfigureAwait(false))
                    {
                        if (item.Failure is not null)
                        {
                            failure = item.Failure;
                            break;
                        }

                        lastEventId = parser.LastEventId ?? lastEventId;
                        yield return item.Message;
                    }

                    serverInterval = ServerInterval(parser);
                }
            }

            attempt++;
            var delay = DelayFor(new SseReconnect(attempt, serverInterval, StatusOf(failure), failure));
            Log.Reconnecting(_logger, attempt, delay, failure);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    // Split out so the loop above can yield: a yield return may not sit inside a try that has a
    // catch, and reading an item needs one.
    private async IAsyncEnumerable<StreamItem> ReadStreamAsync(
        SseParser<string> parser,
        CancellationToken idleToken,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = parser.EnumerateAsync(idleToken).GetAsyncEnumerator(idleToken);
        await using var disposal = events.ConfigureAwait(false);
        while (true)
        {
            SseItem<string> current = default;
            Exception? failure = null;
            var moved = false;
            try
            {
                moved = await events.MoveNextAsync().ConfigureAwait(false);
                if (moved)
                {
                    current = events.Current;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The idle timer fired rather than the caller cancelling.
                failure = new TimeoutException(
                    $"The event stream delivered nothing for {_options.IdleTimeout}.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                failure = error;
            }

            if (failure is not null)
            {
                yield return StreamItem.Failed(failure);
                yield break;
            }

            if (!moved)
            {
                yield return StreamItem.Failed(new SseStreamClosedException());
                yield break;
            }

            yield return StreamItem.Received(current);
        }
    }

    // A null response with a null failure means the server asked the stream to stop.
    private async Task<Opened> OpenAsync(string? lastEventId, CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var handedOff = false;
        try
        {
            if (_options.ConnectTimeout > TimeSpan.Zero)
            {
                deadline.CancelAfter(_options.ConnectTimeout);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(
                        BuildRequest(lastEventId),
                        HttpCompletionOption.ResponseHeadersRead,
                        deadline.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new Opened(
                    null,
                    new TimeoutException($"The event stream did not open within {_options.ConnectTimeout}."),
                    null);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return new Opened(null, error, null);
            }

            var status = (int)response.StatusCode;
            if (status == NoContent)
            {
                response.Dispose();
                Log.ServerEndedStream(_logger, null);
                return default;
            }

            if (status >= 400)
            {
                response.Dispose();
                if (_options.IsFatalStatus(status))
                {
                    throw new SseStatusException(status);
                }

                return new Opened(null, new SseStatusException(status), null);
            }

            // The headers are in, so the deadline has done its job. Disarmed and handed to the
            // reader rather than disposed here, because the token it issued is the one HttpClient
            // holds while the body is read. Measured on .NET 10, cancelling that token after the
            // headers arrive does not abort the body -- so this is insurance for the
            // netstandard2.0 target, where .NET Framework bounds the whole exchange instead. No
            // test can observe it on a runtime this suite can run.
            deadline.CancelAfter(Timeout.InfiniteTimeSpan);
            handedOff = true;
            return new Opened(response, null, deadline);
        }
        finally
        {
            if (!handedOff)
            {
                deadline.Dispose();
            }
        }
    }

    private HttpRequestMessage BuildRequest(string? lastEventId)
    {
        var request = new HttpRequestMessage(_options.Method, _options.Url)
        {
            Content = _options.Body?.Invoke(),
        };

        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        foreach (var header in _options.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        return request;
    }

    private TimeSpan DelayFor(SseReconnect reconnect)
    {
        TimeSpan delay;
        try
        {
            delay = _options.ReconnectDelay(reconnect);
        }
        catch (Exception error)
        {
            // A caller's policy must not take the stream down with it.
            Log.DelayPolicyThrew(_logger, error);
            return ShortestDelay;
        }

        return delay < ShortestDelay || delay > LongestDelay ? ShortestDelay : delay;
    }

    // The parser reports a negative interval when the stream carried no retry field.
    private static TimeSpan? ServerInterval(SseParser<string> parser) =>
        parser.ReconnectionInterval >= TimeSpan.Zero ? parser.ReconnectionInterval : null;

    private static int? StatusOf(Exception? failure) =>
        failure is SseStatusException status ? status.Status : null;

    private static Task<Stream> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken) =>
#if NET8_0_OR_GREATER
        content.ReadAsStreamAsync(cancellationToken);
#else
        content.ReadAsStreamAsync();
#endif

    private readonly record struct Opened(
        HttpResponseMessage? Response,
        Exception? Failure,
        CancellationTokenSource? Deadline);

    private readonly record struct StreamItem(SseItem<string> Message, Exception? Failure)
    {
        internal static StreamItem Received(SseItem<string> message) => new(message, null);

        internal static StreamItem Failed(Exception failure) => new(default, failure);
    }

    private static class Log
    {
        internal static readonly Action<ILogger, int, TimeSpan, Exception?> Reconnecting =
            LoggerMessage.Define<int, TimeSpan>(
                LogLevel.Debug,
                new EventId(1, "Reconnecting"),
                "The event stream ended. Reconnect attempt {Attempt} in {Delay}.");

        internal static readonly Action<ILogger, Exception?> ServerEndedStream =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(2, "ServerEndedStream"),
                "The server answered 204, so the event stream will not reconnect.");

        internal static readonly Action<ILogger, Exception?> DelayPolicyThrew =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(3, "DelayPolicyThrew"),
                "The reconnect delay policy threw. Retrying immediately.");
    }
}
