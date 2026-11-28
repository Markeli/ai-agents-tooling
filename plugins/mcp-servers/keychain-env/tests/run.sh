#!/usr/bin/env bash
# Tests of keychain-env. security is a stub (stubs/security) that answers from fixture files, so the tests run on Linux.
# Usage: plugins/mcp-servers/keychain-env/tests/run.sh
set -uo pipefail

tests_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script=$(dirname "$tests_dir")/keychain-env
failures=0
cases=0
current=""

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fail() {
	echo "  FAIL [$current]: $*"
	failures=$((failures + 1))
}

# Starts a case: $1 name. The probe command writes its arguments to $case_dir/args and the variables to $case_dir/env.
begin() {
	current=$1
	cases=$((cases + 1))
	case_dir=$work/$current
	export KEYCHAIN_FIXTURES=$case_dir/keychain
	mkdir -p "$KEYCHAIN_FIXTURES/agent"
	: >"$KEYCHAIN_FIXTURES/calls"
	probe=$case_dir/probe
	cat >"$probe" <<-EOF
		#!/bin/sh
		printf '%s\n' "\$@" >"$case_dir/args"
		printf 'TOKEN=%s\nOTHER=%s\n' "\${TOKEN-unset}" "\${OTHER-unset}" >"$case_dir/env"
		echo probe ran
	EOF
	chmod +x "$probe"
}

# Stores the secret $2 under the keychain item $1
item() { printf '%s\n' "$2" >"$KEYCHAIN_FIXTURES/$1"; }

run() {
	PATH="$tests_dir/stubs:$PATH" "$script" "$@" >"$case_dir/out" 2>"$case_dir/err"
	status=$?
}

expect_status() {
	[ "$status" = "$1" ] || fail "exit status $status, expected $1; stderr: $(cat "$case_dir/err")"
}

# $1 file, then the exact lines it must have
expect_file() {
	local file=$1 expected actual
	shift
	expected=$(printf '%s\n' "$@")
	actual=$(cat "$file" 2>/dev/null)
	[ "$actual" = "${expected%$'\n'}" ] || fail "$(basename "$file"):
$actual
expected:
$expected"
}

expect_not_in() {
	! grep -qF -- "$2" "$1" 2>/dev/null || fail "$(basename "$1") unexpectedly has '$2'"
}

expect_usage() {
	expect_status 2
	grep -q '^usage: keychain-env VAR=item' "$case_dir/err" || fail "no usage on stderr: $(cat "$case_dir/err")"
	[ ! -s "$KEYCHAIN_FIXTURES/calls" ] || fail "security ran: $(cat "$KEYCHAIN_FIXTURES/calls")"
	[ ! -e "$case_dir/args" ] || fail "the command ran"
}

# --- reading items ---

begin one_pair
item agent/one 's3cret one'
run TOKEN=agent/one -- "$probe" a 'b c' -- VAR=x ''
expect_status 0
expect_file "$case_dir/out" "probe ran"
expect_file "$case_dir/args" a 'b c' -- VAR=x ''
expect_file "$case_dir/env" 'TOKEN=s3cret one' OTHER=unset
expect_file "$KEYCHAIN_FIXTURES/calls" find-generic-password -s agent/one -w --

begin two_pairs
item agent/one 'first'
item agent/two "second \$HOME * 'quoted'"
run TOKEN=agent/one OTHER=agent/two -- "$probe"
expect_status 0
expect_file "$case_dir/env" TOKEN=first "OTHER=second \$HOME * 'quoted'"

begin secret_not_in_arguments_or_output
item agent/one 'very-secret-value'
run TOKEN=agent/one -- "$probe" arg
expect_status 0
expect_not_in "$case_dir/args" very-secret-value
expect_not_in "$case_dir/out" very-secret-value
expect_not_in "$case_dir/err" very-secret-value
expect_not_in "$KEYCHAIN_FIXTURES/calls" very-secret-value

begin missing_item
item agent/one 'first'
run TOKEN=agent/one OTHER=agent/missing -- "$probe"
expect_status 1
expect_file "$case_dir/err" "keychain-env: keychain item agent/missing not found"
[ ! -e "$case_dir/args" ] || fail "the command ran"
expect_not_in "$case_dir/err" first

begin denied_item
echo 51 >"$KEYCHAIN_FIXTURES/agent/one.exit"
run TOKEN=agent/one -- "$probe"
expect_status 1
expect_file "$case_dir/err" "keychain-env: cannot read keychain item agent/one (security exit 51; access denied?)"
[ ! -e "$case_dir/args" ] || fail "the command ran"

begin missing_command_is_the_shells_error
item agent/one 'first'
run TOKEN=agent/one -- "$case_dir/no-such-command"
expect_status 127

# --- usage ---

begin no_arguments
run
expect_usage

begin no_pair
run -- "$probe"
expect_usage

begin no_separator
item agent/one 'first'
run TOKEN=agent/one "$probe"
expect_usage

begin no_command
item agent/one 'first'
run TOKEN=agent/one --
expect_usage

for bad in 1TOKEN=agent/one TO-KEN=agent/one =agent/one 'TO KEN=agent/one' TOKEN= TOKEN; do
	begin "bad_pair_$(printf '%s' "$bad" | tr -c 'A-Za-z0-9' _)"
	item agent/one 'first'
	run "$bad" -- "$probe"
	expect_usage
done

echo "keychain-env: $cases cases, $failures failures"
[ "$failures" = 0 ]
