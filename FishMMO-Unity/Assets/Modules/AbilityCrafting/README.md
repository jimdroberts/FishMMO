# Ability Crafting module

**Short description:** Combining a known ability template with ability events into a new ability at an ability
crafter, paid in currency, and the crafting window.

Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17. Under the
decision that the predicted core (items, abilities, combat) stays in the core until Phase 6, crafting is the part of
Abilities that becomes a module now.

| Folder | Assembly | Holds |
|---|---|---|
| Runtime/ | FishMMO.AbilityCrafting | `AbilityCraftBroadcast`, `AbilityCraftResultBroadcast`, `AbilityCraftFailure`, `AbilityCraftingModule` |
| Server/ | FishMMO.AbilityCrafting.Server | `AbilityCraftingSystem` (+ asset, `SceneServer_Systems`): validates the recipe, charges inside the grant's settle callback, hands the grant to `AbilitySystem.TryGrantAbility` |
| Client/ | FishMMO.AbilityCrafting.Client | The crafting window (+ prefab, `ClientWorldGUI_Panels`) |
| Editor/RenderScratch/ | | The crafter in the ability panel renders |
| Tests/ | FishMMO.AbilityCrafting.Tests | Craft result tests and wiring tests |

## What stays in the core

Abilities: templates, events, learning, the grant funnel and prediction. The ability crafters placed in the world, their
ECA action and the message it sends to open the window (`AbilityCrafterBroadcast`). The currency persistence and ledger
helpers on `ServerBehaviour`. Without the module a crafter opens nothing.
