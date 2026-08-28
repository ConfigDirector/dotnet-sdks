using ConfigDirector;
using ConfigDirector.Samples.Mvc;

// Before the builder, so the environment-variable provider it sets up sees these. Read from the
// working directory, which `dotnet run --project` sets to the project folder.
DotEnv.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Everything the MinimalApi sample writes by hand is this one call: the options class, the
// singleton registration, the host's logger factory, and awaiting InitializeAsync before the
// server starts listening. Settings are bound from the "ConfigDirector" section.
builder.Services.AddConfigDirector()

    // How a request becomes an evaluation context, declared once here rather than repeated in
    // every action. A real application would build this from the authenticated session rather
    // than from the query string.
    .WithContext(http => ContextFrom(http.Request.Query));

// Degraded rather than unhealthy while config state has not arrived: the application still
// answers every request, with each config resolving to the default the calling code supplied.
builder.Services.AddHealthChecks().AddConfigDirector();

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapFallback(() => Results.NotFound(new { error = "Not found. Try GET /configs" }));

// Resolving the client builds it, which makes no network calls. The connection is opened later,
// while the host starts, and that gap is where a watch has to be registered to be called for the
// first config state as well as the updates after it.
var configDirector = app.Services.GetRequiredService<IConfigDirectorClient>();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");

configDirector.ClientReady += (_, _) => log.LogInformation("ConfigDirector is ready");
configDirector.Watch("temporary-feature-flag", false, enabled =>
    log.LogInformation("temporary-feature-flag is now {Enabled}", enabled));

app.Run();

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
