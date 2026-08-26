using ConfigDirector;
using ConfigDirector.Samples.AspNetCore;
using Microsoft.Extensions.Options;

// Before the builder, so the environment-variable provider it sets up sees these. Read from the
// working directory, which `dotnet run --project` sets to the project folder.
DotEnv.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SampleOptions>(builder.Configuration.GetSection("ConfigDirector"));

// One client for the whole process. Never build one per request: each holds its own connection,
// initialization does network I/O, and a fresh client serves defaults until its first config state
// arrives. It is thread safe, so every request shares this one.
//
// Registering it as a singleton is also what closes it: the host disposes singletons on shutdown,
// and the client implements IAsyncDisposable.
builder.Services.AddSingleton<IConfigDirectorClient>(services =>
{
    var settings = services.GetRequiredService<IOptions<SampleOptions>>().Value;

    // Building a client makes no network calls.
    return new ConfigDirectorClient(settings.ServerSdkKey, new ConfigDirectorClientOptions
    {
        Metadata = new Metadata { AppName = "aspnetcore-sample", AppVersion = "1.0.0" },

        // The host's own factory, so SDK output goes through this application's logging pipeline
        // and obeys the levels configured in appsettings.json.
        LoggerFactory = services.GetRequiredService<ILoggerFactory>(),

        Connection =
        {
            Mode = settings.Mode,
            Timeout = settings.Timeout,
            Url = settings.Url,
        },
    });
});

var app = builder.Build();

app.MapGet("/configs", (HttpContext http, IConfigDirectorClient client) =>
{
    var context = ContextFrom(http.Request.Query);

    // Each call reads config state the client already holds, with no network call on the request
    // path, which is what makes several of them in one handler cheap.
    //
    // The default is what this application serves whenever ConfigDirector is unreachable, so it
    // should always be the safe choice. Its type is also what the value is parsed as.
    // Keyed by config key, the same shape the other ConfigDirector sample applications return.
    return Results.Ok(new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["temporary-feature-flag"] = client.GetValue("temporary-feature-flag", true, context),
        ["permanent-kill-switch"] = client.GetValue("permanent-kill-switch", false, context),
        ["integer-config"] = client.GetValue("integer-config", 10, context),
        ["day-of-the-week-config"] = client.GetValue("day-of-the-week-config", "Friday", context),

        // A JSON config read into a type of this application's own, rather than into a dictionary
        // the handler would have to pick apart.
        ["json-value-config"] = client.GetValue("json-value-config", new RetrySettings(), context),
    });
});

// Every config at once, evaluated but unparsed. This is the shape a client SDK hydrates from, and
// it records no telemetry, since the SDK that receives it reports its own evaluations.
app.MapGet("/configs/all", (HttpContext http, IConfigDirectorClient client) =>
    Results.Ok(client.GetAllConfigs(ContextFrom(http.Request.Query))));

app.MapGet("/health", (IConfigDirectorClient client) =>
    Results.Ok(new { ready = client.IsReady, closed = client.IsClosed }));

app.MapFallback(() => Results.NotFound(new { error = "Not found. Try GET /configs" }));

var configDirector = app.Services.GetRequiredService<IConfigDirectorClient>();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");

// Registered before InitializeAsync, so the first config state notifies them too. A handler added
// afterwards only sees later updates.
configDirector.ClientReady += (_, _) => log.LogInformation("ConfigDirector is ready");
configDirector.Watch("temporary-feature-flag", false, enabled =>
    log.LogInformation("temporary-feature-flag is now {Enabled}", enabled));

// Awaited before the server starts listening, so requests are not served config defaults while the
// first config state is still in flight. It does not throw when the connection fails, so IsReady is
// what says whether state actually arrived.
await configDirector.InitializeAsync();
if (!configDirector.IsReady)
{
    log.LogWarning("ConfigDirector is not ready, every config will resolve to its default");
}

app.Run();

// The context is per request; the client that evaluates it is not. A real application would build
// this from the authenticated session rather than from the query string.
static Context ContextFrom(IQueryCollection query)
{
    var context = new Context
    {
        Id = query["id"],
        Name = query["name"],
        Anonymous = query["anonymous"] == "true",
    };

    foreach (var (name, values) in query)
    {
        if (name is "id" or "name" or "anonymous")
        {
            continue;
        }

        // A parameter given more than once becomes an array trait, which is what the "contains any
        // of" operators match on.
        if (values.Count > 1)
        {
            context.Traits[name] = values.ToArray();
        }
        else
        {
            context.Traits[name] = values.ToString();
        }
    }

    return context;
}
