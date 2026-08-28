# ConfigDirector .NET Server SDK Monorepo

## Development

Requires the .NET SDK pinned in [global.json](global.json), plus the .NET 8 runtime, which the
test suite runs against alongside the current one.

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

### Slow tests

[tests/ConfigDirector.ServerSdk.SlowTests](tests/ConfigDirector.ServerSdk.SlowTests) covers
behaviours whose only symptom is elapsed time. It is deliberately outside the solution so the
commands above stay quick, and runs nightly in CI.

```bash
dotnet test tests/ConfigDirector.ServerSdk.SlowTests/ConfigDirector.ServerSdk.SlowTests.csproj
```

## Releasing

Tagging publishes. The tag carries the version, so nothing in the repository is edited to cut a
release:

```bash
git tag v1.2.3
git push origin v1.2.3
```

That builds, tests and packs, then waits for approval on the `nuget` environment before anything
reaches nuget.org. `workflow_dispatch` on the same workflow packs a given version and uploads the
artifact without publishing, which is the way to rehearse one.

It checks the working tree rather than the commits being pushed, so uncommitted changes are
included. Bypass it for a single push with `git push --no-verify`.

## Getting Help

Reach out to us via https://www.configdirector.com/support
