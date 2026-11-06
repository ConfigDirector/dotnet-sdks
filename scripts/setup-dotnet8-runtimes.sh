#!/usr/bin/env bash
# Installs the latest .NET 8 and ASP.NET Core 8 runtimes, which the test suite needs, into the SDK
# on the PATH.
#
# An SDK from a package manager comes without them, and upgrading it with Homebrew removes what was
# installed here, so run this again after each upgrade. Until then the build passes and `dotnet test`
# fails with "You must install or update .NET".
#
# Only the two runtime directories are copied out of Microsoft's archive. It also carries its own
# `dotnet` host binary, which would overwrite the one already there and leave a `dotnet` that macOS
# refuses to run.
#
#   scripts/setup-dotnet8-runtimes.sh
set -euo pipefail

releases_url="https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json"

workspace=$(mktemp -d)
trap 'rm -rf "$workspace"' EXIT

rid=$(dotnet --info | awk '$1 == "RID:" { print $2; exit }')
installed_runtime=$(dotnet --list-runtimes | sed -n 's/^Microsoft\.NETCore\.App [^ ]* \[\(.*\)\]$/\1/p' | head -n 1)
shared=$(dirname "$installed_runtime")

curl -fsSL "$releases_url" -o "$workspace/releases.json"

read -r runtime_version aspnetcore_version archive_url archive_sha512 < <(python3 - "$workspace/releases.json" "$rid" <<'PY'
import json, sys

with open(sys.argv[1]) as releases:
    latest = json.load(releases)["releases"][0]

archives = [
    file
    for file in latest["aspnetcore-runtime"]["files"]
    if file["rid"] == sys.argv[2] and file["name"].endswith(".tar.gz")
]
if not archives:
    sys.exit(f"Microsoft publishes no .NET 8 runtime archive for {sys.argv[2]}.")

print(
    latest["runtime"]["version"],
    latest["aspnetcore-runtime"]["version"],
    archives[0]["url"],
    archives[0]["hash"],
)
PY
)

if [ -d "$shared/Microsoft.NETCore.App/$runtime_version" ] \
  && [ -d "$shared/Microsoft.AspNetCore.App/$aspnetcore_version" ]; then
  echo ".NET $runtime_version and ASP.NET Core $aspnetcore_version are already in $shared"
  exit 0
fi

echo "downloading .NET $runtime_version and ASP.NET Core $aspnetcore_version for $rid"
curl -fsSL "$archive_url" -o "$workspace/runtimes.tar.gz"

actual_sha512=$(shasum -a 512 "$workspace/runtimes.tar.gz" | cut -d' ' -f1)
if [ "$actual_sha512" != "$archive_sha512" ]; then
  echo "The download from $archive_url does not match Microsoft's published SHA-512. Nothing was installed." >&2
  exit 1
fi

mkdir "$workspace/runtimes"
tar -xzf "$workspace/runtimes.tar.gz" -C "$workspace/runtimes"

cp -R "$workspace/runtimes/shared/Microsoft.NETCore.App/$runtime_version" "$shared/Microsoft.NETCore.App/"
cp -R "$workspace/runtimes/shared/Microsoft.AspNetCore.App/$aspnetcore_version" "$shared/Microsoft.AspNetCore.App/"

echo "installed .NET $runtime_version and ASP.NET Core $aspnetcore_version in $shared"
