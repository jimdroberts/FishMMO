# Ambient Life module

**Short description:** The world's ambient life and site effects, all client presentation: the land's birds, bats and
small animals, sea life and what floats on the sea, motes in the air, falling leaves, and the life of the world's sites
(fire embers and flames, lighthouse beams, cave drips and water life, sea hall vents, rockfalls).

Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17.

## Project Structure

```
Assets/FishMMO/Modules/AmbientLife/
├── Runtime/   FishMMO.AmbientLife          AmbientLifeModule (the module list's entry)
├── Client/    FishMMO.AmbientLife.Client   AmbientLife/ (land animals, AmbientLifeRenderProfile), SeaLife/, SeaFloats/,
│                                           SiteLife/ (embers, flames, lighthouse beams, cave drips, cave water life,
│                                           sea hall vents, rockfall), Air/ (air motes, falling leaves), AmbientLifeClient
├── Editor/    FishMMO.AmbientLife.Editor   AmbientLifeRenderAssets (fills the profile with the weather's)
├── Shaders/                                The module's shaders and the sea floats' compute
├── Settings/                               Ambient Life Render Profile.asset (a client static addressable)
├── Tests/     FishMMO.AmbientLife.Tests    The module's tests
└── README.md
```

## How it plugs in

Every system starts itself in play mode: it hooks the render pipeline after the first scene loads and draws round each
camera from the scene's baked data and the shared clock. Nothing is simulated on the server, and nothing is sent.

| Seam | Where | What the module does |
|---|---|---|
| Weather overlay | `WeatherOverlays` (Client, new) | Registers the air motes, drawn with the weather from the weather being shown. |
| Render profile | `WeatherRenderAssets.RegisterModuleProfile` (editor, new) | Fills its own profile whenever the weather's is filled. |
| Test bed switches | `RenderSwitches` (Client, new) | `ambientLife`, `ambientLifeMax`, `ambientHighlight`. |
| Perf report | `RenderReports` (Client, new) | The "ambient life" line in the scene perf probe. |
| Module list | `[assembly: GameModule]` (Core) | `AmbientLifeModule` ("ambient-life"). |

## What stays in the core

- The site data it draws from: the scenes' emitters, cave life spots, sea hall vents and cave water, all baked by the
  world generator and kept in the scenes whether or not this module is installed.
- The cave skylights (`CaveSkylights`): the openings' light reaches every lit surface and the sky, so it stays with the
  terrain; its shaft shader is referenced from the terrain render profile.
- Whether a fire burns (`SiteFireState`): the weather decides it, and the site lights read it too.
- The wetland measure (`TerrainProcess.Wetland`): moss reads it as well as the wisps.
- The "Falling Leaves" option: without the module it does nothing.

The module reads the weather, the sky, the terrain and the water (they are still in the core; when they become modules
this one depends on them).

## License

This project is subject to the FishMMO project license.
