# Changelog

All notable changes to `ConfigDirector.ServerSdk.AspNetCore` are recorded here. Every other package
in this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `AddConfigDirector`, which registers one `IConfigDirectorClient` for the application and binds its
  settings from configuration. Four overloads: bind the `ConfigDirector` section by convention, bind
  a section you name, and either of those followed by a delegate that adjusts the result, which is
  where a key held in a secret store belongs.
- `ConfigDirectorOptions`, the configuration-facing settings. It reuses the SDK's own
  `ConnectionOptions` and `TelemetryOptions` rather than restating them, so a setting added to the
  SDK binds here without a change to this package.
- `AppName` and `AppVersion` default to the host's `ApplicationName` and the entry assembly's
  informational version, so targeting rules can match on the application without either being
  configured. Any build metadata suffix is removed, since `1.2.3+9f4c1a` matches no semver rule.
- Configuration binding runs through the source-generated binder, so the package adds no trimming
  or AOT warnings to a consuming application.
