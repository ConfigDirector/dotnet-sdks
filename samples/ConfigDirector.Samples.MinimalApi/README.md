# ConfigDirector server SDK — ASP.NET Core Minimal API sample

A minimal API showing how to use the SDK from an ASP.NET Core application: one client for the
process, built by dependency injection, initialized before the server starts listening, and
disposed on shutdown.

The [`ConfigDirector.Samples.Mvc`](../ConfigDirector.Samples.Mvc/) sample is the same app written
with controllers, so the two can be read side by side.

It connects to ConfigDirector for real, so it needs a server SDK key:

```bash
cp samples/ConfigDirector.Samples.MinimalApi/.env.example samples/ConfigDirector.Samples.MinimalApi/.env
# put your key in .env, then
dotnet run --project samples/ConfigDirector.Samples.MinimalApi
```

It listens on port 5000, so it and the MVC sample on 5001 can run at the same time.

Without a usable key it still starts. Initialization does not throw on a connection failure, so
the application comes up, logs a warning, and every config resolves to the default the calling
code supplied, which is the behavior to design for in production.

## Which SDK it builds against

By default the sample references the published `ConfigDirector.ServerSdk` package, so it reads the
way a real consuming application does. CI and the pre-push hook set `UseLocalSdk`, which swaps in
the project from this checkout instead:

```bash
UseLocalSdk=true dotnet build samples/ConfigDirector.Samples.MinimalApi
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
curl 'localhost:5000/configs?id=user-123&plan=pro'
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

The config keys are the ones every ConfigDirector sample application uses, so the responses line
up with the Java and Python samples. The values above are what those keys hold in the sample
project; yours will reflect your own environment.

`json-value-config` is read as a `JsonNode`, so whatever the dashboard holds comes back verbatim.
`GetValue` refuses a type it cannot fill faithfully, because `System.Text.Json` silently drops
properties your type does not declare — a config whose shape has moved on would bind to your type's
own defaults and read exactly like the SDK having returned your default value. When you do control
the shape and want that binding, ask for it deliberately:

```csharp
var settings = client.GetJsonValue("json-value-config", new MySettings());
```

`plan=pro` matches a targeting rule and turns `temporary-feature-flag` on. `id` alone decides the
percentage bucket for `permanent-kill-switch`, so the same id always lands in the same half. A
repeated `tags` parameter becomes an array trait, which `day-of-the-week-config` matches on:

```bash
curl 'localhost:5000/configs?id=user-3&tags=beta&tags=vip'
```

## Settings

Bound from the `ConfigDirector` section of `appsettings.json`, which holds the defaults. Anything
there can be overridden by an environment variable, where `__` separates configuration sections:

```bash
ConfigDirector__ServerSdkKey=your-key ConfigDirector__Mode=Polling dotnet run
```

For local development, put the same variables in a `.env` file next to this README:

```bash
cp .env.example .env
```

It is read into the process environment at startup, so it is not a ConfigDirector feature, the
file just holds variables a deployment platform would otherwise export. Precedence runs
`appsettings.json` < `.env` < a real environment variable, so the platform's injected secrets
always win and the file stays a local convenience. An absent `.env` is not an error.

`.env` is gitignored. The key is a secret: never commit one.
