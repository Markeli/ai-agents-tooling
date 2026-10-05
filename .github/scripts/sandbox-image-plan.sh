#!/usr/bin/env bash
# Decides what the sandbox-image workflow does with this run. The image is content-addressed by the git tree of
# sandbox/image (tag tree-<hash>), so an unchanged tree is never rebuilt: a PR skips it, main only retags it.
#
# Input (environment): IMAGE, EVENT (github.event_name), REF (github.ref), DEFAULT_BRANCH, SHA (github.sha),
# SAME_REPO ("true" unless a PR comes from a fork), FORCE ("true" to rebuild anyway).
# Output (GITHUB_OUTPUT, or stdout when unset):
#   action    build | retag | skip
#   push      whether build pushes (false only for fork PRs, which have no registry access)
#   no_cache  whether build ignores the layer cache, so forced and weekly rebuilds pick up base image and apt updates
#   tree_tag  <image>:tree-<hash>
#   tags      newline-separated tags to build or (besides tree_tag) to retag
set -euo pipefail

repo=${IMAGE,,}
tree=$(git rev-parse HEAD:sandbox/image)
tree_tag=$repo:tree-$tree
sha_tag=$repo:sha-${SHA:0:7}

on_default_branch=false
if [ "$EVENT" != pull_request ] && [ "$REF" = "refs/heads/$DEFAULT_BRANCH" ]; then
	on_default_branch=true
fi
forced=false
if [ "$EVENT" = schedule ] || [ "${FORCE:-}" = true ]; then
	forced=true
fi

# A missing tag reports "not found"; anything else (auth, network) must fail the run instead of passing for "absent"
exists() {
	local err
	if err=$(docker buildx imagetools inspect "$1" 2>&1 >/dev/null); then
		return 0
	fi
	if [[ $err == *"not found"* ]]; then
		return 1
	fi
	echo "$err" >&2
	exit 1
}

tags=$tree_tag
action=build
push=true
if [ "$EVENT" = pull_request ] && [ "$SAME_REPO" != true ]; then
	push=false
elif $on_default_branch; then
	tags+=$'\n'$repo:latest
	# sha-<short> stays immutable: a forced rebuild of an already published commit only moves latest and tree-<hash>
	if ! $forced || ! exists "$sha_tag"; then
		tags+=$'\n'$sha_tag
	fi
	if ! $forced && exists "$tree_tag"; then
		action=retag
	fi
elif ! $forced && exists "$tree_tag"; then
	action=skip
fi

{
	echo "action=$action"
	echo "push=$push"
	echo "no_cache=$forced"
	echo "tree_tag=$tree_tag"
	echo "tags<<EOF"
	echo "$tags"
	echo "EOF"
} >>"${GITHUB_OUTPUT:-/dev/stdout}"
