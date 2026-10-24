#!/usr/bin/env bash
# Publishes the release version planned by sandbox-image-plan.sh: points the planned tags at the tree image, then
# creates the git tag claude-sandbox.v<x.y.z> and its GitHub release if they don't exist yet. Safe to re-run.
#
# Input (environment): IMAGE, TREE_TAG, RELEASE, PUBLISH_TAGS (newline-separated), LATEST ("true" to mark the
# GitHub release Latest), SHA (commit to tag), INVENTORY (inventory.tsv of the tree image), GH_TOKEN, GH_REPO.
set -euo pipefail

# shellcheck source=sandbox-image-lib.sh source-path=SCRIPTDIR
. "$(dirname "${BASH_SOURCE[0]}")/sandbox-image-lib.sh"

: "${IMAGE:?}" "${TREE_TAG:?}" "${RELEASE:?}" "${SHA:?}" "${INVENTORY:?}"

changelog=sandbox/image/CHANGELOG.md
repo=${IMAGE,,}
tag=claude-sandbox.v$RELEASE

# Checked again here, under the publish concurrency group: another run may have published since the plan.
# x.y.z is immutable, and a moving tag that a newer release took meanwhile stays where it is.
args=()
while read -r image_tag; do
	[ -n "$image_tag" ] || continue
	if [ "$image_tag" = "$repo:$RELEASE" ]; then
		if image_exists "$image_tag"; then
			release_digest=$(image_digest "$image_tag") || exit 1
			tree_digest=$(image_digest "$TREE_TAG") || exit 1
			if [ "$release_digest" != "$tree_digest" ]; then
				echo "refusing to overwrite $image_tag: it is already published with another digest" >&2
				exit 1
			fi
			continue
		fi
	elif ! moves_to_release "$repo" "${image_tag##*:}" "$RELEASE"; then
		echo "Not moving $image_tag: it already points at a newer release."
		continue
	fi
	args+=(--tag "$image_tag")
done <<<"$PUBLISH_TAGS"
if [ ${#args[@]} -gt 0 ]; then
	docker buildx imagetools create "${args[@]}" "$TREE_TAG"
fi

if err=$(gh release view "$tag" --json tagName 2>&1 >/dev/null); then
	echo "Release $tag already exists."
	exit 0
elif [[ $err != *"release not found"* ]]; then
	echo "$err" >&2
	exit 1
fi

# A maintenance release diffs against the previous release of its own major
major=""
if [ "$LATEST" != true ]; then
	major=${RELEASE%%.*}
fi
previous=$(previous_release "$RELEASE" "$major")

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
notes=$work/notes.md
# The asset is named after the file
cp "$INVENTORY" "$work/inventory.tsv"
{
	changelog_section "$changelog" "$RELEASE"
	echo
	echo "### Toolchain"
	echo
	toolchain_changes "$INVENTORY" "$previous" "$RELEASE"
} >"$notes"

release_args=(--target "$SHA" --title "claude-sandbox v$RELEASE" --notes-file "$notes")
# Latest only while the latest tag points at this release, so a late re-run never takes it from a newer one
if [ "$LATEST" = true ] && points_at_release "$repo" latest "$RELEASE"; then
	release_args+=(--latest)
else
	release_args+=(--latest=false)
fi
gh release create "$tag" "${release_args[@]}" "$work/inventory.tsv"
