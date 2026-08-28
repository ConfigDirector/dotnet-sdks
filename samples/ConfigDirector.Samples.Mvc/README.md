# ConfigDirector server SDK — ASP.NET Core MVC sample

The same app as the [`ConfigDirector.Samples.MinimalApi`](../ConfigDirector.Samples.MinimalApi/)
sample — one `/configs` endpoint evaluating a handful of configs — written with controllers
instead, so the two can be read side by side.

It connects to ConfigDirector for real, so it needs a server SDK key:

```bash
cp samples/ConfigDirector.Samples.Mvc/.env.example samples/ConfigDirector.Samples.Mvc/.env
# put your key in .env, then
dotnet run --project samples/ConfigDirector.Samples.Mvc
```

It listens on port 5001, so it and the Minimal API sample on 5000 can run at the same time.

Without a usable key it still starts. Initialization does not throw on a connection failure, so
the application comes up, logs a warning, and every config resolves to the default the calling
code supplied, which is the behavior to design for in production.

## Which SDK it builds against

By default the sample references the published `ConfigDirector.ServerSdk` package, so it reads the
way a real consuming application does. CI and the pre-push hook set `UseLocalSdk`, which swaps in
the project from this checkout instead:

```bash
UseLocalSdk=true dotnet build samples/ConfigDirector.Samples.Mvc
```

That is what catches a breaking API change here rather than after it ships. The package version is
pinned in [Directory.Packages.props](../../Directory.Packages.props).

## Endpoints

| Endpoint           | What it shows                                                                       |
| ------------------ | ----------------------------------------------------------------------------------- |
| `GET /configs`     | Evaluating several configs against a per-request context                            |
| `GET /configs/all` | Every config at once, evaluated but unparsed — the shape a client SDK hydrates from |
| `GET /health`      | Whether config state has arrived                                                    |

Query parameters double as the evaluation context: `id`, `name`, and `anonymous` map onto the
matching `Context` fields, and anything else becomes a trait. A parameter given more than once
becomes an array trait.

```bash
curl 'localhost:5001/configs?id=user-123&plan=pro'
```

```json
{
  "temporary-feature-flag": true,
  "permanent-kill-switch": true,
  "integer-config": 25,
  "day-of-the-week-config": "Monday",
  "json-value-config": { "retries": 3, "timeoutMs": 1500 }
}
```

## What controllers change, and what they do not

**The client is still one per process.** It is registered as a singleton in
[Program.cs](Program.cs), exactly as in the Minimal API sample. Controllers are built per request,
so injecting `IConfigDirectorClient` into a controller constructor hands every request the same
instance:

```csharp
public ConfigsController(IConfigDirectorClient client) => _client = client;
```

Never build a client in a controller. Each one opens its own connection, initialization does
network I/O, and a fresh client serves defaults until its first config state arrives. It is thread
safe, so sharing one across request threads is the point.

**Initialization still happens before the server listens.** `InitializeAsync` is awaited in
`Program.cs` before `app.Run()`, which is where it belongs regardless of how endpoints are
declared — a controller has no place to put startup work.

**Evaluation is unchanged.** `GetValue` reads config state the client already holds, with no
network call on the request path, so calling it several times in one action is cheap. The default
you pass is what the application serves when ConfigDirector is unreachable, and its type is what
the value is parsed as.

The only real difference is where the endpoint lives:
[Controllers/ConfigsController.cs](Controllers/ConfigsController.cs) and
[Controllers/HealthController.cs](Controllers/HealthController.cs) replace the `app.MapGet` calls.
`app.MapFallback` stays in `Program.cs`, since a 404 for an unrouted path is not a controller's
concern.

## Settings

Bound from the `ConfigDirector` section of [appsettings.json](appsettings.json), which holds the
defaults. Anything there can be overridden by an environment variable, where `__` separates
configuration sections:

```bash
ConfigDirector__ServerSdkKey=your-key ConfigDirector__Mode=Polling dotnet run
```

For local development, put the same variables in a `.env` file next to this README:

```bash
cp .env.example .env
```

Precedence runs `appsettings.json` < `.env` < a real environment variable, so a deployment
platform's injected secrets always win and the file stays a local convenience. An absent `.env` is
not an error.

`.env` is gitignored. The key is a secret: never commit one.
