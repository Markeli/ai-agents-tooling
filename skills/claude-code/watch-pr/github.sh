# shellcheck shell=bash
# GitHub adapter of watch-pr: one GraphQL query per poll, turned into the normalized snapshot (see events.jq).
# Sourced by watch-pr; not executable on its own.

# Prints the snapshot of PR $2 of $1 (owner/repo). Returns 0, 1 (transient failure) or 2 (fatal), reason on stderr.
github_snapshot() {
	local project=$1 number=$2 response status error_file error
	error_file=$(mktemp)
	# -f, not -F, for the strings: -F reads a file for a value starting with @
	response=$(gh api graphql -f query="$github_query" -f owner="${project%%/*}" -f name="${project#*/}" \
		-F number="$number" 2>"$error_file")
	status=$?
	error=$(cat "$error_file")
	rm -f "$error_file"
	if [ "$status" != 0 ]; then
		printf '%s\n' "$error" >&2
		github_fatal "$error" && return 2
		return 1
	fi
	jq -e "$github_normalize" <<<"$response" 2>/dev/null && return 0
	if jq -e '.data.repository.pullRequest == null or any(.errors[]?; .type == "NOT_FOUND")' <<<"$response" \
		>/dev/null 2>&1; then
		echo "the PR doesn't exist or the token can't see it" >&2
		return 2
	fi
	echo "unexpected GraphQL answer: ${response:0:300}" >&2
	return 1
}

# Whether gh's error $1 means the watch can't go on: bad credentials, no access, no such PR. Rate limits (also 403)
# and everything else are transient.
github_fatal() {
	! grep -qiE 'rate limit|abuse|HTTP 429' <<<"$1" && grep -qE 'HTTP 40[134]|Could not resolve to' <<<"$1"
}

# The newest 100 of each kind are enough: fewer than 100 new ones arrive between two polls. The first comment of a
# thread starts it; a thread longer than 100 comments loses that distinction, not any comment.
# GraphQL variables, not shell ones
# shellcheck disable=SC2016
github_query='query($owner: String!, $name: String!, $number: Int!) {
  rateLimit { limit remaining resetAt }
  repository(owner: $owner, name: $name) {
    pullRequest(number: $number) {
      url
      state
      comments(last: 100) { nodes { databaseId author { __typename login } url body createdAt } }
      reviewThreads(last: 100) {
        nodes { comments(last: 100) { nodes { databaseId author { __typename login } url body createdAt } } }
      }
      reviews(last: 100) { nodes { databaseId author { __typename login } url state body submittedAt } }
      commits(last: 1) {
        nodes {
          commit {
            oid
            committedDate
            statusCheckRollup {
              contexts(first: 100) {
                nodes {
                  __typename
                  ... on CheckRun { name conclusion detailsUrl }
                  ... on StatusContext { context state targetUrl }
                }
              }
            }
          }
        }
      }
    }
  }
}'

# jq variables, not shell ones
# shellcheck disable=SC2016
# The snapshot: state, head commit, items (comments, thread comments, reviews worth reading), failed checks
github_normalize='
.data.repository.pullRequest as $pr
| ($pr.commits.nodes[0].commit // {}) as $head
# GraphQL names a GitHub App "github-actions" where REST and the allow-list say "github-actions[bot]"
| def login: if .author == null then "ghost" elif .author.__typename == "Bot" then .author.login + "[bot]"
    else .author.login end;
def comment($kind): {id: "\($kind):\(.databaseId)", author: login, url, body, created: .createdAt};
{
  url: $pr.url,
  state: ($pr.state | ascii_downcase),
  head: {sha: $head.oid, date: $head.committedDate},
  items: (
    [$pr.comments.nodes[] | comment("c") + {kind: "comment"}]
    + [$pr.reviewThreads.nodes[].comments.nodes | to_entries[]
        | .key as $index | .value | comment("rc") + {kind: (if $index == 0 then "thread" else "reply" end)}]
    + [$pr.reviews.nodes[]
        | select(.state == "CHANGES_REQUESTED"
            or ((.state == "APPROVED" or .state == "COMMENTED") and (.body // "") != ""))
        | {id: "r:\(.databaseId)", kind: "review", author: login, url, body,
           created: .submittedAt, verdict: (.state | ascii_downcase)}]
  ),
  failed: [($head.statusCheckRollup.contexts.nodes // [])[]
    | if .__typename == "CheckRun" then
        select(.conclusion == "FAILURE" or .conclusion == "TIMED_OUT" or .conclusion == "STARTUP_FAILURE")
        | {key: "\(.name)@\($head.oid)", name, url: .detailsUrl}
      else
        select(.state == "FAILURE" or .state == "ERROR") | {key: "\(.context)@\($head.oid)", name: .context,
          url: .targetUrl}
      end],
  reviewers: null,
  rate: (.data.rateLimit | {remaining, limit, reset: .resetAt})
}'
