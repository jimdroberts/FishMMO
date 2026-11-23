# Pets module

**Short description:** Pets and summons: the pet character, a player's pet roster and slots, the scene server's pet
system (summon, dismiss, orders, stance, attack priority, persistence across logins), pet AI states, and the pet control
panel.

| | |
|---|---|
| **Module id** | `pets` (`PetsModule`) |
| **Assemblies** | `FishMMO.Pets`, `.Server`, `.Client`, `.Editor`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core; Abilities (pet abilities, summon events); Buffs (a pet's buffs); Combat; NPCs & AI (a pet is driven by the AI) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core |

## Folders

```
Assets/FishMMO/Modules/Pets/
├── Runtime/              FishMMO.Pets          PetsModule; Pet (an NPC; IPersistedPet, IAICompanion), PetController (IPetRoster),
│                                               PetSlotRules, PetAttackPriority, pet broadcasts and events, HasPetCondition
├── Server/               FishMMO.Pets.Server   PetSystem (+ PetSystem.asset); AI/ (PetIdleState, PetAttackingState)
├── Client/               FishMMO.Pets.Client   UITKPetControl (+ UIPetControl.prefab, uxml, uss), PetsClient (the owner's name
│                                               on a pet's nameplate)
├── Editor/               FishMMO.Pets.Editor   PetVariantBuilder, PetAIStates (registers the pet states with the NPCs editor)
└── Tests/                FishMMO.Pets.Tests
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `IPersistedPet`, `IPetRoster` (core) | The character save captures each pet out (slot, template, abilities, attributes, buffs) through these; the Abilities module's summon pre-filter asks `IPetRoster.AllPetSlotsTaken`. |
| `NPC.CompanionOwner` (core) | `Pet` answers its owner, so the harm rule, loot rights, kill credit and party meters see through a pet to its owner without naming it. |
| `IAICompanion` (NPCs module) | The AI follows, guards and obeys a pet through it. |
| `SceneServer_Systems` label | `PetSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIPetControl.prefab` is placed into the world GUI. |

Further detail: [Pet README](Server/Pet/README.md).
