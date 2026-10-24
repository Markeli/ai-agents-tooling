#!/usr/bin/env bash
# Decides what the sandbox-image workflow does with this run (docs/specs/2026-10-23-sandbox-image-versioning.md).
# The image is content-addressed by the git tree of sandbox/image (tag tree-<hash>), built once and never overwritten.
# The release version is the first "## [x.y.z]" section of sandbox/image/CHANGELOG.md; a push to main or
# release/<major>.x publishes it once, by pointing x.y.z and the moving tags at the tree image.
#
# Input (environment): IMAGE, EVENT (github.event_name), REF (github.ref), BASE_REF (github.base_ref, PRs only),
# DEFAULT_BRANCH, SHA (github.sha), HEAD_SHA (PR head commit), SAME_REPO ("true" unless a PR comes from a fork),
# RELEASE_CHECK (where to write the release-check Markdown, default release-check.md).
# Output (GITHUB_OUTPUT, or stdout when unset):
#   build          whether tree_tag is missing and must be built
#   push           whether the build pushes (false only for fork PRs, which have no registry access)
#   tree, tree_tag the git tree hash of sandbox/image and <image>:tree-<hash>
#   version        version baked into the image: x.y.z, or <x.y.z>+unreleased.<tree12>
#   revision       commit the tree is built from: the PR head for PRs, the pushed commit otherwise
#   publish        whether this push publishes (or completes the publish of) release
#   release        the release version x.y.z, empty unless publish
#   publish_tags   newline-separated tags to point at tree_tag
#   latest         whether the GitHub release is marked Latest (pushes to the default branch)
#   changed        whether a PR changes sandbox/image (release_check is written for PRs only)
#   release_check  path of the release-check Markdown
set -euo pipefail

# shellcheck source=sandbox-image-lib.sh source-path=SCRIPTDIR
. "$(dirname "${BASH_SOURCE[0]}")/sandbox-image-lib.sh"

changelog=sandbox/image/CHANGELOG.md
repo=${IMAGE,,}
tree=$(git rev-parse HEAD:sandbox/image)
tree_tag=$repo:tree-$tree
release_check=${RELEASE_CHECK:-release-check.md}

read_changelog "$changelog"
release=$changelog_version

if [ "$EVENT" = pull_request ]; then
	target=$BASE_REF
	revision=$HEAD_SHA
else
	target=${REF#refs/heads/}
	revision=$SHA
fi

if [[ $target =~ ^release/([0-9]+)\.x$ ]] && $changelog_released && [ "${release%%.*}" != "${BASH_REMATCH[1]}" ]; then
	echo "$changelog: version $release does not belong on $target" >&2
	exit 1
fi

published=false
if $changelog_released && image_exists "$repo:$release"; then
	published=true
fi
build=true
if image_exists "$tree_tag"; then
	build=false
fi

version=$release+unreleased.${tree:0:12}
if $changelog_released && ! $changelog_unreleased && ! $published; then
	version=$release
fi

push=true
if [ "$EVENT" = pull_request ] && [ "$SAME_REPO" != true ]; then
	push=false
fi

publish=false
publish_tags=""
latest=false
notes=()
publish_branch=false
if [ "$EVENT" = push ] && { [ "$target" = "$DEFAULT_BRANCH" ] || [[ $target == release/* ]]; }; then
	publish_branch=true
fi
# Whether the published x.y.z is this tree's image (a re-run of the same publish)
same_tree=false
if $publish_branch && $published && ! $build; then
	release_digest=$(image_digest "$repo:$release") || exit 1
	tree_digest=$(image_digest "$tree_tag") || exit 1
	[ "$release_digest" != "$tree_digest" ] || same_tree=true
fi
if $publish_branch && $changelog_released; then
	if $changelog_unreleased && ! $published; then
		# A newer push may have superseded the release push before it ran; publishing now would ship the
		# Unreleased changes under x.y.z
		notes+=("$release is not published, but the tree has Unreleased changes on top of it: re-run the workflow of the commit that added the $release section.")
	elif $published && ! $same_tree; then
		notes+=("$release is already published from another tree; the changes of this tree are unreleased.")
		if ! $changelog_unreleased; then
			echo "::warning title=Unreleased image changes::sandbox/image changed after $release without an Unreleased entry in $changelog"
		fi
	else
		publish=true
		if ! $published; then
			publish_tags=$repo:$release
		fi
		for tag in "${release%.*}" "${release%%.*}"; do
			if moves_to_release "$repo" "$tag" "$release"; then
				publish_tags+=${publish_tags:+$'\n'}$repo:$tag
			fi
		done
		# The GitHub release is Latest only together with the latest tag, so re-running an older release
		# doesn't take the badge from a newer one
		if [ "$target" = "$DEFAULT_BRANCH" ]; then
			if moves_to_release "$repo" latest "$release"; then
				publish_tags+=${publish_tags:+$'\n'}$repo:latest
				latest=true
			elif points_at_release "$repo" latest "$release"; then
				latest=true
			fi
		fi
	fi
fi

changed=false
if [ "$EVENT" = pull_request ]; then
	changed_files=$(git diff --name-only HEAD^1 HEAD -- sandbox/image)
	changelog_changed=false
	if grep -qx "$changelog" <<<"$changed_files"; then
		changelog_changed=true
	fi
	[ -z "$changed_files" ] || changed=true

	if $changelog_released && ! $changelog_unreleased && ! $published; then
		state="🚀 The merge releases **$release**."
	elif $changelog_changed && $published && ! $changelog_unreleased; then
		state="⚠️ The CHANGELOG changed, but nothing will be released: **$release** is already published and \`## [Unreleased]\` is empty. Add the change under \`## [Unreleased]\`, or a new version section to release it."
	elif $changed && $changelog_changed && $changelog_unreleased; then
		state="📝 The image changes, and the change is under \`## [Unreleased]\`: released later."
	elif $changed && ! $changelog_changed; then
		state="⚠️ \`sandbox/image\` changed without a CHANGELOG entry. Add it under \`## [Unreleased]\` of \`$changelog\`, or a \`## [x.y.z] - yyyy-mm-dd\` section to release it. CI, tests and internal refactoring need no entry."
		first=$(head -n 1 <<<"$changed_files")
		echo "::warning file=$first,title=No CHANGELOG entry::sandbox/image changed without an entry in $changelog"
	elif $changed; then
		state="📝 The image changes; nothing will be released."
	else
		state="No image changes."
	fi
	printf '<!-- release-check -->\n### Release check\n\n%s\n' "$state" >"$release_check"
fi

for note in ${notes[@]+"${notes[@]}"}; do
	echo "::notice title=Nothing published::$note"
done

{
	echo "build=$build"
	echo "push=$push"
	echo "tree=$tree"
	echo "tree_tag=$tree_tag"
	echo "version=$version"
	echo "revision=$revision"
	echo "publish=$publish"
	echo "release=$($publish && echo "$release" || true)"
	echo "publish_tags<<EOF"
	echo "$publish_tags"
	echo "EOF"
	echo "latest=$latest"
	echo "changed=$changed"
	echo "release_check=$release_check"
	echo "notes<<EOF"
	printf '%s\n' ${notes[@]+"${notes[@]}"}
	echo "EOF"
} >>"${GITHUB_OUTPUT:-/dev/stdout}"
