#!/usr/bin/env bash
# Waits until nuget.org lists a version of a package, so a package that depends on it exactly can
# be pushed next.
#
# nuget.org accepts a push within seconds but indexes it minutes later, and until then a package
# depending on the pushed version fails scripts/verify-dependencies-published.sh. The testing
# packages pin the SDK's exact version and are pushed right after it from the same run, so the
# run has to wait here rather than fail and be re-run by hand.
#
#   scripts/wait-for-package-indexed.sh ConfigDirector.ServerSdk 1.6.0 [minutes]
set -euo pipefail

id=$1
version=$2
minutes=${3:-20}

lower=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')
index="https://api.nuget.org/v3-flatcontainer/$lower/index.json"
deadline=$(( $(date +%s) + minutes * 60 ))

while :; do
  if versions=$(curl -fsSL "$index" 2>/dev/null) \
    && printf '%s' "$versions" | grep -qiF "\"$version\""; then
    echo "$id $version is on nuget.org"
    exit 0
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "$id $version was not indexed by nuget.org within $minutes minutes. Re-run this job once it is listed at $index." >&2
    exit 1
  fi

  echo "waiting for nuget.org to index $id $version"
  sleep 30
done
