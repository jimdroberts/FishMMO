# Wildlife module

**Short description:** Animals outside the faction system: prey, predators and territorial beasts. The species templates,
the procedural body, the wildlife harm relation, the flee state, the planner that places wildlife spawners across a world
and the builder that makes the species prefabs.

| | |
|---|---|
| **Module id** | `wildlife` (`WildlifeModule`) |
| **Assemblies** | `FishMMO.Wildlife`, `.Server`, `.Editor`, `.Tests` |
| **Depends on** | The core; Abilities; Combat; NPCs & AI (an animal is an NPC driven by the AI) |
| **Depended on by** | World Design (the wildlife spawner cut), Mounts (the mounts are drawn with the wildlife bodies) |
| **Remove together with** | World Design |
| **Moved** | Out of the core |

## Folders

```
Assets/FishMMO/Modules/Wildlife/
├── Runtime/              FishMMO.Wildlife          WildlifeModule; WildlifeTemplate (IWildlifeSpecies), WildlifeBody and its
│                                                   mesh, WildlifeHarmRelation
├── Server/               FishMMO.Wildlife.Server   WildlifePlanner, WildlifeCatalogue, AI/WildlifeFleeState
├── Editor/               FishMMO.Wildlife.Editor   WildlifeBuilder
├── Templates/Wildlife/   The species
└── Tests/                FishMMO.Wildlife.Tests
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `IWildlifeSpecies`, `WildlifeRules` (core) | The species' role, carcass time, herd size, habitats and speeds, read by the core's harm rule and nameplates and by the NPCs module's AI and spawners. The core keeps `WildlifeRole` and the rules. |
| `[assembly: HarmRelation]` | `WildlifeHarmRelation`: who an animal may harm, outside factions. |
| `NPC.Wildlife` (core) | An NPC prefab names its species (a `ScriptableObject`, read as `IWildlifeSpecies`). |
| `GroundQuery`, `WildlifeRules.LivesOn` (core) | Every spawn reads the ground its cast struck: underground (cave rock, or several metres under the terrain) against the species' `Shelter`, and the painted ground (`TerrainGroundKinds`, baked by the server build from the splat maps it strips) against its `Surfaces`. Giant bats are `Cave` only and placed as colonies on dark cave floors (`WildlifePlanner`). |
