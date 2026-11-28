#!/usr/bin/env bash
# Tests of grafana-mcp. A temporary HOME holds ~/.local/bin with keychain-env (the real one), the security stub of
# keychain-env's tests and the mcp-grafana stub (stubs/), which prints its arguments and environment.
# Usage: plugins/mcp-servers/grafana/tests/run.sh
set -uo pipefail

tests_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
servers=$(cd "$tests_dir/../.." && pwd)
script=$servers/grafana/grafana-mcp
failures=0
cases=0
current=""

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fail() {
	echo "  FAIL [$current]: $*"
	failures=$((failures + 1))
}

# Starts a case: $1 name; the keychain has agent/personal-grafana
begin() {
	current=$1
	cases=$((cases + 1))
	case_dir=$work/$current
	home=$case_dir/home
	mkdir -p "$home/.local/bin" "$case_dir/keychain/agent"
	ln -s "$servers/keychain-env/keychain-env" "$home/.local/bin/keychain-env"
	ln -s "$servers/keychain-env/tests/stubs/security" "$home/.local/bin/security"
	ln -s "$tests_dir/stubs/mcp-grafana" "$home/.local/bin/mcp-grafana"
	printf 'glsa_token\n' >"$case_dir/keychain/agent/personal-grafana"
}

# Runs grafana-mcp with a bare PATH, as a process started by the sbx gateway may have
run() {
	env -i HOME="$home" PATH=/usr/bin:/bin KEYCHAIN_FIXTURES="$case_dir/keychain" \
		"$script" "$@" >"$case_dir/out" 2>"$case_dir/err"
	status=$?
}

expect_status() {
	[ "$status" = "$1" ] || fail "exit status $status, expected $1; stderr: $(cat "$case_dir/err")"
}

expect_out() {
	local expected actual
	expected=$(printf '%s\n' "$@")
	actual=$(cat "$case_dir/out")
	[ "$actual" = "${expected%$'\n'}" ] || fail "output:
$actual
expected:
$expected"
}

expect_usage() {
	expect_status 2
	[ "$(cat "$case_dir/err")" = "usage: grafana-mcp <grafana url> <keychain item>" ] ||
		fail "stderr: $(cat "$case_dir/err")"
	[ ! -s "$case_dir/out" ] || fail "mcp-grafana ran: $(cat "$case_dir/out")"
}

begin runs_mcp_grafana_with_the_tool_policy_url_and_token
run https://grafana.example.com agent/personal-grafana
expect_status 0
expect_out \
	"arg: --disable-write" \
	"arg: --enabled-tools" \
	"arg: search,datasource,prometheus,loki,dashboard,folder,navigation,alerting,annotations,snapshot" \
	"arg: --enable-write-tools" \
	"arg: update_dashboard,create_folder,alerting_rules_write,alerting_manage_routing,alerting_routing_write,alerting_silences_write,create_annotation,update_annotation,delete_annotation,create_snapshot,delete_snapshot" \
	"GRAFANA_URL=https://grafana.example.com" \
	"GRAFANA_SERVICE_ACCOUNT_TOKEN=glsa_token"
[ ! -s "$case_dir/err" ] || fail "stderr: $(cat "$case_dir/err")"

begin missing_item_stops_before_mcp_grafana
run https://grafana.example.com agent/other
expect_status 1
[ "$(cat "$case_dir/err")" = "keychain-env: keychain item agent/other not found" ] || fail "stderr: $(cat "$case_dir/err")"
[ ! -s "$case_dir/out" ] || fail "mcp-grafana ran: $(cat "$case_dir/out")"

begin missing_mcp_grafana_is_reported
rm "$home/.local/bin/mcp-grafana"
run https://grafana.example.com agent/personal-grafana
expect_status 127
grep -q 'mcp-grafana' "$case_dir/err" || fail "stderr does not name mcp-grafana: $(cat "$case_dir/err")"

begin usage_without_arguments
run
expect_usage

begin usage_with_one_argument
run https://grafana.example.com
expect_usage

begin usage_with_three_arguments
run https://grafana.example.com agent/personal-grafana extra
expect_usage

begin usage_with_an_empty_argument
run "" agent/personal-grafana
expect_usage

echo "grafana-mcp: $cases cases, $failures failures"
[ "$failures" = 0 ]
