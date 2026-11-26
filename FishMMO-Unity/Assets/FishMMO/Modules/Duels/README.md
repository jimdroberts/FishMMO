# Duels module

**Short description:** One-on-one duels in the open world (issue #250). Right-click a player and choose **Challenge to
Duel**; when they accept, both are set down facing each other inside a small duel ground, a 10 second countdown runs, and
they fight until one is beaten, steps out of the ground, yields or leaves. Nobody dies in a duel. Results feed the duel
rating and the Duel Rating and Duel Wins leaderboards.

| | |
|---|---|
| **Module id** | `duels` (`DuelsModule`) |
| **Assemblies** | `FishMMO.Duels`, `.Server`, `.Client`, `.Tests` |
| **Depends on** | The core (the harm rule's spare and alliance seams), the database's `IDuelRecordService` |
| **Depended on by** | None |
| **Remove together with** | Nothing |

## Folders

```
Assets/FishMMO/Modules/Duels/
├── Runtime/   FishMMO.Duels          DuelBroadcasts, DuelRules (geometry, wording), DuelRegistry, DuelHarmRelation, DuelsModule
├── Server/    FishMMO.Duels.Server   DuelSystem (+ .Handlers, .Ground, its asset), its data containers
├── Client/    FishMMO.Duels.Client   UITKDuel + UIDuel.prefab/uxml/uss, DuelClient (menu entry), DuelGroundVisual
├── Tests/     FishMMO.Duels.Tests    DuelRulesTests, DuelHarmRelationTests, DuelsWiringTests
└── README.md
```

The two duel boards are `LeaderboardTemplate` assets in `Modules/Leaderboards/Templates` (with the other boards), and the
tables are in the database project (`duel_record`, `duel_result`; migration `AddDuels`).

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: HarmRelation]` | `DuelHarmRelation` (order 150, after the arena, before wildlife, companions and factions). |
| `IHarmRelation.Spares` / `OnSpared` | A blow from one duellist that would kill the other leaves them on their last point; `CharacterDamageController.Damage` clamps it and the relation raises `DuelRegistry.OnFinishingBlow`. |
| `IHarmRelation.TryResolveAlliance` | `FactionController` asks it after the arena registry: duellists are each other's enemies while fighting, neutral during the countdown. |
| `SceneServer_Systems` label | `DuelSystem.asset` is adopted by every scene server at start. |
| `ClientWorldGUI_Panels` label | `UIDuel.prefab` is placed into ClientWorldGUI as it loads. |
| `CharacterMenuEntries` | "Challenge to Duel" (order 125, after Trade), on players in the world only. |
| `CharacterSheetSections` | "Duels" on the player's own character sheet (equipment panel): Rating, Wins, Losses, Draws, each with a hover tooltip. |
| `ICharacterSystem.OnSpawnCharacter` | On arrival (login or scene change) the record is read through the persistence queue and sent, so the sheet is filled before the first duel. |
| `[assembly: GameModule]` | `DuelsModule` ("duels"). |

## The rules

- **A challenge** needs both players able to act, out of combat, in the same open-world scene (no instances, no arenas)
  and within `challengeRange` (20 m). It waits `inviteTtlSeconds` (30 s) for an answer. A declined or unanswered
  challenge blocks challenging the same player again for `perTargetCooldownSeconds` (15 s).
- **One duel per player.** A player in a duel, with a challenge out or with one to answer is busy, so nobody can accept
  two duels at once.
- **Acceptance** applies the same rules again, then finds the ground: a box of `arenaSize` (15 x 15 x 15 m), centred
  between the two and running from one to the other. Each start is `DuelRules.StartOffset` (5 m for a 15 m ground) from
  the centre, on stable ground the character could stand on, with room for its capsule. If that ground will not do, the
  box is turned (45 degree steps) and then moved onto either player; if nothing fits, the answer is "no level ground".
  Both players are teleported (through the motor) to their starts, facing each other, and each client turns its camera to
  that facing.
- **The countdown** (`countdownSeconds`, 10 s): neither may strike the other yet.
- **The fight:** the two sides (each player and their pets) may harm each other, whatever their party, guild, faction or
  aggression toggle. Nothing changes for anyone else.
- **It ends** when:

| End | Result |
|---|---|
| A blow would drop a duellist to 0 health | They are left on their last point; the striker wins (`Defeated`) |
| A duellist stands outside the ground (by more than `leaveTolerance`, 0.25 m) at a check, countdown included | They lose (`LeftGround`); both outside at once is a draw |
| A duellist presses Yield twice | They lose (`Yielded`) |
| A duellist disconnects or leaves the scene | They lose (`Left`) |
| A duellist is killed by something else (a monster, a fall) | They lose (`Died`) |
| `timeLimitSeconds` (180 s) of fighting pass | A draw (`TimeUp`) |
| The server shuts down | Called off; nothing is recorded |

## The record and the boards

Every finished duel is one transaction in `IDuelRecordService.RecordResultAsync`: both `duel_record` rows (inserted at
1500 by a first duel), the Elo change from both ratings (`DuelRating`: K = `ratingKFactor`, 32; a win never costs rating,
a loss never takes it under 100), and a `duel_result` history row. It is written through the persistence queue, which
never drops work, and is idempotent by a request key chosen once per duel, so a retry after a lost commit reply counts
nothing twice. Each duellist still online is then told their new rating and record.

| Board | Source | Who appears |
|---|---|---|
| Duel Rating | `duel_record.rating` (`LeaderboardSource.DuelRating`) | Characters with at least `MinimumGames` (5) finished duels |
| Duel Wins | `duel_record.wins` (`LeaderboardSource.DuelWins`) | Characters with a win |

Both show the win/loss record beside the score and are current the moment a duel ends.

## Configuration (`Server/DuelSystem.asset`)

| Field | Default | Meaning |
|---|---|---|
| `arenaSize` | 15, 15, 15 | Width, height and length of the duel ground in metres; each side kept between 6 and 60 |
| `leaveTolerance` | 0.25 | Metres past the edge before it counts as leaving |
| `countdownSeconds` | 10 | Teleport to first blow |
| `timeLimitSeconds` | 180 | Fighting time before a draw; 0 for none |
| `challengeRange` | 20 | Metres between the two to challenge |
| `inviteTtlSeconds` | 30 | How long a challenge waits |
| `perTargetCooldownSeconds` | 15 | Wait before re-challenging a player who declined or did not answer |
| `checkIntervalSeconds` | 0.1 | Seconds between the server's checks of every duel |
| `ratingKFactor` | 32 | The most one duel moves a rating |
| ingress fields | 250 ms / 5 / 30 / 128 | Request debounce and guard housekeeping, as every system |

## Broadcasts

| Broadcast | Direction | Purpose |
|---|---|---|
| `DuelRequestBroadcast` | client → server | Challenge a player |
| `DuelInviteBroadcast` | server → target | You are challenged |
| `DuelInviteResponseBroadcast` | target → server | Fight or decline (names the challenge it answers) |
| `DuelRequestResultBroadcast` | server → client | Sent, or why not (every refusal is answered) |
| `DuelStartBroadcast` | server → both | The ground, the opponent, your facing, the countdown and time limit |
| `DuelLiveBroadcast` | server → both | Fight |
| `DuelEndBroadcast` | server → both | The outcome for you, and why |
| `DuelRecordBroadcast` | server → both | Your rating, its change and your record, once written; also on arrival with `DuelID` 0 (no toast) |
| `DuelYieldBroadcast` | client → server | Yield (or withdraw during the countdown) |

## Client

`UITKDuel` is the single owner of these messages. It publishes the duel to `DuelRegistry` as the server does, so the
client's predicted hits, targeting and finishing-blow clamp agree with the server; it draws the ground
(`DuelGroundVisual`: a golden ward fading up from the floor, with corner and roof lines; unlit, no collider) for the two
duellists; and its strip shows the opponent, the countdown, the clock, a warning within 2 m of the edge, and Yield (two
presses). The result is a banner and a toast; the new rating follows as a toast. Every record it receives also goes to
`DuelClient`, which fills the "Duels" section of the character sheet; the arena's numbers are attributes and list under
their own "Arena" heading.

## Not done

- Bystanders do not see the ground (only the duellists draw it), and nothing stops a third party from attacking a
  duellist under the ordinary rules.
- A start is not checked for water: a ground laid over a lake would set the players on its bed.

## Operational checks

| Check | How | Expected |
|---|---|---|
| The system is loaded | Scene server log at start | `DuelSystem` "Initialized (ground 15x15x15 m, countdown 10s, limit 180s)" |
| A duel runs | Two players 10 m apart, out of combat; one right-clicks the other, Challenge to Duel, the other Fights | Both teleported 10 m apart facing each other inside a golden ward; 10..1, Fight! |
| Nobody dies | Fight to the end | The loser stays on 1 health; Victory / Defeat; the rating toast |
| Leaving forfeits | Walk out of the ward | Defeat "You left the duel ground" |
| The boards | Leaderboards, PvP | Duel Rating (after 5 duels) and Duel Wins list the record |
| The sheet | Log in, open Equipment, scroll the attributes to the end | "Duels": Rating 1500, 0 / 0 / 0 before any duel; the numbers move after one, with no extra toast on arrival |

## License

This project is subject to the FishMMO project license.
