using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace ConfigDirector.Tests.Integration;

// A real HTTP server on loopback answering ConfigDirector's endpoints. The SDK reaches it over a
// real socket with its own HttpClient, so an integration test replaces the server and nothing
// inside the SDK: the transports, the event stream, and the bundle parser all run as shipped.
internal sealed class SdkServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Channel<string>> _streams = [];
    private readonly List<HttpListenerContext> _held = [];
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();
    private readonly List<Task> _connections = [];
    private readonly object _lock = new();
    private readonly Task _accepting;

    internal SdkServer()
    {
        BaseUrl = new Uri($"http://127.0.0.1:{FreePort()}/");
        _listener.Prefixes.Add(BaseUrl.AbsoluteUri);
        _listener.Start();
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

    // A port nothing is listening on, for the case where the server cannot be reached at all.
    internal static Uri UnreachableUrl { get; } = new($"http://127.0.0.1:{FreePort()}/");

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

            foreach (var context in _held)
            {
                Close(context);
            }

            _held.Clear();
        }

        _listener.Close();
        _accepting.Wait(TimeSpan.FromSeconds(5));
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            lock (_lock)
            {
                _connections.Add(HandleAsync(context));
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        (HttpStatusCode Status, string Body)? scripted;
        lock (_lock)
        {
            Paths.Add(path);
            Bodies.Add(body);
            UserAgents.Add(context.Request.Headers["User-Agent"]);
            Accepts.Add(context.Request.Headers["Accept"]);
            scripted = _replies.Count > 0 ? _replies.Dequeue() : null;

            if (Stalls)
            {
                // Held open and unanswered, and kept referenced so the connection stays alive.
                _held.Add(context);
                return;
            }
        }

        try
        {
            if (scripted is { } reply)
            {
                await RespondAsync(context, (int)reply.Status, "application/json", reply.Body)
                    .ConfigureAwait(false);
            }
            else if (path.EndsWith("/sse/v1", StringComparison.Ordinal))
            {
                await StreamAsync(context).ConfigureAwait(false);
            }
            else
            {
                await RespondAsync(context, 200, "application/json", Bundle).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The client hung up, which is what closing a client looks like from here.
        }
        finally
        {
            Close(context);
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, int status, string type, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = type;
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes.AsMemory()).ConfigureAwait(false);
    }

    private async Task StreamAsync(HttpListenerContext context)
    {
        var events = Channel.CreateUnbounded<string>();
        events.Writer.TryWrite(Frame(Bundle));
        lock (_lock)
        {
            _streams.Add(events);
        }

        context.Response.StatusCode = 200;
        context.Response.ContentType = "text/event-stream";
        context.Response.SendChunked = true;

        try
        {
            while (await events.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                while (events.Reader.TryRead(out var text))
                {
                    var frame = Encoding.UTF8.GetBytes(text);
                    await context.Response.OutputStream.WriteAsync(frame.AsMemory()).ConfigureAwait(false);
                    await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
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

    private static void Close(HttpListenerContext context)
    {
        try
        {
            context.Response.Close();
        }
        catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException)
        {
            // Already gone.
        }
    }

    // One event carrying the bundle. The wire format puts it on a single line.
    private static string Frame(string bundle) =>
        "event: config-update\ndata: "
        + bundle.Replace("\n", string.Empty, StringComparison.Ordinal)
        + "\n\n";

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}
