# Turns a poll's snapshot into event lines and the next state (docs/specs/2026-11-14-watch-pr.md).
#
# Input: null (jq -n). Arguments:
#   $snaps    [the snapshot] (--slurpfile): {url, state: open|merged|closed, head: {sha, date}, items: [{id, kind, author, url, body,
#             created, verdict?}], failed: [{key, name, url}], reviewers: {login: state} | null, rate}
#   $states   [the state of the previous poll] (--slurpfile), or [] on the first poll of the session:
#             {seen: [item ids], failed: [check keys], reviewers: {login: state}}
#   $authors  allow-listed logins for the host, lowercase, one per line
#   $me       the bot's own login; $session the session id; $ref the PR/MR reference for the lines
# Output: {lines: [event lines], state: next state, ended: bool}

# Seconds since the epoch of an ISO 8601 time with Z or a ±hh:mm offset; fractions of a second are dropped
def epoch: if . == null then 0 else
  sub("\\.[0-9]+"; "") as $t
  | if ($t | test("[+-][0-9]{2}:[0-9]{2}$")) then
      $t[-6:] as $zone
      | (($zone[1:3] | tonumber) * 3600 + ($zone[4:6] | tonumber) * 60) as $offset
      | ($t[:-6] + "Z" | fromdateiso8601) - (if $zone[0:1] == "+" then $offset else -$offset end)
    else $t | fromdateiso8601 end
  end;

# The body's last marker: the bot's own comes last, a quoted one earlier
def marker: [(.body // "") | match("<!-- watch-pr session=([A-Za-z0-9._-]+) -->"; "g")] | last
  | if . == null then null else .captures[0].string end;

def one_line: gsub("<!-- watch-pr session=[A-Za-z0-9._-]+ -->"; " ") | gsub("\\s+"; " ") | ltrimstr(" ")
  | rtrimstr(" ");

def field: if . == null or . == "" then "-" else gsub("\\s+"; "_") end;

def excerpt: (. // "") | one_line | .[0:200] | if . == "" then "-" else . end;

def line($kind; $author; $url; $text): "watch-pr \($ref) \($kind) \($author | field) \($url | field) \($text)";

$snaps[0] as $snap
| ($states[0] // null) as $state
| ($authors | split("\n") | map(select(. != ""))) as $allowed
| ($me | ascii_downcase) as $bot
# Allow-listed, and the bot's own account only with another session's marker: never its own replies
| def wakes: (.author | ascii_downcase) as $author
    | any($allowed[]; . == $author)
      and (if $author == $bot then (marker as $m | $m != null and $m != $session) else true end);
($state // {seen: [], failed: [], reviewers: {}}) as $previous
| ($snap.items | sort_by(.created | epoch)) as $items
| [$items[] | select(.id as $id | $previous.seen | index([$id]) | not) | select(wakes)] as $new
| [$snap.failed[] | select(.key as $key | $previous.failed | index([$key]) | not)] as $failed
| ($snap.state == "merged" or $snap.state == "closed") as $ended
# The first poll of a session reports what still waits for the bot: items of others after its latest comment or the
# head commit, whichever is later, and the checks that have failed. Its own items can't be told from another
# session's here, so none are caught up.
| ([$items[] | select(.author | ascii_downcase == $bot) | .created | epoch] + [$snap.head.date | epoch] | max)
  as $cutoff
| def text: if .kind == "review" then
      (.verdict + (if (.body // "" | one_line) == "" then "" else ": " + (.body | one_line) end)) | excerpt
    else .body | excerpt end;
# Check names and links come from any CI app: printed only in a strict charset, so they can't carry instructions
def check_name: gsub("[^A-Za-z0-9 ,._/:()-]"; "_") | .[0:100];
def check_url: if (. // "") | test("^https://[A-Za-z0-9.-]+(/[A-Za-z0-9._~%/:?#=&+-]*)?$") then . else "-" end;
{
    lines: (
      if $state == null then
        [$items[] | select(wakes and (.author | ascii_downcase) != $bot and (.created | epoch) > $cutoff)
          | line(.kind; .author; .url; text)]
      else
        [$new[] | line(.kind; .author; .url; text)]
      end
      # GitLab reviewer verdicts: a change of an allow-listed reviewer's state, not caught up on a first poll
      + (if $state == null then [] else
          [($snap.reviewers // {}) | to_entries[]
            | select(.value == "requested_changes" or .value == "approved")
            | select(.value != ($previous.reviewers[.key] // null))
            | select(.key | ascii_downcase as $login | any($allowed[]; . == $login) and $login != $bot)
            | line("review"; .key; $snap.url; .value)]
        end)
      + [$failed[] | line("ci-failed"; null; .url | check_url; .name | check_name | excerpt)]
      + (if $ended then [line($snap.state; null; $snap.url; "-")] else [] end)
    ),
    state: {
      seen: ($previous.seen + [$items[].id] | unique),
      failed: ($previous.failed + [$snap.failed[].key] | unique),
      reviewers: ($snap.reviewers // $previous.reviewers // {})
    },
    ended: $ended
  }
