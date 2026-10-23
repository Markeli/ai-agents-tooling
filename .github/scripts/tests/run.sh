#!/usr/bin/env bash
# Tests of the sandbox-image scripts. Each case builds a throwaway git repository (base commit, then the change under
# test as HEAD, so HEAD^1 is the base as in a PR merge commit) and fakes GHCR and GitHub with the stubs in stubs/.
# Usage: .github/scripts/tests/run.sh
# Each script runs in a subshell with its own environment on purpose; backticks in expected text are literal
# shellcheck disable=SC2030,SC2031,SC2016
set -uo pipefail

tests_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
scripts=$(dirname "$tests_dir")
image=ghcr.io/markeli/claude-sandbox
failures=0
current=""

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

fail() {
	echo "  FAIL [$current]: $*"
	failures=$((failures + 1))
}

# Starts a case: $1 name, $2 base CHANGELOG, $3 head CHANGELOG, $4 "image" to also change the Dockerfile in head
begin() {
	current=$1
	export FIXTURE=$work/$current/fixture
	repo=$work/$current/repo
	mkdir -p "$FIXTURE" "$repo/sandbox/image"
	git -C "$repo" init -q
	git -C "$repo" config user.email test@example.com
	git -C "$repo" config user.name test
	printf '%s' "$2" >"$repo/sandbox/image/CHANGELOG.md"
	echo 'FROM scratch' >"$repo/sandbox/image/Dockerfile"
	git -C "$repo" add -A && git -C "$repo" commit -q -m base
	printf '%s' "$3" >"$repo/sandbox/image/CHANGELOG.md"
	if [ "${4:-}" = image ]; then
		echo 'LABEL changed=true' >>"$repo/sandbox/image/Dockerfile"
	fi
	echo change >"$repo/README.md"
	git -C "$repo" add -A && git -C "$repo" commit -q -m change
	tree=$(git -C "$repo" rev-parse HEAD:sandbox/image)
	tree_tag=$image:tree-$tree
}

# Publishes a tag in the fake registry: $1 tag ("tree" for the case's tree tag), $2 digest, $3 version label
registry() {
	local tag=$1
	[ "$tag" = tree ] && tag=tree-$tree
	echo "$image:$tag $2 ${3:-}" >>"$FIXTURE/registry"
}

# Runs the plan for event $1 on ref $2 (base branch for PRs); extra VAR=value arguments override the environment
plan() {
	local event=$1 ref=$2
	shift 2
	out=$work/$current/out
	(
		cd "$repo" || exit 1
		export PATH="$tests_dir/stubs:$PATH" IMAGE=$image EVENT=$event DEFAULT_BRANCH=main SHA=main-sha \
			HEAD_SHA=head-sha SAME_REPO=true RELEASE_CHECK=$work/$current/release-check.md GITHUB_OUTPUT=$work/$current/out
		if [ "$event" = pull_request ]; then
			export REF=refs/pull/1/merge BASE_REF=$ref
		else
			export REF=refs/heads/$ref BASE_REF=""
		fi
		for assignment in "$@"; do
			export "${assignment?}"
		done
		"$scripts/sandbox-image-plan.sh"
	) >"$work/$current/log" 2>&1
	status=$?
}

# Value of output $1 (multi-line outputs joined with spaces)
output() {
	awk -v key="$1" '
		index($0, key "<<") == 1 { multi = 1; next }
		multi && $0 == "EOF" { exit }
		multi { printf "%s%s", sep, $0; sep = " "; next }
		index($0, key "=") == 1 { print substr($0, length(key) + 2); exit }
	' "$out"
}

expect() {
	local actual
	actual=$(output "$1")
	[ "$actual" = "$2" ] || fail "$1: expected '$2', got '$actual'"
}

expect_status() {
	[ "$status" = "$1" ] || fail "exit status $status, expected $1; log: $(cat "$work/$current/log")"
}

expect_in() {
	grep -qF -- "$2" "$1" 2>/dev/null || fail "$(basename "$1") lacks '$2': $(cat "$1" 2>/dev/null)"
}

expect_not_in() {
	! grep -qF -- "$2" "$1" 2>/dev/null || fail "$(basename "$1") unexpectedly has '$2'"
}

released() { printf '# Changelog\n\n## [%s] - 2026-10-01\n\n### Added\n\n- Things.\n' "$1"; }
unreleased() { printf '# Changelog\n\n## [Unreleased]\n\n### Changed\n\n- %s\n\n' "$2"; [ -z "$1" ] || printf '## [%s] - 2026-10-01\n\n- Things.\n' "$1"; }

