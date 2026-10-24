#!/usr/bin/env bash
# Deletes stale versions of the sandbox image package: untagged or tree-*-only versions older than MAX_AGE_DAYS.
# Keeps every version with a semver, latest or sha-* tag, and every manifest a kept image index references (its
# platform and attestation manifests), so a kept image never loses a part.
#
# Input (environment): OWNER (user that owns the package), PACKAGE, IMAGE (ghcr.io/<owner>/<package>),
# MAX_AGE_DAYS (default 30), DRY_RUN ("true" to only report), NOW (epoch seconds, for tests), GH_TOKEN.
# Writes a Markdown report to GITHUB_STEP_SUMMARY, or stdout when unset.
set -euo pipefail

max_age_days=${MAX_AGE_DAYS:-30}
now=${NOW:-$(date +%s)}
cutoff=$((now - max_age_days * 86400))
repo=${IMAGE,,}
api=/users/$OWNER/packages/container/$PACKAGE/versions
summary=${GITHUB_STEP_SUMMARY:-/dev/stdout}

forbidden() {
	echo "::error title=No access to the package::$1 (HTTP 403). GITHUB_TOKEN can delete versions only when this" \
		"repository has the admin role on the package: see sandbox/README.md, \"Image versioning\", cleanup." >&2
	exit 1
}

if ! versions=$(gh api --paginate "$api?per_page=100" --jq '.[] | [.id, .name, .updated_at, (.metadata.container.tags | join(","))] | @tsv' 2>&1); then
	[[ $versions == *"HTTP 403"* ]] && forbidden "cannot list versions of $PACKAGE"
	echo "$versions" >&2
	exit 1
fi

# id digest updated_at tags; a version is a candidate when it is old and carries no tag but tree-*
candidates=()
kept=()
while IFS=$'\t' read -r id digest updated tags; do
	[ -n "$id" ] || continue
	stale=false
	if [ "$(date -d "$updated" +%s)" -lt "$cutoff" ]; then
		stale=true
		for t in ${tags//,/ }; do
			[[ $t == tree-* ]] || stale=false
		done
	fi
	if $stale; then
		candidates+=("$id $digest $tags")
	else
		kept+=("$id $digest $tags")
	fi
done <<<"$versions"

# Children of every kept index stay, whatever their own age and tags
protected=" "
for v in ${kept[@]+"${kept[@]}"}; do
	read -r _ digest _ <<<"$v"
	children=$(docker buildx imagetools inspect --raw "$repo@$digest" | jq -r '.manifests[]?.digest')
	protected+="${children//$'\n'/ } "
done

deleted=0
{
	echo "### Image cleanup"
	echo
	echo "Versions older than $max_age_days days, untagged or tagged only tree-*$( [ "${DRY_RUN:-}" = true ] && echo ' (dry run)')."
	echo
	echo "| Version | Tags | Action |"
	echo "| --- | --- | --- |"
} >>"$summary"
for v in ${candidates[@]+"${candidates[@]}"}; do
	read -r id digest tags <<<"$v"
	action=deleted
	if [[ $protected == *" $digest "* ]]; then
		action="kept: part of a kept image"
	elif [ "${DRY_RUN:-}" = true ]; then
		action="would delete"
	elif ! current=$(gh api "$api/$id" --jq '.metadata.container.tags | join(",")' 2>&1); then
		[[ $current == *"HTTP 403"* ]] && forbidden "cannot read version $id"
		echo "$current" >&2
		exit 1
	elif tr ',' '\n' <<<"$current" | grep -q -v -e '^tree-' -e '^$'; then
		# Released (or tagged otherwise) since the listing, e.g. by a late publish of an old tree
		action="kept: tagged $current since the listing"
	elif ! err=$(gh api --method DELETE "$api/$id" 2>&1); then
		[[ $err == *"HTTP 403"* ]] && forbidden "cannot delete version $id"
		echo "$err" >&2
		exit 1
	fi
	[[ $action == kept* ]] || deleted=$((deleted + 1))
	echo "| \`$digest\` | ${tags:-—} | $action |" >>"$summary"
done
for v in ${kept[@]+"${kept[@]}"}; do
	read -r _ digest tags <<<"$v"
	echo "| \`$digest\` | ${tags:-—} | kept |" >>"$summary"
done
echo >>"$summary"
echo "$deleted of ${#candidates[@]} stale versions $( [ "${DRY_RUN:-}" = true ] && echo 'would be' || echo 'were') deleted; ${#kept[@]} versions kept." >>"$summary"
