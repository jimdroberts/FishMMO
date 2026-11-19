# World Design module

**Short description:** The world's editor tools: the scene generator and the cut, points of interest and their
templates, paths and roads, caves, the procedural biome art, the terrain bakers (hydrology, erosion, props), housing plot
placement, the Solar System and World Atlas designers, and the world content refresh. Editor only: nothing of it ships
in a build.

| | |
|---|---|
| **Module id** | None: there is no runtime assembly and no `GameModule` |
| **Assemblies** | `FishMMO.WorldDesign.Editor`, `.Client.Editor`, `.RenderScratch`, `.Tests`, `.Harness.Editor` |
| **Depends on** | World Simulation, Water, Naming; its tests and harness also reference Terrain and the World Test Bed |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-18 |

## Folders

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

## Core seams it uses

| Seam | Where | What the module does |
|---|---|---|
| Dashboard pages | `DashboardFullPages` (core editor) | Registers Solar System and World Atlas under World. |
| Tool lock | `DashboardJobs` (core editor) | The biome art generator locks the dashboard's tool buttons while it runs. |
| Generated content | `AddressableGeneratedContent` (core editor) | The procedural art and planet surfaces are build output: kept out of the committed Addressables groups. |
| Weather content | `WeatherContentGenerator.BiomeGround` (World Simulation editor) | The biome spec says what each biome's ground gives the air. |
| Build steps | `BuildStep` (core build pipeline) | `WorldContentBuildSteps`: see Builds below. |
| Dashboard tools | `[DashboardTool]` | The generator, cut, bakers and probes. |

## What stays elsewhere

- **In the core** (`Shared/Implementation/Tools/Extensions/Unity/Editor/World`): `WorldEditorAssets`,
  `ClientBackdropScenes`, `SceneTerrainEvents`, `PortalViewBaker` (portals are core), and four small helpers split out of
  module tools: `SceneBakeAssets` (where a scene's bakes are kept), `TerrainTileLayerStore` (a tile's layer map, for the
  client build's layer stripper), `GeneratedArtPaths` and `ShaderPassTypes` (for the shader prewarm). The world maps
  build step (`WorldMapBuildSteps.cs`) is core too. The WorldSim test scene generator is in the World Test Bed.
- **In World Simulation's editor:** `WeatherContentGenerator` (the substances; the biomes' ground comes from this
  module), `WeatherTextureBaker`, `CloudNoiseBaker`, `WorldSystemsAudit` / `WorldSystemsConfigurator`, `SkyCostReport`.

## Removing it

Delete `Assets/FishMMO/Modules/WorldDesign` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
The game is unchanged; what goes is the ability to generate or edit world scenes. The procedural biome art is gitignored
and rebuilt by this module's generator, so a project without the module must commit that art, or not ship scenes that use
it.

## Builds

The module adds its own build steps: the procedural biome art (generated before anything is baked), the climate tables,
and the generated scenes' terrain arrays and planet surfaces (Order 290, before the core's world maps photograph them).
The core keeps the portal views, the world maps and the world scene details cache (`WorldMapBuildSteps`, Order 300).

**Removing the module from a project with generated scenes:** the procedural art is gitignored and rebuilt on every
machine by this module's generator. A project without the module must commit that art, or not ship scenes that use it.

## Templates

The point-of-interest templates and the NPC table are this module's content: `Templates/PointsOfInterest` (moved from
`Assets/FishMMO/Modules/WorldSimulation/Templates/World/PointsOfInterest`, 2026-11-18, GUIDs kept).

## Serialized data

The point-of-interest templates hold their features as `[SerializeReference]` data, which records the assembly. The 585
templates were rewritten from `FishMMO.Shared.Tools.Editor` to `FishMMO.WorldDesign.Editor`, and the feature classes carry
`[MovedFrom(false, sourceAssembly: "FishMMO.Shared.Tools.Editor")]` for any copy that was not.

## Tests across modules

A test assembly may not reference two optional modules: removing either would break it. Tests that ran the generator and
then read its output on the client were split at the baked data between them (2026-11-18): this module's half checks
what the generator stores; the Ambient Life module's half reads hand-made or template data.

## Editing generated props by hand

Generated structures, cliffs, boulders and trees are data, not GameObjects. Each source writes a `ScenePropSet` beside
the scene's terrain (for example `Moron 2 Props POI.asset`), and the client draws it instanced, which is why clicking a
building in the Scene view selects nothing. The **Edit Generated Props** tool fixes placement by hand, and the edits
survive every POI regenerate, repaint and cut.

**Using the tool:**

1. Open the scene and choose **Edit Generated Props** in the Scene view's Tools overlay (the prefab icon).
2. Click a generated prop to select it; its box is outlined. The panel's source list narrows what clicks pick ("Pick
   only POI", "Pick only Cliffs" and so on).
3. Change it:
   - **W / E / R** choose the move, rotate and scale handles. The change is applied when you let go.
   - The panel's fields take exact position, rotation and scale.
   - **Snap to ground** sets it on the terrain under it.
   - **Remove** (or Delete / Backspace) takes it out; **Restore as generated** puts it back.
   - **F** frames it; **Esc** deselects.
4. Every change is one Undo step.
5. Press **Rebake NavMesh** in the panel after moving anything that blocks.

**What moves with a structure:**

- **At once:** its doors, the rooms that darken its inside, its chimney smoke, fires and lamps, and its collision (server
  and client). A removed structure takes these with it.
- **On the next POI regenerate:** the ground dug under its floor.

**Where edits live:**

- `<Scene> Prop Edits.asset` beside the scene's terrain. It is plain text, so it diffs in reviews.
- An edit names its prop by what the generator gave it: its source, its prefab and where it was placed. Every bake of
  the same generation finds the prop again. POI pieces are matched as they are laid; other sources when their set is
  written.
- Select the asset (or press **Edits** in the panel) to list every edit.
  - **UNMATCHED** means the prop is no longer generated where it was, because its template or site changed. Unmatched
    edits are kept, never dropped.
  - **Frame** shows where the prop is, or was. **Forget** removes the edit and restores the generated placement.

**Code** (all in `Editor/Terrain/` unless noted):

- `ScenePropEdits` holds the data and matching (`ScenePropEditMath`).
- `ScenePropEditTool` is the Scene view tool; `MoveProp`, `RemoveProp` and `Forget` are public for scripts and tests.
- `ScenePropEditsEditor` draws the list.
- Edits are applied in `PointOfInterestSiteContext.AddProp` (`Editor/PointsOfInterest/`, POI pieces, while
  `ScenePropEdits.Active` is set by `PointOfInterestStage.Place`) and `ScenePropBaker.Write` (every other source).
- Tests: `ScenePropEditsTests` (matching, ray picking, wiring) and `ScenePropEditFlowTests` (move, remove, re-bake,
  forget, in a throwaway scene).

## License

This project is subject to the FishMMO project license.