# --- plan: pull requests ---

begin pr-unreleased-entry "$(released 1.2.0)" "$(unreleased 1.2.0 'New tool.')" image
registry 1.2.0 sha256:old 1.2.0
plan pull_request main
expect_status 0
expect build true
expect push true
expect version "1.2.0+unreleased.${tree:0:12}"
expect revision head-sha
expect publish false
expect changed true
expect_in "$work/$current/release-check.md" '<!-- release-check -->'
expect_in "$work/$current/release-check.md" '📝 The image changes, and the change is under'

begin pr-no-entry "$(released 1.2.0)" "$(released 1.2.0)" image
registry 1.2.0 sha256:old 1.2.0
plan pull_request main
expect_status 0
expect version "1.2.0+unreleased.${tree:0:12}"
expect_in "$work/$current/release-check.md" '⚠️ `sandbox/image` changed without a CHANGELOG entry'
expect_in "$work/$current/log" '::warning file=sandbox/image/Dockerfile,title=No CHANGELOG entry'

begin pr-release "$(unreleased 1.2.0 'New tool.')" "$(released 1.3.0)"
registry 1.2.0 sha256:old 1.2.0
plan pull_request main
expect_status 0
expect version 1.3.0
expect_in "$work/$current/release-check.md" '🚀 The merge releases **1.3.0**.'

begin pr-version-already-published "$(released 1.3.0)" "$(printf '%s\n\n- Late addition.\n' "$(released 1.3.0)")"
registry 1.3.0 sha256:old 1.3.0
plan pull_request main
expect_in "$work/$current/release-check.md" '⚠️ The CHANGELOG changed, but nothing will be released'

begin pr-workflow-only "$(released 1.2.0)" "$(released 1.2.0)"
registry 1.2.0 sha256:old 1.2.0
registry tree sha256:old 1.2.0
plan pull_request main
expect build false
expect changed false
expect_in "$work/$current/release-check.md" 'No image changes.'

begin pr-fork "$(released 1.2.0)" "$(unreleased 1.2.0 'New tool.')" image
registry 1.2.0 sha256:old 1.2.0
plan pull_request main SAME_REPO=false
expect_status 0
expect build true
expect push false
expect_in "$work/$current/release-check.md" '📝'

# --- plan: pushes ---

begin main-unreleased-only "$(released 1.2.0)" "$(unreleased 1.2.0 'New tool.')" image
registry 1.2.0 sha256:old 1.2.0
registry latest sha256:old 1.2.0
plan push main
expect_status 0
expect publish false
expect release ""
expect publish_tags ""
expect revision main-sha

begin main-release "$(unreleased 1.2.0 'New tool.')" "$(released 1.3.0)"
registry 1.2.0 sha256:old 1.2.0
registry 1.2 sha256:old 1.2.0
registry 1 sha256:old 1.2.0
registry latest sha256:old 1.2.0
registry tree sha256:new 1.3.0
plan push main
expect_status 0
expect build false
expect publish true
expect release 1.3.0
expect latest true
expect publish_tags "$image:1.3.0 $image:1.3 $image:1 $image:latest"

begin main-first-release-over-unversioned-latest "$(unreleased '' 'Baseline.')" "$(released 1.0.0)"
registry latest sha256:legacy
plan push main
expect publish true
expect build true
expect version 1.0.0
expect publish_tags "$image:1.0.0 $image:1.0 $image:1 $image:latest"

begin main-already-published "$(released 1.3.0)" "$(released 1.3.0)"
for tag in 1.3.0 1.3 1 latest tree; do registry "$tag" sha256:new 1.3.0; done
plan push main
expect_status 0
expect publish true
expect publish_tags ""

begin main-published-other-tree "$(released 1.3.0)" "$(released 1.3.0)" image
registry 1.3.0 sha256:old 1.3.0
registry tree sha256:new 1.3.0+unreleased.x
plan push main
expect_status 0
expect publish false
expect_in "$work/$current/log" 'already published from another tree'
expect_in "$work/$current/log" '::warning title=Unreleased image changes'

begin main-release-superseded "$(released 1.2.0)" "$(unreleased 1.3.0 'After the release.')"
registry 1.2.0 sha256:old 1.2.0
plan push main
expect publish false
expect version "1.3.0+unreleased.${tree:0:12}"
expect_in "$work/$current/log" 'has Unreleased changes on top of it'

