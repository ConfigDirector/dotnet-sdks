#!/usr/bin/env bash
# Waits until every dependency a package takes on another package in this repository is listed on
# nuget.org, so the package can be pushed next.
#
# nuget.org accepts a package whose dependencies do not exist, and a published version can only be
# delisted, never replaced, so a package pushed before what it depends on would be one nobody can
# ever restore. The release workflow pushes packages in dependency order from one run, and a
# package released on its own may follow the release it depends on by minutes, so this waits for
# nuget.org to index each dependency rather than failing and asking for a re-run. A dependency
# that was never published fails here once the deadline passes.
#
# Only dependencies that are themselves packages here are checked. Everything else came from
# nuget.org to begin with.
#
#   scripts/wait-for-dependencies-indexed.sh artifacts/ConfigDirector.ServerSdk.Testing.1.11.0.nupkg [minutes]
set -euo pipefail

scripts_directory=$(cd "$(dirname "$0")" && pwd)
nupkg=$1
minutes=${2:-20}

# The nuspec is XML, so it is parsed rather than pattern matched.
dependencies=$(python3 - "$nupkg" <<'PY'
import sys, xml.etree.ElementTree as ET, zipfile

with zipfile.ZipFile(sys.argv[1]) as package:
    nuspec = next(name for name in package.namelist() if name.endswith(".nuspec"))
    root = ET.fromstring(package.read(nuspec))

seen = set()
for element in root.iter():
    if element.tag.rsplit("}", 1)[-1] != "dependency":
        continue

    entry = (element.get("id"), element.get("version"))
    if all(entry) and entry not in seen:
        seen.add(entry)
        print(*entry)
PY
)

while read -r id version; do
  [ -n "$id" ] || continue

  # Not one of ours, so it is on nuget.org by virtue of having restored.
  [ -f "src/$id/$id.csproj" ] || continue

  # A dependency version is a range. NuGet writes a bare version for a project reference, which
  # means "this or newer"; the brackets and upper bound of a written-out range are trimmed off so
  # the lower bound is what gets waited for.
  required=${version//[\[\]()]/}
  required=${required%%,*}
  required=${required// /}

  "$scripts_directory/wait-for-package-indexed.sh" "$id" "$required" "$minutes"
done <<< "$dependencies"

echo "every dependency of $(basename "$nupkg") within this repository is on nuget.org"
