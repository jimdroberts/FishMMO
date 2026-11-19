# Terrain module

**Short description:** The client's own drawing of the ground and what grows and lies on it: the GPU-driven trees, rocks
and props (with their HLOD and cached shadow casters), the heightmap draw, the blade grass and the detail scatter, the rock
seam fillets, the cave cover, footprints and trodden grass, and the moss layer's globals. All of it is client presentation
of the scene's baked data.

Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-18.

## Project Structure

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

## How it plugs in

Every system starts itself in play mode: it hooks the render pipeline after the first scene loads and draws round each
camera from the scene's baked data. Without the module, a scene's Unity terrain draws itself (its FishMMO ground material
is core) and there are no GPU trees, props, grass or scatter.

| Seam | Where | What the module does |
|---|---|---|
| Ground cover | `GroundCoverQuery` (Client, new) | Installs the grass cover and the builders' state: footsteps, the wind's beds, ambient life, the frame series. |
| Camera opt-out | `CameraGroundCover` (Client, new) | Reads it: the minimap draws no ground cover. |
| Weather overlay | `WeatherOverlays` (Client) | Registers the moss ("terrain.moss"), whose dampness follows the biome under the camera. |
| Shadow cache | `ShadowCacheCasters` (Client) | The GPU renderer registers as a cached static shadow caster. |
| Render profile | `WeatherRenderAssets.RegisterModuleProfile` (editor) | Fills its own profile whenever the weather's is filled. |
| Module list | `[assembly: GameModule]` (Core) | `TerrainModule` ("terrain"). |

## What stays in the core

- The baked scene data it draws from (`Shared/.../WorldSceneDetails/Terrain`: tile layers, index maps, detail bakes,
  prop colliders) and the ground's lit shaders (the core surface stack and the terrain material's shaders).
- Lighting it shares with every surface: `CaveSkylights` (its shaft shader is on the core world-passes feature),
  `IndoorRooms`, `SiteLightPool`, `SiteFireState`, `DirectionalShadowDraws`, `CaveLavaHeat`.
- The biome look tables the core shaders read: `FloraPaletteMap`, `GlowModeTable`, `BurnMosaicTable`, `GroundGlowTable`
  (to the Biomes module next), `TerrainHoleMask` (the clouds read it), `SyncTextureReadback`.

## Depends on it

- World Design (its tests check the generated content against the renderer's limits).
- World Test Bed (the WorldSim harness drives the renderer's switches).

## License

This project is subject to the FishMMO project license.
