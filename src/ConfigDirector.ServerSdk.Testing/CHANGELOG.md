# Changelog

All notable changes to `ConfigDirector.ServerSdk.Testing` are recorded here. Every other package in
this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This package is
released together with `ConfigDirector.ServerSdk` and always carries the SDK's version, because it
relies on the SDK's internals: it declares an exact dependency on that SDK version, and checks it
again when a test client is created. Both are published from the `ConfigDirector.ServerSdk-v<version>`
tag.

## [Unreleased]

## [1.11.0] - 2026-11-07

### Changed

- Depends on [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.11.0, which accepts a date inside tracked event details and serves a context without an `Id`
  by the targeting rules after a skipped percentage rollout. `TestClient.TrackedEvents` holds a
  date as the ISO 8601 text the SDK sends.

## [1.10.0] - 2026-11-06

### Added

- `TestClient.TrackedEvents` holds the events the code under test tracked through
  `IConfigDirectorClient.Track`, new in `ConfigDirector.ServerSdk`, in call order, as the new
  `TrackedEvent(Name, Context, Value, Details)` record. An event whose name the SDK would not report
  is not recorded, and a value or details the SDK would leave out are null in the record, with a
  warning logged each time. The limit of 1,000 events per telemetry batch does not apply to the
  record. It is the same list every time, and safe to read while other threads track: later events
  are added to it, and a test client shared across tests can `Clear()` it.

### Changed

- Depends on [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.10.0, which adds `Track` and `Evaluate`.
- A test client serves plain values without variations, so `ConfigEvaluation.Variation` is null on
  every evaluation. `Evaluate` and `EvaluateJson<T>`, new in `ConfigDirector.ServerSdk`, work on
  `Client` like every other read. The testing API is unchanged.

## [1.9.0] - 2026-10-22

### Changed

- Depends on [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.9.0, which adds offline mode, serving values typed by the same rule as a test client. A value a
  test client cannot take is now reported as "The value for '<key>' ..." or "Invalid value for
  '<key>'" rather than "The test value ..." or "Invalid test value ...", a null value no longer
  suggests `RemoveValue`, and these exceptions no longer name a parameter in `ParamName`. The
  testing API is unchanged.

## [1.8.0] - 2026-10-14

### Changed

- Depends on [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.8.0, which adds `Context.Kind`. A test client evaluates kinds as the SDK does. It sends no
  telemetry, so every evaluation supplied a context with an invalid kind logs the
  `InvalidContextKind` error (EventId 9). The testing API is unchanged.

## [1.7.1] - 2026-10-05

### Changed

- Depends on [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.7.1, in which a segment change reaches watches. The testing API is unchanged.

## [1.7.0] - 2026-10-05

### Changed

- Depends on [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.7.0, which evaluates segment conditions in targeting rules. The testing API is unchanged.

## [1.6.0] - 2026-10-01

### Added

- `ConfigDirectorTesting.CreateTestClient`, returning a `TestClient` whose `Client` is a real
  `IConfigDirectorClient` connected to an in-memory server that the test controls: `SetValue`,
  `RemoveValue`, `ReplaceValues`, `HoldInitialization`, `CompleteInitialization`, and
  `FailInitialization`. `TestClientOptions` carries the client's `Timeout` and `LoggerFactory`. No
  network connection is opened and no telemetry is sent.

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.11.0...HEAD
[1.11.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.10.0...ConfigDirector.ServerSdk-v1.11.0
[1.10.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.9.0...ConfigDirector.ServerSdk-v1.10.0
[1.9.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.8.0...ConfigDirector.ServerSdk-v1.9.0
[1.8.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.7.1...ConfigDirector.ServerSdk-v1.8.0
[1.7.1]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.7.0...ConfigDirector.ServerSdk-v1.7.1
[1.7.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.6.0...ConfigDirector.ServerSdk-v1.7.0
[1.6.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.5.1...ConfigDirector.ServerSdk-v1.6.0
