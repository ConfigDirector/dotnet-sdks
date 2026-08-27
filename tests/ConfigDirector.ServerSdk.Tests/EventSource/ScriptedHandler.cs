using System.Net;
using System.Text;

namespace ConfigDirector.Tests.EventSource;

// Answers each request with the next scripted response, repeating the last one once the script
// runs out, and records what was asked for.
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _script;
    private Func<CancellationToken, Task<HttpResponseMessage>> _last = Then(() => Sse(""));

    internal ScriptedHandler(params Func<HttpResponseMessage>[] script)
        : this(Array.ConvertAll(script, Then))
    {
    }

    internal ScriptedHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] script) =>
        _script = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(script);

    internal List<HttpRequestMessage> Requests { get; } = [];

    internal List<string?> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (_script.Count > 0)
        {
            _last = _script.Dequeue();
        }

        var response = await _last(cancellationToken).ConfigureAwait(false);

        // What a real handler does, and what the SDK's connect deadline relies on: the token that
        // opened the request keeps aborting the response body after the headers have arrived.
        if (response.Content is SilentContent streamed)
        {
            streamed.Abort = cancellationToken;
        }

        return response;
    }

    internal static Func<CancellationToken, Task<HttpResponseMessage>> Then(Func<HttpResponseMessage> reply) =>
        _ => Task.FromResult(reply());

    // Never answers at all, which is what a request to a server that has stopped talking looks
    // like: no status, no headers, nothing to time out on but the clock.
    internal static Func<CancellationToken, Task<HttpResponseMessage>> Stalls() =>
        async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return Sse("");
        };

    internal static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };

    internal static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    internal static HttpResponseMessage Status(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal static Func<HttpResponseMessage> Throws() =>
        () => throw new HttpRequestException("the connection failed");

    // A stream that delivers what it was given and then stays open, so an idle timeout has
    // something to fire against.
    internal static HttpResponseMessage Silent(string body) =>
        new(HttpStatusCode.OK) { Content = new SilentContent([body], TimeSpan.Zero) };

    // Delivers each chunk `gap` apart, then stays open. Enough to tell a stream that has gone
    // quiet from one that is merely unhurried.
    internal static HttpResponseMessage Trickle(TimeSpan gap, params string[] chunks) =>
        new(HttpStatusCode.OK) { Content = new SilentContent(chunks, gap) };

    // Overriding CreateContentReadStreamAsync is what makes this incremental: the default buffers
    // the whole body first, which never finishes for a stream that stays open.
    private sealed class SilentContent(string[] chunks, TimeSpan gap) : HttpContent
    {
        internal CancellationToken Abort { get; set; }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new SilentStream(chunks, gap, Abort));

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var source = await CreateContentReadStreamAsync().ConfigureAwait(false);
            await source.CopyToAsync(stream).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class SilentStream(string[] chunks, TimeSpan gap, CancellationToken abort) : Stream
    {
        private int _chunk;
        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, abort);

            if (_chunk >= chunks.Length)
            {
                // Open, and silent from here on.
                await Task.Delay(Timeout.Infinite, linked.Token).ConfigureAwait(false);
                return 0;
            }

            if (_chunk > 0 && _offset == 0 && gap > TimeSpan.Zero)
            {
                await Task.Delay(gap, linked.Token).ConfigureAwait(false);
            }

            linked.Token.ThrowIfCancellationRequested();

            var bytes = Encoding.UTF8.GetBytes(chunks[_chunk]);
            var count = Math.Min(buffer.Length, bytes.Length - _offset);
            bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            if (_offset == bytes.Length)
            {
                _chunk++;
                _offset = 0;
            }

            return count;
        }

        // Only the async path is exercised; a synchronous read would park the test host forever,
        // so it fails loudly instead.
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("This stream is read asynchronously.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
