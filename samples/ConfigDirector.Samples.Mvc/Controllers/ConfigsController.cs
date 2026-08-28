using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ConfigDirector.Samples.Mvc.Controllers;

/// Evaluates configs against a context built from the query string.
///
/// Query parameters double as the evaluation context: id, name and anonymous map onto the matching
/// Context fields, and anything else becomes a trait.
[ApiController]
[Route("configs")]
public sealed class ConfigsController : ControllerBase
{
    private static readonly string[] ContextFields = ["id", "name", "anonymous"];

    // Parsed once, and cloned so it outlives the document it came from. A default(JsonElement)
    // would not do: its kind is Undefined, which throws when the response is serialised.
    private static readonly JsonElement EmptyJson = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly IConfigDirectorClient _client;

    /// The one client for the process, injected. Never build one per request.
    public ConfigsController(IConfigDirectorClient client) => _client = client;

    [HttpGet]
    public IReadOnlyDictionary<string, object> Get()
    {
        var context = ContextFrom(Request.Query);

        // Each call reads config state the client already holds, with no network call on the
        // request path, which is what makes several of them in one action cheap.
        //
        // The default is what this application serves whenever ConfigDirector is unreachable, so it
        // should always be the safe choice. Its type is also what the value is parsed as.
        // Keyed by config key, the same shape the other ConfigDirector sample applications return.
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["temporary-feature-flag"] = _client.GetValue("temporary-feature-flag", true, context),
            ["permanent-kill-switch"] = _client.GetValue("permanent-kill-switch", false, context),
            ["integer-config"] = _client.GetValue("integer-config", 10, context),
            ["day-of-the-week-config"] = _client.GetValue("day-of-the-week-config", "Friday", context),

            // A JSON config read as it stands, because its shape lives in the dashboard rather than
            // here. GetJsonValue binds it to a type of this application's own instead, for a config
            // whose shape this application is the one that decides.
            ["json-value-config"] = _client.GetValue("json-value-config", EmptyJson, context),
        };
    }

    /// Every config at once, evaluated but unparsed. This is the shape a client SDK hydrates from,
    /// and it records no telemetry, since the SDK that receives it reports its own evaluations.
    [HttpGet("all")]
    public IReadOnlyDictionary<string, ConfigState> GetAll() =>
        _client.GetAllConfigs(ContextFrom(Request.Query));

    // The context is per request; the client that evaluates it is not. A real application would
    // build this from the authenticated session rather than from the query string.
    private static Context ContextFrom(IQueryCollection query)
    {
        var context = new Context
        {
            Id = query["id"],
            Name = query["name"],
            Anonymous = query["anonymous"] == "true",
        };

        foreach (var (name, values) in query)
        {
            if (ContextFields.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            // A parameter given more than once becomes an array trait, which is what the "contains
            // any of" operators match on.
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
}
