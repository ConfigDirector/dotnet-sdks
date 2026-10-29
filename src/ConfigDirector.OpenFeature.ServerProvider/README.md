# ConfigDirector OpenFeature .NET Server Provider

[OpenFeature](https://openfeature.dev) server provider for
[ConfigDirector](https://www.configdirector.com), published to NuGet as
`ConfigDirector.OpenFeature.ServerProvider`. It wraps the
[ConfigDirector .NET server SDK](https://github.com/ConfigDirector/dotnet-sdks/tree/main/src/ConfigDirector.ServerSdk)
and targets `netstandard2.0` and `net8.0`.

## Installation

```bash
dotnet add package ConfigDirector.OpenFeature.ServerProvider
```

The OpenFeature .NET SDK (`OpenFeature`) and the ConfigDirector .NET server SDK come with it as
dependencies.

## Retrieve a value

```csharp
using ConfigDirector.OpenFeature;
using OpenFeature;

// The server SDK key is a secret. Do not commit it to your source code.
await Api.Instance.SetProviderAsync(new ConfigDirectorProvider("YOUR-SERVER-SDK-KEY"));
var client = Api.Instance.GetClient();

var newCheckout = await client.GetBooleanValueAsync("new-checkout", false);
```

The provider owns its ConfigDirector client: it connects when OpenFeature initializes the provider,
and closes when OpenFeature shuts it down. Targeting rules are evaluated locally, so an evaluation
makes no network calls.

## Settings

The second constructor argument takes the same `ConfigDirectorClientOptions` a
`ConfigDirectorClient` accepts:

```csharp
new ConfigDirectorProvider("YOUR-SERVER-SDK-KEY", new ConfigDirectorClientOptions
{
    Metadata = new Metadata { AppName = "checkout", AppVersion = "1.2.3" },
    LoggerFactory = loggerFactory,
    Connection = { Mode = ConnectionMode.Polling },
});
```

## The evaluation context

An OpenFeature evaluation context maps onto the ConfigDirector context that targeting rules are
evaluated against:

| OpenFeature | ConfigDirector |
| --- | --- |
| targeting key, or failing that an `id` attribute | `Id` |
| `kind`, beside the targeting key rather than inside `traits` | `Kind` |
| `name` | `Name` |
| `traits`, a structure | `Traits` |
| `anonymous`, a boolean | `Anonymous` |

An omitted `kind` means `user`, and a `kind` inside `traits` is an ordinary trait. A context whose
kind is invalid is disregarded: the SDK logs an error, and the flag resolves successfully to its
value for no context.

```csharp
var context = EvaluationContext.Builder()
    .SetTargetingKey("user-1")
    .Set("name", "Ada Lovelace")
    .Set("traits", Structure.Builder().Set("plan", "pro").Build())
    .Build();

var newCheckout = await client.GetBooleanValueAsync("new-checkout", false, context);
```

## Resolution details

A match reports the reason `TARGETING_MATCH` with the ConfigDirector value id as the variant. A
config the server does not know reports `FLAG_NOT_FOUND`, a value that does not match the requested
type reports `TYPE_MISMATCH`, and an evaluation made before the first config state has arrived
reports `PROVIDER_NOT_READY`. In every error case the default you passed is returned.

## Events

The provider emits `PROVIDER_CONFIGURATION_CHANGED` with the keys each update carried, followed by
the keys a full update removed.

If no config arrives within the connection timeout, or ConfigDirector rejects the server SDK key,
initialization fails with OpenFeature's `ProviderNotReadyException`. `SetProviderAsync` still
completes, and OpenFeature reports the `Error` status with a `PROVIDER_ERROR` event. Evaluations
return their in-code default values with `PROVIDER_NOT_READY` until config arrives. After a
timeout, or an initialization you cancelled, the provider keeps connecting, and emits
`PROVIDER_READY` once config arrives.

## Dependency injection

With the `OpenFeature.Hosting` package, register the provider through the OpenFeature builder:

```csharp
builder.Services.AddOpenFeature(openFeature =>
    openFeature.AddProvider(_ => new ConfigDirectorProvider(serverSdkKey)));
```

The hosted lifecycle that `AddOpenFeature` registers initializes the provider as the host starts,
which connects the client before the server listens, and shuts it down as the host stops. Take
`IFeatureClient`, and `Api` if you need it, from the container: `AddOpenFeature` drives its own
`Api` instance, so the static `Api.Instance` is not the one your provider is registered with.

## Test your code

Tests of code that reads flags through OpenFeature swap the provider for the in-memory one the
OpenFeature .NET SDK ships, `InMemoryProvider` in `OpenFeature.Providers.Memory`, so the test
controls the values and nothing from ConfigDirector is involved:

```csharp
using OpenFeature.Providers.Memory;

var provider = new InMemoryProvider(new Dictionary<string, Flag>
{
    ["new-checkout"] = new Flag<bool>(new Dictionary<string, bool> { ["on"] = true, ["off"] = false }, "on"),
});
await Api.Instance.SetProviderAsync(provider);

Assert.True(await client.GetBooleanValueAsync("new-checkout", false));

await provider.UpdateFlagsAsync(new Dictionary<string, Flag>
{
    ["new-checkout"] = new Flag<bool>(new Dictionary<string, bool> { ["on"] = true, ["off"] = false }, "off"),
});
Assert.False(await client.GetBooleanValueAsync("new-checkout", true));
```

Full details are in the
[testing section of the official documentation](https://docs.configdirector.com/sdks/openfeature/dotnet#test-your-code).

## Documentation

Refer to the [official documentation for the OpenFeature .NET provider](https://docs.configdirector.com/sdks/openfeature/dotnet).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.OpenFeature.ServerProvider/CHANGELOG.md).

## Getting Help

Reach out to us via https://www.configdirector.com/support
