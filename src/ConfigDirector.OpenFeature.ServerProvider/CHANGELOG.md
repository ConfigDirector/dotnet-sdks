# Changelog

All notable changes to `ConfigDirector.OpenFeature.ServerProvider` are recorded here. Every other
package in this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[semantic versioning](https://semver.org/spec/v2.0.0.html). Releases are tagged
`ConfigDirector.OpenFeature.ServerProvider-v<version>`.

## [Unreleased]

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

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.OpenFeature.ServerProvider-v1.0.0...HEAD
[1.0.0]: https://github.com/ConfigDirector/dotnet-sdks/releases/tag/ConfigDirector.OpenFeature.ServerProvider-v1.0.0
