using ConfigDirector;
using ConfigDirector.Samples.Mvc;
using Microsoft.Extensions.Options;

// Before the builder, so the environment-variable provider it sets up sees these. Read from the
// working directory, which `dotnet run --project` sets to the project folder.
DotEnv.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SampleOptions>(builder.Configuration.GetSection("ConfigDirector"));
builder.Services.AddControllers();

// One client for the whole process. Never build one per request: each holds its own connection,
// initialization does network I/O, and a fresh client serves defaults until its first config state
// arrives. It is thread safe, so every request shares this one.
//
// Controllers are created per request, so injecting the client into one is injecting this same
// instance every time. Registering it as a singleton is also what closes it: the host disposes
// singletons on shutdown, and the client implements IAsyncDisposable.
builder.Services.AddSingleton<IConfigDirectorClient>(services =>
{
    var settings = services.GetRequiredService<IOptions<SampleOptions>>().Value;

    // Building a client makes no network calls.
    return new ConfigDirectorClient(settings.ServerSdkKey, new ConfigDirectorClientOptions
    {
        Metadata = new Metadata { AppName = "mvc-sample", AppVersion = "1.0.0" },

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

app.MapControllers();
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
