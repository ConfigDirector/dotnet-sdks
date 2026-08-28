using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace ConfigDirector.Samples.Mvc.Controllers;

/// Evaluates configs against the context built for this request in Program.cs.
///
/// Query parameters double as the evaluation context: id, name and anonymous map onto the matching
/// Context fields, and anything else becomes a trait.
[ApiController]
[Route("configs")]
public sealed class ConfigsController : ControllerBase
{
    // Parsed once, and cloned so it outlives the document it came from. A default(JsonElement)
    // would not do: its kind is Undefined, which throws when the response is serialised.
    private static readonly JsonElement EmptyJson = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly IConfigDirectorClient _client;
    private readonly IConfigDirectorContextAccessor _context;

    /// The one client for the process, and the context for this request. Never build a client per
    /// request.
    public ConfigsController(IConfigDirectorClient client, IConfigDirectorContextAccessor context)
    {
        _client = client;
        _context = context;
    }

    [HttpGet]
    public IReadOnlyDictionary<string, object> Get()
    {
        // Built once for the request however many times it is read, so the six evaluations below
        // cost one pass over the query string rather than six.
        var context = _context.Context;

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
        _client.GetAllConfigs(_context.Context);
}
