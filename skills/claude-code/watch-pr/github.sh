# shellcheck shell=bash
# GitHub adapter of watch-pr: one GraphQL query per poll, turned into the normalized snapshot.
# Sourced by watch-pr; not executable on its own.

# Prints the snapshot of PR $2 of $1 (owner/repo). Returns 0, 1 (transient failure) or 2 (fatal), reason on stderr.
github_snapshot() {
	local project=$1 number=$2 response
	response=$(gh api graphql -f query="$github_query" -F owner="${project%%/*}" -F name="${project#*/}" \
		-F number="$number") || return 1
	printf '%s\n' "$response"
}

# GraphQL variables, not shell ones
# shellcheck disable=SC2016
github_query='query($owner: String!, $name: String!, $number: Int!) {
  rateLimit { limit remaining resetAt }
  repository(owner: $owner, name: $name) { pullRequest(number: $number) { url state } }
}'
