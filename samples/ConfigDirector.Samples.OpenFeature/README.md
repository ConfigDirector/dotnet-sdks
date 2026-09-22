# ConfigDirector OpenFeature provider — ASP.NET Core sample

The same app as the [`ConfigDirector.Samples.MinimalApi`](../ConfigDirector.Samples.MinimalApi/)
sample — one `/configs` endpoint evaluating a handful of configs — with every value read through an
[OpenFeature](https://openfeature.dev) client instead of the ConfigDirector client. The
`ConfigDirector.OpenFeature.ServerProvider` package is registered through `OpenFeature.Hosting`,
which initializes it before the server listens and shuts it down with the host.

It connects to ConfigDirector for real, so it needs a server SDK key:

```bash
cp samples/ConfigDirector.Samples.OpenFeature/.env.example samples/ConfigDirector.Samples.OpenFeature/.env
# put your key in .env, then
dotnet run --project samples/ConfigDirector.Samples.OpenFeature
```

It listens on port 5002, so it and the other web samples on 5000 and 5001 can run at the same
time.

Without a usable key it still starts. Initializing the provider does not throw on a connection
failure, so the application comes up and every evaluation returns the default the calling code
supplied with the error code `PROVIDER_NOT_READY`, which is the behavior to design for in
production.

## Which provider it builds against

By default the sample references the published `ConfigDirector.OpenFeature.ServerProvider`
package, so it reads the way a real consuming application does. CI and the pre-push hook set
`UseLocalSdk`, which swaps in the projects from this checkout instead:

```bash
UseLocalSdk=true dotnet build samples/ConfigDirector.Samples.OpenFeature
```

Either way it references only the provider and `OpenFeature.Hosting`. The server SDK and the
OpenFeature SDK arrive transitively. The package versions are pinned in
[Directory.Packages.props](../../Directory.Packages.props).

## Endpoints

| Endpoint               | What it shows                                                                 |
| ---------------------- | ----------------------------------------------------------------------------- |
| `GET /configs`         | Evaluating several configs against a per-request evaluation context           |
| `GET /configs/details` | The same evaluations with their reason, variant and error code                |
| `GET /health`          | The provider's name and status as OpenFeature sees it                         |

Query parameters double as the evaluation context: `id` becomes the targeting key, `name` and
`anonymous` become attributes of the same name, and anything else becomes a member of the `traits`
structure. The provider maps those onto the ConfigDirector context that targeting rules are
evaluated against. A parameter given more than once becomes an array trait.

```bash
curl 'localhost:5002/configs?id=user-123&plan=pro'
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
up with the other samples here and with the Java and Python ones.

`json-value-config` comes back as an OpenFeature `Value`: a `Structure` for an object, a list for
an array, and nested values inside. The sample walks it into plain dictionaries and lists for the
response, because a `Structure` is not something the response serializer knows how to write. The
provider builds it from the JSON the server SDK parses, so nothing is bound to a type of the
application's own.

```bash
curl 'localhost:5002/configs/details?id=user-123&plan=pro'
```

```json
{
  "temporary-feature-flag": {
    "value": true,
    "reason": "TARGETING_MATCH",
    "variant": "temporary-feature-flag-on",
    "errorType": "None",
    "errorMessage": null
  },
  "no-such-config": {
    "value": "fallback",
    "reason": "ERROR",
    "variant": null,
    "errorType": "FlagNotFound",
    "errorMessage": "No config with the key 'no-such-config' was found."
  }
}
```

A match reports `TARGETING_MATCH` with the ConfigDirector value id as the variant. A config the
server does not know reports `FLAG_NOT_FOUND`, a value of another type reports `TYPE_MISMATCH`,
and an evaluation before the first config state has arrived reports `PROVIDER_NOT_READY`. In every
error case the default you passed is what comes back.

## How it is wired

`AddOpenFeature` registers the OpenFeature API with the container and hands out `IFeatureClient`
to anything that asks for one. Inside it:

**The hosted lifecycle** `AddOpenFeature` registers initializes the provider as the host starts and
shuts it down as it stops. Initializing is what connects the ConfigDirector client, and it runs
before the server listens, so no request is served defaults while the first config state is still
in flight. Shutting down closes the client and flushes its telemetry.

**`AddProvider(...)`** builds the one provider for the process, which owns the one ConfigDirector
client. Never build a provider per request: each holds a connection, and a fresh one serves defaults
until its first config state arrives. It takes the same `ConfigDirectorClientOptions` a
`ConfigDirectorClient` does, so the settings, the host's logger factory and the connection mode are
declared exactly as they would be for the SDK on its own.

**`AddHandler(ProviderEventTypes.ProviderConfigurationChanged, ...)`** logs the keys each update
carried. Handlers registered this way attach once the provider has initialized, so they see the
updates that follow startup rather than the first config state.

**`Api` comes from the container.** `AddOpenFeature` drives its own `Api` instance rather than the
static `Api.Instance`, so anything that needs the API, such as the `/health` endpoint reading the
provider's name, takes `Api` as a dependency. Reading `Api.Instance` in a hosted application would
show the no-op provider.

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
