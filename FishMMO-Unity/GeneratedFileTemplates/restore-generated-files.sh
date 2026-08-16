#!/usr/bin/env bash
# Restores the *.generated.cs files from their sentinel templates when missing.
#
# Run this after a fresh clone (or in CI before invoking Unity). It is a no-op for
# files that already exist, so it never clobbers real hosts, pins, or secrets.
#
# The Unity Editor does the same thing automatically on load
# (Assets/Editor/GeneratedFiles/GeneratedFileRestorer.cs); this script exists for
# the case where Unity has not run yet, or is running headless in a build pipeline.

set -euo pipefail

template_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_root="$(dirname "$template_dir")"

files=(
	"Assets/Scripts/Shared/Implementation/HostConfig.generated.cs"
	"Assets/Scripts/Client/Security/CertificatePins.generated.cs"
	"Assets/Scripts/Client/Security/ClientApiSecret.generated.cs"
)

restored=0
for relative_path in "${files[@]}"; do
	target="$project_root/$relative_path"
	template="$template_dir/$(basename "$relative_path").template"

	if [ -f "$target" ]; then
		continue
	fi

	if [ ! -f "$template" ]; then
		echo "error: missing template '$template' for '$relative_path'" >&2
		exit 1
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
