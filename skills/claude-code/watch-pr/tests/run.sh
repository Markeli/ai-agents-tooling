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
		timeout 30 "$script" --state-dir "$case_dir/state" --authors "$case_dir/authors" --session "$session" "$@"
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

# GitHub GraphQL nodes for the fixtures: a comment node ($1 id, $2 login, $3 body, $4 time)
gh_comment() {
	jq -cn --argjson id "$1" --arg login "$2" --arg body "$3" --arg at "$4" \
		'{databaseId: $id, author: {login: $login}, url: "https://github.com/Markeli/demo/pull/7#c\($id)",
		  body: $body, createdAt: $at}'
}

# A review node: $1 id, $2 login, $3 state, $4 body, $5 time
gh_review() {
	jq -cn --argjson id "$1" --arg login "$2" --arg state "$3" --arg body "$4" --arg at "$5" \
		'{databaseId: $id, author: {login: $login}, url: "https://github.com/Markeli/demo/pull/7#r\($id)",
		  state: $state, body: $body, submittedAt: $at}'
}

# A check node: $1 name, $2 conclusion
gh_check() {
	jq -cn --arg name "$1" --arg conclusion "$2" \
		'{__typename: "CheckRun", name: $name, conclusion: $conclusion, detailsUrl: "https://ci.example/\($name)"}'
}

# Writes the n-th GraphQL answer. $1 n; then key=value pairs: state (OPEN), head (a1), comments, threads (an array of
# arrays of comment nodes), reviews, checks (JSON arrays, default []), remaining (4990)
gh_answer() {
	local n=$1 state=OPEN head=a1 comments='[]' threads='[]' reviews='[]' checks='[]' remaining=4990
	shift
	local pair
	for pair in "$@"; do
		local "$pair"
	done
	jq -n --arg state "$state" --arg head "$head" --argjson comments "$comments" --argjson threads "$threads" \
		--argjson reviews "$reviews" --argjson checks "$checks" --argjson remaining "$remaining" '
		{data: {rateLimit: {limit: 5000, remaining: $remaining, resetAt: "2099-01-01T00:00:00Z"},
		 repository: {pullRequest: {url: "https://github.com/Markeli/demo/pull/7", state: $state,
		  comments: {nodes: $comments},
		  reviewThreads: {nodes: [$threads[] | {comments: {nodes: .}}]},
		  reviews: {nodes: $reviews},
		  commits: {nodes: [{commit: {oid: $head, committedDate: "2026-11-14T10:00:00Z",
		   statusCheckRollup: {contexts: {nodes: $checks}}}}]}}}}}' >"$FIXTURE/graphql.$n"
}

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

# --- GitHub events ---

ref='Markeli/demo#7'
owner_old=$(gh_comment 11 Markeli 'Old thread start' 2026-11-14T09:00:00Z)
bot_reply=$(gh_comment 12 markeli-agent 'Fixed.' 2026-11-14T09:30:00Z)

begin first_poll_on_fresh_pr_is_silent
gh_answer 1
watch_once "$gh_url"
expect_status 0
expect_no_out
[ -s "$case_dir/state/t1/github.com/Markeli/demo/7.json" ] || fail "no state file"

begin new_items_report_once
gh_answer 1 threads="[[$owner_old,$bot_reply]]"
gh_answer 2 \
	comments="[$(gh_comment 21 Markeli 'Top-level remark' 2026-11-14T11:00:00Z)]" \
	threads="[[$owner_old,$bot_reply,$(gh_comment 22 Markeli 'Still wrong' 2026-11-14T11:02:00Z)],
		[$(gh_comment 23 Markeli 'New thread' 2026-11-14T11:01:00Z)]]" \
	reviews="[$(gh_review 24 Markeli CHANGES_REQUESTED '' 2026-11-14T11:03:00Z)]"
watch_once "$gh_url"
expect_no_out
watch_once "$gh_url"
expect_status 0
expect_out \
	"watch-pr $ref comment Markeli https://github.com/Markeli/demo/pull/7#c21 Top-level remark" \
	"watch-pr $ref thread Markeli https://github.com/Markeli/demo/pull/7#c23 New thread" \
	"watch-pr $ref reply Markeli https://github.com/Markeli/demo/pull/7#c22 Still wrong" \
	"watch-pr $ref review Markeli https://github.com/Markeli/demo/pull/7#r24 changes_requested"
watch_once "$gh_url"
expect_no_out

begin reviews_filtered
gh_answer 1
gh_answer 2 reviews="[$(gh_review 31 Markeli COMMENTED '' 2026-11-14T11:00:00Z),
	$(gh_review 32 Markeli APPROVED '' 2026-11-14T11:01:00Z),
	$(gh_review 33 Markeli APPROVED 'Ship it' 2026-11-14T11:02:00Z)]"
watch_once "$gh_url"
watch_once "$gh_url"
expect_out "watch-pr $ref review Markeli https://github.com/Markeli/demo/pull/7#r33 approved: Ship it"

