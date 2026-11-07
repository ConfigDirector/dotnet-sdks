# Contributing

## Development

Requires the .NET SDK pinned in [global.json](global.json), plus the .NET 8 and ASP.NET Core 8
runtimes, which the test suite runs against alongside the current ones.

On a machine whose SDK came from a package manager, install those two with
[scripts/setup-dotnet8-runtimes.sh](scripts/setup-dotnet8-runtimes.sh). Upgrading the SDK with
Homebrew removes them, so run it again after each upgrade: until then the build passes and
`dotnet test` fails with "You must install or update .NET". Do not point
`dotnet-install.sh --install-dir` at the SDK: the archives carry their own `dotnet` host binary,
which overwrites the one already there and leaves a `dotnet` that macOS refuses to run.

```bash
dotnet build -c Release
dotnet test -c Release --no-build --timeout 5m
dotnet format --verify-no-changes
scripts/release-plan-test.sh
scripts/wait-for-package-indexed-test.sh
```

The timeout bounds each test project's run, so a hung test fails the run within minutes rather than
holding the job until its own limit. `dotnet test` does not show which test hung; to find it, run
the test assembly directly, where xunit names any test still running after 30 seconds:

```bash
dotnet tests/ConfigDirector.ServerSdk.Tests/bin/Release/net10.0/ConfigDirector.ServerSdk.Tests.dll
```

Those five are what CI runs, and what the shared `pre-push` hook runs. Wire the hook up once
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

The three web samples each have a test project next to them, `ConfigDirector.Samples.<Name>.Tests`,
which `dotnet test` runs with the rest of the solution. The Minimal API and MVC ones test through
the testing packages, referenced the same way the samples reference the SDK: the published
packages at the version pinned in [Directory.Packages.props](Directory.Packages.props) by default,
the projects from this checkout under `UseLocalSdk`. The OpenFeature one uses the in-memory
provider the OpenFeature SDK ships. Raise the pins after a release so the samples and their tests
track the published packages, and run the samples once without `UseLocalSdk` to check them.

### Slow tests

[tests/ConfigDirector.ServerSdk.SlowTests](tests/ConfigDirector.ServerSdk.SlowTests) covers
behaviors whose only symptom is elapsed time. It is deliberately outside the solution so the
commands above stay quick, and runs nightly in CI.

```bash
dotnet test tests/ConfigDirector.ServerSdk.SlowTests/ConfigDirector.ServerSdk.SlowTests.csproj --timeout 15m
```

## Releasing

The [release](.github/workflows/release.yml) workflow publishes. It is manual (`workflow_dispatch`)
by design, as in every other SDK repository: run it against `main` with the package id, and it
releases whatever version that package's project declares, then tags the released commit as
`<PackageId>-v<version>` once nuget.org has what the tag names. The tag is the workflow's last
step, never the trigger, so a tag always names a version that was published and checked.

```bash
gh workflow run release.yml -f package=ConfigDirector.ServerSdk
```

One workflow serves every package: a package living in `src/<PackageId>/<PackageId>.csproj` needs
no workflow of its own. What a release publishes and tags is decided by
[scripts/release-plan.sh](scripts/release-plan.sh), which
[scripts/release-plan-test.sh](scripts/release-plan-test.sh) covers. Releases before 2026-11-07
were published by pushing the tag by hand, and releases before that scheme were tagged `v1.2.3`;
the tag names are unchanged, so the changelogs' compare links keep working.

The version lives in the package's project file, as `VersionPrefix` plus `VersionSuffix` for a
prerelease; the SDK's lives in `src/ConfigDirector.ServerSdk/Version.props`, which its project and
the two testing packages import. The workflow reads it from there, and refuses a version that is on nuget.org or tagged already.
It is deliberately not passed in on the command line: `-p:Version` is a global MSBuild property, so it
flows across a `ProjectReference` and would stamp the wrong dependency version on a package that
depends on another one here.

A published version is permanent. nuget.org can delist it but never replace it, so a bad release
is fixed by releasing the next patch version.

### Releasing one package

The same steps apply to every package; `ConfigDirector.ServerSdk` stands in for `<PackageId>`.

1. **Choose the version.** Semantic versioning: patch for a fix, minor for something added or
   changed in a compatible way, major for a break. For a prerelease, add a `VersionSuffix` such as
   `beta.1`, which produces `1.3.0-beta.1`.

2. **Bump the project.** Set `VersionPrefix` (and `VersionSuffix`, if any) in
   `src/ConfigDirector.ServerSdk/Version.props`. Every other package keeps its own in its
   `.csproj`.

3. **Close out the changelog.** In `src/ConfigDirector.ServerSdk/CHANGELOG.md`, rename the
   `[Unreleased]` section to `[1.2.0] - YYYY-MM-DD` and open a fresh, empty `[Unreleased]` above
   it. At the bottom, point `[Unreleased]` at `compare/ConfigDirector.ServerSdk-v1.2.0...HEAD` and
   add a `[1.2.0]` link comparing the previous tag to the new one. For the SDK, do the same in
   `src/ConfigDirector.ServerSdk.Testing/CHANGELOG.md` and
   `src/ConfigDirector.ServerSdk.AspNetCore.Testing/CHANGELOG.md`, which release with it at the
   same version and link to the same tags.

