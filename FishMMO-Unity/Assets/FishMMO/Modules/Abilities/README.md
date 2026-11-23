# Abilities module

**Short description:** Everything a character casts. Ability templates and ability events, crafted abilities and their
predicted activation, cooldowns, ability objects (projectiles, areas, hitscan, movement), ability knowledge (what a
character may craft with), the ability ECA actions and conditions, the scene server's ability system (granting and
forgetting abilities, the known-abilities login send), and the client's ability book, cast bar and cast nameplates.

| | |
|---|---|
| **Module id** | `abilities` (`AbilitiesModule`) |
| **Assemblies** | `FishMMO.Abilities`, `.Server`, `.Client`, `.Editor`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core; Combat (damage and heal actions act through the damage controller); World Simulation (`WeatherAbilityModifier`); Buffs (abilities apply and read buffs) |
| **Depended on by** | Ability Crafting, Achievements (tier rewards), Archetypes, Commerce (premade abilities, knowledge sales), Hotkeys, Items (consumable casts), NPCs & AI, Pets, Wildlife, Staff, World Interactables (lore rewards), Mounts (the Mount ability, AbilityPressOverrides); and through them Matchmaking, Social, World Design |
| **Remove together with** | Every module above |
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Abilities/
├── Runtime/              FishMMO.Abilities         AbilitiesModule; Ability/ (AbilityController and its partials, Ability,
│                                                   AbilityObject, templates, cooldowns, activation, modifiers, snapshots,
│                                                   AbilityGrantsInstaller); Contracts/ (IAbilityKnowledgeController,
│                                                   cooldown contracts); ECA/ (ability actions, conditions, data, value
│                                                   providers; ApplyDamage/Heal, KnockbackHit, ChargeStageValue);
│                                                   Network/ (ability broadcasts and observer serializers);
│                                                   Templates/ (scroll consumables)
├── Server/               FishMMO.Abilities.Server  IAbilitySystem, AbilitySystem (+ AbilitySystem.asset)
├── Client/               FishMMO.Abilities.Client  UITKAbilities, UITKCastBar (+ prefabs, uxml, uss), ClientCastNameplateDisplay
├── Editor/               FishMMO.Abilities.Editor  Dashboard entries (Abilities, Ability Events), MockConsumableBuilder (the
│                                                   potions and scrolls); RenderScratch/ (cast bar, ability tooltips, panels)
├── Templates/            Types/, Events/ (the ability and ability event assets; the Mock/ set was deleted)
└── Tests/                FishMMO.Abilities.Tests   The activation, prediction, persistence and wire tests that exercise abilities
```

The test ability suite generator (`FishMMO.AbilityContent.Editor`, `Assets/FishMMO/Editor/AbilityContent`) stays outside the
module: it writes abilities, premades and merchant stock, so it belongs to no single module.

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `AbilitiesModule` ("abilities"). |
| `IAbilityController`, `ICooldownController` (core) | The controller implements the core's view of casting (the cast in progress, the longest reach, live ability objects) that prediction, observer streaming and the cast nameplate read. `IAbilityKnowledgeController` (the module's) extends it with knowledge and crafted abilities. |
| `IAbilityPersistence` (core) | The character system loads and saves abilities and knowledge through it, under the character's claims and versions, without naming a module type. |
| `AbilityGrants.Granter`, `AbilityGrants.Equipper` (core) | Installed by `AbilityGrantsInstaller`: content that names abilities (a lore reward, `AbilityGrants.Grant`) teaches them; an NPC's authored kit is equipped as it starts on the server (`AbilityGrants.Equip`). |
| `IAbilityObject`, `AbilityObjects` (core) | Target selection and FX actions resolve an ability object and hold its spawn effect through these hooks. |
| `IAbilityTemplateInfo`, `TemplateOf` (core) | Core content (race starting abilities, NPC kits, lore) holds ability templates as `CachedScriptableObject` with `[TemplateOf("AbilityTemplate")]`. |
| `SceneServer_Systems` label | `AbilitySystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIAbilities.prefab` and `UICastBar.prefab` are placed into the world GUI. |
| `ClientServices` | `ClientCastNameplateDisplay` ("abilities.cast-nameplates"). |
| Dashboard providers | `AbilityDashboardCategories` adds Abilities and Ability Events to the Character group. |

## What stays in the core

`IAbilityController`, `ICooldownController`, `CooldownInstance`, `CooldownReconcileEntry`, `AbilityType`,
`AbilityActivationFlags`, `IAbilityPersistence`, `AbilityGrants`, `IAbilityObject`/`AbilityObjects`, `IAbilityTemplateInfo`,
the prediction controller (it reads the cast through `IAbilityController`), and the character save pipeline.

Further detail: [Ability/README.md](Runtime/Ability/README.md), [Cooldown/README.md](Runtime/Ability/Cooldown/README.md).
