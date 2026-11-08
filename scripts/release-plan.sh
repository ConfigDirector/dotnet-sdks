#!/usr/bin/env bash
# Decides what a release of one package publishes and tags, in dependency order, and refuses a
# release that cannot ship. Writes the plan to the given file, one decision per line:
#
#   publish <PackageId> <version>
#   tag <tag> <PackageId> <version>
#
# The versions are the ones the projects declare; nothing is passed in. A release of
# ConfigDirector.ServerSdk also publishes ConfigDirector.ServerSdk.Testing and
# ConfigDirector.ServerSdk.AspNetCore.Testing, which carry the SDK's version, and
# ConfigDirector.ServerSdk.AspNetCore when the version it declares is not on nuget.org yet: the
# ASP.NET Core testing package requires that version, so the release would otherwise be stuck. An
# ASP.NET Core version that is on nuget.org already is left as it is.
#
# A release is refused when the package's version is on nuget.org already or its tag exists, when
# a version is not what CI would build, and when a package to publish has no changelog section for
# its version, since that section becomes the release notes. scripts/release-plan-test.sh runs
# this against scripted versions, nuget.org reads and tags.
#
#   scripts/release-plan.sh ConfigDirector.ServerSdk release-plan
set -euo pipefail

scripts_directory=$(cd "$(dirname "$0")" && pwd)
package=$1
plan=$2

# Also what keeps the package id out of the paths below.
if ! printf '%s' "$package" | grep -Eq '^[A-Za-z0-9._-]+$'; then
  echo "'$package' is not a package id." >&2
  exit 1
fi

if [ ! -f "src/$package/$package.csproj" ]; then
  echo "'$package' names no package in this repository: src/$package/$package.csproj does not exist." >&2
  exit 1
fi

case "$package" in
  ConfigDirector.ServerSdk.Testing | ConfigDirector.ServerSdk.AspNetCore.Testing)
    echo "$package is released with ConfigDirector.ServerSdk and carries its version. Release ConfigDirector.ServerSdk instead, which publishes both testing packages." >&2
    exit 1
    ;;
esac

declared_version() {
  local id=$1
  local version
  version=$(dotnet msbuild "src/$id/$id.csproj" -getProperty:Version -nologo)

  case "$version" in
    *-dev)
      echo "$id declares $version, the version of a local build. Set CI so the version is what the release builds." >&2
      exit 1
      ;;
  esac

  if ! printf '%s' "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$'; then
    echo "$id declares '$version', which is not a semantic version. Set VersionPrefix, and VersionSuffix for a prerelease, to one such as 1.2.3 or 1.2.3-beta.1." >&2
    exit 1
  fi

  printf '%s' "$version"
}

is_on_nuget_org() {
  local id=$1
  local version=$2
  local lower versions
  lower=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')

  versions=$(curl -fsSL "https://api.nuget.org/v3-flatcontainer/$lower/index.json" 2>/dev/null) \
    && printf '%s' "$versions" | grep -qiF "\"$version\""
}

tag_exists() {
  [ -n "$(git ls-remote --tags origin "refs/tags/$1")" ]
}

# Refuses to release a version twice: nuget.org never replaces a version, and the tag names it.
refuse_a_released_version() {
  local id=$1
  local version=$2
  local tag="$id-v$version"

  if is_on_nuget_org "$id" "$version"; then
    echo "$id $version is on nuget.org already. nuget.org never replaces a version: bump the version, add a changelog section and release again. If a run of this workflow failed after pushing it, re-run that run's failed Publish job instead, which resumes where it stopped." >&2
    exit 1
  fi

  if tag_exists "$tag"; then
    echo "$tag exists already, so $id $version has been released before. Bump the version, add a changelog section and release again." >&2
    exit 1
  fi
}

version=$(declared_version "$package")
refuse_a_released_version "$package" "$version"

publishes=("$package")
tags=("$package-v$version $package $version")

if [ "$package" = ConfigDirector.ServerSdk ]; then
  publishes+=(ConfigDirector.ServerSdk.Testing)

  aspnetcore=ConfigDirector.ServerSdk.AspNetCore
  aspnetcore_version=$(declared_version "$aspnetcore")

  if is_on_nuget_org "$aspnetcore" "$aspnetcore_version"; then
    echo "$aspnetcore $aspnetcore_version is on nuget.org already, so this release leaves it as it is."
  else
    refuse_a_released_version "$aspnetcore" "$aspnetcore_version"
    publishes+=("$aspnetcore")
    tags+=("$aspnetcore-v$aspnetcore_version $aspnetcore $aspnetcore_version")
  fi

  publishes+=(ConfigDirector.ServerSdk.AspNetCore.Testing)
fi

decisions=()

for id in "${publishes[@]}"; do
  id_version=$(declared_version "$id")

  # The section is what the GitHub release publishes as its notes, and a package whose changelog
  # does not name its version is not ready to ship.
  "$scripts_directory/release-notes.sh" "$id" "$id_version" > /dev/null

  decisions+=("publish $id $id_version")
done

for tag in "${tags[@]}"; do
  decisions+=("tag $tag")
done

# Written only now, so a refusal above leaves no plan behind. The plan is the first thing written
# into the directory the packages are packed into later, so the directory is created here.
mkdir -p "$(dirname "$plan")"
printf '%s\n' "${decisions[@]}" > "$plan"

echo "Releasing $package $version:"
sed 's/^/  /' "$plan"
