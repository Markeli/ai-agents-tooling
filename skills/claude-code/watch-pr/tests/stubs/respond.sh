# shellcheck shell=bash
# Shared by the gh and glab stubs: logs the call and answers from fixture files in $FIXTURE.
#   <name>.<n>        the answer to the n-th call of <name>; after the last numbered file, the last one repeats
#   <name>            the answer when there are no numbered files
#   <name>[.<n>].exit the exit code (default 0); <name>[.<n>].stderr what goes to stderr
#   calls             every call, one per line
respond() {
	local name=$1 count_file n file pick=""
	shift
	echo "$*" >>"$FIXTURE/calls"
	count_file=$FIXTURE/.count-$name
	n=$(($(cat "$count_file" 2>/dev/null || echo 0) + 1))
	echo "$n" >"$count_file"
	for ((file = n; file >= 1; file--)); do
		if [ -e "$FIXTURE/$name.$file" ] || [ -e "$FIXTURE/$name.$file.exit" ]; then
			pick=$name.$file
			break
		fi
	done
	[ -n "$pick" ] || pick=$name
	[ ! -e "$FIXTURE/$pick.stderr" ] || cat "$FIXTURE/$pick.stderr" >&2
	[ ! -e "$FIXTURE/$pick" ] || cat "$FIXTURE/$pick"
	exit "$(cat "$FIXTURE/$pick.exit" 2>/dev/null || echo 0)"
}
