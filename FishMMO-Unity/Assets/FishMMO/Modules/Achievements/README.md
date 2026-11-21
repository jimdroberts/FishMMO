# Achievements module

**Short description:** A player's achievement progress and tiers: counting accomplishments, granting tier rewards,
saving progress, announcing a completed tier, and the achievements window. [Server/README.md](Server/README.md)
documents the achievement system; the templates and tiers are documented with the core
([Shared/Implementation/Entity/Achievement/README.md](../../Shared/Implementation/Entity/Achievement/README.md)).

| | |
|---|---|
| **Module id** | `achievements` (`AchievementModule`) |
| **Assemblies** | `FishMMO.Achievements`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (the accomplishment signal, `TemplateOf` references to its templates); Abilities (abilities and ability events granted as tier rewards); Buffs (buffs granted as tier rewards). Its templates (`AchievementTemplate`, `AchievementTier`, `AchievementCategory`) are the module's since 2026-11-20, in `Runtime/Template` and `Templates/` |
| **Depended on by** | World Design (the points of interest's discovery achievements). Everything else that counts an achievement (Social, Matchmaking, Pets, merchants, lore) holds it as a core `CachedScriptableObject` and records through the core's `Accomplishments` |
| **Remove together with** | Nothing |
| **Moved** | Out of the core, 2026-11-20 (Phase 5: the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Achievements/
├── Runtime/              FishMMO.Achievements          AchievementController (a CharacterPart), IAchievementController,
│                                                       Achievement, AchievementBroadcasts, AchievementCompletedCondition,
│                                                       AchievementModule (counts the Accomplishment signal)
├── Server/               FishMMO.Achievements.Server   AchievementSystem (+ asset): batched progress, tier rewards, the
│                                                       spawn send; IAchievementSystem; AchievementPersistence (participant)
├── Client/               FishMMO.Achievements.Client   UITKAchievements + UIAchievements.prefab/uxml/uss; AchievementsClient
│                                                       (the completed-tier announcement)
├── Editor/RenderScratch/ FishMMO.Achievements.RenderScratch   The fake controller, fixture and window render
├── Tests/                FishMMO.Achievements.Tests    Counting, the participant, dirty tracking, wiring
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `Accomplishment` signal / `Accomplishments.Record` | `AchievementModule` counts every recorded accomplishment on the server toward the character's achievement. |
| `[assembly: CharacterPart]` | `AchievementController` is added to every player at wake; no prefab carries it. |
| `[assembly: CharacterPersistence]` | `AchievementPersistence` over `character_achievements`. |
| `CharacterSystem.OnSpawnCharacter` | The system sends a spawned player its achievements. |
| `SceneServer_Systems` label | `AchievementSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | `UIAchievements.prefab` is placed into the world GUI; the Achievements key still opens it by name. |
| `ClientMilestoneDisplay.ShowMilestone` | The floating label over the character when a tier completes (with a system chat line). |
| `[assembly: GameModule]` | `AchievementModule` ("achievements"). |

## What stays in the core

Content all over the game names achievements, so the core keeps `AchievementTemplate`, `AchievementTier`,
`AchievementCategory` and the template database; the `Accomplishment` signal and `Accomplishments.Record`, which every
feature calls where progress happens; and `AchievementIncrementAction`, the ECA action the core combat triggers
(Count Death, Count Monster Kill, …) carry, which records an accomplishment. The interactables' optional
`AchievementTemplate` fields stay, and their actions record through the signal.

## Removing it

Delete `Assets/FishMMO/Modules/Achievements` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Accomplishments are then recorded and counted toward nothing, there is no achievements window, and no tier rewards
are granted. The `character_achievements` table and its rows stay in place, unused. The leaderboards that rank
achievement values (Monsters Slain, Explorers) keep reading the table and stop moving.

## License

This project is subject to the FishMMO project license.
