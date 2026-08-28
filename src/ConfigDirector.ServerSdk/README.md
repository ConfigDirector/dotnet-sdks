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
<PackageReference Include="ConfigDirector.ServerSdk" Version="0.1.0" />
```

## Documentation

Refer to the [official documentation for the .NET SDK](https://docs.configdirector.com/sdks/server/dotnet).

There is also [a quickstart guide for ConfigDirector and any of our SDKs](https://docs.configdirector.com/getting-started/quickstart).

## Sample apps

[`samples/`](https://github.com/ConfigDirector/dotnet-sdks/tree/main/samples) holds small, runnable
applications built on this SDK. Start with
[`ConfigDirector.Samples.AspNetCore`](https://github.com/ConfigDirector/dotnet-sdks/tree/main/samples/ConfigDirector.Samples.AspNetCore):

```bash
dotnet run --project samples/ConfigDirector.Samples.AspNetCore
```

## Getting Help

Reach out to us via https://www.configdirector.com/support
