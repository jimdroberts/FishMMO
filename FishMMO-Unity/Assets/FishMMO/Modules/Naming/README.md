# Naming module

**Short description:** The name generator: character names and titles, city, dungeon, point of interest and item names,
the grammar, modifier and title pool templates, the editor's Name Generation page, and the names scene objects are
given.

| | |
|---|---|
| **Module id** | `naming` (`NamingModule`) |
| **Assemblies** | `FishMMO.Naming`, `.Tests`; editor `FishMMO.Shared.NameGenerator.Editor` |
| **Depends on** | World Simulation (biome templates and phonology); its tests also reference Water and the World Test Bed |
| **Depended on by** | World Design (it names its places with this module) |
| **Remove together with** | World Design |
| **Moved** | Phase 4 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-18 |

## Folders

```
Assets/FishMMO/Modules/Naming/
├── Runtime/    FishMMO.Naming                       NameGenerator, the builders, NameGrammar, RaceRegistry, ModifierRegistry,
│                                                    TitlePoolRegistry, the templates, SceneObjectNameResolver,
│                                                    NamingInstaller (+ SceneObjectNameSource), NamingModule
├── Editor/     FishMMO.Shared.NameGenerator.Editor  NameGeneratorWindow, the template loader and CSV tools, the dashboard's
│                                                    Naming categories and Name Generation page
├── Templates/                                       The grammar, modifier and title pool assets (from Assets/Templates/Entity/Naming)
├── Tests/      FishMMO.Naming.Tests
└── README.md
```

## Core seams it uses

| Seam | Where | What the module does |
|---|---|---|
| Scene object names | `SceneObjectNames.Source` (core) | Installs `SceneObjectNameSource`, the generator behind the namer. |
| Races | `RaceTemplate.RaceLoaded` / `RaceUnloaded` / `Loaded` (core) | `RaceRegistry` follows the races the core loads. |
| Template picker | `TemplateReferenceAttribute(string)` (core) | The namer's modifier field names `NameModifierTemplate` by name. |
| Dashboard | `DashboardFullPages` | The Name Generation page, in the Naming group. |
| Module list | `[assembly: GameModule]` | `NamingModule` ("naming"). |

## What stays in the core

The naming data on the templates other systems load: `RaceNamingData` and its serializable types on every race,
`SerializableBiomePhonology` on every biome (World Simulation's biome templates), the shared vocabulary (`POIType`, `CityType`, `ItemType`, `TitleRegister`,
`RacePhonology`, `BiomePhonology`), `StableHash` and `GeneratorUtility`. And the `SceneObjectNamer` on the interactables and
NPCs, with its `SceneObjectNamingSettings`: its seed, gender and place travel in its spawn payload whether or not the module
is installed.

## Removing it

Delete `Assets/FishMMO/Modules/Naming` and `Assets/FishMMO/Modules/WorldDesign` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). Every scene object then keeps its authored name.

## License

This project is subject to the FishMMO project license.
