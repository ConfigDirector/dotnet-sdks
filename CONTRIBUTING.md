# Contributing

## Development

Requires the .NET SDK pinned in [global.json](global.json), plus the .NET 8 and ASP.NET Core 8
runtimes, which the test suite runs against alongside the current ones.

On a machine whose SDK came from a package manager, install those two by unpacking the official
runtime archives and copying only their `shared/Microsoft.NETCore.App/<version>` and
`shared/Microsoft.AspNetCore.App/<version>` directories into the SDK's `shared` directory. Do not
point `dotnet-install.sh --install-dir` at it: the archives carry their own `dotnet` host binary,
which overwrites the one already there and leaves a `dotnet` that macOS refuses to run.

```bash
dotnet build -c Release
dotnet test -c Release --no-build
dotnet format --verify-no-changes
```

Those three are what CI runs, and what the shared `pre-push` hook runs. Wire the hook up once
per clone:

```bash
git config core.hooksPath .githooks
```

It checks the working tree rather than the commits being pushed, so uncommitted changes are
included. Bypass it for a single push with `git push --no-verify`.

### Samples

Samples reference the published package by default, so they read the way a consuming application
does. CI and the pre-push hook set `UseLocalSdk`, which swaps in the SDK from this checkout so a
breaking API change fails the build before it ships. Set it by hand to reproduce what they check:

```bash
UseLocalSdk=true dotnet build -c Release
```

### Slow tests

[tests/ConfigDirector.ServerSdk.SlowTests](tests/ConfigDirector.ServerSdk.SlowTests) covers
behaviors whose only symptom is elapsed time. It is deliberately outside the solution so the
commands above stay quick, and runs nightly in CI.

```bash
dotnet test tests/ConfigDirector.ServerSdk.SlowTests/ConfigDirector.ServerSdk.SlowTests.csproj
```

## Releasing

Tagging publishes. The tag names the package and the version it releases:

```bash
git tag ConfigDirector.ServerSdk.AspNetCore-v1.0.0
git push origin ConfigDirector.ServerSdk.AspNetCore-v1.0.0
```

Every package is tagged and versioned on its own, as `<PackageId>-v<version>`.
[release.yml](.github/workflows/release.yml) derives the project from the id, so a package living
in `src/<PackageId>/<PackageId>.csproj` needs no workflow of its own. Releases cut before this
scheme were tagged `v1.2.3`, which the workflow no longer matches.

That builds and tests the whole solution, packs the one package, and publishes to nuget.org if all
of it passes. A published version is permanent and can only be delisted, so rehearse with
`workflow_dispatch` on the same workflow first: it packs a given package and version and uploads
the artifact without publishing anything.

The version lives in the package's project file, as `VersionPrefix` plus `VersionSuffix` for a
prerelease. Bump it in a commit, then tag that commit: the workflow checks the tag against the
project and refuses a release where the two disagree. It is deliberately not passed in on the
command line -- `-p:Version` is a global MSBuild property, so it flows across a `ProjectReference`
and would stamp the wrong dependency version on a package that depends on another one here.

That also fixes the order when several packages move together: a package is published after
everything it depends on, since it declares a dependency on the version it was built against. Two
tags pushed at once run in parallel and in no particular order, so the workflow checks before
publishing that every dependency on another package here is already on nuget.org, and fails rather
than publishing a package nobody could restore. Re-run the job once the dependency has indexed.

A package published for the first time needs a trusted publishing policy for its id on nuget.org,
matching this repository and `release.yml`.
