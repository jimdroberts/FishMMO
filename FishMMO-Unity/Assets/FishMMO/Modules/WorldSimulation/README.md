# World Simulation module

**Short description:** The world's sky, weather, climate and biomes, simulated on the shared clock and presented on the client:
the solar system and calendar, the day-night cycle and sky, the weather timelines, storms and the director, precipitation,
clouds and fog, the biome templates and maps, the world atlas, and the weather's sounds.

Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-18. Sky and Time,
Climate and Weather, Biomes and the atlas reference each other in a cycle, so they are one module (Jim, 2026-11-18).

## Project Structure

```
Assets/FishMMO/Modules/WorldSimulation/
├── Runtime/  FishMMO.WorldSimulation          Weather/, Celestial/, Atlas/, Biomes/, Network/ (weather broadcasts),
│                                              ECA/ (weather actions and conditions, ChangeSkyProfileAction),
│                                              SceneData/ (WorldDayNightCycle, glow modes and places, sea hall vents,
│                                              mill rotors, generated landforms and geothermal ground, shelter map)
├── Client/   FishMMO.WorldSimulation.Client   Weather/ (presentation, sky, the four render features), Looks/ (flora
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

## How it plugs in

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
| Module list | `[assembly: GameModule]` (Core) | `WorldSimulationModule` ("world-simulation"). |

## What stays in the core

The world contracts (clock, tick, weather reading, water query, climbing), the core surface shaders and their neutral
defaults, the terrain material shaders, weather exposure, the race and wildlife templates (biome affinity by id, habitat
flags), and the scene object namer.

## Depends on it

Terrain, Ambient Life, Naming, World Design, the World Test Bed and Water. Its gate removes all seven.

## License

This project is subject to the FishMMO project license.
