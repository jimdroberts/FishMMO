#!/usr/bin/env bash
#
# Publishes the standalone Updater for every platform FishMMO ships a client on.
#
# The .NET apphost is platform- and architecture-specific, so a Windows client and a
# Linux client each need their own binary; there is no portable one. All targets
# cross-publish from any host, so a single machine (including an Arch/CachyOS box) can
# produce the whole set.
#
# Output, one directory per RID:
#   Updater/bin/Release/net8.0/<rid>/publish/Updater[.exe]
#
# That layout is exactly what the FishMMO Dashboard's BuildExecutor.CopyUpdaterToBuild
# looks for when it copies the updater into a client build, so publishing here is what
# makes a client build shippable.
#
# Usage:
#   ./publish-updater.sh                  # win-x64 and linux-x64 (the shipped defaults)
#   ./publish-updater.sh --all            # every RID declared in Updater.csproj
#   ./publish-updater.sh linux-arm64 ...  # an explicit list
#
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project="${script_dir}/Updater/Updater.csproj"

# Defaults: the two targets Unity can actually produce a standalone client for.
default_rids=(win-x64 linux-x64)
# Everything Updater.csproj declares, for shipping to hosts Unity does not target directly.
all_rids=(win-x64 win-x86 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)

if [[ $# -eq 0 ]]; then
	rids=("${default_rids[@]}")
elif [[ "$1" == "--all" ]]; then
	rids=("${all_rids[@]}")
else
	rids=("$@")
fi

if ! command -v dotnet >/dev/null 2>&1; then
	echo "error: 'dotnet' is not on PATH. Install the .NET 8 SDK (or newer) first." >&2
	exit 1
fi

echo "Publishing Updater for: ${rids[*]}"
echo

for rid in "${rids[@]}"; do
	echo "=== ${rid} ==="
	# Self-contained + single-file + compression come from Updater.csproj, which applies
	# them whenever a RuntimeIdentifier is set. Passing them here too would just be a
	# second place to keep in sync.
	dotnet publish "${project}" -c Release -r "${rid}" --nologo

	publish_dir="${script_dir}/Updater/bin/Release/net8.0/${rid}/publish"
	case "${rid}" in
		win-*) exe="Updater.exe" ;;
		*)     exe="Updater" ;;
	esac

	if [[ ! -f "${publish_dir}/${exe}" ]]; then
		echo "error: expected '${publish_dir}/${exe}' but it was not produced." >&2
		exit 1
	fi

	# The executable bit does not survive a copy onto a filesystem that drops it, and the
	# Dashboard re-applies it after copying; set it here so the publish output is directly
	# runnable too.
	[[ "${exe}" == "Updater" ]] && chmod +x "${publish_dir}/${exe}"

	echo "    -> ${publish_dir}/${exe}"
	echo
done

echo "Done. The FishMMO Dashboard copies these into client builds automatically."
