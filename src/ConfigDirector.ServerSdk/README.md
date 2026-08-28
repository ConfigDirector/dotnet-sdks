# ConfigDirector .NET Server SDK

Remote configuration and feature flags for .NET server applications.

The client keeps config state in memory and evaluates targeting rules locally, so reading a config
is an in-process lookup rather than a network call.

## Install

```bash
dotnet add package ConfigDirector.ServerSdk
```

Targets `net8.0` and `netstandard2.0`.

## Use

Build one client for the lifetime of the application and share it. Initializing connects and waits
for the first config state.

```csharp
await using var client = new ConfigDirectorClient("your-server-sdk-key");
await client.InitializeAsync();

var context = new Context
{
    Id = "user-1",
    Traits = { ["plan"] = "pro" },
};

if (client.GetValue("temporary-feature-flag", false, context))
{
    // ...
}

var retries = client.GetValue("max-retries", 3);
var greeting = client.GetValue("welcome-message", "Hello");
```

The type you read is the overload you call, not how the config was declared in the dashboard. A
value that will not read as the type you asked for gives you your default back.

### Failing to connect is not an error

`InitializeAsync` does not throw when ConfigDirector cannot be reached. The application still
starts and every config returns its default; `IsReady` says whether config state arrived, and in
the default streaming mode the client keeps retrying in the background.

### Reacting to changes

```csharp
using var watch = client.Watch("welcome-message", "Hello", message =>
{
    Console.WriteLine($"now: {message}");
});
```

### JSON configs

```csharp
using System.Text.Json;

var raw = client.GetValue("json-value-config", default(JsonElement));
var bound = client.GetJsonValue("json-value-config", new WeatherOptions());
```

### Options

```csharp
var client = new ConfigDirectorClient(key, new ConfigDirectorClientOptions
{
    Metadata = new Metadata { AppName = "checkout", AppVersion = "1.2.3" },
    LoggerFactory = loggerFactory,
    Connection =
    {
        Mode = ConnectionMode.Polling,
        PollingInterval = TimeSpan.FromSeconds(30),
    },
});
```

`ConnectionMode.Streaming` is the default and keeps a connection open for updates.
`ConnectionMode.Polling` fetches on an interval, and `ConnectionMode.OneTime` fetches once.

## Telemetry

The SDK reports which configs were evaluated, what they returned and the contexts they were
evaluated against. ConfigDirector uses this to power config usage and insights in the dashboard.
Reports are batched and sent on an interval; `TelemetryOptions` tunes the interval and the queue
size. Disposing the client sends whatever is left.

## License

MIT.
