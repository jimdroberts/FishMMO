#!/usr/bin/env bash
# Restores the *.generated.cs files from their sentinel templates when missing.
#
# Run this after a fresh clone (or in CI before invoking Unity). It is a no-op for
# files that already exist, so it never clobbers real hosts, pins, or secrets.
#
# The Unity Editor does the same thing automatically on load
# (Assets/Editor/GeneratedFiles/GeneratedFileRestorer.cs); this script exists for
# the case where Unity has not run yet, or is running headless in a build pipeline.
#
# It then checks the files that already existed against their templates and exits 1
# if one is missing a member the template declares. Restoring cannot fix that case —
# it never overwrites — so an old generated file silently keeps its old shape and the
# only symptom is a CS0117 in an assembly that looks unrelated. Failing here is the
# same failure, several minutes earlier and naming the actual field.
# See https://github.com/jimdroberts/FishMMO/issues/122.

set -euo pipefail

template_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(dirname "$template_dir")"

files=(
	"Assets/Scripts/Shared/Implementation/HostConfig.generated.cs"
	"Assets/Scripts/Client/Security/CertificatePins.generated.cs"
	"Assets/Scripts/Client/Security/ClientApiSecret.generated.cs"
)

# Public const / static readonly field names a generated file or template declares.
# The trailing "=" keeps method and property declarations out of the results.
declared_members() {
	# A file that declares nothing makes grep exit 1, which pipefail would turn into
	# an abort; an empty member list is the answer here, not an error.
	{ grep -oE '^[[:space:]]*public[[:space:]]+(const|static[[:space:]]+readonly)[[:space:]]+[^[:space:]=;]+[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*=' "$1" || true; } \
		| sed -E 's/[[:space:]]*=$//; s/.*[[:space:]]([A-Za-z_][A-Za-z0-9_]*)$/\1/' \
		| sort -u
}

restored=0
drifted=0
drift_report=""
for relative_path in "${files[@]}"; do
	target="$project_root/$relative_path"
	template="$template_dir/$(basename "$relative_path").template"

	if [ ! -f "$template" ]; then
		echo "error: missing template '$template' for '$relative_path'" >&2
		exit 1
	fi

	if [ -f "$target" ]; then
		# Already present, so it was never touched by a restore. Report anything the
		# template declares that it does not, rather than leaving it to the compiler.
		missing="$(comm -23 <(declared_members "$template") <(declared_members "$target"))"
		if [ -n "$missing" ]; then
			# Held until after the restore summary so the two do not interleave.
			drift_report+="error: '$relative_path' does not declare $(echo "$missing" | wc -l | tr -d ' ') member(s) its template declares:"$'\n'
			drift_report+="$(echo "$missing" | sed 's/^/  /')"$'\n'
			drifted=$((drifted + 1))
		fi
		continue
	fi

	mkdir -p "$(dirname "$target")"
	cp "$template" "$target"
	echo "restored $relative_path"
	restored=$((restored + 1))
done

if [ "$restored" -eq 0 ]; then
	echo "All generated files present — nothing to restore."
else
	echo "Restored $restored file(s) from sentinel templates."
	echo "Open FishMMO Dashboard > Game Settings to write your real hosts, pins, and secret."
fi

if [ "$drifted" -ne 0 ]; then
	echo >&2
	printf '%s' "$drift_report" >&2
	echo >&2
	echo "error: $drifted generated file(s) are older than their template and will not compile." >&2
	echo "Paste the missing declaration(s) from the template into the file — it is not" >&2
	echo "overwritten automatically because it holds your real hosts, pins, and secret —" >&2
	echo "or delete the file and re-run this script to restore it, discarding those values." >&2
	exit 1
fi
