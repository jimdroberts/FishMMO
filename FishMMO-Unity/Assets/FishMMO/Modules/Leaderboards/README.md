# Leaderboards module

**Short description:** Server-side PvP and PvE leaderboards for the scene server: one page of a board and the asking
player's own standing, read from the database and held in the scene server's memory so that every player viewing a page
shares one read.

| | |
|---|---|
| **Module id** | `leaderboards` (`LeaderboardsModule`) |
| **Assemblies** | `FishMMO.Leaderboards`, `.Server`, `.Client`, `.Tests` |
| **Depends on** | The core |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Phase 2 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-15: the first module, which set the folder and assembly pattern |

## Folders

```
Assets/FishMMO/Modules/Leaderboards/
├── Runtime/   FishMMO.Leaderboards          LeaderboardTemplate, LeaderboardPaging, the broadcasts, LeaderboardsModule
├── Server/    FishMMO.Leaderboards.Server   LeaderboardSystem (+ its asset), runtime data and their contracts
├── Client/    FishMMO.Leaderboards.Client   UITKLeaderboards + UILeaderboards.prefab/uxml/uss, LeaderboardRequester,
│                                            LeaderboardsClient (menu entry, arena standings source)
├── Templates/                               The board assets (Shared_Static_Permanent)
├── Tests/     FishMMO.Leaderboards.Tests    LeaderboardRulesTests, LeaderboardWiringTests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `SceneServer_Systems` label | `LeaderboardSystem.asset` is adopted by the scene server at start; `SceneServer.unity`'s own list does not name it. |
| `ClientWorldGUI_Panels` label | `UILeaderboards.prefab` is placed into ClientWorldGUI as that scene loads. |
| `GameMenuEntries` | `LeaderboardsClient` registers the "Leaderboards" entry. |
| `ArenaStandings.Source` | `LeaderboardArenaStandings` supplies the arena board's standings; with no source the arena board hides its Leaderboard tab. |
| `[assembly: GameModule]` | `LeaderboardsModule` ("leaderboards"). |

## What stays in the core

`SingleFlightCache` (`Server/Core/Collections`), the shared-read cache, which is general purpose. The SQL is in the
database project (`FishMMO-Database/FishMMO-DB/Npgsql/Services/Scene/Leaderboard/`), with the tables it reads, which
belong to other features.

## Removing it

Delete `Assets/FishMMO/Modules/Leaderboards` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). The game menu then has no Leaderboards entry and the arena
board hides its Leaderboard tab. No table belongs to the module.

## Overview

Issue #261. A board ranks the whole shard, and the characters on it are spread across every scene server or offline, so the database is the only place a board can be read from. `LeaderboardSystem` never ranks from memory: every number it sends is one `ILeaderboardService` returned. What it keeps in memory is the *reads*, not the ranking — each page of each board is cached for a short, configurable time and shared by everyone who asks for it.

Boards are `LeaderboardTemplate` assets (`Assets/FishMMO/Modules/Leaderboards/Templates`), loaded by both peers from the `Shared_Static_Permanent` addressables group. Each names a category (PvP or PvE), a display order, a score label and a source:

| Source | Reads | Current as of |
|---|---|---|
| Arena Season Rating | `arena_rating.rating` in the active season, placed players only (`MinimumGames`) | The end of the character's last ranked match |
| Character Attribute | `character_attributes.value` for one attribute (PvP Rank, PvP Wins) | The owner's last character save (every 30 s online) |
| Achievement | `character_achievements.value` for one achievement (Kills, Waypoints Discovered) | The owner's last character save (every 30 s online) |

A new board is a new asset pointing at a number the game already keeps. Nothing here adds a column.

## Features

- **Readable from anywhere.** `LeaderboardPageRequestBroadcast` needs no interactable and passes `PlayerRequestGate.SkipCanAct`: reading a board is not an action and leads to none, so a dead or stunned player may still look.
- **Paged, fixed-size pages** (`LeaderboardPaging.PageSize` = 25), browsable to `maxBrowsableRank`. A player further down still receives their own rank.
- **One read per page per cache lifetime** (`SingleFlightCache`): concurrent requests for a page nobody has read yet share the read in flight; a failed read is shared by its callers and never cached.
- **Standing that agrees with the page.** When the requester is on the page, their rank is the page's own row; only otherwise is it read separately, cached per player and board.
- **Eligibility decided in SQL**, identically for page, count and standing: soft-deleted characters and banned accounts are never ranked; staff accounts only with `rankStaff`; a zero score is not a placing.
- **Honest age.** The reply carries how old the read is (`AgeSeconds`), and the Leaderboards window shows it.
- **Always answers.** An unknown board, a missing service or a failed read is answered with `Unavailable = true` rather than silence, so the window never loads forever.

## Configuration

### Inspector Settings (`Assets/FishMMO/Modules/Leaderboards/Server/LeaderboardSystem.asset`)

| Field | Default | Meaning |
|---|---|---|
| `cacheSeconds` | 60 | How long a page or standing read is served before it is read again. The most a board can lag the database, and how often each viewed page costs a read per scene server. |
| `cacheSweepIntervalSeconds` | 10 | Seconds between bounded sweeps of expired cache entries |
| `cacheSweepMaxRemovals` | 256 | Maximum expired entries removed per sweep, per cache |
| `maxBrowsableRank` | 1000 | Deepest position anyone may page to (40 pages) |
| `rankStaff` | false | Rank game master and administrator characters. Off for a live shard; on to see staff test characters while testing |
| `maxMainThreadActionsPerFrame` | 100 | Replies drained from the main-thread queue per frame |
| `ingressDebounceMilliseconds` | 150 | Minimum time between board requests per connection. The client's `LeaderboardRequester` waits at least 250 ms between sends, so it is never refused for this |
| `ingressSweepIntervalSeconds` / `ingressEntryTtlSeconds` / `ingressSweepMaxRemovals` | 5 / 30 / 128 | Ingress guard housekeeping, as every system |

### Required Data Containers

- `LeaderboardSystemRuntimeData` (`ILeaderboardSystemRuntimeData`) — the ingress guard, the page cache and the standing cache.
- `LeaderboardSystemMainThreadQueueData` (`ILeaderboardSystemMainThreadQueueData`) — replies marshalled back to the main thread.
- `AsyncWorkerData` — database reads run off the main thread, keyed by the requesting character.

### Database Service Dependencies

- `ILeaderboardService` (`FishMMO-DB`) — `FetchPageAsync` and `FetchStandingAsync`. Read-only.

The achievement and attribute boards read through the `(template_id, value)` indexes declared in `CharacterAchievementEntityConfiguration` and `CharacterAttributeEntityConfiguration`; without them each read is a scan of the whole table.

## Usage Examples

### Broadcast Handled

| Broadcast | Handler | Purpose |
|---|---|---|
| `LeaderboardPageRequestBroadcast` | `OnServerLeaderboardPageRequestReceived` | One page of one board, and the sender's standing on it |

### Broadcast Emitted

| Broadcast | When |
|---|---|
| `LeaderboardPageBroadcast` | For every accepted request: the page, the board's size and page count, the read's age, the requester's rank and score — or `Unavailable` |

### Adding a Board

Create a `LeaderboardTemplate` (FishMMO → Leaderboard → Leaderboard), set its source and reference, and add it to the `Shared_Static_Permanent` addressables group. The attribute or achievement it reads must be in that group too: its cached ID is what the character save wrote as `template_id`. `LeaderboardWiringTests` checks both.

## Accuracy and Staleness

- **Every scene server keeps its own cache.** Two servers can show the same board up to one cache lifetime apart; each re-reads the same rows when its copy expires.
- **Attribute and achievement boards trail online play by one save interval.** Those values live on the character in memory and are written by the character save. The board deliberately does not patch in the live values of characters on this server: a board mixing live values for some characters with saved values for the rest would order people by which server they stand on.
- **Arena placement.** The arena board ranks a character only after `MinimumGames` ranked games this season, matching the placement games after which their own profile stops showing the rating as provisional. `LeaderboardWiringTests` fails if a board's minimum drops below any arena's `PlacementGames`.
- **Ties** share a rank (1, 2, 2, 4) and keep a fixed order within it (arena: fewer games first; then character ID), so paging never repeats or skips a character.

## Operational Checks

| Check | How | Expected |
|---|---|---|
| The system is loaded | Addressables: `LeaderboardSystem.asset` in `Server_Static_Permanent` | It carries the `SceneServer_Systems` label (`LeaderboardWiringTests.TheSceneServer_LoadsTheLeaderboardSystem`) |
| Boards are offered | Open Leaderboards from the game menu | PvP: Arena Rating, PvP Rank, PvP Wins. PvE: Monsters Slain, Explorers |
| Reads are shared | Several players open the same page within `cacheSeconds` | One database read; every reply reports the same age |
| A failed read is not cached | Stop the database, request, restart, request again | First reply `Unavailable`, second a board |
| Staff are excluded | Rank a character on a GM account | Absent with `rankStaff` off, present with it on |

## Flow Diagram

```
Client (UITKLeaderboards / UITKArenaBoard)
  │  LeaderboardRequester: one request outstanding, clicks folded, bounded retry
  ▼
LeaderboardPageRequestBroadcast ──► OnServerLeaderboardPageRequestReceived   (main thread)
                                      ├─ ingress guard: one in flight per connection
                                      ├─ resolve player (SkipCanAct) and board → LeaderboardQuery
                                      └─ TryEnqueueAsyncWork ──► SendPageAsync   (async worker)
                                                                   ├─ Pages.GetOrFetchAsync(board, page)
                                                                   │     └─ miss: ILeaderboardService.FetchPageAsync
                                                                   ├─ standing: from the page's row, else
                                                                   │   Standings.GetOrFetchAsync(board, character)
                                                                   └─ ComposePage ──► main-thread queue
                                                                                          │
LeaderboardPageBroadcast ◄───────────────────────────────────────────────────────────────┘
```

## License

This project is subject to the FishMMO project license.
