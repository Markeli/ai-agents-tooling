# shellcheck shell=bash
# GitLab.com adapter of watch-pr: the MR, its discussions and its reviewers per poll (plus the failed jobs of a failed
# pipeline, and the head commit's date on a session's first poll), turned into the normalized snapshot (see
# events.jq). Sourced by watch-pr, whose state_file it reads; not executable on its own.

# Prints the snapshot of MR $2 of $1 (group/project). Returns 0, 1 (transient failure) or 2 (fatal), reason on stderr.
gitlab_snapshot() {
	local project=$1 number=$2 api mr discussions reviewers jobs='[]' head_date=null pipeline
	api="projects/$(jq -rn --arg p "$project" '$p | @uri')"
	mr=$(gitlab_get --fatal "$api/merge_requests/$number") || return
	discussions=$(gitlab_get --paginate "$api/merge_requests/$number/discussions?per_page=100") || return
	reviewers=$(gitlab_get "$api/merge_requests/$number/reviewers") || return
	pipeline=$(jq -r 'select(.head_pipeline.status == "failed") | .head_pipeline.id' <<<"$mr")
	if [[ $pipeline =~ ^[0-9]+$ ]]; then
		jobs=$(gitlab_get --paginate "$api/pipelines/$pipeline/jobs?scope[]=failed&per_page=100") || return
	fi
	# Only the catch-up of a session's first poll needs it; state_file is watch-pr's
	# shellcheck disable=SC2154
	if [ ! -s "$state_file" ]; then
		head_date=$(gitlab_get "$api/repository/commits/$(jq -r .sha <<<"$mr")" | jq '.committed_date') || return
	fi
	# Files, not arguments: long discussions outgrow the argument size limit
	jq -n --slurpfile mrs <(printf '%s\n' "$mr") --slurpfile pages <(printf '%s\n' "$discussions") \
		--slurpfile reviewer_lists <(printf '%s\n' "$reviewers") --slurpfile job_pages <(printf '%s\n' "$jobs") \
		--argjson head_date "${head_date:-null}" "$gitlab_normalize" || {
		echo "unexpected GitLab answer for $api/merge_requests/$number" >&2
		return 1
	}
}

# glab api with its stderr as the reason. Returns 1 (transient), or with --fatal first, 2 when the API answers 401,
# 403 or 404: no credentials, no access or no such MR. Other requests' errors are all transient.
gitlab_get() {
	local error_file response status error fatal=false
	if [ "$1" = --fatal ]; then
		fatal=true
		shift
	fi
	error_file=$(mktemp)
	response=$(glab api "$@" 2>"$error_file")
	status=$?
	error=$(cat "$error_file")
	rm -f "$error_file"
	if [ "$status" != 0 ]; then
		printf '%s\n' "$error" >&2
		if $fatal && grep -qE '(HTTP|^glab:) 40[134]([^0-9]|$)' <<<"$error"; then
			return 2
		fi
		return 1
	fi
	printf '%s\n' "$response"
}

# jq variables, not shell ones
# shellcheck disable=SC2016
gitlab_normalize='
$mrs[0] as $mr
| ([$pages[][]]) as $discussions
| ($reviewer_lists[0] // []) as $reviewers
| ([$job_pages[][]]) as $jobs
| {
  url: $mr.web_url,
  state: ({opened: "open", merged: "merged"}[$mr.state] // "closed"),
  head: {sha: $mr.sha, date: $head_date},
  items: [$discussions[]
    | .individual_note as $individual
    | [.notes[] | select(.system | not)] | to_entries[]
    | .key as $index | .value
    | {id: "n:\(.id)", kind: (if $individual then "comment" elif $index == 0 then "thread" else "reply" end),
       author: .author.username, url: "\($mr.web_url)#note_\(.id)", body, created: .created_at}],
  failed: (if $mr.head_pipeline.status == "failed" then
      [{key: "pipeline:\($mr.head_pipeline.id)", name: ([$jobs[].name] | join(",")),
        url: $mr.head_pipeline.web_url}]
    else [] end),
  reviewers: (reduce $reviewers[] as $r ({}; .[$r.user.username] = $r.state)),
  rate: null
}'
