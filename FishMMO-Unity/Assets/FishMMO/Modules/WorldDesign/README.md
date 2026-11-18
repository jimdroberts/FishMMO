# World Design module

**Short description:** The world's editor tools: the scene generator and the cut, points of interest and their templates,
paths and roads, caves, the procedural biome art, the terrain bakers (hydrology, erosion, props), housing plot placement,
the Solar System and World Atlas designers, and the world content refresh. Editor only: nothing of it ships in a build.

Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-18.

## Project Structure

```
Assets/FishMMO/Modules/WorldDesign/
├── Editor/        FishMMO.WorldDesign.Editor          The tools (moved from Tools/Extensions/Unity/Editor/World, folders kept),
│                                                     WorldDesignEditor (the installer)
│   └── RenderScratch/ FishMMO.WorldDesign.RenderScratch   WorldDesignRender (renders the two designer pages)
├── ClientEditor/  FishMMO.WorldDesign.Client.Editor   WorldContentRefresh, PointOfInterestMapRender (they read client types)
├── Templates/                                         The point-of-interest templates and NPC table
├── Tests/         FishMMO.WorldDesign.Tests           The module's tests (moved from Tests/UnitTests)
│   └── Harness/   FishMMO.WorldDesign.Harness.Editor  GeneratedSceneDressing, LookArrayProbe, PlayerPerfBuild, WebGpuSmokeBuild
└── README.md
```

There is no runtime assembly and no `GameModule`: a build never runs any of it. What it makes is ordinary content (scenes,
terrains, baked data, templates, generated art), and that content stays and plays without the module.

## How it plugs in

| Seam | Where | What the module does |
|---|---|---|
| Dashboard pages | `DashboardFullPages` (core editor, new) | Registers Solar System and World Atlas under World. |
| Tool lock | `DashboardJobs` (core editor, new) | The biome art generator locks the dashboard's tool buttons while it runs. |
| Generated content | `AddressableGeneratedContent` (core editor, new) | The procedural art and planet surfaces are build output: kept out of the committed Addressables groups. |
| Weather content | `WeatherContentGenerator.BiomeGround` (core editor, new) | The biome spec says what each biome's ground gives the air. |

## What stays in the core

The editor helpers that core tools use stay in `Tools/Extensions/Unity/Editor/World`: `WorldEditorAssets`,
`WeatherContentGenerator` (the substances; the biomes' ground comes from this module), `WeatherTextureBaker`,
`CloudNoiseBaker`, `ClientBackdropScenes`, `SceneTerrainEvents`, `WorldSystemsAudit`/`WorldSystemsConfigurator`,
`SkyCostReport`, `PortalViewBaker` (portals are core), and four small helpers split out of module tools: `SceneBakeAssets` (where a scene's bakes are kept),
`TerrainTileLayerStore` (a tile's layer map, for the client build's layer stripper), `GeneratedArtPaths` and
`ShaderPassTypes` (for the shader prewarm). The WorldSim test scene generator stays in the core test harness.

## Builds

The module adds its own build steps: the procedural biome art (generated before anything is baked), the climate tables,
and the generated scenes' terrain arrays and planet surfaces (Order 290, before the core's world maps photograph them).
The core keeps the portal views, the world maps and the world scene details cache (`WorldMapBuildSteps`, Order 300).

**Removing the module from a project with generated scenes:** the procedural art is gitignored and rebuilt on every
machine by this module's generator. A project without the module must commit that art, or not ship scenes that use it.

## Templates

The point-of-interest templates and the NPC table are this module's content: `Templates/PointsOfInterest` (moved from
`Assets/Templates/World/PointsOfInterest`, 2026-11-18, GUIDs kept).

## Serialized data

The point-of-interest templates hold their features as `[SerializeReference]` data, which records the assembly. The 585
templates were rewritten from `FishMMO.Shared.Tools.Editor` to `FishMMO.WorldDesign.Editor`, and the feature classes carry
`[MovedFrom(false, sourceAssembly: "FishMMO.Shared.Tools.Editor")]` for any copy that was not.

## Tests across modules

A test assembly may not reference two optional modules: removing either would break it. Tests that ran the generator and
then read its output on the client were split at the baked data between them (2026-11-18): this module's half checks
what the generator stores; the Ambient Life module's half reads hand-made or template data.

## License

This project is subject to the FishMMO project license.
