#!/usr/bin/env bash
# Claude Code status line for the markeli-agent sandbox — same layout and colours as the owner's host
# statusline-command.sh, but the identity segment shows the bot and the sandbox instead of `user / host`,
# and the path is shortened relative to the repo root instead of $HOME (the workspace is mounted at its
# host path, e.g. /Users/..., while $HOME here is /home/agent, so the host's `~` substitution never fires).
# The path is bright magenta rather than the host's blue, which is barely readable on a dark terminal.

input=$(cat)

cwd=$(printf '%s' "$input" | jq -r '.workspace.current_dir // .cwd // ""' 2>/dev/null)
model=$(printf '%s' "$input" | jq -r '.model.display_name // ""' 2>/dev/null)
used_pct=$(printf '%s' "$input" | jq -r '.context_window.used_percentage // empty' 2>/dev/null)

sandbox_name="${SANDBOX_NAME:-$(hostname -s 2>/dev/null)}"

toplevel=""
if [ -n "$cwd" ] && git -C "$cwd" -c gc.auto=0 --no-optional-locks rev-parse --is-inside-work-tree >/dev/null 2>&1; then
	toplevel=$(git -C "$cwd" -c gc.auto=0 --no-optional-locks rev-parse --show-toplevel 2>/dev/null)
fi

short_path="$cwd"
if [ -n "$toplevel" ]; then
	short_path="$(basename "$toplevel")${cwd#"$toplevel"}"
elif [ -n "$cwd" ] && [[ "$cwd" == "$HOME"* ]]; then
	short_path="${cwd/#$HOME/\~}"
fi

branch=""
if [ -n "$toplevel" ]; then
	branch=$(git -C "$cwd" -c gc.auto=0 --no-optional-locks symbolic-ref --short HEAD 2>/dev/null \
		|| git -C "$cwd" -c gc.auto=0 --no-optional-locks rev-parse --short HEAD 2>/dev/null)
fi

now=$(date +%H:%M:%S)

ctx_part=""
if [ -n "$used_pct" ]; then
	ctx_part=$(printf " | ctx: %.0f%% used" "$used_pct")
fi

printf "\033[36m%s\033[0m @ \033[36m%s\033[0m  " "markeli-agent" "$sandbox_name"
printf "\033[95m%s\033[0m" "$short_path"
if [ -n "$branch" ]; then
	printf "  \033[32m[%s]\033[0m" "$branch"
fi
printf "  \033[90m%s\033[0m" "$now"
if [ -n "$model" ]; then
	printf "  | \033[33m%s\033[0m" "$model"
fi
printf "%s\n" "$ctx_part"
