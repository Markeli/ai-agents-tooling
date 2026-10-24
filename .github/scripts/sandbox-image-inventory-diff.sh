#!/usr/bin/env bash
# Prints the toolchain changes between two inventories (name<TAB>version lines) as a Markdown table of the added,
# removed and changed rows. With "-" as the old inventory, prints the whole new inventory instead.
# Usage: sandbox-image-inventory-diff.sh <old.tsv|-> <new.tsv> <old label> <new label>
set -euo pipefail

old=$1 new=$2 old_label=$3 new_label=$4

# Inventory values are plain versions and commits; a pipe in one would still break the table
# shellcheck disable=SC2016 # an awk program, expanded by awk
row='{ for (i = 1; i <= NF; i++) gsub(/\|/, "\\|", $i); printf "|"; for (i = 1; i <= NF; i++) printf " %s |", $i; print "" }'

if [ "$old" = - ]; then
	echo "No earlier release with an inventory to compare with. Full inventory:"
	echo
	echo "| Item | $new_label |"
	echo "| --- | --- |"
	sort "$new" | awk -F '\t' "NF $row"
	exit 0
fi

rows=$(awk -F '\t' '
	FILENAME == ARGV[1] { if (NF) { old[$1] = $2; seen[$1] = 1 }; next }
	NF { cur[$1] = $2; seen[$1] = 1 }
	END {
		for (k in seen) {
			o = (k in old) ? old[k] : "—"
			n = (k in cur) ? cur[k] : "—"
			if (o != n) printf "%s\t%s\t%s\n", k, o, n
		}
	}
' "$old" "$new" | sort | awk -F '\t' "$row")

if [ -z "$rows" ]; then
	echo "No toolchain changes since $old_label."
	exit 0
fi
echo "| Item | $old_label | $new_label |"
echo "| --- | --- | --- |"
echo "$rows"
