# ConfigDirector .NET SDKs

[![CI][ci-badge]][ci] [![NuGet ServerSdk][nuget-badge]][nuget] [![NuGet AspNetCore][nuget-aspnet-badge]][nuget-aspnet] [![NuGet OpenFeature][nuget-openfeature-badge]][nuget-openfeature]

.NET server SDK and [OpenFeature](https://openfeature.dev) server provider for [ConfigDirector](https://www.configdirector.com), remote config and feature flags with typed values, JSON Schema validation, and safe renames of live flags. Start free, no card required.

Each package carries its own README and changelog:

- [`ConfigDirector.ServerSdk`](src/ConfigDirector.ServerSdk/) --
  [changelog](src/ConfigDirector.ServerSdk/CHANGELOG.md)
- [`ConfigDirector.ServerSdk.AspNetCore`](src/ConfigDirector.ServerSdk.AspNetCore/) --
  [changelog](src/ConfigDirector.ServerSdk.AspNetCore/CHANGELOG.md)
- [`ConfigDirector.OpenFeature.ServerProvider`](src/ConfigDirector.OpenFeature.ServerProvider/) --
  [changelog](src/ConfigDirector.OpenFeature.ServerProvider/CHANGELOG.md)

## Install

```bash
dotnet add package ConfigDirector.ServerSdk
```

In an ASP.NET Core application, add `ConfigDirector.ServerSdk.AspNetCore` instead; it registers the client, binds its settings from configuration, connects before the server starts listening, and disposes it on shutdown.

To read configs through the [OpenFeature](https://openfeature.dev) API instead, add `ConfigDirector.OpenFeature.ServerProvider`, which brings the server SDK and the OpenFeature .NET SDK with it.

## Retrieve a value

```csharp
using ConfigDirector;

// The server SDK key is a secret. Do not commit it to your source code.
await using var client = new ConfigDirectorClient("YOUR-SERVER-SDK-KEY");
await client.InitializeAsync();

var newCheckout = client.GetValue("new-checkout", false);
```

Full details are in the [official documentation](https://docs.configdirector.com/sdks/server/dotnet).

## Retrieve a value through OpenFeature

```csharp
using ConfigDirector.OpenFeature;
using OpenFeature;

await Api.Instance.SetProviderAsync(new ConfigDirectorProvider("YOUR-SERVER-SDK-KEY"));
var client = Api.Instance.GetClient();

var newCheckout = await client.GetBooleanValueAsync("new-checkout", false);
```

Full details are in the [official documentation for the OpenFeature .NET provider](https://docs.configdirector.com/sdks/openfeature/dotnet).

## Documentation

Refer to the [official documentation for the .NET SDK](https://docs.configdirector.com/sdks/server/dotnet) and
the [OpenFeature .NET provider](https://docs.configdirector.com/sdks/openfeature/dotnet).

There is also [a quickstart guide for ConfigDirector and any of our SDKs](https://docs.configdirector.com/getting-started/quickstart).

## Sample apps

[`samples/`](samples/) holds small, runnable applications built on this SDK. They are the same app
written each way -- a single `/configs` endpoint -- so they can be read side by side.

[`ConfigDirector.Samples.MinimalApi`](samples/ConfigDirector.Samples.MinimalApi/):

```bash
dotnet run --project samples/ConfigDirector.Samples.MinimalApi
```

[`ConfigDirector.Samples.Mvc`](samples/ConfigDirector.Samples.Mvc/), the same app wired up with
the `ConfigDirector.ServerSdk.AspNetCore` package rather than by hand:

```bash
dotnet run --project samples/ConfigDirector.Samples.Mvc
```

[`ConfigDirector.Samples.NativeAot`](samples/ConfigDirector.Samples.NativeAot/), a console
application published to native code:

```bash
dotnet run --project samples/ConfigDirector.Samples.NativeAot
```

[`ConfigDirector.Samples.OpenFeature`](samples/ConfigDirector.Samples.OpenFeature/), the same app
reading every value through an OpenFeature client, with the
`ConfigDirector.OpenFeature.ServerProvider` package registered through `OpenFeature.Hosting`:

```bash
dotnet run --project samples/ConfigDirector.Samples.OpenFeature
```

[`ConfigDirector.Samples.OpenFeature.NativeAot`](samples/ConfigDirector.Samples.OpenFeature.NativeAot/),
the OpenFeature provider in a console application published to native code:

```bash
dotnet run --project samples/ConfigDirector.Samples.OpenFeature.NativeAot
```

## Getting Help

- [Ask a question in Discussions](https://github.com/orgs/ConfigDirector/discussions)
- [Contact support](https://www.configdirector.com/support)

[//]: # "links"
[ci-badge]: https://github.com/ConfigDirector/dotnet-sdks/actions/workflows/server-sdk-ci.yml/badge.svg
[ci]: https://github.com/ConfigDirector/dotnet-sdks/actions/workflows/server-sdk-ci.yml
[nuget-badge]: https://img.shields.io/nuget/v/ConfigDirector.ServerSdk?label=ConfigDirector.ServerSdk
[nuget]: https://www.nuget.org/packages/ConfigDirector.ServerSdk
[nuget-aspnet-badge]: https://img.shields.io/nuget/v/ConfigDirector.ServerSdk.AspNetCore?label=ConfigDirector.ServerSdk.AspNetCore
[nuget-aspnet]: https://www.nuget.org/packages/ConfigDirector.ServerSdk.AspNetCore
[nuget-openfeature-badge]: https://img.shields.io/nuget/v/ConfigDirector.OpenFeature.ServerProvider?label=ConfigDirector.OpenFeature.ServerProvider
[nuget-openfeature]: https://www.nuget.org/packages/ConfigDirector.OpenFeature.ServerProvider
