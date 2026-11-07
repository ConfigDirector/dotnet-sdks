#!/usr/bin/env bash
#
# Runs release-plan.sh against scripted project versions, nuget.org listings and tags, one scenario
# each. dotnet, curl and git are replaced on the PATH, so no build runs and no request leaves the
# machine. Each scenario gets a throwaway repository with the five packages' project files and
# changelogs, where every package's version has a non-empty section unless the scenario says
# otherwise.

set -euo pipefail

scripts_directory=$(cd "$(dirname "$0")" && pwd)
release_plan="$scripts_directory/release-plan.sh"
sandbox=$(mktemp -d)
trap 'rm -rf "$sandbox"' EXIT
failures=0

packages=(
  ConfigDirector.ServerSdk
  ConfigDirector.ServerSdk.Testing
  ConfigDirector.ServerSdk.AspNetCore
  ConfigDirector.ServerSdk.AspNetCore.Testing
  ConfigDirector.OpenFeature.ServerProvider
)

mkdir "$sandbox/bin"

# dotnet answers the version query of a project with the version the scenario declared for it.
cat > "$sandbox/bin/dotnet" <<'DOTNET'
#!/usr/bin/env bash
set -euo pipefail
project=$2
id=$(basename "$project" .csproj)
awk -v id="$id" '$1 == id { print $2 }' "$DECLARED_VERSIONS"
DOTNET

