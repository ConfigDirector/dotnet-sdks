# Changelog

All notable changes to `ConfigDirector.ServerSdk.Testing` are recorded here. Every other package in
this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This package is
released together with `ConfigDirector.ServerSdk` and always carries the SDK's version, because it
relies on the SDK's internals: it declares an exact dependency on that SDK version, and checks it
again when a test client is created. Both are published from the `ConfigDirector.ServerSdk-v<version>`
tag.

## [Unreleased]

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

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.7.0...HEAD
[1.7.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.6.0...ConfigDirector.ServerSdk-v1.7.0
[1.6.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.5.1...ConfigDirector.ServerSdk-v1.6.0