4. **Check the version the workflow will see.** A local build appends `-dev`; setting `CI` shows
   the version as the workflow evaluates it, which is what it releases and tags:

   ```bash
   CI=true dotnet msbuild src/ConfigDirector.ServerSdk/ConfigDirector.ServerSdk.csproj -getProperty:Version
   ```

5. **Build, test, and format** with the commands under [Development](#development), then commit
   and push to `main`. The workflow releases the commit it runs against, so the release is
   reproducible from the branch.

6. **Rehearse, if anything about the packaging changed.** Run the workflow with `rehearsal` set,
   from the Actions tab or with the CLI:

   ```bash
   gh workflow run release.yml -f package=ConfigDirector.ServerSdk -f rehearsal=true
   ```

   It plans the release, builds and tests the solution, packs the packages the plan publishes at
   the versions in their projects, and uploads the plan, the `.nupkg` and the `.snupkg` files as a
   workflow artifact without publishing or tagging anything. Download the artifact and check what
   would ship: the version, the README, and the dependencies in the `.nuspec`.

7. **Run the workflow.**

   ```bash
   gh workflow run release.yml -f package=ConfigDirector.ServerSdk
   ```

8. **Watch the run.** The `Pack` job plans the release first: it refuses a version that is on
   nuget.org or tagged already, a version a local build would produce, and a package whose
   changelog has no section for its version, since that section becomes the release notes. It
   then restores every packed package into a throwaway consumer project on each supported runtime
   (`scripts/verify-packages-restore.sh`), so a `.nuspec` that declares a dependency graph NuGet
   rejects, such as a downgrade, fails here rather than in a user's build. The `Publish` job pushes
   each package and its symbols in dependency order, waiting for nuget.org to index every package
   here that one depends on before pushing it (see [The testing packages](#the-testing-packages)),
   then tags the commit once per released package and creates a GitHub release per tag with the
   package's changelog section as its notes. If `Pack` fails, fix the cause, commit, and run the
   workflow again. If `Publish` fails partway, for a reason outside this repository such as a
   nuget.org outage, re-run its failed job rather than the workflow: the plan is kept with the
   packages, a push of what is already on nuget.org is skipped, and the run resumes where it
   stopped. A new run would refuse the version as already on nuget.org.

9. **Confirm on nuget.org.** Indexing takes a few minutes. The package page shows the new version
   once it has been indexed, and a consumer can restore it from then on.

A package published for the first time also needs a trusted publishing policy for its id on
nuget.org, matching this repository and `release.yml`. Without one, `Publish` fails at the login
step with nothing pushed. The first SDK release that carries the testing packages needs policies
for `ConfigDirector.ServerSdk.Testing` and `ConfigDirector.ServerSdk.AspNetCore.Testing` as well.

### The testing packages

`ConfigDirector.ServerSdk.Testing` builds a client over the SDK's internal in-memory connection,
which the SDK exposes to it through `InternalsVisibleTo`. That binding is by signature, and NuGet
lets a consumer's graph resolve a newer SDK than the package was built against, so the two are kept
together three ways:

- **One version.** Both testing packages import `src/ConfigDirector.ServerSdk/Version.props`
  instead of declaring a `VersionPrefix`, so they always carry the SDK's version.
- **Exact dependencies.** A `ProjectReference` packs as a floor, so each testing project rewrites
  its `ProjectVersion` metadata to `[x.y.z]` in a target that runs after
  `_GetProjectReferenceVersions`: `ConfigDirector.ServerSdk.Testing` pins the SDK, and
  `ConfigDirector.ServerSdk.AspNetCore.Testing` pins `ConfigDirector.ServerSdk.Testing`. Its
  dependency on `ConfigDirector.ServerSdk.AspNetCore` stays a floor, because it uses only public
  types of that package. When a consumer's graph still forces a newer SDK, NuGet warns (NU1608)
  and resolves it.
- **A runtime check.** `CreateTestClient` compares the SDK assembly's informational version with
  its own before touching any internal member, and throws an `InvalidOperationException` naming
  both when they differ.

They release with the SDK: a release of `ConfigDirector.ServerSdk` pushes the SDK, waits for
nuget.org to index it (`scripts/wait-for-package-indexed.sh`), pushes
`ConfigDirector.ServerSdk.Testing`, waits again, and pushes
`ConfigDirector.ServerSdk.AspNetCore.Testing`. The waits are what keep the exact dependencies
restorable: nuget.org accepts a package whose dependencies it has not indexed yet. The plan refuses
a release of a testing package on its own, and both ship from the SDK's tag. Every SDK release
therefore releases both testing packages too, with a changelog entry in each, if only to say that
they now require the new SDK version.

### A package that depends on another package here

`ConfigDirector.ServerSdk.AspNetCore` and `ConfigDirector.OpenFeature.ServerProvider` each take
`ConfigDirector.ServerSdk` as a `ProjectReference`. When such a package is packed, that becomes a
NuGet dependency whose version is whatever the ServerSdk project declared at that commit, and NuGet reads a bare dependency version as a floor: `1.1.0` means
"1.1.0 or newer". NuGet also resolves the *lowest* version that satisfies every floor. So a
consumer that references only `ConfigDirector.ServerSdk.AspNetCore` 1.0.0 restores
`ConfigDirector.ServerSdk` 1.1.0, and keeps restoring 1.1.0 after 1.2.0 ships, unless it adds a
direct reference to the SDK itself.

That has two consequences for releasing.

**Order.** A package is published after everything it depends on, because it declares a dependency
on the version it was built against. `Publish` enforces it: before pushing a package, it reads the
packed `.nuspec` and waits for nuget.org to list every dependency on another package here
(`scripts/wait-for-dependencies-indexed.sh`), for up to 20 minutes each. A release of the provider
run right after the SDK's therefore waits for the SDK to index instead of failing; a dependency that
was never published fails the job once the deadline passes, having pushed nothing.

**Carrying consumers forward.** If only `ConfigDirector.ServerSdk` changes and the AspNetCore
package is left alone, its consumers stay on the older SDK. To move them, release a new AspNetCore
version whose only difference is the floor it declares:

1. Bump `ConfigDirector.ServerSdk` as described above. In the same commit, bump `VersionPrefix` in
   `src/ConfigDirector.ServerSdk.AspNetCore/ConfigDirector.ServerSdk.AspNetCore.csproj`. A patch
   bump is enough when the SDK change is a fix. Use a minor bump when the SDK change is visible to
   an application that only references the AspNetCore package, such as a changed default, a new
   option that binds from configuration, or a new API.

2. Record it in `src/ConfigDirector.ServerSdk.AspNetCore/CHANGELOG.md` the same way, with an entry
   saying the package now requires `ConfigDirector.ServerSdk` at the new version, and a line on what
   that brings if the SDK change reaches consumers of this package.

3. Check the dependency the package will declare. Packing locally with `CI` set produces the same
   `.nuspec` the workflow will:

   ```bash
   CI=true dotnet pack src/ConfigDirector.ServerSdk.AspNetCore/ConfigDirector.ServerSdk.AspNetCore.csproj -c Release -o artifacts
   unzip -p artifacts/ConfigDirector.ServerSdk.AspNetCore.*.nupkg '*.nuspec' | grep 'dependency id="ConfigDirector'
   ```

   The `ConfigDirector.ServerSdk` dependency should show the version being released.

4. Commit, push to `main`, and rehearse if anything about the packaging changed.

5. Release `ConfigDirector.ServerSdk`. One run publishes the whole set: the plan sees that the
   AspNetCore version is not on nuget.org yet and includes the package, because
   `ConfigDirector.ServerSdk.AspNetCore.Testing` requires that version. The run pushes the SDK,
   then `ConfigDirector.ServerSdk.Testing` and `ConfigDirector.ServerSdk.AspNetCore` once the SDK
   is indexed, then `ConfigDirector.ServerSdk.AspNetCore.Testing` once those two are, and tags the
   commit as both `ConfigDirector.ServerSdk-v1.2.0` and `ConfigDirector.ServerSdk.AspNetCore-v1.1.0`,
   each with its own GitHub release. An AspNetCore version that is on nuget.org already is left as
   it is, so an SDK release that did not bump it releases the SDK and the testing packages alone.

   ```bash
   gh workflow run release.yml -f package=ConfigDirector.ServerSdk
   ```

The reverse case needs nothing special. A change to the AspNetCore package alone is released on
its own, with `package=ConfigDirector.ServerSdk.AspNetCore`, and declares whatever SDK version the
project holds at that commit.

### The OpenFeature provider and the SDK's internals

`ConfigDirector.OpenFeature.ServerProvider` identifies itself to ConfigDirector under its own SDK
name and version. The server SDK exposes that only to assemblies it names in `InternalsVisibleTo`,
so the public API offers no way for an application to claim an arbitrary identity. The provider
therefore compiles against internal members of `ConfigDirector.ServerSdk`: `SdkIdentity`, the
internal `ConfigDirectorClient` constructor that takes one, `EvaluationReasons`, and
`ConfigDirectorClient.EvaluateShapedLikeInCodeDefaultValue`, which reads an OpenFeature object flag.

That binding is by signature, and NuGet lets a consumer restore a newer SDK than the provider was
built against. A released provider must keep working against every later SDK within the same major
version, so those members are a compatibility contract:

- Do not change or remove them in a minor or patch release of the SDK. Add a new overload and leave
  the existing one in place.
- When one of them does change in a major release, bump the provider in the same commit and
  release it right after the SDK, with `package=ConfigDirector.OpenFeature.ServerProvider`; its
  run waits for the SDK version it declares to be on nuget.org. Its changelog should say it
  requires the new SDK.
- A wrapper that is new to the repository needs its assembly name added to the SDK's
  `InternalsVisibleTo` list before it can build.