# curl answers a nuget.org index read with the versions the scenario listed for that package.
cat > "$sandbox/bin/curl" <<'CURL'
#!/usr/bin/env bash
set -euo pipefail
url=${!#}
lower=$(basename "$(dirname "$url")")
versions=$(awk -v id="$lower" '$1 == id { printf "\"%s\",", $2 }' "$LISTED_VERSIONS")
printf '{"versions":[%s]}' "${versions%,}"
CURL

# git answers a tag lookup with a ref line when the scenario says the tag exists.
cat > "$sandbox/bin/git" <<'GIT'
#!/usr/bin/env bash
set -euo pipefail
ref=${!#}
if grep -qxF "${ref#refs/tags/}" "$EXISTING_TAGS"; then
  printf '0000000000000000000000000000000000000000\t%s\n' "$ref"
fi
GIT
chmod +x "$sandbox/bin/dotnet" "$sandbox/bin/curl" "$sandbox/bin/git"

# Starts the scenario's repository: every package declares the given version, with a changelog
# section for it. Scenarios then adjust what nuget.org lists, which tags exist and the changelogs.
# Sets $repository, $declared, $listed and $tags.
start_repository() {
  repository="$sandbox/$scenario"
  declared="$repository/declared-versions"
  listed="$repository/listed-versions"
  tags="$repository/existing-tags"
  mkdir -p "$repository"
  : > "$declared"
  : > "$listed"
  : > "$tags"
  for id in "${packages[@]}"; do
    declare_version "$id" "$1"
  done
}

declare_version() {
  mkdir -p "$repository/src/$1"
  : > "$repository/src/$1/$1.csproj"
  sed -i.bak "/^$1 /d" "$declared" && rm "$declared.bak"
  printf '%s %s\n' "$1" "$2" >> "$declared"
  printf '# Changelog\n\n## [Unreleased]\n\n## [%s] - 2026-11-07\n\n### Fixed\n\n- A fix.\n\n## [0.9.0] - 2026-01-01\n\n- Older.\n' "$2" > "$repository/src/$1/CHANGELOG.md"
}

list_on_nuget_org() {
  printf '%s %s\n' "$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" "$2" >> "$listed"
}

# Runs the plan for a package in the scenario's repository. Sets $status, $plan_file and $output.
run_plan() {
  plan_file="$repository/plan"
  status=0
  output=$(cd "$repository" && PATH="$sandbox/bin:$PATH" DECLARED_VERSIONS="$declared" LISTED_VERSIONS="$listed" EXISTING_TAGS="$tags" \
    "$release_plan" "$1" "$plan_file" 2>&1) || status=$?
}

fail() {
  printf 'FAIL %s: %s\n' "$scenario" "$1"
  failures=$((failures + 1))
}

assert_status() {
  [ "$status" -eq "$1" ] || fail "exit status was $status, expected $1 (output: $output)"
}

assert_plan() {
  local actual
  actual=$(cat "$plan_file")
  [ "$actual" = "$1" ] || fail "plan was:
$actual
expected:
$1"
}

assert_refusal() {
  [ "$status" -eq 1 ] || fail "exit status was $status, expected 1 (output: $output)"
  printf '%s' "$output" | grep -qF "$1" || fail "output does not say '$1': $output"
  [ ! -s "$plan_file" ] || fail "a refused release wrote a plan"
}

pass() {
  printf 'ok   %s\n' "$scenario"
}

scenario=an_sdk_release_publishes_the_testing_packages_and_a_new_aspnetcore_version_in_dependency_order
start_repository 1.11.0
declare_version ConfigDirector.OpenFeature.ServerProvider 1.8.1
list_on_nuget_org ConfigDirector.ServerSdk 1.10.0
list_on_nuget_org ConfigDirector.ServerSdk.AspNetCore 1.10.0
run_plan ConfigDirector.ServerSdk
assert_status 0
assert_plan "publish ConfigDirector.ServerSdk 1.11.0
publish ConfigDirector.ServerSdk.Testing 1.11.0
publish ConfigDirector.ServerSdk.AspNetCore 1.11.0
publish ConfigDirector.ServerSdk.AspNetCore.Testing 1.11.0
tag ConfigDirector.ServerSdk-v1.11.0 ConfigDirector.ServerSdk 1.11.0
tag ConfigDirector.ServerSdk.AspNetCore-v1.11.0 ConfigDirector.ServerSdk.AspNetCore 1.11.0"
pass

scenario=an_sdk_release_leaves_an_aspnetcore_version_that_is_on_nuget_org_as_it_is
start_repository 1.10.1
declare_version ConfigDirector.ServerSdk.AspNetCore 1.10.0
list_on_nuget_org ConfigDirector.ServerSdk 1.10.0
list_on_nuget_org ConfigDirector.ServerSdk.AspNetCore 1.10.0
run_plan ConfigDirector.ServerSdk
assert_status 0
assert_plan "publish ConfigDirector.ServerSdk 1.10.1
publish ConfigDirector.ServerSdk.Testing 1.10.1
publish ConfigDirector.ServerSdk.AspNetCore.Testing 1.10.1
tag ConfigDirector.ServerSdk-v1.10.1 ConfigDirector.ServerSdk 1.10.1"
printf '%s' "$output" | grep -qF "ConfigDirector.ServerSdk.AspNetCore 1.10.0 is on nuget.org already, so this release leaves it as it is" \
  || fail "output does not say the ASP.NET Core package is left alone: $output"
pass

scenario=a_provider_release_publishes_and_tags_the_provider_alone
start_repository 1.11.0
declare_version ConfigDirector.OpenFeature.ServerProvider 1.8.1
list_on_nuget_org ConfigDirector.OpenFeature.ServerProvider 1.8.0
run_plan ConfigDirector.OpenFeature.ServerProvider
assert_status 0
assert_plan "publish ConfigDirector.OpenFeature.ServerProvider 1.8.1
tag ConfigDirector.OpenFeature.ServerProvider-v1.8.1 ConfigDirector.OpenFeature.ServerProvider 1.8.1"
pass

scenario=an_aspnetcore_release_on_its_own_publishes_and_tags_that_package_alone
start_repository 1.11.0
declare_version ConfigDirector.ServerSdk.AspNetCore 1.11.1
run_plan ConfigDirector.ServerSdk.AspNetCore
assert_status 0
assert_plan "publish ConfigDirector.ServerSdk.AspNetCore 1.11.1
tag ConfigDirector.ServerSdk.AspNetCore-v1.11.1 ConfigDirector.ServerSdk.AspNetCore 1.11.1"
pass

scenario=a_prerelease_version_is_released_like_any_other
start_repository 2.0.0-beta.1
run_plan ConfigDirector.OpenFeature.ServerProvider
assert_status 0
assert_plan "publish ConfigDirector.OpenFeature.ServerProvider 2.0.0-beta.1
tag ConfigDirector.OpenFeature.ServerProvider-v2.0.0-beta.1 ConfigDirector.OpenFeature.ServerProvider 2.0.0-beta.1"
pass

scenario=refuses_a_testing_package_which_releases_with_the_sdk
start_repository 1.11.0
run_plan ConfigDirector.ServerSdk.Testing
assert_refusal "Release ConfigDirector.ServerSdk instead"
pass

scenario=refuses_a_package_that_is_not_in_the_repository
start_repository 1.11.0
run_plan ConfigDirector.Missing
assert_refusal "names no package in this repository"
pass

scenario=refuses_a_version_that_is_on_nuget_org_already
start_repository 1.11.0
list_on_nuget_org ConfigDirector.ServerSdk 1.11.0
run_plan ConfigDirector.ServerSdk
assert_refusal "ConfigDirector.ServerSdk 1.11.0 is on nuget.org already"
pass

scenario=refuses_a_version_whose_tag_exists
start_repository 1.11.0
echo ConfigDirector.ServerSdk-v1.11.0 >> "$tags"
run_plan ConfigDirector.ServerSdk
assert_refusal "ConfigDirector.ServerSdk-v1.11.0 exists already"
pass

scenario=refuses_an_sdk_release_whose_new_aspnetcore_version_has_a_tag_already
start_repository 1.11.0
echo ConfigDirector.ServerSdk.AspNetCore-v1.11.0 >> "$tags"
run_plan ConfigDirector.ServerSdk
assert_refusal "ConfigDirector.ServerSdk.AspNetCore-v1.11.0 exists already"
pass

scenario=refuses_the_version_of_a_local_build
start_repository 1.11.0-dev
run_plan ConfigDirector.ServerSdk
assert_refusal "the version of a local build"
pass

scenario=refuses_a_version_that_is_not_semantic
start_repository 1.11
run_plan ConfigDirector.OpenFeature.ServerProvider
assert_refusal "is not a semantic version"
pass

scenario=refuses_a_release_when_a_published_package_has_no_changelog_section_for_its_version
start_repository 1.11.0
printf '# Changelog\n\n## [Unreleased]\n\n## [1.10.0] - 2026-11-06\n\n- Older.\n' > "$repository/src/ConfigDirector.ServerSdk.AspNetCore.Testing/CHANGELOG.md"
run_plan ConfigDirector.ServerSdk
assert_refusal "src/ConfigDirector.ServerSdk.AspNetCore.Testing/CHANGELOG.md has no '## [1.11.0]' section"
pass

scenario=refuses_a_release_when_the_changelog_section_is_empty
start_repository 1.11.0
printf '# Changelog\n\n## [Unreleased]\n\n## [1.11.0] - 2026-11-07\n\n## [1.10.0] - 2026-11-06\n\n- Older.\n' > "$repository/src/ConfigDirector.ServerSdk/CHANGELOG.md"
run_plan ConfigDirector.ServerSdk
assert_refusal "The '## [1.11.0]' section in src/ConfigDirector.ServerSdk/CHANGELOG.md is empty"
pass

scenario=the_release_notes_are_the_changelog_section_without_its_surrounding_blank_lines
start_repository 1.11.0
notes=$(cd "$repository" && "$scripts_directory/release-notes.sh" ConfigDirector.ServerSdk 1.11.0)
[ "$notes" = "### Fixed

- A fix." ] || fail "notes were: $notes"
pass

if [ "$failures" -ne 0 ]; then
  printf '\n%s scenario(s) failed\n' "$failures"
  exit 1
fi
