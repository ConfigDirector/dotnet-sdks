# Changelog

All notable changes to `ConfigDirector.ServerSdk.AspNetCore` are recorded here. Every other package
in this repository keeps its own changelog beside it, covering that package alone.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the package uses
[semantic versioning](https://semver.org/spec/v2.0.0.html). Releases are tagged
`ConfigDirector.ServerSdk.AspNetCore-v<version>`.

## [Unreleased]

## [1.0.0] - 2026-08-28

Initial release. Targets `net8.0`, and requires
[`ConfigDirector.ServerSdk`](https://www.nuget.org/packages/ConfigDirector.ServerSdk) 1.1.0 or
later.

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
- Connecting to ConfigDirector during startup, as an `IHostedLifecycleService`. Every
  `StartingAsync` completes before any hosted service is started, and the web host is itself a
  hosted service registered while the builder is constructed -- so initialization finishes before
  Kestrel accepts a request, and no request is served config defaults while the first config state
  is still in flight.
- `RequireReadyOnStartup`, off by default. Left off, a host that cannot reach ConfigDirector logs a
  warning and starts anyway, every config resolving to the default its caller supplied, which is
  what the SDK does on its own. Turned on, startup fails with a `ConfigDirectorConnectionException`.
- `WithContext`, which declares once how a request becomes an evaluation `Context`. Actions inject
  an `IConfigDirectorContextAccessor` instead of each rebuilding one. The delegate runs at most
  once per request, so several evaluations in one action cost one call. It is registered only when
  `WithContext` has been called: a silently null context would disable targeting without saying so.
- `AddHealthChecks().AddConfigDirector()`, reporting whether config state has arrived. `Degraded`
  rather than `Unhealthy` by default, since the application still answers every request with each
  config resolving to its caller's default; pass a failure status to say otherwise. A closed client
  always reports unhealthy, since it cannot be reopened.
- Configuration binding runs through the source-generated binder, so the package adds no trimming
  or AOT warnings to a consuming application.

[Unreleased]: https://github.com/ConfigDirector/dotnet-sdks/compare/ConfigDirector.ServerSdk.AspNetCore-v1.0.0...HEAD
[1.0.0]: https://github.com/ConfigDirector/dotnet-sdks/releases/tag/ConfigDirector.ServerSdk.AspNetCore-v1.0.0
