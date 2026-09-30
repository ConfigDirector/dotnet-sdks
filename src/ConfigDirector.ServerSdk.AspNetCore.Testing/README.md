# ConfigDirector ASP.NET Core Testing

Registers a [ConfigDirector test client](../ConfigDirector.ServerSdk.Testing/) with an ASP.NET
Core application under test, published to NuGet as `ConfigDirector.ServerSdk.AspNetCore.Testing`.
It targets `net8.0`.

An application wired up with
[`ConfigDirector.ServerSdk.AspNetCore`](../ConfigDirector.ServerSdk.AspNetCore/) builds its client
from configuration and connects during startup. This package replaces that client with the test
client's, so the application starts without an SDK key, opens no connection, and reads the values
the test seeds.

## Installation

The package is released together with `ConfigDirector.ServerSdk.Testing` and the SDK, at the same
version, and brings the testing package along.

```bash
dotnet add package ConfigDirector.ServerSdk.AspNetCore.Testing
```

## Test your application

```csharp
using ConfigDirector.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

await using var testClient = ConfigDirectorTesting.CreateTestClient(
    new Dictionary<string, object> { ["new-checkout"] = true });
using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
    host.ConfigureTestServices(services => services.AddConfigDirectorTestClient(testClient)));
using var http = factory.CreateClient();

(await http.GetStringAsync("/checkout")).ShouldBe("new");

testClient.SetValue("new-checkout", false);
(await http.GetStringAsync("/checkout")).ShouldBe("classic");
```

`AddConfigDirectorTestClient(testClient)`:

- Removes every `IConfigDirectorClient` registration already in the collection and registers
  `testClient.Client` as an instance. It therefore works from `ConfigureTestServices`, which runs
  after the application's own `AddConfigDirector`. The container never disposes an instance
  registration; the test owns the test client and disposes it.
- Supplies a placeholder SDK key when none is configured, so the startup validation of the key
  passes whatever the order of registrations. A configured key is left alone.
- Leaves the application's startup initialization in place, so the host calls `InitializeAsync` as
  in production. With no hold or failure armed, it completes at once with the stored values. With
  `FailInitialization` and `RequireReadyOnStartup`, host startup throws
  `ConfigDirectorConnectionException`, as it would in production.
- With `HoldInitialization`, host startup waits until `CompleteInitialization` or the client's
  timeout. `WebApplicationFactory.CreateClient()` blocks on host startup, so call
  `CompleteInitialization` from another task.

Because the test client is built outside the container, it does not see the application's
configuration: `ConfigDirector:Connection:Timeout`, the host's `ILoggerFactory`, and the `AppName`
and `AppVersion` metadata. Pass the timeout and logger factory to `CreateTestClient` through
`TestClientOptions`.

### A factory shared across tests

A `WebApplicationFactory` shared by a test class through `IClassFixture` shares one test client
across its tests. Keep the test client with the factory, register it once, and call
`ReplaceValues` in each test's setup: it resets the values and disarms any armed hold or failure.

```csharp
public sealed class CheckoutTests : IClassFixture<CheckoutTests.Factory>
{
    private readonly Factory _factory;

    public CheckoutTests(Factory factory)
    {
        _factory = factory;
        _factory.TestClient.ReplaceValues(new Dictionary<string, object> { ["new-checkout"] = true });
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public TestClient TestClient { get; } = ConfigDirectorTesting.CreateTestClient();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.AddConfigDirectorTestClient(TestClient));

        protected override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await TestClient.DisposeAsync();
        }
    }
}
```

## Documentation

Full details are in the [official documentation](https://docs.configdirector.com/sdks/server/dotnet).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.ServerSdk.AspNetCore.Testing/CHANGELOG.md).

## Getting Help

Reach out to us via https://www.configdirector.com/support
