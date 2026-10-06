# Changelog

All notable changes to `ConfigDirector.ServerSdk.AspNetCore.Testing` are recorded here. Every
other package in this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This package is
released together with `ConfigDirector.ServerSdk.Testing` and `ConfigDirector.ServerSdk` and always
carries the SDK's version: it declares an exact dependency on that version of
`ConfigDirector.ServerSdk.Testing`. All three are published from the
`ConfigDirector.ServerSdk-v<version>` tag.

## [Unreleased]

## [1.7.1] - 2026-10-05

### Changed

- Depends on `ConfigDirector.ServerSdk.Testing` 1.7.1, and through it on
  [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk) 1.7.1, in which
  a segment change reaches watches. The testing API is unchanged.

## [1.7.0] - 2026-10-05

### Changed

- Depends on `ConfigDirector.ServerSdk.Testing` 1.7.0, and through it on
  [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk) 1.7.0, which
  evaluates segment conditions in targeting rules. The testing API is unchanged.

## [1.6.0] - 2026-10-01

### Added

- `AddConfigDirectorTestClient(testClient)`, which replaces the application's
  `IConfigDirectorClient` registration with the test client's client, supplies a placeholder SDK
  key when none is configured, and leaves startup initialization in place. It works from
  `WebApplicationFactory.ConfigureTestServices`.

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.7.1...HEAD
[1.7.1]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.7.0...ConfigDirector.ServerSdk-v1.7.1
[1.7.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.6.0...ConfigDirector.ServerSdk-v1.7.0
[1.6.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk-v1.5.1...ConfigDirector.ServerSdk-v1.6.0
