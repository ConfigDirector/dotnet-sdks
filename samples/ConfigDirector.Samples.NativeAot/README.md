# ConfigDirector server SDK — native AOT sample

A console application published to native code, showing that the SDK works with no JIT and no
reflection. It reads the same config keys as the [Minimal API](../ConfigDirector.Samples.MinimalApi/)
and [MVC](../ConfigDirector.Samples.Mvc/) samples, without a web framework in the way.

```bash
ConfigDirector__ServerSdkKey=your-key dotnet run --project samples/ConfigDirector.Samples.NativeAot
```

Settings come straight from the environment rather than through the configuration system, so
nothing sits between the sample and the SDK:

| Variable                       | Default           | Meaning                            |
| ------------------------------ | ----------------- | ---------------------------------- |
| `ConfigDirector__ServerSdkKey` | `fake-sample-key` | Your server SDK key. A secret.     |
| `ConfigDirector__Url`          | _(none)_          | Only when routing through a proxy. |

## Publishing to native code

```bash
dotnet publish samples/ConfigDirector.Samples.NativeAot -c Release -r linux-x64 -o aot
./aot/ConfigDirector.Samples.NativeAot
```

The result is a single executable with no .NET runtime to install. Publishing runs ILC over the
whole program, so anything in the SDK that trimming or AOT cannot support fails the publish rather
than the process.

On macOS the linker needs OpenSSL and Brotli on its search path, which Homebrew installs outside
the default one:

```bash
dotnet publish samples/ConfigDirector.Samples.NativeAot -c Release -r osx-arm64 -o aot \
  -p:CustomAfterMicrosoftCommonTargets=$PWD/scripts/macos-aot-linker.props
```

## Reading JSON without reflection

`GetValue` with a `JsonElement` default is the AOT-safe way to read a JSON config: the SDK parses
it with `JsonDocument` and never looks at a type of yours.

`GetJsonValue<T>` and `WatchJson<T>` bind to a type you declare, which means reading that type
reflectively. Both carry `RequiresUnreferencedCode` and `RequiresDynamicCode`, so calling either
from here would produce a publish warning naming the call site rather than failing silently at
runtime. That is the whole reason this sample exists.

## What CI does with it

[`scripts/verify-native-aot.sh`](../../scripts/verify-native-aot.sh) runs the published binary
against a stubbed SDK server and checks it evaluated real config state and reported telemetry.
Publishing without warnings only proves the SDK compiles; metadata the trimmer drops fails at
runtime, and this is what would catch that.
