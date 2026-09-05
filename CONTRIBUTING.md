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

Tagging publishes. Every package is versioned and tagged on its own, as `<PackageId>-v<version>`:

```bash
git tag ConfigDirector.ServerSdk-v1.2.0
git push origin ConfigDirector.ServerSdk-v1.2.0
```

[release.yml](.github/workflows/release.yml) serves every package. It derives the project from the
tag, so a package living in `src/<PackageId>/<PackageId>.csproj` needs no workflow of its own.
Releases cut before this scheme were tagged `v1.2.3`, which the workflow no longer matches.

The version lives in the package's project file, as `VersionPrefix` plus `VersionSuffix` for a
prerelease. The workflow reads it from there and refuses a tag that disagrees with it. It is
deliberately not passed in on the command line: `-p:Version` is a global MSBuild property, so it
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
   `src/ConfigDirector.ServerSdk/ConfigDirector.ServerSdk.csproj`.

3. **Close out the changelog.** In `src/ConfigDirector.ServerSdk/CHANGELOG.md`, rename the
   `[Unreleased]` section to `[1.2.0] - YYYY-MM-DD` and open a fresh, empty `[Unreleased]` above
   it. At the bottom, point `[Unreleased]` at `compare/ConfigDirector.ServerSdk-v1.2.0...HEAD` and
   add a `[1.2.0]` link comparing the previous tag to the new one.

4. **Check the version the workflow will see.** A local build appends `-dev`; setting `CI` shows
   the version as the workflow evaluates it, which is what the tag has to match:

   ```bash
   CI=true dotnet msbuild src/ConfigDirector.ServerSdk/ConfigDirector.ServerSdk.csproj -getProperty:Version
   ```

5. **Build, test, and format** with the three commands under [Development](#development), then
   commit and push to `main`. Anything a tag points at should already be on `main`, so the release
   is reproducible from the branch.

6. **Rehearse.** Run the workflow by hand, from the Actions tab or with the CLI:

   ```bash
   gh workflow run release.yml -f package=ConfigDirector.ServerSdk
   ```

   It builds and tests the solution, packs the package at the version in the project, and uploads
   the `.nupkg` and `.snupkg` as a workflow artifact without publishing anything. Download the
   artifact and check what would ship: the version, the README, and the dependencies in the
   `.nuspec`. The rehearsal does not run the dependency check described below; that runs only when
   publishing.

7. **Tag the commit and push the tag.**

   ```bash
   git tag ConfigDirector.ServerSdk-v1.2.0
   git push origin ConfigDirector.ServerSdk-v1.2.0
   ```

8. **Watch the run.** The `Pack` job rejects a tag whose version disagrees with the project. The
   `Publish` job checks that every dependency on another package here is already on nuget.org,
   pushes the package and its symbols, and creates a GitHub release named after the tag with
   generated notes. If `Pack` fails, fix the cause, delete the tag, and start again from step 5. If
   `Publish` fails before pushing, nothing has been published and the job can simply be re-run.

9. **Confirm on nuget.org.** Indexing takes a few minutes. The package page shows the new version
   once it has been indexed, and a consumer can restore it from then on.

A package published for the first time also needs a trusted publishing policy for its id on
nuget.org, matching this repository and `release.yml`. Without one, `Publish` fails at the login
step with nothing pushed.

### A package that depends on another package here

`ConfigDirector.ServerSdk.AspNetCore` takes `ConfigDirector.ServerSdk` as a `ProjectReference`.
When it is packed, that becomes a NuGet dependency whose version is whatever the ServerSdk project
declared at that commit, and NuGet reads a bare dependency version as a floor: `1.1.0` means
"1.1.0 or newer". NuGet also resolves the *lowest* version that satisfies every floor. So a
consumer that references only `ConfigDirector.ServerSdk.AspNetCore` 1.0.0 restores
`ConfigDirector.ServerSdk` 1.1.0, and keeps restoring 1.1.0 after 1.2.0 ships, unless it adds a
direct reference to the SDK itself.

That has two consequences for releasing.

**Order.** A package is published after everything it depends on, because it declares a dependency
on the version it was built against. `Publish` enforces it: before fetching a credential, it reads
the packed `.nuspec` and fails if any dependency on another package here is not yet on nuget.org.
Two tags pushed at once run in parallel and in no particular order, so the dependent package's job
can fail this way even when both tags are correct. Re-run that job once the dependency has indexed.

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

4. Commit, push to `main`, and rehearse each package with `workflow_dispatch` if anything about the
   packaging changed.

5. Tag the commit once per package. Push the `ConfigDirector.ServerSdk` tag first, wait for its
   run to finish and for nuget.org to index the version, then push the
   `ConfigDirector.ServerSdk.AspNetCore` tag:

   ```bash
   git tag ConfigDirector.ServerSdk-v1.2.0
   git tag ConfigDirector.ServerSdk.AspNetCore-v1.1.0
   git push origin ConfigDirector.ServerSdk-v1.2.0
   # once ConfigDirector.ServerSdk 1.2.0 is listed on nuget.org:
   git push origin ConfigDirector.ServerSdk.AspNetCore-v1.1.0
   ```

   Pushing both at once also works; the AspNetCore `Publish` job then fails its dependency check
   until the SDK has indexed, and is re-run rather than re-tagged. Indexing can be checked with the
   same lookup the workflow uses:

   ```bash
   curl -s https://api.nuget.org/v3-flatcontainer/configdirector.serversdk/index.json
   ```

The reverse case needs nothing special. A change to the AspNetCore package alone is released on
its own, and declares whatever SDK version the project holds at that commit.
