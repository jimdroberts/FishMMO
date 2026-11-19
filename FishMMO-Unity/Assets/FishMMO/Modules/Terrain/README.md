# Terrain module

**Short description:** The client's own drawing of the ground and what grows and lies on it: the GPU-driven trees, rocks
and props (with their HLOD and cached shadow casters), the heightmap draw, the blade grass and the detail scatter, the
rock seam fillets, the cave cover, footprints and trodden grass, and the moss layer's globals. All of it is client
presentation of the scene's baked data.

| | |
|---|---|
| **Module id** | `terrain` (`TerrainModule`) |
| **Assemblies** | `FishMMO.Terrain`, `.Client`, `.Editor`, `.Tests` |
| **Depends on** | World Simulation (the weather's moss and render profile); its tests also reference Water |
| **Depended on by** | World Test Bed (the WorldSim harness drives the renderer's switches), World Design (its tests check generated content against the renderer's limits) |
| **Remove together with** | World Test Bed, World Design, Naming (Naming's tests reference the World Test Bed) |
| **Moved** | Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-18 |

## Folders

```
Assets/FishMMO/Modules/Terrain/
├── Runtime/   FishMMO.Terrain          TerrainModule (the module list's entry)
├── Client/    FishMMO.Terrain.Client   TerrainGpuRenderer (+ partials), CliffRockInstancing, tree/detail instancing,
│                                       WorldHlod, RockFillets/RockContactField, Grass/, Scatter/, Heightmap/, Caves/ (cover),
│                                       GroundColourMap, GroundMaterialMap, GroundTrailMap, MossShaderGlobals + MossOverlay,
│                                       VegetationDistanceFade, TerrainRenderProfile, TerrainClient (the installer)
├── Editor/    FishMMO.Terrain.Editor   TerrainRenderAssets (fills the profile with the weather's), TerrainDetailBaker,
│                                       the terrain diagnostics' dashboard tools, MossEditorPreview
├── Shaders/                            Grass, scatter, cave cover, rock fillets, heightmap compute and draw, instancing, HLOD
├── Settings/                           Terrain Render Profile.asset (a client static addressable)
├── Tests/     FishMMO.Terrain.Tests    The module's tests (Probes/: the grass kernels' test computes)
└── README.md
```

## Core seams it uses

Every system starts itself in play mode: it hooks the render pipeline after the first scene loads and draws round each
camera from the scene's baked data.

| Seam | Where | What the module does |
|---|---|---|
| Ground cover | `GroundCoverQuery` (core client) | Installs the grass cover and the builders' state: footsteps, the wind's beds, ambient life, the frame series. |
| Camera opt-out | `CameraGroundCover` (core client) | Reads it: the minimap draws no ground cover. |
| Weather overlay | `WeatherOverlays` (World Simulation) | Registers the moss ("terrain.moss"), whose dampness follows the biome under the camera. |
| Shadow cache | `ShadowCacheCasters` (core client) | The GPU renderer registers as a cached static shadow caster. |
| Render profile | `WeatherRenderAssets.RegisterModuleProfile` (World Simulation editor) | Fills its own profile whenever the weather's is filled. |
| Dashboard tools | `[DashboardTool]` | The terrain diagnostics. |
| Module list | `[assembly: GameModule]` | `TerrainModule` ("terrain"). |

## What stays elsewhere

- **In the core:** the baked scene data it draws from (`Shared/.../WorldSceneDetails/Terrain`: tile layers, index maps,
  detail bakes, prop colliders), the ground's lit shaders (the core surface stack and the terrain material's shaders),
  and lighting it shares with every surface (`Client/World/Terrain`: `IndoorRooms`, `DirectionalShadowDraws`,
  `CaveLavaHeat`, `TerrainHoleMask`, `SyncTextureReadback`).
- **In World Simulation:** the cave skylights (`CaveSkylights`; their shaft shader is on the core world-passes feature),
  `SiteLightPool`, `SiteFireState`, and the biome look tables the shaders read (`FloraPaletteMap`, `GlowModeTable`,
  `BurnMosaicTable`, `GroundGlowTable`).

## Removing it

Delete `Assets/FishMMO/Modules/Terrain` and the modules in "Remove together with" (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). A scene's Unity terrain then draws itself (its FishMMO ground
material is core), and there are no GPU trees, props, grass or scatter.

## License

This project is subject to the FishMMO project license.
