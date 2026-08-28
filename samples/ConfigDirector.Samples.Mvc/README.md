# ConfigDirector server SDK — ASP.NET Core MVC sample

The same app as the [`ConfigDirector.Samples.MinimalApi`](../ConfigDirector.Samples.MinimalApi/)
sample — one `/configs` endpoint evaluating a handful of configs — written with controllers, and
wired up with the `ConfigDirector.ServerSdk.AspNetCore` package instead of by hand. Read the two
`Program.cs` files side by side: the difference between them is the whole of what the package does.

It connects to ConfigDirector for real, so it needs a server SDK key:

```bash
cp samples/ConfigDirector.Samples.Mvc/.env.example samples/ConfigDirector.Samples.Mvc/.env
# put your key in .env, then
dotnet run --project samples/ConfigDirector.Samples.Mvc
```

It listens on port 5001, so it and the Minimal API sample on 5000 can run at the same time.

Without a usable key it still starts. Failing to connect does not stop the host, so the application
comes up, logs a warning, and every config resolves to the default the calling code supplied, which
is the behavior to design for in production.

## Which SDK it builds against

This sample references
[`ConfigDirector.ServerSdk.AspNetCore`](../../src/ConfigDirector.ServerSdk.AspNetCore/) from this
checkout directly, because that package is not published yet. `UseLocalSdk` — which the other
samples use to swap the published package for the projects here — does not apply to it. The server
SDK arrives transitively, which is how a consuming application gets both from one install.

## Endpoints

| Endpoint           | What it shows                                                                       |
| ------------------ | ----------------------------------------------------------------------------------- |
| `GET /configs`     | Evaluating several configs against a per-request context                            |
| `GET /configs/all` | Every config at once, evaluated but unparsed — the shape a client SDK hydrates from |
| `GET /health`      | The SDK's readiness as an ASP.NET Core health check                                 |

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

## What the package replaces

**Registration.** `AddConfigDirector()` binds the `ConfigDirector` configuration section, registers
one `IConfigDirectorClient` for the process, and hands the SDK the host's own logger factory. The
options class, the `AddSingleton` lambda and the `ILoggerFactory` wiring the Minimal API sample
writes out are all folded into it.

**Connecting.** There is no `InitializeAsync` to place. The package connects during startup, before
any hosted service starts — which puts it ahead of the web server, so no request is served config
defaults while the first config state is still in flight. Set `RequireReadyOnStartup` to fail the
deployment instead of starting degraded.

**The per-request context.** `.WithContext(...)` declares once how a request becomes a `Context`.
Actions take an `IConfigDirectorContextAccessor` and read `Context` from it, rather than each
rebuilding one from the query string:

```csharp
public ConfigsController(IConfigDirectorClient client, IConfigDirectorContextAccessor context)
```

It is built at most once per request however many times it is read, so the six evaluations in
`Get()` cost one pass over the query string.

**Readiness.** `AddHealthChecks().AddConfigDirector()` plus `MapHealthChecks("/health")` replaces
the hand-written health controller. It reports `Degraded` rather than `Unhealthy` while config
state has not arrived, because the application still answers every request.

## What it does not change

**The client is still one per process**, and still thread safe. Controllers are built per request,
so injecting `IConfigDirectorClient` hands every request the same instance. Never build a client in
a controller.

**Evaluation is unchanged.** `GetValue` reads config state the client already holds, with no
network call on the request path. The default you pass is what the application serves when
ConfigDirector is unreachable, and its type is what the value is parsed as. Nothing about reading a
config differs between this sample and the hand-wired one.

**Watches still have to be registered before the connection opens.** Resolving the client in
`Program.cs` builds it — which makes no network calls — while the connection is opened later, as
the host starts. Registering a watch in that gap is what has it called for the first config state
as well as the updates after it.

## Settings

Bound from the `ConfigDirector` section of [appsettings.json](appsettings.json), which holds the
defaults. Anything there can be overridden by an environment variable, where `__` separates
configuration sections:

```bash
ConfigDirector__ServerSdkKey=your-key ConfigDirector__Connection__Mode=Polling dotnet run
```

For local development, put the same variables in a `.env` file next to this README:

```bash
cp .env.example .env
```

Precedence runs `appsettings.json` < `.env` < a real environment variable, so a deployment
platform's injected secrets always win and the file stays a local convenience. An absent `.env` is
not an error.

`.env` is gitignored. The key is a secret: never commit one.
