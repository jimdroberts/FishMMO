# World Simulation module

**Short description:** The world's sky, weather, climate and biomes, simulated on the shared clock and presented on the
client: the solar system and calendar, the day-night cycle and sky, the weather timelines, storms and the director,
precipitation, clouds and fog, the biome templates and maps, the world atlas, and the weather's sounds.

| | |
|---|---|
| **Module id** | `world-simulation` (`WorldSimulationModule`) |
| **Assemblies** | `FishMMO.WorldSimulation`, `.Server`, `.Client`, `.Editor`, `.Tests`; editor `FishMMO.Client.Weather.Editor`, `FishMMO.Shared.Biomes.Editor` |
| **Depends on** | The core; its tests reference Water |
| **Depended on by** | Water, Terrain, Ambient Life, Naming, World Design, World Test Bed; Abilities (`WeatherAbilityModifier`, which moved into the Abilities module) |
| **Remove together with** | All six: the seven world modules go together |
| **Moved** | Out of the core. Sky and Time, Climate and Weather, Biomes and the atlas referenced each other in a cycle, so they are one module. |

## Folders

```
Assets/FishMMO/Modules/WorldSimulation/
├── Runtime/  FishMMO.WorldSimulation          Weather/, Celestial/, Atlas/, Biomes/, Network/ (weather broadcasts),
│                                              ECA/ (weather actions and conditions, ChangeSkyProfileAction),
│                                              SceneData/ (WorldDayNightCycle, glow modes and places, sea hall vents,
│                                              mill rotors, generated landforms and geothermal ground, shelter map)
├── Client/   FishMMO.WorldSimulation.Client   Weather/ (presentation, sky, the four module render passes), Looks/ (flora
│                                              palette, glow, burn and ground glow tables, site fires and lights, cave
│                                              skylights), Audio/ (world audio, thunder, wind, drips, footsteps)
├── Server/   FishMMO.WorldSimulation.Server   WeatherHost, WeatherServerService, WorldSimulationOperatorCommands
├── Editor/   Weather/ (FishMMO.Client.Weather.Editor), Biomes/ (FishMMO.Shared.Biomes.Editor),
│             World/ (FishMMO.WorldSimulation.Editor: world systems audit, content generator, texture bakers,
│             solar system and atlas assets, the Weather and Biome Tools dashboard pages)
├── Templates/ Biomes/, Substances/, World/ (Atlas; Systems is generated and gitignored)
├── Settings/  Weather Render Profile (with the render features' settings), Weather Audio Profile, materials, textures
├── Shaders/   The sky, clouds, fog, precipitation, vortex and heat shimmer shaders
├── Tests/     FishMMO.WorldSimulation.Tests
└── README.md
```

## Core seams it uses

| Seam | Where (core) | What the module does |
|---|---|---|
| Weather reading | `WeatherQuery` (Sampler, SceneHasWeather) | Answers for every scene with a timeline; calm without it. |
| Clock | `WorldClock`, `WorldTick`, `WorldEpoch` | Reads them; the calendar installs the epoch. |
| Shelter, habitat | `ShelterQuery`, `BiomeHabitatQuery` | The AI's shelters and the spawners' habitats. |
| Client service | `ClientServices` | The client's mirror of the scene's weather. |
| Scene server service | `SceneServerModuleServices` | The weather host, started after the clock. |
| Render passes | `ModuleRenderFeatures` on the renderers' `FishModulePassesFeature` | Clouds, height fog, froxel fog, heat shimmer. |
| Fog switch | `FogComposer.DrawFog` | The sky reads and writes the core's switch. |
| Operator commands | `OperatorCommandProvider` | `/gm weather`, `/gm clock`, `/admin weather`. |
| Map capture | `WorldMapCaptureContext.Resolver` | Places the scene and lights the map at the equinox noon. |
| World systems audit | `WorldSystemsHooks` | Checks each scene's atlas entry, climate and sky with the scene details cache. |
| Server preload | `ServerPreloadLabels` | Registers the biomes label, so the server loads the biome templates at start. |
| Module list | `[assembly: GameModule]` | `WorldSimulationModule` ("world-simulation"). |

## What stays in the core

The world contracts (`WorldClock`, `WorldTick`, `WeatherQuery`, `WaterQuery`, `ShelterQuery`, `BiomeHabitatQuery`,
climbing), the core surface shaders and their neutral defaults, the terrain material shaders, weather exposure, the race
and wildlife templates (biome affinity by id, habitat flags), the scene object namer, `FogComposer`, and the module render
pass host (`FishModulePassesFeature`) on the three URP renderers.

## Removing it

Delete the seven world modules: `WorldSimulation`, `Water`, `Terrain`, `AmbientLife`, `Naming`, `WorldDesign` and
`WorldTestBed` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)). World scenes then have no
sky, weather, clouds or fog: the camera clears to a plain sky colour (`CoreSkyColour`), the scene's own directional light
lights it, and the weather reads calm everywhere. World scenes built with the module keep missing scripts that point into
it; a core-only game uses scenes made without it, such as the Core Gate world
(`Tests/TestHarness/Editor/CoreGateSceneGenerator.cs`).

## License

This project is subject to the FishMMO project license.
