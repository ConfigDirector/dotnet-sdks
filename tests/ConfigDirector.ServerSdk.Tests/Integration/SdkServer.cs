using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace ConfigDirector.Tests.Integration;

// A real HTTP server on loopback answering ConfigDirector's endpoints. The SDK reaches it over a
// real socket with its own HttpClient, so an integration test replaces the server and nothing
// inside the SDK: the transports, the event stream, and the bundle parser all run as shipped.
//
// Spoken over a plain socket rather than through HttpListener, which is http.sys on Windows and a
// managed implementation elsewhere -- two stacks that differ on URL reservations, prefix matching,
// and when a partial response is actually flushed. A stub the tests rely on has to behave the same
// on every platform, and HTTP/1.1 is small enough to answer directly.
internal sealed class SdkServer : IDisposable
{
    private static readonly byte[] HeaderEnd = Encoding.ASCII.GetBytes("\r\n\r\n");

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Channel<string>> _streams = [];
    private readonly List<TcpClient> _connections = [];
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();
    private readonly object _lock = new();
    private readonly Task _accepting;

    internal SdkServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");
        _accepting = AcceptAsync();
    }

    internal Uri BaseUrl { get; }

    internal string Bundle { get; set; } = SampleConfigs.Bundle;

    // Accepts requests and never answers them, which is what a server that has stopped talking
    // looks like from the client's side.
    internal bool Stalls { get; set; }

    internal List<string> Paths { get; } = [];

    internal List<string> Bodies { get; } = [];

    internal List<string?> UserAgents { get; } = [];

    internal List<string?> Accepts { get; } = [];

    // Anything the server itself tripped over, and how much of a stream it managed to write.
    // Without these a stream that never arrives looks identical to one that was never asked for.
    internal List<string> Failures { get; } = [];

    internal int StreamedBytes { get; private set; }

    // A port nothing is listening on, for the case where the server cannot be reached at all.
    internal static Uri UnreachableUrl { get; } = Unreachable();

    internal int Requests
    {
        get
        {
            lock (_lock)
            {
                return Paths.Count;
            }
        }
    }

    // Points a client at this server through the same setting an application uses for a proxy.
    internal void Attach(ConfigDirectorClientOptions options) => options.Connection.Url = BaseUrl;

    // Answers the next request with this instead of config state. Queued, so a test can script a
    // failure followed by a recovery.
    internal void Replies(HttpStatusCode status, string body = "")
    {
        lock (_lock)
        {
            _replies.Enqueue((status, body));
        }
    }

    // Sends config state down every stream that is currently open.
    internal void Push(string bundle) => PushRaw(Frame(bundle));

    // Sends a frame verbatim, for the events a stream carries that are not config state.
    internal void PushRaw(string frame)
    {
        lock (_lock)
        {
            foreach (var stream in _streams)
            {
                stream.Writer.TryWrite(frame);
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();

        lock (_lock)
        {
            foreach (var stream in _streams)
            {
                stream.Writer.TryComplete();
            }

            _streams.Clear();

            foreach (var connection in _connections)
            {
                connection.Dispose();
            }

            _connections.Clear();
        }

        _listener.Stop();
        _accepting.Wait(TimeSpan.FromSeconds(5));
        _stop.Dispose();
    }

    private static Uri Unreachable()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return new Uri($"http://127.0.0.1:{port}/");
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient connection;
            try
            {
                connection = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            lock (_lock)
            {
                _connections.Add(connection);
            }

            _ = HandleAsync(connection);
        }
    }

    private async Task HandleAsync(TcpClient connection)
    {
        connection.NoDelay = true;
        var socket = connection.GetStream();

        // A stalled request keeps its socket open, so the connection outlives this method and is
        // the server's to close when it is disposed.
        var stalled = false;
        try
        {
            var request = await ReadRequestAsync(socket).ConfigureAwait(false);
            if (request is not { } asked)
            {
                return;
            }

            (HttpStatusCode Status, string Body)? scripted;
            lock (_lock)
            {
                Paths.Add(asked.Path);
                Bodies.Add(asked.Body);
                UserAgents.Add(asked.Header("user-agent"));
                Accepts.Add(asked.Header("accept"));
                scripted = _replies.Count > 0 ? _replies.Dequeue() : null;

                if (Stalls)
                {
                    // Accepted and never answered.
                    stalled = true;
                }
            }

            if (stalled)
            {
                return;
            }

            if (scripted is { } reply)
            {
                await RespondAsync(socket, (int)reply.Status, "application/json", reply.Body).ConfigureAwait(false);
            }
            else if (asked.Path.EndsWith("/sse/v1", StringComparison.Ordinal))
            {
                await StreamAsync(socket).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(socket, 200, "application/json", Bundle).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client hung up, which is what closing a client looks like from here.
        }
        catch (Exception error)
        {
            lock (_lock)
            {
                Failures.Add($"{error.GetType().Name}: {error.Message}");
            }
        }
        finally
        {
            if (!stalled)
            {
                lock (_lock)
                {
                    _connections.Remove(connection);
                }

                socket.Dispose();
                connection.Dispose();
            }
        }
    }

    // Content-Length framed, which is all the SDK ever sends.
    private static async Task<Request?> ReadRequestAsync(NetworkStream socket)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd(buffer)) < 0)
        {
            var read = await socket.ReadAsync(chunk.AsMemory()).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        var raw = buffer.ToArray();
        var head = Encoding.ASCII.GetString(raw, 0, headerEnd);
        var lines = head.Split(["\r\n"], StringSplitOptions.None);
        var target = lines[0].Split(' ')[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }
        }

        var length = headers.TryGetValue("Content-Length", out var declared)
            ? int.Parse(declared, CultureInfo.InvariantCulture)
            : 0;

        var body = new MemoryStream();
        var carried = raw.Length - (headerEnd + HeaderEnd.Length);
        if (carried > 0)
        {
            body.Write(raw, headerEnd + HeaderEnd.Length, Math.Min(carried, length));
        }

        while (body.Length < length)
        {
            var read = await socket.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, length - body.Length)))
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            body.Write(chunk, 0, read);
        }

        return new Request(new Uri(new Uri("http://stub"), target).AbsolutePath, Encoding.UTF8.GetString(body.ToArray()), headers);
    }

    private static int IndexOfHeaderEnd(MemoryStream buffer)
    {
        var raw = buffer.GetBuffer();
        for (var index = 0; index + HeaderEnd.Length <= buffer.Length; index++)
        {
            if (raw[index] == HeaderEnd[0] && raw[index + 1] == HeaderEnd[1]
                && raw[index + 2] == HeaderEnd[2] && raw[index + 3] == HeaderEnd[3])
            {
                return index;
            }
        }

        return -1;
    }

    private static async Task RespondAsync(NetworkStream socket, int status, string type, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {Reason(status)}\r\n"
            + $"Content-Type: {type}\r\n"
            + $"Content-Length: {payload.Length}\r\n"
            + "Connection: close\r\n\r\n");

        await socket.WriteAsync(head.AsMemory()).ConfigureAwait(false);
        await socket.WriteAsync(payload.AsMemory()).ConfigureAwait(false);
        await socket.FlushAsync().ConfigureAwait(false);
    }

    // No Content-Length and no chunking: the body runs until the connection closes, which HTTP/1.1
    // allows and which delivers each frame the moment it is written.
    private async Task StreamAsync(NetworkStream socket)
    {
        var events = Channel.CreateUnbounded<string>();
        events.Writer.TryWrite(Frame(Bundle));
        lock (_lock)
        {
            _streams.Add(events);
        }

        try
        {
            var head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n");
            await socket.WriteAsync(head.AsMemory()).ConfigureAwait(false);
            await socket.FlushAsync().ConfigureAwait(false);

            while (await events.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                while (events.Reader.TryRead(out var text))
                {
                    var frame = Encoding.UTF8.GetBytes(text);
                    await socket.WriteAsync(frame.AsMemory()).ConfigureAwait(false);
                    await socket.FlushAsync().ConfigureAwait(false);
                    lock (_lock)
                    {
                        StreamedBytes += frame.Length;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The server is shutting down.
        }
        finally
        {
            lock (_lock)
            {
                _streams.Remove(events);
            }
        }
    }

    // One event carrying the bundle, which the wire format puts on a single line. Both line
    // endings have to go: a checkout on Windows makes the sample bundles CRLF, and a carriage
    // return left behind is whitespace to a JSON reader but a line terminator to an event stream.
    private static string Frame(string bundle) =>
        "event: config-update\ndata: "
        + bundle.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
        + "\n\n";

    private static string Reason(int status) =>
        status switch
        {
            200 => "OK",
            204 => "No Content",
            403 => "Forbidden",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            _ => "Status",
        };

    private sealed record Request(string Path, string Body, Dictionary<string, string> Headers)
    {
        internal string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }
}
