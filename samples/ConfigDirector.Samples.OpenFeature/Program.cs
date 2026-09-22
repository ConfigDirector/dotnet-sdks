using ConfigDirector;
using ConfigDirector.OpenFeature;
using ConfigDirector.Samples.OpenFeature;
using Microsoft.Extensions.Options;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

// Before the builder, so the environment-variable provider it sets up sees these. Read from the
// working directory, which `dotnet run --project` sets to the project folder.
DotEnv.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SampleOptions>(builder.Configuration.GetSection("ConfigDirector"));

// AddOpenFeature also registers the hosted lifecycle that initializes the provider while the host
// starts, before the server listens, and shuts it down when the host stops. That is what connects
// the ConfigDirector client and later closes it.
builder.Services.AddOpenFeature(openFeature =>
{
    // One provider for the whole process, which means one ConfigDirector client: it holds the
    // connection, and a fresh one serves defaults until its first config state arrives. The provider
    // takes the same options a ConfigDirectorClient does.
    openFeature.AddProvider(services =>
    {
        var settings = services.GetRequiredService<IOptions<SampleOptions>>().Value;

        return new ConfigDirectorProvider(settings.ServerSdkKey, new ConfigDirectorClientOptions
        {
            Metadata = new ConfigDirector.Metadata { AppName = "openfeature-sample", AppVersion = "1.0.0" },

            // The host's own factory, so SDK output goes through this application's logging
            // pipeline and obeys the levels configured in appsettings.json.
            LoggerFactory = services.GetRequiredService<ILoggerFactory>(),

            Connection =
            {
                Mode = settings.Mode,
                Timeout = settings.Timeout,
                Url = settings.Url,
            },
        });
    });

    // The provider raises this with the keys each update carried. Handlers registered here attach
    // once the provider has initialized, so they see the updates that follow startup.
    openFeature.AddHandler(ProviderEventTypes.ProviderConfigurationChanged, services =>
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");
        return payload => log.LogInformation("Configs updated: {Keys}", payload?.FlagsChanged);
    });
});

var app = builder.Build();

app.MapGet("/configs", async (HttpContext http, IFeatureClient client) =>
{
    var context = ContextFrom(http.Request.Query);

    // Each call reads config state the provider already holds, with no network call on the request
    // path. The default is what this application serves whenever ConfigDirector is unreachable, so
    // it should always be the safe choice; its type is also what the value is parsed as.
    // Keyed by config key, the same shape the other ConfigDirector sample applications return.
    return Results.Ok(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["temporary-feature-flag"] = await client.GetBooleanValueAsync("temporary-feature-flag", true, context),
        ["permanent-kill-switch"] = await client.GetBooleanValueAsync("permanent-kill-switch", false, context),
        ["integer-config"] = await client.GetIntegerValueAsync("integer-config", 10, context),
        ["day-of-the-week-config"] = await client.GetStringValueAsync("day-of-the-week-config", "Friday", context),

        // A JSON config comes back as an OpenFeature Value: a Structure for an object, a list for an
        // array. Plain() turns it into dictionaries and lists the response serializer understands.
        ["json-value-config"] = Plain(await client.GetObjectValueAsync("json-value-config", new Value(Structure.Empty), context)),
    });
});

// The same evaluations with everything OpenFeature knows about each one. A match reports the
// ConfigDirector value id as the variant; a key the server does not know, a value of another type,
// or a provider that is not ready yet each report an error code and the default you passed.
app.MapGet("/configs/details", async (HttpContext http, IFeatureClient client) =>
{
    var context = ContextFrom(http.Request.Query);

    return Results.Ok(new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["temporary-feature-flag"] = Describe(await client.GetBooleanDetailsAsync("temporary-feature-flag", true, context)),
        ["permanent-kill-switch"] = Describe(await client.GetBooleanDetailsAsync("permanent-kill-switch", false, context)),
        ["integer-config"] = Describe(await client.GetIntegerDetailsAsync("integer-config", 10, context)),
        ["day-of-the-week-config"] = Describe(await client.GetStringDetailsAsync("day-of-the-week-config", "Friday", context)),
        ["no-such-config"] = Describe(await client.GetStringDetailsAsync("no-such-config", "fallback", context)),
    });
});

app.MapGet("/health", (IFeatureClient client, Api api) => Results.Ok(new
{
    provider = api.GetProviderMetadata()?.Name,
    status = client.ProviderStatus.ToString(),
}));

app.MapFallback(() => Results.NotFound(new { error = "Not found. Try GET /configs" }));

app.Run();

static object? Plain(Value value)
{
    if (value.IsStructure)
    {
        return value.AsStructure!.ToDictionary(member => member.Key, member => Plain(member.Value), StringComparer.Ordinal);
    }

    if (value.IsList)
    {
        return value.AsList!.Select(Plain).ToList();
    }

    return value.AsObject;
}

static object Describe<T>(FlagEvaluationDetails<T> details) => new
{
    value = details.Value,
    reason = details.Reason,
    variant = details.Variant,
    errorType = details.ErrorType.ToString(),
    errorMessage = details.ErrorMessage,
};

// The evaluation context is per request; the provider that evaluates it is not. The targeting key
// becomes the ConfigDirector context id, and "traits" its traits. A real application would build
// this from the authenticated session rather than from the query string.
static EvaluationContext ContextFrom(IQueryCollection query)
{
    var context = EvaluationContext.Builder();
    if (query["id"] is { Count: > 0 } id)
    {
        context.SetTargetingKey(id.ToString());
    }

    if (query["name"] is { Count: > 0 } name)
    {
        context.Set("name", name.ToString());
    }

    context.Set("anonymous", query["anonymous"] == "true");

    var traits = Structure.Builder();
    foreach (var (parameter, values) in query)
    {
        if (parameter is "id" or "name" or "anonymous")
        {
            continue;
        }

        // A parameter given more than once becomes an array trait, which is what the "contains any
        // of" operators match on.
        if (values.Count > 1)
        {
            traits.Set(parameter, values.Select(value => new Value(value ?? string.Empty)).ToList());
        }
        else
        {
            traits.Set(parameter, values.ToString());
        }
    }

    return context.Set("traits", traits.Build()).Build();
}
