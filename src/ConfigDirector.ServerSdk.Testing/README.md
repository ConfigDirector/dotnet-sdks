# ConfigDirector .NET Server SDK Testing

Testing tools for the [ConfigDirector .NET server SDK](../ConfigDirector.ServerSdk/), published
to NuGet as `ConfigDirector.ServerSdk.Testing`. They let you test the code that reads your configs
and flags without a network connection and without changing production code.

A **test client** is the SDK's real client connected to an in-memory server that your test
controls. Only the connection to ConfigDirector is replaced. Everything else is the same code that
runs in production, so the code under test behaves exactly as it does against ConfigDirector, and
no network connection is opened and no telemetry is sent.

In an ASP.NET Core application, the companion `ConfigDirector.ServerSdk.AspNetCore.Testing`
package registers the test client with the application under test; see
[its README](../ConfigDirector.ServerSdk.AspNetCore.Testing/).

## Installation

The package is released together with the SDK and must be the same version as the
`ConfigDirector.ServerSdk` your test project restores; creating a test client fails with a message
naming both versions otherwise. It declares that exact dependency, so restoring it alongside a
different SDK version is reported by NuGet as well.

```bash
dotnet add package ConfigDirector.ServerSdk.Testing
```

It targets `net8.0` and `netstandard2.0`, like the SDK.

## Test your code

```csharp
using ConfigDirector.Testing;

await using var testClient = ConfigDirectorTesting.CreateTestClient(
    new Dictionary<string, object> { ["new-checkout"] = true });
var service = new CheckoutService(testClient.Client);
await testClient.Client.InitializeAsync();

service.IsNewCheckoutEnabled("user-123").ShouldBeTrue();

testClient.SetValue("new-checkout", false);
service.IsNewCheckoutEnabled("user-123").ShouldBeFalse();
```

`testClient.Client` is an `IConfigDirectorClient`, so it goes anywhere your code accepts one. It
starts uninitialized, like a production client, because the code under test usually owns the call
to `InitializeAsync`; with no hold or failure armed, `InitializeAsync` completes at once with the
stored values. `TestClient` is `IAsyncDisposable` and disposes its client.

### Values

`CreateTestClient`, `SetValue`, and `ReplaceValues` take native values, and the config type follows
from each value's runtime type: a `bool`; an integral type from `sbyte` to `long` (an integer
config); a `float`, `double`, or `decimal` (a float config); a `string`; or a dictionary with
string keys, a list, or a `JsonElement` or `JsonNode` holding an object or an array (a JSON
config). A `string` is always a string config, so JSON text meant as a JSON config is given as
`JsonNode.Parse(text)`. Every value is served as an unconditional config, so every context receives
the same value; a test that needs different values per context is written as one test per value.
A `null` value throws `ArgumentNullException`; an empty key, a non-finite number, an unsupported
type such as `BigInteger` or `DateTime`, a dictionary with non-string keys, or JSON contents that
cannot be encoded throw `ArgumentException`; in every case nothing changes.

Reads behave as they do in production: `SetValue("k", true)` read as a string returns the in-code
default value with the `TypeMismatch` reason, and `SetValue("k", "")` serves the in-code default
value with the `ValueMissing` reason. JSON objects are encoded with their keys sorted.

### Controls

| Control | Effect |
| --- | --- |
| `SetValue(key, value)` | Stores the value and, once the client is connected, delivers an update carrying only `key`. Watches of `key`, `ConfigsUpdated` handlers, and reads see it before the call returns. |
| `RemoveValue(key)` | Removes the value and delivers a full update without it. Reads return the in-code default value with the `ConfigStateMissing` reason, watches of `key` receive the default, and `ConfigsUpdated` lists `key` in `RemovedKeys`. |
| `ReplaceValues(values)` | Replaces every stored value, disarms any armed hold or failure, and delivers a full update. Use it to reset a test client shared across tests. |
| `HoldInitialization()` | The next `InitializeAsync` waits until `CompleteInitialization()` or `FailInitialization()`, or until the client's timeout elapses. |
| `CompleteInitialization()` | Delivers the stored values to the held `InitializeAsync`, on the calling thread, so the client is ready when the call returns. Called while a hold is armed but not picked up, it disarms the hold. |
| `FailInitialization()` | Fails the held `InitializeAsync` the way an invalid SDK key does: it completes promptly, the client is not ready, and the error is logged. Called with no `InitializeAsync` held, it arms the next one to fail. |

After a failed attempt, the next `InitializeAsync` succeeds and delivers the values stored in the
meantime.

### Options

`CreateTestClient(values, new TestClientOptions { ... })` takes `Timeout` (the client's connection
timeout, which bounds how long a held `InitializeAsync` waits; defaults to the SDK's production
timeout) and `LoggerFactory` (defaults to the SDK's default, `NullLoggerFactory`, which discards
everything; pass a factory to see the client's logs).

### What to expect

- The first update is delivered while `InitializeAsync` connects, so watches registered before it
  run before it completes, and `ConfigsUpdated` fires before `ClientReady`.
- Watches and handlers run on the thread that calls `SetValue`, `RemoveValue`, `ReplaceValues`,
  `CompleteInitialization`, or `InitializeAsync`, not on a transport thread.
- A held `InitializeAsync` that times out, or that is interrupted by disposing the client, leaves
  the client not ready until the next `InitializeAsync`, and logs the SDK's timeout warning;
  production streaming would keep retrying in the background.
- Watches fire on every update carrying their key, whether or not the value changed, as in
  production. `RemoveValue` and `ReplaceValues` deliver a full update, so they also fire the
  watches of every remaining key.
- An exception thrown by a watch or handler is caught and logged by the client, as in production,
  so a failed assertion inside a watch does not fail the test by itself.
- After the client is disposed, `IsReady` is false, reads return the in-code default value,
  `GetAllConfigs` returns an empty dictionary, `InitializeAsync` throws
  `ObjectDisposedException`, every watch and event handler has been removed, and the test client's
  controls are silent no-ops.

## Documentation

Full details are in the [official documentation](https://docs.configdirector.com/sdks/server/dotnet).

What changed in each release is in
[the changelog](https://github.com/ConfigDirector/dotnet-sdks/blob/main/src/ConfigDirector.ServerSdk.Testing/CHANGELOG.md).

## Getting Help

Reach out to us via https://www.configdirector.com/support
