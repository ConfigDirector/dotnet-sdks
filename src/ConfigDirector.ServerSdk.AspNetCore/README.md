# ConfigDirector for ASP.NET Core

Dependency injection and configuration binding for the
[ConfigDirector](https://www.configdirector.com) .NET server SDK, published to NuGet as
`ConfigDirector.ServerSdk.AspNetCore`. It targets `net8.0`.

This package sits on top of
[`ConfigDirector.ServerSdk`](https://github.com/ConfigDirector/dotnet-sdks/tree/main/src/ConfigDirector.ServerSdk)
and brings it along, so install this one and you have both. It replaces the wiring only: reading a
config is the same `IConfigDirectorClient` API either way, and an application that would rather
register the client by hand can keep doing so.

## Installation

```bash
dotnet add package ConfigDirector.ServerSdk.AspNetCore
```

## Usage

```csharp
builder.Services.AddConfigDirector();
```

That binds the `ConfigDirector` configuration section and registers one client for the application:

```json
{
  "ConfigDirector": {
    "ServerSdkKey": "...",
    "Connection": { "Mode": "Streaming", "Timeout": "00:00:03" }
  }
}
```

The key is a secret, so supply it as an environment variable, a user secret, or in code:

```csharp
builder.Services.AddConfigDirector(options => options.ServerSdkKey = secrets.ConfigDirectorKey);
```

Pass an `IConfiguration` to bind a section under a different name.

`AppName` and `AppVersion` default to the host's application name and the entry assembly's
informational version, so targeting rules can match on the application without configuring either.

The client is a singleton and the container disposes it on shutdown. It is registered with
`TryAdd`, so an `IConfigDirectorClient` already in the collection is left alone -- which is how an
integration test substitutes a fake.

Connecting is still the caller's to do, with `InitializeAsync` during startup.

## Documentation

Refer to the [official documentation for the .NET SDK](https://docs.configdirector.com/sdks/server/dotnet).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.ServerSdk.AspNetCore/CHANGELOG.md).

## Getting Help

Reach out to us via https://www.configdirector.com/support
