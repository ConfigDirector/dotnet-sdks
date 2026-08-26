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

It checks the working tree rather than the commits being pushed, so uncommitted changes are
included. Bypass it for a single push with `git push --no-verify`.

## Getting Help

Reach out to us via https://www.configdirector.com/support
