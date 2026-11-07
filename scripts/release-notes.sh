#!/usr/bin/env bash
# Prints the changelog section of one version of a package, which the release workflow publishes
# as the GitHub release's notes. Fails when the changelog has no such section, or an empty one,
# so a version ships only once its changelog says what it changes.
#
#   scripts/release-notes.sh ConfigDirector.ServerSdk 1.11.0
set -euo pipefail

id=$1
version=$2
changelog="src/$id/CHANGELOG.md"
heading="## [$version]"

if [ ! -f "$changelog" ]; then
  echo "Cannot find $changelog. Run this from the repository root." >&2
  exit 1
fi

# The section runs from its heading to the next "## " heading, with the blank lines at either end
# trimmed off. Exit status 3 means no heading matched.
if ! notes=$(awk -v heading="$heading" '
    /^## / {
        if (inside) exit
        inside = ($0 == heading || index($0, heading " ") == 1)
        if (inside) found = 1
        next
    }
    inside { lines[++count] = $0 }
    END {
        if (!found) exit 3
        first = 1
        while (first <= count && lines[first] ~ /^[[:space:]]*$/) first++
        last = count
        while (last >= first && lines[last] ~ /^[[:space:]]*$/) last--
        for (i = first; i <= last; i++) print lines[i]
    }
' "$changelog"); then
  echo "$changelog has no '$heading' section. Rename its '## [Unreleased]' heading to '$heading - YYYY-MM-DD' and open a fresh one above it before releasing." >&2
  exit 1
fi

if [ -z "$notes" ]; then
  echo "The '$heading' section in $changelog is empty, so there are no release notes to publish." >&2
  exit 1
fi

printf '%s\n' "$notes"
