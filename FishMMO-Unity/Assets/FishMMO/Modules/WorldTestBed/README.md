# World Test Bed module

**Short description:** The world test bed: the WorldSim scene components (controller, camera, panel, walker), the
harness settings and switches, the player perf and WebGPU smoke probes, the scene perf probe, cloud walk, cut digest,
HLOD baker, the WorldSim scene generator, the shader variant log, and the weather and sky sim renders. Development
tooling that drives the world modules' internals; no game code names it.

| | |
|---|---|
| **Module id** | None (development tooling, no `GameModule`) |
| **Assemblies** | `FishMMO.TestHarness.World`, `FishMMO.WorldTestBed`, `FishMMO.TestHarness.World.Editor`, `FishMMO.TestHarness.Weather.Editor`, `FishMMO.TestHarness.Sky.Editor` |
| **Depends on** | World Simulation, Water, Terrain |
| **Depended on by** | World Design (its tests and harness), Naming (its tests) |
| **Remove together with** | World Design, Naming |
| **Moved** | Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-18 |

## Folders

```
Assets/FishMMO/Modules/WorldTestBed/
├── Scene/          FishMMO.TestHarness.World          WorldSimController, WorldSimCamera, WorldSimPanel (+ uss),
│                                                     WorldSimWalker, WorldSimWalkerBody
├── World/          FishMMO.WorldTestBed               HarnessSettings, PlayerPerfProbe, WebGpuSmokeProbe
│   └── Editor/     FishMMO.TestHarness.World.Editor   ScenePerfProbe, CloudWorldWalkProbe, CutDigestProbe,
│                                                     WorldHlodBaker, WorldSimSceneGenerator, ShaderVariantLog
├── Weather/Editor/ FishMMO.TestHarness.Weather.Editor WeatherSimRender
├── Sky/Editor/     FishMMO.TestHarness.Sky.Editor     SkySimRender
└── README.md
```

The editor assemblies keep their old names; the runtime part is `FishMMO.WorldTestBed`.

## Core seams it uses

| Seam | What the module does |
|---|---|
| `RenderSwitches` | `HarnessSettings` applies the modules' switches by name (Ambient Life's `ambientLife`, `ambientLifeMax`, `ambientHighlight`). |
| Terrain's renderers (direct reference) | The harness and the perf probe drive and measure `TerrainGpuRenderer`, `CliffRockInstancing`, the grass and the scatter directly. |
| `RenderReports` | The scene perf probe prints every module's report line. |
| `WeatherQuery` | The probes read the weather being shown. |
| `[DashboardTool]` | Generate World Sim scene; Render Weather Sim; Render Sky Sim. |

## What stays in the core

Nothing of the test bed. The core test harness (`Assets/FishMMO/Tests/TestHarness`) is separate: the four simulation
scenes and the Core Gate world.

## Removing it

Delete `Assets/FishMMO/Modules/WorldTestBed`, `WorldDesign` and `Naming` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). World Design's scene dressing (`GeneratedSceneDressing`) puts
the WorldSim controller and camera into every generated world scene, so those scenes show missing scripts while the
module is removed; the asset audit accepts them during the gate. World Design's harness
(`Modules/WorldDesign/Tests/Harness`) builds the perf player from this module.

## License

This project is subject to the FishMMO project license.
