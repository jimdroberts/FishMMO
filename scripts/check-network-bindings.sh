#!/usr/bin/env bash
# Fails when a networked component's serialized owner points at a NetworkObject in another asset.
#
# FishNet caches the owning NetworkObject on every NetworkBehaviour in two hidden fields,
# _addedNetworkObject and _networkObjectCache. Inside a prefab or scene that reference is a bare
# fileID. A guid on it means it names a different asset, which is never valid for an owner and
# silently breaks targeting and health on the entity (PR #212). Unity reports nothing, so git does.
#
#   scripts/check-network-bindings.sh            scan every prefab and scene under FishMMO-Unity/Assets
#   scripts/check-network-bindings.sh --staged   scan the staged content of staged prefabs and scenes
#   scripts/check-network-bindings.sh FILE...    scan the given files
#
# The editor-side twin is FishMMO-Unity/Assets/.../Editor/NetworkObjectBindingValidator.cs; keep
# the pattern below in step with it.
set -u
pattern='_(addedNetworkObject|networkObjectCache): \{fileID: -?[0-9]+, guid: [0-9a-f]{32}'
root="$(cd "$(dirname "$0")/.." && pwd)"
status=0

report() { # $1 label, stdin content
	local hits
	hits="$(grep -nE "$pattern" || true)"
	if [ -n "$hits" ]; then
		status=1
		printf '%s\n' "$hits" | sed "s|^|$1:|"
	fi
}

if [ "${1:-}" = "--staged" ]; then
	while IFS= read -r -d '' file; do
		# Process substitution, not a pipe: a pipe would run report in a subshell and drop $status.
		case "$file" in *.prefab|*.unity) report "$file" < <(git show ":$file") ;; esac
	done < <(git diff --cached --name-only -z --diff-filter=ACMR)
elif [ $# -gt 0 ]; then
	for file in "$@"; do
		[ -f "$file" ] && report "$file" < "$file"
	done
else
	while IFS= read -r -d '' file; do
		report "$file" < "$file"
	done < <(find "$root/FishMMO-Unity/Assets" \( -name '*.prefab' -o -name '*.unity' \) -print0)
fi

if [ $status -ne 0 ]; then
	echo >&2
	echo "A NetworkBehaviour must be bound to the NetworkObject in its own asset." >&2
	echo "Fix: drop the ', guid: ..., type: N' from each line above (keep the fileID), or run" >&2
	echo "FishMMO > Validate > Repair NetworkObject Bindings in the Unity editor." >&2
fi
exit $status
