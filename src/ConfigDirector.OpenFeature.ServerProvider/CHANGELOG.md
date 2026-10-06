# Changelog

All notable changes to `ConfigDirector.OpenFeature.ServerProvider` are recorded here. Every other
package in this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[semantic versioning](https://semver.org/spec/v2.0.0.html). Releases are tagged
`ConfigDirector.OpenFeature.ServerProvider-v<version>`.

## [Unreleased]

## [1.4.1] - 2026-10-05

### Fixed

- A segment change now emits `PROVIDER_CONFIGURATION_CHANGED` with the affected flags in
  `FlagsChanged`: every flag whose targeting rules use the changed segment. Before, editing a
  segment or one of its environment overrides emitted the event with an empty `FlagsChanged`.
  Requires [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.7.1 or later, which reports those flags in `ConfigsUpdatedEventArgs.Keys`.

## [1.4.0] - 2026-10-05

### Added

- Targeting rules can now use segments: the provider evaluates segment conditions through the
  server SDK it depends on, which reads the payload's segments. Requires
  [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk) 1.7.0 or
  later. ConfigDirector only sends rules with segment conditions to provider versions that evaluate
  them, so upgrading is what makes rules that use segments apply to this application.

## [1.3.0] - 2026-10-01

### Changed

- `PROVIDER_CONFIGURATION_CHANGED` now lists the flags a full update removed in `FlagsChanged`,
  after the flags the update carried. Before, a removed flag was not reported as changed. Requires
  [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk) 1.6.0 or later.

- Default a percentage rollout to bucket 0 when there is no context identifier provided, rather than randomly assign on each evaluation.

## [1.2.1] - 2026-09-26

### Changed

- Requires [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.5.1 or later. Telemetry reports now include the app name and version.

## [1.2.0] - 2026-09-25

### Changed

- Requires [`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk)
  1.5.0 or later. A boolean, integer, or float flag resolved as a string now returns the default
  value with the `TypeMismatch` error, as resolving a string flag as a number already did, instead
  of the value's text as a targeting match. Resolving a JSON flag as a string still returns its raw
  document.

## [1.1.0] - 2026-09-23

### Fixed

- Depends on ConfigDirector.ServerSdk 1.4.0 to pick up the conditional rule evaluation fix: multiple conditions in a rule are now ANDed rather than ORed.

## [1.0.1] - 2026-09-22

### Fixed

- The package no longer declares the server SDK's own dependencies as its own. Version 1.0.0
  listed `Microsoft.Extensions.Logging.Abstractions` 8.0.3 directly, which made restoring it into
  a .NET 9 or .NET 10 application fail with a package downgrade error (`NU1605`), because the
  OpenFeature SDK's asset for those runtimes needs a newer version of that assembly. The package
  now depends only on `ConfigDirector.ServerSdk` and `OpenFeature`, and restores on every runtime
  they support.

## [1.0.0] - 2026-09-22

Initial release. Targets `netstandard2.0` and `net8.0`, and requires
[`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk) 1.3.0 or
later and [`OpenFeature`](https://www.nuget.org/packages/OpenFeature) 2.14.1 or later.

### Added

- `ConfigDirectorProvider`, an OpenFeature `FeatureProvider` that owns a `ConfigDirectorClient`.
  It connects when OpenFeature initializes it, closes when OpenFeature shuts it down, and accepts
  the same `ConfigDirectorClientOptions` the client does.
- Boolean, string, integer, double and structure resolution. A match reports
  `TARGETING_MATCH` with the value id as the variant; a config the server does not know reports
  `FLAG_NOT_FOUND`; a value of another type reports `TYPE_MISMATCH`; and an evaluation before the
  first config state arrives reports `PROVIDER_NOT_READY`.
- The evaluation context maps onto the ConfigDirector context: the targeting key or an `id`
  attribute, `name`, a `traits` structure and a boolean `anonymous`.
- `PROVIDER_CONFIGURATION_CHANGED` with the keys each update carried, and `PROVIDER_READY` when
  config state arrives after initialization timed out.
- The provider identifies itself to ConfigDirector as `dotnet-openfeature-server-provider` with its
  own version.

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.4.1...HEAD
[1.4.1]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.4.0...ConfigDirector.OpenFeature.ServerProvider-v1.4.1
[1.4.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.3.0...ConfigDirector.OpenFeature.ServerProvider-v1.4.0
[1.3.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.2.1...ConfigDirector.OpenFeature.ServerProvider-v1.3.0
[1.2.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.1.0...ConfigDirector.OpenFeature.ServerProvider-v1.2.0
[1.1.0]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.0.1...ConfigDirector.OpenFeature.ServerProvider-v1.1.0
[1.0.1]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.0.0...ConfigDirector.OpenFeature.ServerProvider-v1.0.1
[1.0.0]: https://github.com/ConfigDirector/dotnet-sdks/releases/tag/ConfigDirector.OpenFeature.ServerProvider-v1.0.0
