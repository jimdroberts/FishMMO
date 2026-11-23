# Ambient Life module

**Short description:** The world's ambient life and site effects, all client presentation: the land's birds, bats and
small animals, sea life and what floats on the sea, motes in the air, falling leaves, and the life of the world's sites
(fire embers and flames, lighthouse beams, cave drips and water life, sea hall vents, rockfalls).

| | |
|---|---|
| **Module id** | `ambient-life` (`AmbientLifeModule`) |
| **Assemblies** | `FishMMO.AmbientLife`, `.Client`, `.Editor`, `.Tests` |
| **Depends on** | World Simulation (the weather, sky and biomes it draws with); its tests also reference Water |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core |

## Folders

```
Assets/FishMMO/Modules/AmbientLife/
├── Runtime/   FishMMO.AmbientLife          AmbientLifeModule
├── Client/    FishMMO.AmbientLife.Client   AmbientLife/ (land animals, AmbientLifeRenderProfile), SeaLife/, SeaFloats/,
│                                           SiteLife/ (embers, flames, lighthouse beams, cave drips, cave water life,
│                                           sea hall vents, rockfall), Air/ (air motes, falling leaves), AmbientLifeClient
├── Editor/    FishMMO.AmbientLife.Editor   AmbientLifeRenderAssets (fills the profile with the weather's)
├── Shaders/                                The module's shaders and the sea floats' compute
├── Settings/                               Ambient Life Render Profile.asset (a client static addressable)
├── Tests/     FishMMO.AmbientLife.Tests
└── README.md
```

## Core seams it uses

Every system starts itself in play mode: it hooks the render pipeline after the first scene loads and draws round each
camera from the scene's baked data and the shared clock. Nothing is simulated on the server, and nothing is sent.

| Seam | Where | What the module does |
|---|---|---|
| Ground cover | `GroundCoverQuery` (core client) | Reads the grass cover, so it does not need the Terrain module. |
| Test bed switches | `RenderSwitches` (core client) | `ambientLife`, `ambientLifeMax`, `ambientHighlight`. |
| Perf report | `RenderReports` (core client) | The "ambient life" line in the scene perf probe. |
| Weather overlay | `WeatherOverlays` (World Simulation) | Registers the air motes, drawn with the weather from the weather being shown. |
| Render profile | `WeatherRenderAssets.RegisterModuleProfile` (World Simulation editor) | Fills its own profile whenever the weather's is filled. |
| Module list | `[assembly: GameModule]` | `AmbientLifeModule` ("ambient-life"). |

## What stays elsewhere

- **In the core:** the site data it draws from (the scenes' emitters, cave life spots, sea hall vents and cave water,
  baked by the world generator and kept in the scenes), and the "Falling Leaves" option (`ClientVisualSettings`), which
  does nothing without the module.
- **In World Simulation:** whether a fire burns (`SiteFireState`; the weather decides it and the site lights read it),
  the cave skylights (`CaveSkylights`), and the wetland measure (`TerrainProcess.Wetland`), which moss reads too.

## Removing it

Delete `Assets/FishMMO/Modules/AmbientLife` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
The world then has no animals, sea life, motes, leaves or site effects; nothing else changes.

## License

This project is subject to the FishMMO project license.
