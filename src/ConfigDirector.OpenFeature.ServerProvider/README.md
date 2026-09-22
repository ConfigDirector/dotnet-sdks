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
| `name` | `Name` |
| `traits`, a structure | `Traits` |
| `anonymous`, a boolean | `Anonymous` |

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

The provider emits `PROVIDER_CONFIGURATION_CHANGED` with the keys each update carried. If the first
config state does not arrive within the configured timeout, initialization still completes and the
provider emits `PROVIDER_READY` once it does.

## Dependency injection

With the `OpenFeature.Hosting` package, register the provider through the OpenFeature builder:

```csharp
builder.Services.AddOpenFeature(openFeature =>
{
    openFeature.AddHostedFeatureLifecycle();
    openFeature.AddProvider(_ => new ConfigDirectorProvider(serverSdkKey));
});
```

## Documentation

Refer to the [official documentation for the OpenFeature .NET provider](https://docs.configdirector.com/sdks/openfeature/dotnet).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.OpenFeature.ServerProvider/CHANGELOG.md).

## Getting Help

Reach out to us via https://www.configdirector.com/support
