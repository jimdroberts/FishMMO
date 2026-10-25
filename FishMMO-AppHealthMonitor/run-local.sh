#!/usr/bin/env bash
set -euo pipefail
monitor_source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
monitor_build_dir="$monitor_source_dir/../Builds/AppHealthMonitor/Debug/net8.0"
if [[ ! -x "$monitor_build_dir/AppHealthMonitor" ]]; then
    echo "Build AppHealthMonitor/AppHealthMonitor.csproj before running the local monitor." >&2
    exit 1
fi
# The daemon prefers appsettings.json in its working directory.
cd -- "$monitor_build_dir"
exec ./AppHealthMonitor "$@"