begin release-branch "$(unreleased 1.4.0 'Fix.')" "$(released 1.4.1)"
registry 1.4.0 sha256:a 1.4.0
registry 1.4 sha256:a 1.4.0
registry 1 sha256:a 1.4.0
registry latest sha256:b 2.0.0
registry tree sha256:c 1.4.1
plan push release/1.x
expect_status 0
expect publish true
expect latest false
expect publish_tags "$image:1.4.1 $image:1.4 $image:1"

begin release-branch-older-minor "$(released 1.3.5)" "$(released 1.3.6)"
registry 1.4 sha256:a 1.4.0
registry 1 sha256:a 1.4.0
plan push release/1.x
expect publish_tags "$image:1.3.6 $image:1.3"

begin release-branch-major-mismatch "$(released 2.0.0)" "$(released 2.0.1)"
plan push release/1.x
expect_status 1
expect_in "$work/$current/log" 'does not belong on release/1.x'

begin malformed-heading "$(released 1.2.0)" "$(printf '## [1.3] - 2026-10-01\n')"
plan push main
expect_status 1
expect_in "$work/$current/log" "malformed version heading '## [1.3] - 2026-10-01'"

begin no-version-yet "" "$(unreleased '' 'Baseline.')" image
plan push main
expect_status 0
expect publish false
expect version "0.0.0+unreleased.${tree:0:12}"

begin registry-error "$(released 1.2.0)" "$(released 1.2.0)"
echo "$image:1.2.0" >"$FIXTURE/errors"
plan push main
expect_status 1
expect_in "$work/$current/log" '401 Unauthorized'

begin label-read-error "$(unreleased 1.4.0 'Fix.')" "$(released 1.4.1)"
registry 1.4 sha256:a 1.4.0
registry 1 sha256:a 1.4.0
registry tree sha256:c 1.4.1
echo "$image:1" >"$FIXTURE/format-errors"
plan push release/1.x
expect_status 1
expect_in "$work/$current/log" '401 Unauthorized'

begin digest-read-error "$(released 1.3.0)" "$(released 1.3.0)"
registry 1.3.0 sha256:new 1.3.0
registry tree sha256:new 1.3.0
echo "$image:1.3.0" >"$FIXTURE/format-errors"
plan push main
expect_status 1

begin main-rerun-older-release "$(released 1.3.0)" "$(released 1.3.0)"
for tag in 1.3.0 1.3 tree; do registry "$tag" sha256:new 1.3.0; done
registry 1 sha256:newer 1.4.0
registry latest sha256:newer 1.4.0
plan push main
expect_status 0
expect publish true
expect latest false
expect publish_tags ""

# --- publish ---

publish() {
	(
		cd "$repo" || exit 1
		export PATH="$tests_dir/stubs:$PATH" IMAGE=$image TREE_TAG=$tree_tag SHA=main-sha \
			INVENTORY=$work/$current/inventory.tsv LATEST=true
		for assignment in "$@"; do
			export "${assignment?}"
		done
		"$scripts/sandbox-image-publish.sh"
	) >"$work/$current/log" 2>&1
	status=$?
}

begin publish-release "$(unreleased 1.2.0 'New tool.')" "$(printf '# Changelog\n\n## [1.3.0] - 2026-11-02\n\n### Added\n\n- New tool.\n\n## [1.2.0] - 2026-10-01\n\n- Things.\n')"
registry tree sha256:new 1.3.0
printf 'Claude Code\t2.1.309\nyq\t4.54.1\n' >"$FIXTURE/inv-claude-sandbox.v1.2.0.tsv"
printf 'claude-sandbox.v1.2.0 inventory.tsv\nclaude-sandbox.v1.1.0 inventory.tsv\n' >"$FIXTURE/releases"
printf 'Claude Code\t2.1.320\nyq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.3.0 "PUBLISH_TAGS=$image:1.3.0"$'\n'"$image:latest"
expect_status 0
expect_in "$FIXTURE/calls" "imagetools create --tag $image:1.3.0 --tag $image:latest $tree_tag"
expect_in "$FIXTURE/calls" "release create claude-sandbox.v1.3.0 --target main-sha --title claude-sandbox\\ v1.3.0"
expect_in "$FIXTURE/calls" '--latest '
expect_in "$FIXTURE/notes.md" '- New tool.'
expect_not_in "$FIXTURE/notes.md" 'Things.'
expect_in "$FIXTURE/notes.md" '| Claude Code | 2.1.309 | 2.1.320 |'
expect_not_in "$FIXTURE/notes.md" 'yq'

