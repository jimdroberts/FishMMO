#!/usr/bin/env python3
"""Sequential native-1440p/4x-MSAA standalone comparisons; close other GPU workloads first.

Build with PlayerPerfBuild. Each run is bounded by guarded_run.py and must write DONE.
HLOD suites require a scene baked with FishMMO/Rendering/Bake experimental HLOD for open scene.
"""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path, default=Path('Builds/RenderingBenchmark/three-player/FishRenderingBenchmark.x86_64'))
    parser.add_argument('--out', type=Path)
    parser.add_argument('--suite', choices=('combined', 'isolated', 'visual', 'hlod', 'weather', 'three', 'three-visual', 'all'), default='combined')
    parser.add_argument('--weather', choices=('scene', 'clear', 'overcast', 'rain', 'storm'), default='scene')
    parser.add_argument('--samples', type=int, default=900)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    player = args.player.resolve()
    if not player.is_file():
        parser.error(f'Player missing: {player}; build PlayerPerfBuild first')
    if not 60 <= args.samples <= 3600:
        parser.error('--samples must be between 60 and 3600')
    output = (args.out or root / 'Library/RenderingBenchmark' / ('comparison-' + datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S'))).resolve()
    if output.exists():
        parser.error(f'Use a new output directory: {output}')
    jobs = []
    common = ['--samples=' + str(args.samples), '--warm=240', '--screenshots=false', '--weather=' + args.weather]
    if args.suite in ('combined', 'all'):
        for i, enabled in enumerate((False, True, True, False)):
            name = f'combined-{i}-' + ('optimized' if enabled else 'legacy')
            jobs.append((name, common + ['--mode=baseline', '--views=ground,forest-vista,forest-ground,rocks-vista,settlement-vista', '--index-filter=' + ('fast' if enabled else 'full'), '--grass-looks=' + ('cached' if enabled else 'per-vertex')]))
    if args.suite in ('isolated', 'all'):
        for mode in ('terrain-index', 'grass-looks'):
            jobs.append((mode, common + ['--mode=' + mode, '--views=ground,forest-ground,settlement-vista']))
    if args.suite in ('hlod', 'all'):
        jobs.append(('hlod', common + ['--mode=abba', '--views=ground,forest-vista,forest-ground,rocks-vista,settlement-vista,settlement-horizon,hlod-diagnostic']))
    if args.suite in ('visual', 'all'):
        for mode, views in [('terrain-index', 'ground,settlement-vista'), ('grass-looks', 'forest-ground')]:
            jobs.append((mode + '-visual', ['--weather=' + args.weather, '--mode=' + mode, '--views=' + views, '--samples=60', '--warm=240', '--clock=frozen', '--moving=false', '--screenshots=true']))
    if args.suite == 'weather':
        for weather in ('clear', 'scene', 'overcast', 'rain', 'storm'):
            jobs.append(('weather-' + weather, ['--weather=' + weather, '--mode=baseline', '--views=ground,sky-up,forest-ground,settlement-vista', '--samples=' + str(args.samples), '--warm=240', '--screenshots=true']))
    if args.suite == 'three':
        for mode in ('grass-coarse', 'grass-colour'):
            jobs.append(('three-' + mode, common + ['--mode=' + mode, '--views=ground,forest-ground,settlement-vista']))
        for i, enabled in enumerate((False, True, True, False)):
            jobs.append((f'three-combined-{i}', common + ['--mode=baseline', '--views=ground,forest-vista,forest-ground,rocks-vista,settlement-vista', '--grass-coarse=' + ('on' if enabled else 'off'), '--grass-colour=' + ('prepared' if enabled else 'full'), '--foam=' + ('shortcut' if enabled else 'full')]))
        foam = [('--samples=' + str(min(args.samples * 2, 3600))) if a.startswith('--samples=') else a for a in common]
        jobs.append(('three-foam', foam + ['--mode=foam', '--views=river-foam', '--water-fixture=true', '--moving=false']))
    if args.suite == 'three-visual':
        jobs.append(('three-visual', ['--mode=three', '--views=ground,forest-ground,settlement-vista', '--samples=60', '--warm=240', '--weather=' + args.weather, '--clock=frozen', '--moving=false', '--screenshots=true']))
        jobs.append(('water-visual', ['--mode=foam', '--water-fixture=true', '--views=river-foam', '--samples=60', '--warm=240', '--weather=' + args.weather, '--clock=frozen', '--moving=false', '--screenshots=true']))
    for name, options in jobs:
        out = output / name
        command = [sys.executable, str(root / 'Scripts/Rendering/guarded_run.py'), '--out', str(out), '--timeout', '1200', '--gpu', '--', str(player), '-force-vulkan', '-force-x11', '-screen-fullscreen', '1', '-screen-width', '2560', '-screen-height', '1440', '-logFile', str(out / 'player.log'), '--out=' + str(out)] + options
        print('START ' + name, flush=True)
        result = subprocess.run(command, cwd=root)
        if result.returncode or not (out / 'DONE').is_file():
            print(f'FAILED {name}; see {out}', file=sys.stderr)
            return result.returncode or 2
        print('DONE ' + name, flush=True)
    print(f'Results: {output}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
