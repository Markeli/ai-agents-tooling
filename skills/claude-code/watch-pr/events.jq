# Turns a poll's snapshot into event lines and the next state (docs/specs/2026-11-14-watch-pr.md).
#
# Input: null (jq -n). Arguments:
#   $snap     the snapshot: {url, state: open|merged|closed, head: {sha, date}, items: [{id, kind, author, url, body,
#             created, verdict?}], failed: [{key, name, url}], reviewers: {login: state} | null, rate}
#   $state    the state of the previous poll, or null on the first poll of the session:
#             {seen: [item ids], failed: [check keys], reviewers: {login: state}}
#   $authors  allow-listed logins for the host, lowercase, one per line
#   $me       the bot's own login; $session the session id; $ref the PR/MR reference for the lines
# Output: {lines: [event lines], state: next state, ended: bool}

def marker: (.body // "") | (capture("<!-- watch-pr session=(?<s>[A-Za-z0-9._-]+) -->").s // null);

def one_line: gsub("<!-- watch-pr session=[A-Za-z0-9._-]+ -->"; " ") | gsub("\\s+"; " ") | ltrimstr(" ")
  | rtrimstr(" ");

def field: if . == null or . == "" then "-" else gsub("\\s+"; "_") end;

def excerpt: (. // "") | one_line | .[0:200] | if . == "" then "-" else . end;

def line($kind; $author; $url; $text): "watch-pr \($ref) \($kind) \($author | field) \($url | field) \($text)";

($authors | split("\n") | map(select(. != ""))) as $allowed
| ($me | ascii_downcase) as $bot
# Allow-listed, and the bot's own account only with another session's marker: never its own replies
| def wakes: (.author | ascii_downcase) as $author
    | any($allowed[]; . == $author)
      and (if $author == $bot then (marker as $m | $m != null and $m != $session) else true end);
($state // {seen: [], failed: [], reviewers: {}}) as $previous
| ($snap.items | sort_by(.created)) as $items
| [$items[] | select(.id as $id | $previous.seen | index([$id]) | not) | select(wakes)] as $new
| [$snap.failed[] | select(.key as $key | $previous.failed | index([$key]) | not)] as $failed
| ($snap.state == "merged" or $snap.state == "closed") as $ended
| {
    lines: (
      if $state == null then [] else
        [$new[] | line(.kind; .author; .url;
          if .kind == "review" then
            (.verdict + (if (.body // "" | one_line) == "" then "" else ": " + (.body | one_line) end)) | excerpt
          else .body | excerpt end)]
        + [$failed[] | line("ci-failed"; null; .url; .name | excerpt)]
      end
      + (if $ended then [line($snap.state; null; $snap.url; "-")] else [] end)
    ),
    state: {
      seen: ($previous.seen + [$items[].id] | unique),
      failed: ($previous.failed + [$snap.failed[].key] | unique),
      reviewers: ($snap.reviewers // $previous.reviewers)
    },
    ended: $ended
  }