begin publish-maintenance "$(released 1.4.0)" "$(released 1.4.1)"
registry tree sha256:c 1.4.1
printf 'claude-sandbox.v2.0.0 inventory.tsv\nclaude-sandbox.v1.4.0\n' >"$FIXTURE/releases"
printf 'yq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.4.1 "PUBLISH_TAGS=$image:1.4.1" LATEST=false
expect_status 0
expect_in "$FIXTURE/calls" '--latest=false'
expect_in "$FIXTURE/notes.md" 'No earlier release with an inventory'

begin publish-completes-missing-release "$(released 1.3.0)" "$(released 1.3.0)"
registry 1.3.0 sha256:new 1.3.0
registry tree sha256:new 1.3.0
printf 'yq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.3.0 PUBLISH_TAGS=""
expect_status 0
expect_not_in "$FIXTURE/calls" 'imagetools create'
expect_in "$FIXTURE/calls" 'release create claude-sandbox.v1.3.0'

begin publish-release-exists "$(released 1.3.0)" "$(released 1.3.0)"
registry 1.3.0 sha256:new 1.3.0
registry tree sha256:new 1.3.0
echo 'claude-sandbox.v1.3.0 inventory.tsv' >"$FIXTURE/releases"
printf 'yq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.3.0 PUBLISH_TAGS=""
expect_status 0
[ ! -e "$FIXTURE/calls" ] || fail "nothing should be written: $(cat "$FIXTURE/calls")"

begin publish-refuses-overwrite "$(released 1.3.0)" "$(released 1.3.0)"
registry 1.3.0 sha256:other 1.3.0
registry tree sha256:new 1.3.0
printf 'yq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.3.0 "PUBLISH_TAGS=$image:1.3.0"
expect_status 1
expect_in "$work/$current/log" 'refusing to overwrite'
[ ! -e "$FIXTURE/calls" ] || fail "nothing should be written: $(cat "$FIXTURE/calls")"

begin publish-digest-error "$(released 1.3.0)" "$(released 1.3.0)"
registry 1.3.0 sha256:new 1.3.0
registry tree sha256:new 1.3.0
echo "$image:1.3.0" >"$FIXTURE/format-errors"
printf 'yq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.3.0 "PUBLISH_TAGS=$image:1.3.0"
expect_status 1
[ ! -e "$FIXTURE/calls" ] || fail "nothing should be written: $(cat "$FIXTURE/calls")"

begin publish-tag-moved-ahead "$(released 1.4.0)" "$(released 1.4.1)"
registry tree sha256:c 1.4.1
registry 1 sha256:d 1.5.0
registry latest sha256:e 2.0.0
printf 'yq\t4.54.1\n' >"$work/$current/inventory.tsv"
publish RELEASE=1.4.1 "PUBLISH_TAGS=$image:1.4.1"$'\n'"$image:1.4"$'\n'"$image:1"$'\n'"$image:latest"
expect_status 0
expect_in "$FIXTURE/calls" "imagetools create --tag $image:1.4.1 --tag $image:1.4 $tree_tag"
expect_in "$work/$current/log" "Not moving $image:1:"
expect_in "$FIXTURE/calls" '--latest=false'

# --- inventory diff ---

diff_case() {
	current=$1
	mkdir -p "$work/$current"
	printf '%b' "$2" >"$work/$current/old.tsv"
	printf '%b' "$3" >"$work/$current/new.tsv"
	"$scripts/sandbox-image-inventory-diff.sh" "$work/$current/old.tsv" "$work/$current/new.tsv" 1.2.0 'This PR' \
		>"$work/$current/diff.md"
}

diff_case diff-changes 'a\t1\nb\t2\nremoved\t3\n' 'a\t1\nb\t9\nadded\t4\n'
expect_in "$work/$current/diff.md" '| Item | 1.2.0 | This PR |'
expect_in "$work/$current/diff.md" '| b | 2 | 9 |'
expect_in "$work/$current/diff.md" '| added | — | 4 |'
expect_in "$work/$current/diff.md" '| removed | 3 | — |'
expect_not_in "$work/$current/diff.md" '| a |'

diff_case diff-none 'a\t1\n' 'a\t1\n'
expect_in "$work/$current/diff.md" 'No toolchain changes since 1.2.0.'

diff_case diff-empty-old '' 'a\t1\n'
expect_in "$work/$current/diff.md" '| a | — | 1 |'

