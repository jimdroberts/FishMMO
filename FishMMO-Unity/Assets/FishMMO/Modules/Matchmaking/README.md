# Matchmaking module

**Short description:** The group finder, the dungeon finder and dungeon instances (`/closedungeon`), arenas and arena
matches: queues, ready checks, scoring, ratings, results, and their windows.

Phase 3 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), moved 2026-11-17. Depends on
Social through the core party contracts (`IPartyController`, `IPartySystem`).

| Folder | Assembly | Holds |
|---|---|---|
| Runtime/ | FishMMO.Matchmaking | Group finder, dungeon finder and arena broadcasts and serializers, `ArenaRules`, `ArenaRating`, `ArenaHarmRelation`, `MatchmakingModule` |
| Server/ | FishMMO.Matchmaking.Server | `MatchmakingSystem` (+ asset, `SceneServer_Systems`): group finder, dungeon finder, arena and arena match partials; its own request guard and main-thread queue |
| Client/ | FishMMO.Matchmaking.Client | Arena board, HUD, results and scoreboard, dungeon finder (+ prefabs), arena client events, flags, team styles |
| Editor/RenderScratch/ | | The dungeon finder fixture, its footer probe, and the arena HUD in the HUD probes |
| Tests/ | FishMMO.Matchmaking.Tests | Arena rules, hot path, wire and wiring tests |

## What stays in the core

The world and its content: dungeon and arena templates, dungeon difficulties (instancing uses them), the dungeon
entrances, arena boards and arena objectives placed by the world generator, their interfaces and ECA actions and the
two messages those actions send (`DungeonFinderBroadcast`, `ArenaBoardBroadcast`), `ArenaRespawnBroadcast` (the death
dialog), `ArenaEventKind`/`ArenaEventData` (arena templates' triggers), `ArenaTeamRegistry` (Factions and arena chat
read it), `GroupFinderRules` and its state and refusal enums, the arena spectator camera, and the `ArenaStandings` seam
the Leaderboards module fills. Without the module the entrances and boards open nothing.

These were partials of `InteractableSystem`; they are their own system now. The group finder, dungeon instance, arena
rating and match tables stay in place, unused, without the module.
