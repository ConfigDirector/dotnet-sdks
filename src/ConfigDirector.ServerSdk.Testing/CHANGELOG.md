# Changelog

All notable changes to `ConfigDirector.ServerSdk.Testing` are recorded here. Every other package in
this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This package is
released together with `ConfigDirector.ServerSdk` and always carries the SDK's version, because it
relies on the SDK's internals: it declares an exact dependency on that SDK version, and checks it
again when a test client is created. Both are published from the `ConfigDirector.ServerSdk-v<version>`
tag.

## [Unreleased]

### Added

- `ConfigDirectorTesting.CreateTestClient`, returning a `TestClient` whose `Client` is a real
  `IConfigDirectorClient` connected to an in-memory server that the test controls: `SetValue`,
  `RemoveValue`, `ReplaceValues`, `HoldInitialization`, `CompleteInitialization`, and
  `FailInitialization`. `TestClientOptions` carries the client's `Timeout` and `LoggerFactory`. No
  network connection is opened and no telemetry is sent.