current=diff-no-previous
mkdir -p "$work/$current"
printf 'a\t1\n' >"$work/$current/new.tsv"
"$scripts/sandbox-image-inventory-diff.sh" - "$work/$current/new.tsv" "" 1.0.0 >"$work/$current/diff.md"
expect_in "$work/$current/diff.md" 'No earlier release with an inventory'
expect_in "$work/$current/diff.md" '| a | 1 |'

# Diff target: the highest release overall for main, the highest of the major for release/<major>.x
current=previous-release
export FIXTURE=$work/$current
mkdir -p "$FIXTURE"
printf 'claude-sandbox.v2.1.0\nclaude-sandbox.v1.10.0\nclaude-sandbox.v1.9.3\nother.v9.0.0\n' >"$FIXTURE/releases"
for check in ":2.1.0" "1:1.10.0" "2.1.0::1.10.0" "1.10.0:1:1.9.3"; do
	IFS=: read -r below major want <<<"$check"
	if [ -z "$want" ]; then want=$major major=$below below=""; fi
	got=$(PATH="$tests_dir/stubs:$PATH" bash -c '. "$1"; previous_release "$2" "$3"' _ "$scripts/sandbox-image-lib.sh" "$below" "$major")
	[ "$got" = "$want" ] || fail "previous_release '$below' '$major': expected $want, got $got"
done

# --- cleanup ---

cleanup() {
	(
		NOW=$(date -d 2026-10-31 +%s)
		export NOW PATH="$tests_dir/stubs:$PATH" OWNER=Markeli PACKAGE=claude-sandbox IMAGE=$image \
			GITHUB_STEP_SUMMARY=$work/$current/summary.md
		for assignment in "$@"; do
			export "${assignment?}"
		done
		"$scripts/sandbox-image-cleanup.sh"
	) >"$work/$current/log" 2>&1
	status=$?
}

cleanup_case() {
	current=$1
	export FIXTURE=$work/$current/fixture
	mkdir -p "$FIXTURE"
	{
		printf '1\tsha256:oldtree\t2026-09-01T00:00:00Z\ttree-aaa\n'
		printf '2\tsha256:oldorphan\t2026-09-01T00:00:00Z\t\n'
		printf '3\tsha256:newtree\t2026-10-25T00:00:00Z\ttree-bbb\n'
		printf '4\tsha256:release\t2026-09-01T00:00:00Z\t1.0.0,1.0,1,latest,tree-ccc\n'
		printf '5\tsha256:releasechild\t2026-09-01T00:00:00Z\t\n'
		printf '6\tsha256:legacy\t2026-08-01T00:00:00Z\tsha-1234567\n'
		printf '7\tsha256:newtreechild\t2026-09-01T00:00:00Z\t\n'
	} >"$FIXTURE/versions.tsv"
	printf 'sha256:release sha256:releasechild\nsha256:newtree sha256:newtreechild\n' >"$FIXTURE/children"
}

cleanup_case cleanup-deletes-stale
cleanup
expect_status 0
expect_in "$FIXTURE/calls" 'api DELETE /users/Markeli/packages/container/claude-sandbox/versions/1'
expect_in "$FIXTURE/calls" 'api DELETE /users/Markeli/packages/container/claude-sandbox/versions/2'
for id in 3 4 5 6 7; do
	expect_not_in "$FIXTURE/calls" "versions/$id"
done
expect_in "$work/$current/summary.md" '2 of 4 stale versions were deleted; 3 versions kept.'

cleanup_case cleanup-dry-run
cleanup DRY_RUN=true
expect_status 0
[ ! -e "$FIXTURE/calls" ] || fail "dry run deleted: $(cat "$FIXTURE/calls")"
expect_in "$work/$current/summary.md" 'would delete'

cleanup_case cleanup-forbidden
touch "$FIXTURE/forbidden"
cleanup
expect_status 1
expect_in "$work/$current/log" 'admin role on the package'

cleanup_case cleanup-released-since-listing
echo '1.3.0,1.3,tree-aaa' >"$FIXTURE/tags-1"
cleanup
expect_status 0
expect_not_in "$FIXTURE/calls" 'versions/1'
expect_in "$FIXTURE/calls" 'versions/2'
expect_in "$work/$current/summary.md" 'kept: tagged 1.3.0,1.3,tree-aaa since the listing'

if [ "$failures" -gt 0 ]; then
	echo "$failures check(s) failed"
	exit 1
fi
echo "All sandbox-image script tests passed"
