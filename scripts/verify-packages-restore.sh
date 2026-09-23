#!/usr/bin/env bash
# Restores every packed package into a consumer project, on each runtime an application might use
# it from, and fails when NuGet refuses.
#
# A package's own build proves nothing about the dependency graph a consumer ends up with. The
# .nuspec is assembled at pack time, and what it declares interacts with the consumer's target
# framework and with everything else in the graph: a dependency floor declared too shallow, for
# instance, is a downgrade error (NU1605) for a consumer whose other packages need a newer version
# of the same assembly. Only restoring the packed artifact, the way a consumer would, catches that.
#
# The packages are restored from the directory they were packed into, so a version that is not yet
# on nuget.org, or one that differs from what is published under the same number, is what gets
# checked. Everything else comes from nuget.org into a fresh package folder, so a package already
# in the machine's cache cannot stand in for the artifact.
#
#   scripts/verify-packages-restore.sh artifacts/*.nupkg
set -euo pipefail

FRAMEWORKS="net8.0;net10.0"

artifacts=$(cd "$(dirname "$1")" && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

cat > "$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="artifacts" value="$artifacts" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

fail=0

for nupkg in "$@"; do
  # The nuspec is XML, so it is parsed rather than pattern matched.
  read -r id version < <(python3 - "$nupkg" <<'PY'
import sys, xml.etree.ElementTree as ET, zipfile

with zipfile.ZipFile(sys.argv[1]) as package:
    nuspec = next(name for name in package.namelist() if name.endswith(".nuspec"))
    root = ET.fromstring(package.read(nuspec))

def text(name):
    return next(element.text for element in root.iter() if element.tag.rsplit("}", 1)[-1] == name)

print(text("id"), text("version"))
PY
)

  project="$work/$id"
  mkdir -p "$project"
  cat > "$project/consumer.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>$FRAMEWORKS</TargetFrameworks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="$id" Version="[$version]" />
  </ItemGroup>

</Project>
XML

  # An exact version, so the artifact is what restores and not a newer release from nuget.org.
  if output=$(dotnet restore "$project/consumer.csproj" --packages "$work/packages" --nologo 2>&1); then
    echo "  ok   $id $version restores on $FRAMEWORKS"
    printf '%s\n' "$output" | grep "warning NU" || true
  else
    printf '%s\n' "$output" | grep -v "^ *Determining\|^ *Restored" || true
    echo "  FAIL $id $version does not restore"
    fail=1
  fi
done

if [ "$fail" -ne 0 ]; then
  echo "A consumer cannot restore what was packed. Check the dependencies in the .nuspec." >&2
  exit 1
fi

echo "every package restores into a consumer"
