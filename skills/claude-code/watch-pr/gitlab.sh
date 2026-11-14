# shellcheck shell=bash
# GitLab.com adapter of watch-pr: the MR, its discussions and its reviewers per poll, turned into the normalized
# snapshot. Sourced by watch-pr; not executable on its own.

# Prints the snapshot of MR $2 of $1 (group/project). Returns 0, 1 (transient failure) or 2 (fatal), reason on stderr.
gitlab_snapshot() {
	local project=$1 number=$2 encoded mr
	encoded=$(jq -rn --arg p "$project" '$p | @uri')
	mr=$(glab api "projects/$encoded/merge_requests/$number") || return 1
	glab api --paginate "projects/$encoded/merge_requests/$number/discussions?per_page=100" >/dev/null || return 1
	glab api "projects/$encoded/merge_requests/$number/reviewers" >/dev/null || return 1
	printf '%s\n' "$mr"
}
