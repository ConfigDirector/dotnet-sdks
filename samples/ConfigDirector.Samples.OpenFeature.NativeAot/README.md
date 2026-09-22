# ConfigDirector OpenFeature provider — native AOT sample

A console application published to native code, showing that the OpenFeature provider works with no
JIT and no reflection. It reads the same config keys as the
[`ConfigDirector.Samples.OpenFeature`](../ConfigDirector.Samples.OpenFeature/) sample, through an
OpenFeature client, without a web framework in the way. It is the provider's counterpart to the
server SDK's [native AOT sample](../ConfigDirector.Samples.NativeAot/).

```bash
ConfigDirector__ServerSdkKey=your-key dotnet run --project samples/ConfigDirector.Samples.OpenFeature.NativeAot
```

Settings come straight from the environment rather than through the configuration system, so
nothing sits between the sample and the provider:

| Variable                       | Default           | Meaning                            |
| ------------------------------ | ----------------- | ---------------------------------- |
| `ConfigDirector__ServerSdkKey` | `fake-sample-key` | Your server SDK key. A secret.     |
| `ConfigDirector__Url`          | _(none)_          | Only when routing through a proxy. |

## Publishing to native code

```bash
dotnet publish samples/ConfigDirector.Samples.OpenFeature.NativeAot -c Release -o aot-openfeature
./aot-openfeature/ConfigDirector.Samples.OpenFeature.NativeAot
```

The result is a single executable with no .NET runtime to install. Publishing runs ILC over the
whole program, so anything in the provider, the server SDK or the OpenFeature SDK that trimming or
AOT cannot support fails the publish rather than the process. All three are annotated
`IsAotCompatible`.

On macOS this needs OpenSSL and Brotli, which native AOT links against and which do not ship with
the system:

```bash
brew install openssl@3 brotli
```

Homebrew puts them outside the linker's default search path, so the project adds that path itself.
Linux and Windows need nothing extra.

## Why this sample pins the provider in this checkout

The other samples reference the published package, so they read the way your own application
would. This one always builds
[`src/ConfigDirector.OpenFeature.ServerProvider`](../../src/ConfigDirector.OpenFeature.ServerProvider/)
instead, because its job is to prove that what is about to ship compiles and runs as native code.

## Reading JSON without reflection

`GetObjectValueAsync` returns an OpenFeature `Value`: a `Structure` for an object, a list for an
array. The provider builds it from the `JsonElement` the server SDK parses, so no type of your own
is ever reflected over. Read the members you need from the structure rather than binding it to a
class.

## What CI does with it

[`scripts/verify-native-aot.sh`](../../scripts/verify-native-aot.sh) runs the published binary
against a stubbed SDK server and checks that the provider became ready, that a targeting rule
matched and reported its variant, and that telemetry was reported. Publishing without warnings only
proves the code compiles; metadata the trimmer drops fails at runtime, and this is what would catch
that.
