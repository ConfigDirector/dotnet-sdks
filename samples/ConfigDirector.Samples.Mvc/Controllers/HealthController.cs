using Microsoft.AspNetCore.Mvc;

namespace ConfigDirector.Samples.Mvc.Controllers;

/// Whether config state has arrived. An application that is not ready still serves every request,
/// with each config resolving to the default the calling code supplied.
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    private readonly IConfigDirectorClient _client;

    public HealthController(IConfigDirectorClient client) => _client = client;

    [HttpGet]
    public object Get() => new { ready = _client.IsReady, closed = _client.IsClosed };
}
