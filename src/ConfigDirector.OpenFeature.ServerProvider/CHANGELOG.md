# Changelog

All notable changes to `ConfigDirector.OpenFeature.ServerProvider` are recorded here. Every other
package in this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[semantic versioning](https://semver.org/spec/v2.0.0.html). Releases are tagged
`ConfigDirector.OpenFeature.ServerProvider-v<version>`.

## [Unreleased]

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

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/commits/main/src/ConfigDirector.OpenFeature.ServerProvider
