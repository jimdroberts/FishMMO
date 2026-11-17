# Quests module

**Short description:** Quests: templates, a player's quest log, the quest system that accepts, advances, completes,
turns in and abandons them, quest givers, and the quest ECA actions and conditions.

Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17. The plan
grouped quests with achievements as "Objectives"; achievements stay in the core, because every system records
accomplishments (`Accomplishments.Record`) and dozens of core assets name an `AchievementTemplate`.

| Folder | Assembly | Holds |
|---|---|---|
| Runtime/ | FishMMO.Quests | `QuestController` (a CharacterPart), `IQuestController`, quest and objective instances, `QuestTemplate`, `QuestInteractable`/`IQuestInteractable`, broadcasts, `QuestEventDispatch`, `ECA/` (actions, conditions, `QuestEventData`, `QuestObjectiveAmountValue`), `QuestsModule` |
| Server/ | FishMMO.Quests.Server | `QuestSystem` (+ asset, `SceneServer_Systems`; sends a spawned player its quests), runtime data, contracts, `QuestPersistenceParticipant` (load) |
| Editor/ | FishMMO.Quests.Editor | The Dashboard's "Quests" category (templates in `Assets/Modules/Quests/Templates`) |
| Tests/ | FishMMO.Quests.Tests | `QuestsModuleTests`, `QuestEventDispatchTests` |

There is no quest content yet (no templates, no quest givers placed). Quests are written by the quest system when they
change; the `character_quests` table stays in place, unused, without the module.
