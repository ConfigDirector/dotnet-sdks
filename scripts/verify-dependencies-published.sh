#!/usr/bin/env bash
# Checks that every dependency a package takes on another package in this repository is already on
# nuget.org.
#
# nuget.org accepts a package whose dependencies do not exist, and a published version can only be
# delisted, never replaced. Two release tags pushed together run in parallel, so a package can
# reach nuget.org before the one it depends on, or after that one's run has failed, leaving a
# package nobody can ever restore. This turns that into a failed job instead.
#
# Only dependencies that are themselves packages here are checked. Everything else came from
# nuget.org to begin with.
#
#   scripts/verify-dependencies-published.sh artifacts/*.nupkg
set -euo pipefail

fail=0

for nupkg in "$@"; do
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
    # the lower bound is what gets looked up.
    required=${version//[\[\]()]/}
    required=${required%%,*}
    required=${required// /}

    lower=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')
    index="https://api.nuget.org/v3-flatcontainer/$lower/index.json"

    if versions=$(curl -fsSL "$index" 2>/dev/null) \
      && printf '%s' "$versions" | grep -qiF "\"$required\""; then
      echo "  ok   $id $required is on nuget.org"
    else
      echo "  FAIL $(basename "$nupkg") depends on $id $required, which is not on nuget.org"
      fail=1
    fi
  done <<< "$dependencies"
done

if [ "$fail" -ne 0 ]; then
  echo "Publish the missing packages first, let nuget.org index them, then re-run this job." >&2
  exit 1
fi

echo "every dependency within this repository is published"
