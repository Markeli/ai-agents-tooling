# Shared helpers of the sandbox-image scripts; sourced, not executed.
# shellcheck shell=bash

# Reads a Keep a Changelog file. Sets:
#   changelog_version     first "## [x.y.z] - yyyy-mm-dd" version, or 0.0.0 when there is none
#   changelog_released    true when such a section exists
#   changelog_unreleased  true when "## [Unreleased]" has any non-blank line
# A version heading that isn't x.y.z with an ISO date fails, naming the heading.
# shellcheck disable=SC2034 # the variables are the function's output to the sourcing script
read_changelog() {
	local file=$1 line section="" heading
	changelog_version=0.0.0
	changelog_released=false
	changelog_unreleased=false
	[ -f "$file" ] || return 0
	while IFS= read -r line || [ -n "$line" ]; do
		if [[ $line == '## '* ]]; then
			heading=$line
			if [[ $heading =~ ^'## [Unreleased]'[[:space:]]*$ ]]; then
				section=unreleased
				continue
			fi
			if [[ ! $heading =~ ^'## ['([0-9]+\.[0-9]+\.[0-9]+)'] - '[0-9]{4}-[0-9]{2}-[0-9]{2}[[:space:]]*$ ]]; then
				echo "$file: malformed version heading '$heading', expected '## [x.y.z] - yyyy-mm-dd'" >&2
				return 1
			fi
			changelog_version=${BASH_REMATCH[1]}
			changelog_released=true
			return 0
		fi
		if [ "$section" = unreleased ] && [[ $line =~ [^[:space:]] ]]; then
			changelog_unreleased=true
		fi
	done <"$file"
	return 0
}

# Prints the body of the "## [<version>]" section, without its heading.
changelog_section() {
	local file=$1 version=$2
	awk -v heading="## [$version]" '
		index($0, heading) == 1 { found = 1; next }
		found && /^## / { exit }
		found { print }
	' "$file" | sed -e '/./,$!d'
}

# A missing tag reports "not found"; anything else (auth, network) must fail the run instead of passing for "absent"
image_exists() {
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

# Callers use these in $(...), where an exit only leaves the subshell: assign with "|| exit 1"
image_digest() {
	local json digest
	json=$(docker buildx imagetools inspect "$1" --format '{{json .Manifest}}') || return 1
	digest=$(jq -r '.digest // empty' <<<"$json") || return 1
	[ -n "$digest" ] || { echo "no digest for $1" >&2; return 1; }
	echo "$digest"
}

# org.opencontainers.image.version of an image or an image index (whose first platform image is used); empty if none
image_version_label() {
	local json
	json=$(docker buildx imagetools inspect "$1" --format '{{json .Image}}') || return 1
	jq -r '(.config // (to_entries | map(.value) | first).config).Labels["org.opencontainers.image.version"] // empty' <<<"$json"
}

# True when tag $2 of $1 should move forward to release $3: absent, unversioned (before 1.0.0) or a lower version.
# A registry error exits the script: a failed lookup must never pass for "move" or "keep".
moves_to_release() {
	local repo=$1 tag=$2 release=$3 current
	image_exists "$repo:$tag" || return 0
	current=$(image_version_label "$repo:$tag") || exit 1
	! is_semver "$current" || version_lt "$current" "$release"
}

# True when tag $2 of $1 points at release $3 already
points_at_release() {
	local current
	image_exists "$1:$2" || return 1
	current=$(image_version_label "$1:$2") || exit 1
	[ "$current" = "$3" ]
}

# Prints the highest claude-sandbox.v<x.y.z> release below $1 (any, when empty) and in major $2 (any, when empty)
previous_release() {
	local below=$1 major=$2
	gh release list --limit 1000 --json tagName --jq '.[].tagName' \
		| sed -n 's/^claude-sandbox\.v\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)$/\1/p' \
		| while read -r v; do
			if { [ -z "$below" ] || version_lt "$v" "$below"; } && { [ -z "$major" ] || [ "${v%%.*}" = "$major" ]; }; then
				echo "$v"
			fi
		done \
		| sort -V | tail -n 1
}

# Prints the toolchain changes of inventory $1 against the inventory.tsv of release $2 ("This image" for $3 if empty)
toolchain_changes() {
	local inventory=$1 base=$2 label=$3 diff old assets
	diff="$(dirname "${BASH_SOURCE[0]}")/sandbox-image-inventory-diff.sh"
	if [ -n "$base" ]; then
		assets=$(gh release view "claude-sandbox.v$base" --json assets --jq '.assets[].name') || return 1
	fi
	# Releases before the inventory existed have nothing to diff against; a failed download is an error
	if [ -z "$base" ] || ! grep -qx inventory.tsv <<<"$assets"; then
		"$diff" - "$inventory" "" "$label"
		return
	fi
	old=$(mktemp)
	gh release download "claude-sandbox.v$base" --pattern inventory.tsv --output "$old" --clobber \
		&& "$diff" "$old" "$inventory" "$base" "$label"
	local status=$?
	rm -f "$old"
	return $status
}

is_semver() {
	[[ $1 =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

# True when $1 is a lower version than $2
version_lt() {
	[ "$1" != "$2" ] && [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -n 1)" = "$1" ]
}
