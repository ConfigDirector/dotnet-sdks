# ConfigDirector .NET Server SDK

The .NET server SDK for [ConfigDirector](https://www.configdirector.com), published to NuGet as
`ConfigDirector.ServerSdk`. It targets `net8.0` and `netstandard2.0`.

This is one of several ConfigDirector packages for .NET published from
[this repository](https://github.com/ConfigDirector/dotnet-sdks), each in a project of its own.

## Installation

```bash
dotnet add package ConfigDirector.ServerSdk
```

Or as a package reference:

```xml
<PackageReference Include="ConfigDirector.ServerSdk" Version="1.1.0" />
```

## Trimming and native AOT

The package is annotated `IsAotCompatible`, so a trimmed or AOT-published application gets no
warnings from it. The exception is `GetJsonValue<T>` and `WatchJson<T>`, which bind a config to a
type of your own and therefore read that type reflectively; both are annotated, so the trimmer
points at your call site. Read the config as `JsonElement` to stay fully AOT-safe.

## Documentation

Refer to the [official documentation for the .NET SDK](https://docs.configdirector.com/sdks/server/dotnet).

There is also [a quickstart guide for ConfigDirector and any of our SDKs](https://docs.configdirector.com/getting-started/quickstart).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.ServerSdk/CHANGELOG.md).

## Sample apps

[`samples/`](https://github.com/ConfigDirector/dotnet-sdks/tree/main/samples) holds small, runnable
applications built on this SDK -- the same app written as a
[Minimal API](https://github.com/ConfigDirector/dotnet-sdks/tree/main/samples/ConfigDirector.Samples.MinimalApi), with
[MVC controllers](https://github.com/ConfigDirector/dotnet-sdks/tree/main/samples/ConfigDirector.Samples.Mvc), and as a
[native AOT console application](https://github.com/ConfigDirector/dotnet-sdks/tree/main/samples/ConfigDirector.Samples.NativeAot).

```bash
dotnet run --project samples/ConfigDirector.Samples.MinimalApi
```

## Getting Help

Reach out to us via https://www.configdirector.com/support
