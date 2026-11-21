# NPCs & AI module

**Short description:** The server's NPC brains and spawners. The AI (archetypes, states, combat decisions, threat,
behaviour trees, boss scripts, level of detail), the object spawners and their baked spawn tables, the AI and spawner
systems, and the editor tools that design NPC prefabs and manage AI assets.

| | |
|---|---|
| **Module id** | `npcs` (`NpcsModule`) |
| **Assemblies** | `FishMMO.NPCs`, `.Server`, `.Editor`, `.Tests` |
| **Depends on** | The core; Abilities (an NPC casts); Buffs (the AI reads and applies buffs); Combat |
| **Depended on by** | Pets, Wildlife (both drive their creatures with this AI), Staff (simulated players), World Design (spawner placement) |
| **Remove together with** | Pets, Wildlife, Staff, World Design |
| **Moved** | Out of the core, 2026-11-20 |

The NPC character itself (`NPC`) stays in the core: a merchant or quest giver needs no brain.

## Folders

```
Assets/FishMMO/Modules/NPCs/
├── Runtime/              FishMMO.NPCs          NpcsModule; IAICompanion and AICompanions (the companion seam a pet implements);
│                                               PetStance, PetMovementOrder (a companion's orders, read by the AI)
├── Server/               FishMMO.NPCs.Server   AI/ (AIController, states, archetypes, threat, behaviour trees, boss scripts,
│                                               AIBrainHost and AIBrainCatalogue), Spawner/ (ObjectSpawner, spawn settings,
│                                               SpawnerRuntime), Contracts/; AISystem.asset, SpawnerSystem.asset
├── Editor/               FishMMO.NPCs.Editor   The dashboard's NPCs group (NpcDesignerCategory: design an NPC prefab from a
│                                               recipe; NpcDashboardCategories; SpawnTablesCategory), NPCPrefabFactory,
│                                               AIAssetOrganizer, AIStateExtensions, the behaviour tree editor, the spawner
│                                               gizmo labels, spawn table baking
├── Templates/AI/         Archetypes, states, personalities, rotations, behaviour trees, LOD, aim, conditions
└── Tests/                FishMMO.NPCs.Tests    The AI, threat, spawner and NPC factory tests
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `NpcsModule` ("npcs"). |
| `IAICompanion`, `AICompanions` (this module) | The AI reads a pet's owner, stance and order through the interface the Pets module's `Pet` implements, and hears owner changes through `AICompanions.OnOwnerChanged`. This module never references Pets. |
| `IWildlifeSpecies`, `WildlifeRules` (core) | Wildlife speeds, herd sizes and habitats are read through the core species interface, so this module never references Wildlife. |
| `NPC.CompanionOwner` (core) | Who a companion fights for, for the harm rule and credit. |
| `AbilityGrants.Equip` (core) | An NPC's authored kit is equipped by the Abilities module as the NPC starts on the server. |
| `SceneServer_Systems` label | `AISystem.asset` and `SpawnerSystem.asset` are adopted by the scene server. |
| Dashboard hooks | `TemplateCategory.ShowInspector`/`Invalidate`/`AfterDuplicate`/`AfterRename`/`ShowLanding` and the `FishMMODashboard` host surface for the NPC designer and the spawn tables; `DashboardHooks` for AI asset folders and the behaviour tree button. |
| `AIStateExtensions` (this module, editor) | Other modules add AI state types to the dashboard and the asset organizer (the Pets module's pet states). |

Further detail: [AI README](Server/AI/README.md), [Spawner README](Server/Spawner/README.md).
