# Ability Crafting module

**Short description:** Combining a known ability template with ability events into a new ability at an ability
crafter, paid in currency, and the crafting window.

| | |
|---|---|
| **Module id** | `abilitycrafting` (`AbilityCraftingModule`) |
| **Assemblies** | `FishMMO.AbilityCrafting`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (abilities, currency) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-17. The predicted core (items, abilities, combat) stays in the core until Phase 6; crafting is the part of abilities that became a module. |

## Folders

```
Assets/FishMMO/Modules/AbilityCrafting/
├── Runtime/              FishMMO.AbilityCrafting          AbilityCraftBroadcast, AbilityCraftResultBroadcast,
│                                                          AbilityCraftFailure, AbilityCraftingModule
├── Server/               FishMMO.AbilityCrafting.Server   AbilityCraftingSystem (+ asset, runtime data): validates the
│                                                          recipe, charges inside the grant's settle callback, hands the
│                                                          grant to AbilitySystem.TryGrantAbility
├── Client/               FishMMO.AbilityCrafting.Client   UITKAbilityCraft + UIAbilityCraft.prefab/uxml/uss
├── Editor/RenderScratch/ FishMMO.AbilityCrafting.RenderScratch   The crafter in the ability panel renders
├── Tests/                FishMMO.AbilityCrafting.Tests    Craft result and wiring tests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `SceneServer_Systems` label (`Server.AdoptModuleSystems`) | `AbilityCraftingSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label (`ModulePanelHost`) | `UIAbilityCraft.prefab` is placed into the world GUI. |
| `AbilityCrafterBroadcast` (core) | The window opens when a crafter's ECA action sends it. |
| `Accomplishments.Record` | A crafter's achievement is credited on a successful craft. |
| `[assembly: GameModule]` | `AbilityCraftingModule` ("abilitycrafting"). |

## What stays in the core

Abilities: templates, events, learning, the grant funnel and prediction. The ability crafters placed in the world, their
ECA action and the message it sends (`AbilityCrafterBroadcast`). The currency persistence and ledger helpers on
`ServerBehaviour`.

## Removing it

Delete `Assets/FishMMO/Modules/AbilityCrafting` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). A crafter then opens nothing. No table belongs to the module.

## License

This project is subject to the FishMMO project license.
