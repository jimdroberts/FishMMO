#!/usr/bin/env bash
# Restores the *.generated.cs files from their sentinel templates when missing.
#
# Run this after a fresh clone (or in CI before invoking Unity). It is a no-op for
# files that already exist, so it never clobbers real hosts, pins, or secrets.
#
# A missing file no longer breaks the compile — each one only overrides the sentinel
# defaults of a tracked class (HostConfig.cs, CertificatePinSet.cs, ClientGateSecret.cs)
# — but CI substitutes the real values INTO these files, so they must exist first.
# The Unity Editor does the same restore on load
# (Assets/Editor/GeneratedFiles/GeneratedFileRestorer.cs); this script exists for
# the case where Unity has not run yet, or is running headless in a build pipeline.
#
# It then checks the files that already existed against their templates and exits 1
# if one does not assign a value the template assigns. Restoring cannot fix that case —
# it never overwrites — so an old generated file silently keeps its old shape and that
# value stays a sentinel, which the build validator then refuses. Failing here is the
# same failure, several minutes earlier and naming the actual value.
# See https://github.com/jimdroberts/FishMMO/issues/122.

set -euo pipefail

template_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(dirname "$template_dir")"

files=(
	"Assets/Scripts/Shared/Implementation/HostConfig.generated.cs"
	"Assets/Scripts/Client/Security/CertificatePins.generated.cs"
	"Assets/Scripts/Client/Security/ClientApiSecret.generated.cs"
)

# Value names a generated file or template assigns inside its Configure method
# ("values.ApiHost ="). A file from before the partial-class split assigns none, so
# every value is reported for it — correctly, since nothing reads its old class.
declared_members() {
	# A file that assigns nothing makes grep exit 1, which pipefail would turn into
	# an abort; an empty member list is the answer here, not an error.
	{ grep -oE '^[[:space:]]*values\.[A-Za-z_][A-Za-z0-9_]*[[:space:]]*=' "$1" || true; } \
		| sed -E 's/^[[:space:]]*values\.//; s/[[:space:]]*=$//' \
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
			drift_report+="error: '$relative_path' does not assign $(echo "$missing" | wc -l | tr -d ' ') value(s) its template assigns:"$'\n'
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
	echo "error: $drifted generated file(s) are older than their template; the values above stay sentinels." >&2
	echo "Rewrite the file from FishMMO Dashboard > Game Settings, or paste the missing line(s) from the" >&2
	echo "template into its Configure method — it is not" >&2
	echo "overwritten automatically because it holds your real hosts, pins, and secret —" >&2
	echo "or delete the file and re-run this script to restore it, discarding those values." >&2
	exit 1
fi