begin two_replies_in_one_poll
gh_answer 1 threads="[[$owner_old,$bot_reply]]"
gh_answer 2 threads="[[$owner_old,$bot_reply,$(gh_comment 42 Markeli 'Second' 2026-11-14T11:05:00Z),
	$(gh_comment 41 Markeli 'First' 2026-11-14T11:04:00Z)]]"
watch_once "$gh_url"
watch_once "$gh_url"
expect_out \
	"watch-pr $ref reply Markeli https://github.com/Markeli/demo/pull/7#c41 First" \
	"watch-pr $ref reply Markeli https://github.com/Markeli/demo/pull/7#c42 Second"

begin others_ignored
gh_answer 1
gh_answer 2 comments="[$(gh_comment 51 github-advanced-security[bot] 'Alert' 2026-11-14T11:00:00Z),
	$(gh_comment 52 someone 'Drive-by' 2026-11-14T11:01:00Z)]" \
	threads="[[$(gh_comment 53 copilot-pull-request-reviewer[bot] 'Nit' 2026-11-14T11:02:00Z)]]"
watch_once "$gh_url"
watch_once "$gh_url"
expect_no_out
rm -rf "$case_dir/state" "$FIXTURE"/.count-*
printf 'github.com Markeli\ngithub.com Copilot-Pull-Request-Reviewer[bot]\n' >"$case_dir/authors"
watch_once "$gh_url"
watch_once "$gh_url"
expect_out "watch-pr $ref thread copilot-pull-request-reviewer[bot] https://github.com/Markeli/demo/pull/7#c53 Nit"

begin own_marker_ignored
printf 'github.com Markeli\ngithub.com markeli-agent\n' >"$case_dir/authors"
gh_answer 1 threads="[[$owner_old]]"
gh_answer 2 threads="[[$owner_old,
	$(gh_comment 61 markeli-agent $'Done.\n\n<!-- watch-pr session=t1 -->' 2026-11-14T11:00:00Z),
	$(gh_comment 62 markeli-agent $'Please rename.\n<!-- watch-pr session=t2 -->' 2026-11-14T11:01:00Z),
	$(gh_comment 63 markeli-agent 'No marker' 2026-11-14T11:02:00Z)]]"
watch_once "$gh_url"
watch_once "$gh_url"
expect_out "watch-pr $ref reply markeli-agent https://github.com/Markeli/demo/pull/7#c62 Please rename."

begin ci_failed_once_per_head
gh_answer 1
gh_answer 2 checks="[$(gh_check build FAILURE),$(gh_check lint SUCCESS),$(gh_check docs CANCELLED)]"
gh_answer 3 checks="[$(gh_check lint SUCCESS)]"
gh_answer 4 checks="[$(gh_check build FAILURE)]"
gh_answer 5 head=b2 checks="[$(gh_check build TIMED_OUT)]"
watch_once "$gh_url"
watch_once "$gh_url"
expect_out "watch-pr $ref ci-failed - https://ci.example/build build"
watch_once "$gh_url"
expect_no_out
watch_once "$gh_url"
expect_no_out
watch_once "$gh_url"
expect_out "watch-pr $ref ci-failed - https://ci.example/build build"

begin merged_ends
gh_answer 1
gh_answer 2 state=MERGED
watch_once "$gh_url"
watch "$gh_url"
expect_status 0
expect_out "watch-pr $ref merged - https://github.com/Markeli/demo/pull/7 -"
[ ! -e "$case_dir/state/t1/github.com/Markeli/demo/7.json" ] || fail "state file kept after the merge"

begin closed_ends
gh_answer 1 state=CLOSED
watch "$gh_url"
expect_status 0
expect_out "watch-pr $ref closed - https://github.com/Markeli/demo/pull/7 -"

begin body_is_one_safe_line
gh_answer 1
long=$(printf 'x%.0s' {1..300})
# The $(...) is the comment's text, which must never run
# shellcheck disable=SC2016
gh_answer 2 comments="[$(gh_comment 71 Markeli "$(printf 'a\n\tb $(touch %s/pwned) "q" %s' "$case_dir" "$long")" \
	2026-11-14T11:00:00Z)]"
watch_once "$gh_url"
watch_once "$gh_url"
[ "$(wc -l <"$case_dir/out")" = 1 ] || fail "not one line: $(cat "$case_dir/out")"
excerpt=$(cut -d' ' -f6- "$case_dir/out")
[ "${#excerpt}" = 200 ] || fail "excerpt is ${#excerpt} characters: $excerpt"
expect_in "$case_dir/out" "a b \$(touch $case_dir/pwned) \"q\" x"
[ ! -e "$case_dir/pwned" ] || fail "the body was evaluated"

if [ "$failures" -gt 0 ]; then
	echo "$failures check(s) failed"
	exit 1
fi
echo "All watch-pr tests passed"
