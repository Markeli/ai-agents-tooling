#!/usr/bin/env bash
# Tests of watch-pr. gh and glab are stubs (stubs/) that answer from fixture files; each case gets its own fixture,
# state and authors files. Usage: skills/claude-code/watch-pr/tests/run.sh
# Each run is a subshell with its own PATH on purpose
# shellcheck disable=SC2030,SC2031
set -uo pipefail

tests_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
script=$(dirname "$tests_dir")/watch-pr
fixtures=$tests_dir/fixtures
failures=0
current=""

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fail() {
	echo "  FAIL [$current]: $*"
	failures=$((failures + 1))
}

# Starts a case: $1 name. Sets FIXTURE, case_dir; the authors file allows the owner on both hosts.
begin() {
	current=$1
	case_dir=$work/$current
	export FIXTURE=$case_dir/fixture
	mkdir -p "$FIXTURE" "$case_dir/state" "$case_dir/cwd"
	printf '# owner\ngithub.com Markeli\ngitlab.com markeli\n' >"$case_dir/authors"
	session=t1
}

# Answers the n-th call of $1 with fixture file $3: given $2 = n
answer() { cp "$fixtures/$3" "$FIXTURE/$1.$2"; }

# Answers the n-th call of $1 with the JSON in $3
answer_json() { printf '%s\n' "$3" >"$FIXTURE/$1.$2"; }

# Runs watch-pr once (or as given) from $case_dir/cwd with the stubs: arguments are passed on
watch() {
	(
		cd "$case_dir/cwd" || exit 1
		export PATH="$tests_dir/stubs:$PATH" WATCH_PR_SLEEP=true
		unset CLAUDE_CODE_SESSION_ID
		"$script" --state-dir "$case_dir/state" --authors "$case_dir/authors" --session "$session" "$@"
	) >"$case_dir/out" 2>"$case_dir/err"
	status=$?
}

# Like watch, with --once
watch_once() { watch --once "$@"; }

expect_status() {
	[ "$status" = "$1" ] || fail "exit status $status, expected $1; stderr: $(cat "$case_dir/err")"
}

# The output has exactly the lines given, in order
expect_out() {
	local expected actual
	expected=$(printf '%s\n' "$@")
	actual=$(cat "$case_dir/out")
	[ "$actual" = "${expected%$'\n'}" ] || fail "output:
$actual
expected:
$expected"
}

expect_no_out() {
	[ ! -s "$case_dir/out" ] || fail "unexpected output: $(cat "$case_dir/out")"
}

expect_in() {
	grep -qF -- "$2" "$1" 2>/dev/null || fail "$(basename "$1") lacks '$2': $(cat "$1" 2>/dev/null)"
}

expect_not_in() {
	! grep -qF -- "$2" "$1" 2>/dev/null || fail "$(basename "$1") unexpectedly has '$2'"
}

gh_url=https://github.com/Markeli/demo/pull/7
gl_url=https://gitlab.com/g/sub/proj/-/merge_requests/3

# --- arguments and target ---

begin url_github
answer graphql 1 github-open.json
watch_once "$gh_url"
expect_status 0
expect_no_out
expect_in "$FIXTURE/calls" 'api graphql'
expect_in "$FIXTURE/calls" 'owner=Markeli'
expect_in "$FIXTURE/calls" 'name=demo'
expect_in "$FIXTURE/calls" 'number=7'

begin url_gitlab_subgroup
answer gitlab-mr 1 gitlab-mr-open.json
answer_json gitlab-discussions 1 '[]'
answer_json gitlab-reviewers 1 '[]'
answer_json gitlab-commit 1 '{"committed_date": "2026-11-14T10:00:00Z"}'
watch_once "$gl_url"
expect_status 0
expect_no_out
expect_in "$FIXTURE/calls" 'projects/g%2Fsub%2Fproj/merge_requests/3'

begin number_uses_origin
git -C "$case_dir/cwd" init -q
git -C "$case_dir/cwd" remote add origin git@github.com:Markeli/demo.git
answer graphql 1 github-open.json
watch_once 7
expect_status 0
expect_in "$FIXTURE/calls" 'name=demo'
expect_in "$FIXTURE/calls" 'number=7'

begin number_uses_https_origin
git -C "$case_dir/cwd" init -q
git -C "$case_dir/cwd" remote add origin https://gitlab.com/g/sub/proj.git
answer gitlab-mr 1 gitlab-mr-open.json
answer_json gitlab-discussions 1 '[]'
answer_json gitlab-reviewers 1 '[]'
answer_json gitlab-commit 1 '{"committed_date": "2026-11-14T10:00:00Z"}'
watch_once 3
expect_status 0
expect_in "$FIXTURE/calls" 'projects/g%2Fsub%2Fproj/merge_requests/3'

begin current_branch
git -C "$case_dir/cwd" init -q
git -C "$case_dir/cwd" remote add origin git@github.com:Markeli/demo.git
answer_json pr-view 1 '9'
answer graphql 1 github-open.json
watch_once
expect_status 0
expect_in "$FIXTURE/calls" 'pr view'
expect_in "$FIXTURE/calls" 'number=9'

begin current_branch_without_pr
git -C "$case_dir/cwd" init -q
git -C "$case_dir/cwd" remote add origin git@github.com:Markeli/demo.git
echo 1 >"$FIXTURE/pr-view.exit"
watch_once
expect_status 2
expect_in "$case_dir/err" 'no open PR/MR for the current branch'
expect_not_in "$FIXTURE/calls" 'api graphql'

begin no_repo_no_url
watch_once
expect_status 2
expect_in "$case_dir/err" 'pass a PR/MR URL'

begin unknown_host
watch_once https://bitbucket.org/x/y/pull-requests/1
expect_status 2
expect_in "$case_dir/err" 'bitbucket.org'

begin no_session
(
	cd "$case_dir/cwd" || exit 1
	export PATH="$tests_dir/stubs:$PATH"
	unset CLAUDE_CODE_SESSION_ID
	"$script" --once --state-dir "$case_dir/state" --authors "$case_dir/authors" "$gh_url"
) >"$case_dir/out" 2>"$case_dir/err"
status=$?
expect_status 2
expect_in "$case_dir/err" '--session'

begin bad_option
watch_once --interval abc "$gh_url"
expect_status 2
expect_in "$case_dir/err" '--interval'

begin no_authors_for_host
printf 'gitlab.com markeli\n' >"$case_dir/authors"
watch_once "$gh_url"
expect_status 2
expect_in "$case_dir/err" 'no allow-listed authors for github.com'

if [ "$failures" -gt 0 ]; then
	echo "$failures check(s) failed"
	exit 1
fi
echo "All watch-pr tests passed"
