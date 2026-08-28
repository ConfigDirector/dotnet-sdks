# ConfigDirector .NET SDKs

[![Actions Status][ci-badge]][ci]

This is the .NET server SDK for [ConfigDirector](https://www.configdirector.com), in
[`src/ConfigDirector.ServerSdk/`](src/ConfigDirector.ServerSdk/). More ConfigDirector packages for
.NET will be published from this repository over time, each in a project of its own.

## Documentation

Refer to the [official documentation for the .NET SDK](https://docs.configdirector.com/sdks/server/dotnet).

There is also [a quickstart guide for ConfigDirector and any of our SDKs](https://docs.configdirector.com/getting-started/quickstart).

## Sample apps

[`samples/`](samples/) holds small, runnable applications built on this SDK. Start with
[`ConfigDirector.Samples.AspNetCore`](samples/ConfigDirector.Samples.AspNetCore/):

```bash
dotnet run --project samples/ConfigDirector.Samples.AspNetCore
```

## Getting Help

Reach out to us via https://www.configdirector.com/support

[//]: # "links"
[ci-badge]: https://github.com/ConfigDirector/dotnet-sdks/actions/workflows/server-sdk-ci.yml/badge.svg
[ci]: https://github.com/ConfigDirector/dotnet-sdks/actions/workflows/server-sdk-ci.yml
