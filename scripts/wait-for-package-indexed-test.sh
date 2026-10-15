#!/usr/bin/env bash
#
# Runs wait-for-package-indexed.sh against scripted nuget.org reads, one scenario each. curl and
# sleep are replaced on the PATH, so no request leaves the machine and no scenario waits.

set -euo pipefail

scripts_directory=$(cd "$(dirname "$0")" && pwd)
wait_for_package_indexed="$scripts_directory/wait-for-package-indexed.sh"
sandbox=$(mktemp -d)
trap 'rm -rf "$sandbox"' EXIT
failures=0

# curl answers each read with the next scripted index: "listed" holds 1.8.0, "missing" does not.
mkdir "$sandbox/bin"
cat > "$sandbox/bin/curl" <<'CURL'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "${!#}" >> "$READS_LOG"
answer=$(head -n 1 "$SCRIPTED_READS")
tail -n +2 "$SCRIPTED_READS" > "$SCRIPTED_READS.rest"
mv "$SCRIPTED_READS.rest" "$SCRIPTED_READS"
case "$answer" in
  listed) printf '{"versions":["1.7.1","1.8.0"]}' ;;
  missing) printf '{"versions":["1.7.1"]}' ;;
esac
CURL

# sleep fails once no read is left, which stops a script that would otherwise wait for another.
cat > "$sandbox/bin/sleep" <<'SLEEP'
#!/usr/bin/env bash
if [ ! -s "$SCRIPTED_READS" ]; then
  echo "the scenario scripted no further read" >&2
  exit 1
fi
SLEEP
chmod +x "$sandbox/bin/curl" "$sandbox/bin/sleep"

# Waits for ConfigDirector.ServerSdk 1.8.0 for the given minutes, answering reads with the rest of
# the arguments in order. Sets $status, $reads_file and $stderr_file.
run_wait() {
  minutes=$1
  shift
  directory="$sandbox/$scenario"
  mkdir "$directory"
  reads_file="$directory/reads"
  stderr_file="$directory/stderr"
  printf '%s\n' "$@" > "$directory/scripted-reads"
  : > "$reads_file"
  status=0
  PATH="$sandbox/bin:$PATH" READS_LOG="$reads_file" SCRIPTED_READS="$directory/scripted-reads" \
    "$wait_for_package_indexed" ConfigDirector.ServerSdk 1.8.0 "$minutes" \
    > "$directory/stdout" 2> "$stderr_file" || status=$?
}

fail() {
  printf 'FAIL %s: %s\n' "$scenario" "$1"
  failures=$((failures + 1))
}

assert_status() {
  [ "$status" -eq "$1" ] || fail "exit status was $status, expected $1 (stderr: $(cat "$stderr_file"))"
}

assert_read_count() {
  actual=$(wc -l < "$reads_file" | tr -d ' ')
  [ "$actual" -eq "$1" ] || fail "read nuget.org $actual time(s), expected $1"
}

pass() {
  printf 'ok   %s\n' "$scenario"
}

scenario=reads_the_index_of_the_lowercased_package_id
run_wait 20 listed listed listed
assert_status 0
actual=$(sort -u "$reads_file")
[ "$actual" = "https://api.nuget.org/v3-flatcontainer/configdirector.serversdk/index.json" ] \
  || fail "read '$actual'"
pass

scenario=returns_once_three_reads_in_a_row_list_the_version
run_wait 20 missing listed listed listed
assert_status 0
assert_read_count 4
pass

scenario=counts_from_zero_again_when_a_read_misses_a_version_listed_before
run_wait 20 listed missing listed listed listed
assert_status 0
assert_read_count 5
pass

scenario=gives_up_once_the_deadline_passes_without_the_version_listed
run_wait 0 missing
assert_status 1
assert_read_count 1
grep -qF "ConfigDirector.ServerSdk 1.8.0 was not indexed by nuget.org within 0 minutes" "$stderr_file" \
  || fail "stderr does not explain the timeout: $(cat "$stderr_file")"
pass

if [ "$failures" -ne 0 ]; then
  printf '\n%s scenario(s) failed\n' "$failures"
  exit 1
fi
