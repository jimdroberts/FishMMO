#!/usr/bin/env python3
"""Run an owned local render test with bounded memory, logs and optional GPU telemetry.

Example: python3 Scripts/Rendering/guarded_run.py --out Library/RenderingBenchmark/run --gpu --timeout 900 -- <player> <arguments>
Only the new command's process group is signalled. Does not close an existing Editor.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import time


def group_rss(pid):
    total = 0
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            if int((entry / 'stat').read_text().rsplit(')', 1)[1].split()[2]) != pid:
                continue
            total += next((int(line.split()[1]) * 1024 for line in (entry / 'status').read_text().splitlines() if line.startswith('VmRSS:')), 0)
        except (OSError, ValueError, ProcessLookupError):
            pass
    return total


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--timeout', type=int, default=3600)
    parser.add_argument('--max-gib', type=float, default=14)
    parser.add_argument('--min-free-gib', type=float, default=4)
    parser.add_argument('--gpu', action='store_true')
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        parser.error('a command after -- is required')
    args.out.mkdir(parents=True, exist_ok=True)
    start = time.monotonic()
    peak = 0
    stop = None
    with (args.out / 'stdout.log').open('w') as log, (args.out / 'gpu.csv').open('w') as telemetry:
        telemetry.write('seconds,name,pstate,graphics_mhz,memory_mhz,gpu_percent,power_w,temp_c,vram_mib\n')
        process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            while process.poll() is None:
                rss = group_rss(process.pid)
                peak = max(peak, rss)
                available = next(int(line.split()[1]) * 1024 for line in Path('/proc/meminfo').read_text().splitlines() if line.startswith('MemAvailable:'))
                if rss > args.max_gib * 1024**3 or available < args.min_free_gib * 1024**3:
                    stop = 'memory_guard'
                elif time.monotonic() - start > args.timeout:
                    stop = 'timeout'
                if stop:
                    os.killpg(process.pid, signal.SIGTERM)
                    break
                if args.gpu:
                    try:
                        query = subprocess.run(['nvidia-smi', '--query-gpu=name,pstate,clocks.current.graphics,clocks.current.memory,utilization.gpu,power.draw,temperature.gpu,memory.used', '--format=csv,noheader,nounits'], capture_output=True, text=True, timeout=3)
                        telemetry.write(f'{time.monotonic() - start:.2f},{query.stdout.strip()}\n')
                        telemetry.flush()
                    except (subprocess.SubprocessError, OSError):
                        pass
                time.sleep(1)
            try:
                code = process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                code = process.wait()
        except BaseException:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait()
            raise
    result = dict(exitCode=code, stop=stop, peakGroupRss=peak, seconds=time.monotonic() - start, command=command)
    (args.out / 'process.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))
    return code if code else (2 if stop else 0)


if __name__ == '__main__':
    raise SystemExit(main())
