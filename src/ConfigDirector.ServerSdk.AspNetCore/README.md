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

## Startup

Connecting happens during startup, before any hosted service is started -- which puts it ahead of
the web server, so no request is served config defaults while the first config state is still in
flight. There is no `InitializeAsync` call for you to place.

Failing to reach ConfigDirector does not stop the host: a warning is logged and every config
resolves to the default its caller supplied, which is the SDK's own posture. Set
`RequireReadyOnStartup` to fail the deployment instead:

```json
{ "ConfigDirector": { "RequireReadyOnStartup": true } }
```

## The per-request context

Declare once how a request becomes an evaluation context, rather than rebuilding one in every
action:

```csharp
builder.Services.AddConfigDirector()
    .WithContext(http => new Context { Id = http.User.FindFirst("sub")?.Value });
```

Then inject `IConfigDirectorContextAccessor` and read `Context` from it. It is built at most once
per request however many times it is read, so six evaluations in one action cost one call to the
delegate.

It is registered only when `WithContext` has been called. That is deliberate: evaluating with a
null context silently disables targeting, so a missing `WithContext` fails loudly instead.

## Health checks

```csharp
builder.Services.AddHealthChecks().AddConfigDirector();
```

Reports `Degraded` rather than `Unhealthy` while no config state has arrived -- the application
still answers every request, so taking an instance out of rotation is the wrong response to
ConfigDirector being unreachable. Pass a failure status to say otherwise. A client that has been
closed always reports unhealthy, since it cannot be reopened.

## Documentation

Refer to the [official documentation for the .NET SDK](https://docs.configdirector.com/sdks/server/dotnet).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.ServerSdk.AspNetCore/CHANGELOG.md).

## Getting Help

Reach out to us via https://www.configdirector.com/support
