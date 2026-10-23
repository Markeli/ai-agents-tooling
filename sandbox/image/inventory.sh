#!/usr/bin/env bash
# Prints the toolchain of this image as "name<TAB>version" lines, sorted by name. CI runs it in every built image to
# list it in the job summary, the release-check comment and the GitHub release; the bot can run it too.
# A tool that is missing or prints no version shows as "missing" or "unknown" instead of failing.
set -uo pipefail

# First version-looking token of a command's output ("Terraform v1.16.5" -> 1.16.5)
version_of() {
	if ! command -v "$1" >/dev/null 2>&1; then
		echo missing
		return
	fi
	local v
	v=$("$@" 2>&1 </dev/null | grep -oE '[0-9]+\.[0-9]+(\.[0-9]+)?([-+][0-9A-Za-z.-]+)?' | head -n 1)
	echo "${v:-unknown}"
}

plugins_dir=${HOME}/.claude/plugins

{
	printf 'Claude Code\t%s\n' "$(version_of claude --version)"
	dotnet --list-sdks 2>/dev/null | while read -r sdk _; do
		printf '.NET SDK %s\t%s\n' "${sdk%.*}" "$sdk"
	done
	printf 'Node.js\t%s\n' "$(version_of node --version)"
	printf 'npm\t%s\n' "$(version_of npm --version)"
	printf 'pnpm\t%s\n' "$(version_of pnpm --version)"
	printf 'yarn\t%s\n' "$(version_of yarn --version)"
	printf 'typescript\t%s\n' "$(version_of tsc --version)"
	printf 'typescript-language-server\t%s\n' "$(version_of typescript-language-server --version)"
	printf 'csharp-ls\t%s\n' "$(dotnet tool list --global 2>/dev/null | awk '$1 == "csharp-ls" { print $2 }')"
	for tool in gh glab yq actionlint hadolint shellcheck shfmt terraform plannotator uv ansible ansible-lint; do
		name=$tool
		[ "$tool" = ansible ] && name=ansible-core
		printf '%s\t%s\n' "$name" "$(version_of "$tool" --version)"
	done
	printf 'Python\t%s\n' "$(version_of python3 --version)"
	sdk_python=${SECURITY_WARNINGS_STATE_DIR:-}/agent-sdk-venv/bin/python
	printf 'claude-agent-sdk\t%s\n' \
		"$("$sdk_python" -c 'import importlib.metadata as m; print(m.version("claude-agent-sdk"))' 2>/dev/null || echo missing)"

	# Plugins as installed at build time: version and the commit they came from
	jq -r '.plugins | to_entries[] | .key as $k | .value[0] | "plugin \($k)\t\(.version // "unknown") (\((.gitCommitSha // "unknown")[0:12]))"' \
		"$plugins_dir/installed_plugins.json" 2>/dev/null
	jq -r 'to_entries[] | "\(.key)\t\(.value.installLocation)"' "$plugins_dir/known_marketplaces.json" 2>/dev/null \
		| while IFS=$'\t' read -r marketplace location; do
			# A git clone at build time; Claude Code's auto-update may replace it with a snapshot named by .gcs-sha
			commit=$(git -C "$location" rev-parse HEAD 2>/dev/null || cat "$location/.gcs-sha" 2>/dev/null || echo unknown)
			printf 'marketplace %s\t%s\n' "$marketplace" "$commit"
		done
	cat /etc/claude-code/skill-sources.tsv 2>/dev/null || true
} | LC_ALL=C sort
