using Microsoft.AspNetCore.Http;

namespace ConfigDirector;

internal sealed class HttpContextConfigDirectorContextAccessor : IConfigDirectorContextAccessor
{
    private readonly IHttpContextAccessor _http;
    private readonly Func<HttpContext, Context?> _build;
    private Context? _context;
    private bool _built;

    public HttpContextConfigDirectorContextAccessor(
        IHttpContextAccessor http, Func<HttpContext, Context?> build)
    {
        _http = http;
        _build = build;
    }

    // Registered scoped, so this caches for the life of one request: an action reading six configs
    // builds the context once rather than six times.
    public Context? Context
    {
        get
        {
            if (!_built)
            {
                var http = _http.HttpContext;
                _context = http is null ? null : _build(http);
                _built = true;
            }

            return _context;
        }
    }
}
