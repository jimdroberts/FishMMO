# Quests module

**Short description:** Quests: templates, a player's quest log, the quest system that accepts, advances, completes,
turns in and abandons them, quest givers, and the quest ECA actions and conditions.

| | |
|---|---|
| **Module id** | `quests` (`QuestsModule`) |
| **Assemblies** | `FishMMO.Quests`, `.Server`, `.Editor`, `.Tests` |
| **Depends on** | The core |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core. The plan grouped quests with achievements as "Objectives"; achievements stay in the core, because every system records accomplishments (`Accomplishments.Record`) and many core assets name an `AchievementTemplate`. |

## Folders

```
Assets/FishMMO/Modules/Quests/
├── Runtime/   FishMMO.Quests          QuestController (a CharacterPart), IQuestController, QuestInstance,
│                                      QuestObjectiveInstance, QuestStatus, Template/ (QuestTemplate, QuestObjective,
│                                      QuestAttributeRequirement), QuestInteractable / IQuestInteractable,
│                                      QuestBroadcasts, QuestEventDispatch, ECA/ (accept, advance, complete, fail, turn in,
│                                      abandon, send offer; has quest, can accept, status, objective complete;
│                                      QuestEventData, QuestObjectiveAmountValue), QuestsModule
├── Server/    FishMMO.Quests.Server   QuestSystem (+ asset; sends a spawned player its quests), runtime data, contracts,
│                                      QuestPersistenceParticipant (load)
├── Editor/    FishMMO.Quests.Editor   QuestDashboardCategories: the Dashboard's "Quests" category (templates in
│                                      Assets/FishMMO/Modules/Quests/Templates)
├── Tests/     FishMMO.Quests.Tests    QuestsModuleTests, QuestEventDispatchTests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: CharacterPart]` | `QuestController` is added to every player at wake. |
| `[assembly: CharacterPersistence]` | `QuestPersistenceParticipant` loads a player's quests; the quest system writes them when they change, not the character save. |
| `SceneServer_Systems` label | `QuestSystem.asset` is adopted by the scene server. |
| `IDashboardCategoryProvider` | `QuestDashboardCategories`. |
| `[assembly: GameModule]` | `QuestsModule` ("quests"). |

## What stays in the core

Achievements (`AchievementTemplate`, `Accomplishments`, the achievement system). The ECA framework the quest actions and
conditions plug into.

## Removing it

Delete `Assets/FishMMO/Modules/Quests` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
There is no quest content yet (no templates, no quest givers placed), so nothing in the world changes. The
`character_quests` table stays in place, unused.

## License

This project is subject to the FishMMO project license.
