# Matchmaking module

**Short description:** The group finder, the dungeon finder and dungeon instances (`/closedungeon`), arenas and arena
matches: queues, ready checks, scoring, ratings, results, and their windows.

| | |
|---|---|
| **Module id** | `matchmaking` (`MatchmakingModule`) |
| **Assemblies** | `FishMMO.Matchmaking`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core. It reads parties through the core contracts (`IPartyController`, `IPartySystem`) that Social implements, and compiles without Social. |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core. These were partials of `InteractableSystem`; they are their own system now. |

## Folders

```
Assets/FishMMO/Modules/Matchmaking/
├── Runtime/              FishMMO.Matchmaking          Group finder, dungeon finder and arena broadcasts and serializers,
│                                                      ArenaRules, ArenaRating, ArenaHarmRelation, MatchmakingModule
├── Server/               FishMMO.Matchmaking.Server   MatchmakingSystem (+ asset): group finder, dungeon finder, arena and
│                                                      arena match partials; its own request guard and main-thread queue
├── Client/               FishMMO.Matchmaking.Client   UITKArenaBoard, UITKArenaHud, UITKArenaResults, UITKArenaScoreboard,
│                                                      UITKDungeonFinder (+ prefabs, uxml, uss); ArenaClientEvents,
│                                                      ArenaFlagVisuals, ArenaTeamStyle
├── Editor/RenderScratch/ FishMMO.Matchmaking.RenderScratch   The dungeon finder fixture, its footer probe, the arena HUD
├── Tests/                FishMMO.Matchmaking.Tests    Arena rules, hot path, wire and wiring tests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `SceneServer_Systems` label | `MatchmakingSystem.asset` is adopted by the scene server. |
| `ClientWorldGUI_Panels` label | The arena board, HUD, results and scoreboard and the dungeon finder are placed into the world GUI. |
| `[assembly: HarmRelation]` | `ArenaHarmRelation`: inside a live match the teams decide who may harm whom. |
| `CharacterSystem.OnSpawnCharacter` / `OnDespawnCharacter` / `OnDisconnect` | Arena seating and leaving; the group finder drops a disconnected player. |
| `ArenaStandings.Source` | The arena board's Leaderboard tab reads it (the Leaderboards module fills it). |
| `Accomplishments.Record` | A dungeon entrance's achievement is credited on entering; the group finder credits its entry's. |
| `[assembly: GameModule]` | `MatchmakingModule` ("matchmaking"). |

## What stays in the core

The world and its content: dungeon and arena templates, dungeon difficulties (instancing uses them), the dungeon
entrances, arena boards and arena objectives placed by the world generator, their interfaces and ECA actions and the two
messages those actions send (`DungeonFinderBroadcast`, `ArenaBoardBroadcast`), `ArenaRespawnBroadcast` (the death
dialog), `ArenaEventKind` / `ArenaEventData` (arena templates' triggers), `ArenaTeamRegistry` (Factions and arena chat
read it), `GroupFinderRules` and its state and refusal enums, the arena spectator camera, and the `ArenaStandings` seam.

## Removing it

Delete `Assets/FishMMO/Modules/Matchmaking` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). The dungeon entrances and arena boards then open nothing. The
group finder, dungeon instance, arena rating and match tables stay in place, unused.

## License

This project is subject to the FishMMO project license.
