# Changelog

All notable changes to `ConfigDirector.ServerSdk` are recorded here. Every other package in this
repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[semantic versioning](https://semver.org/spec/v2.0.0.html). Releases are tagged
`ConfigDirector.ServerSdk-v<version>`; 1.0.0 predates that scheme and is tagged `v1.0.0`.

## [Unreleased]

### Changed

- `ConnectionOptions.PollingInterval` now defaults to 5 minutes rather than 60 seconds, and
  rejects anything shorter than 1 minute. Both bounds are published as
  `ConnectionOptions.DefaultPollingInterval` and `ConnectionOptions.MinPollingInterval`. An
  application that set a shorter interval will now get an `ArgumentOutOfRangeException` where it
  is assigned, including when bound from configuration.

## [1.1.0] - 2026-08-28

### Added

- `ConfigDirectorClientOptions.Connection` and `Telemetry` can now be assigned as well as populated
  in place, so a caller holding a `ConnectionOptions` or `TelemetryOptions` of its own -- one bound
  from configuration, say -- can hand it over whole rather than copying it property by property.
  Assigning null throws, as it does for `LoggerFactory`, and populating them in place is unchanged.
- The package is trim and native AOT safe. `IsAotCompatible` is set for `net8.0`, telemetry
  serializes through a source-generated `System.Text.Json` context, and a native AOT sample is
  published and run by CI to keep it that way.

### Changed

- `GetJsonValue<T>` and `WatchJson<T>` are annotated `RequiresUnreferencedCode` and
  `RequiresDynamicCode`. They bind a config to a type of your own, which reads that type
  reflectively; a trimmed or AOT application now gets a warning at the call site instead of a
  failure at runtime. Read the config as `JsonElement` to stay fully AOT safe. Nothing else in the
  public API is affected, and no behaviour changes for an untrimmed application.
- Reading a JSON config as `JsonElement` parses with `JsonDocument` rather than the serializer.

## [1.0.0] - 2026-08-27

Initial release. Targets `net8.0` and `netstandard2.0`.

### Added

- `ConfigDirectorClient`, a thread-safe client meant to live for the lifetime of the process. It
  holds config state in memory and evaluates targeting rules locally, so reading a config is an
  in-process lookup rather than a network call.
- `InitializeAsync` connects and waits for the first config state. It does not throw when
  ConfigDirector cannot be reached: the application still starts, `IsReady` says whether config
  state arrived, and every config resolves to the default the calling code supplied.
- One `GetValue` overload per type the SDK can read — `bool`, `int`, `long`, `double`, `float`,
  `decimal`, `string` and `JsonElement`. The default you pass decides both the fallback and the
  type the value is parsed as; a value that will not read as that type gives you the default back.
- `GetJsonValue<T>` binds a JSON config to a type of your own.
- `GetAllConfigs` evaluates every config at once, unparsed — the shape a client SDK hydrates from.
- `Watch` and `WatchJson<T>` observe a config, with `Unwatch` and `UnwatchAll` to stop.
- `ClientReady`, `ConfigsUpdated` and `ConfigEvaluated` events. A handler that throws costs neither
  the caller nor the handlers after it.
- Three connection modes on `ConnectionOptions.Mode`: `Streaming` (the default, which holds a
  connection open for updates and reconnects on its own), `Polling`, and `OneTime`.
- `Context` carries `Id`, `Name`, `Anonymous` and arbitrary traits, including array traits, for
  targeting rules to match on.
- Telemetry reporting of which configs were evaluated, what they returned and the contexts they
  were evaluated against, batched on an interval and flushed on dispose. `TelemetryOptions` tunes
  the flush interval and queue size.
- `ConfigDirectorClientOptions` for `Metadata` (`AppName`, `AppVersion`), `LoggerFactory`,
  `Connection` (mode, polling interval, request timeout, base URL) and `Telemetry`.
- `IDisposable` and `IAsyncDisposable`. Disposing closes the connection and sends whatever
  telemetry is queued.
- Source Link, deterministic builds, and a symbol package.

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.1.0...HEAD
[1.1.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/v1.0.0...ConfigDirector.ServerSdk-v1.1.0
[1.0.0]: https://github.com/ConfigDirector/dotnet-sdks/releases/tag/v1.0.0
