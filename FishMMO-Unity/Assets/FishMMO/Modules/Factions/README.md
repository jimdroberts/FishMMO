# Factions module

**Short description:** Per-character faction standings, the alliance level between two characters, standings shifted by
kills, the aggression toggle and its lock, and the faction relation in the harm rule, with the factions window and the
faction matrix editor.

| | |
|---|---|
| **Module id** | `factions` (`FactionsModule`) |
| **Assemblies** | `FishMMO.Factions`, `.Server`, `.Client`, `.Tests`, `.RenderScratch`; editor `FishMMO.Shared.Faction.Editor` |
| **Depends on** | The core |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Phase 2 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-16 |

## Folders

```
Assets/FishMMO/Modules/Factions/
├── Runtime/    FishMMO.Factions          FactionController (a CharacterPart on every character), Faction, IFactionStandings,
│                                         FactionTemplate (+ StartingRaces), FactionMatrix (+ Template), FactionHarmRelation,
│                                         FactionBroadcasts, AddFactionAction, HasFactionCondition, FactionEventData,
│                                         FactionAmountValue, FactionsModule
├── Server/     FishMMO.Factions.Server   FactionSystem (+ asset: the aggression request), FactionPersistence (save
│                                         participant), FactionCreationContributor (starting standings)
├── Client/     FishMMO.Factions.Client   UITKFactions + UIFactions.prefab/uxml/uss
├── Editor/     FishMMO.Shared.Faction.Editor   FactionMatrixEditor, FactionDashboardCategories; RenderScratch/
├── Templates/                            The faction templates and FactionMatrix.asset
├── Tests/      FishMMO.Factions.Tests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `IFactionController` (core contract) | `FactionController` implements it: alliance level and colour, aggression, setting a standing by id. Without the module a character has none. |
| `[assembly: CharacterPart]` | `FactionController` is added at wake to every `BaseCharacter`; no prefab carries it. Its standings and aggression state ride the spawn payload (`factions.standings`). |
| `[assembly: HarmRelation]` | `FactionHarmRelation` (order 400): standing and the aggression toggles decide who may harm whom. |
| `[assembly: CharacterPersistence]` | `FactionPersistence` over `character_factions`. |
| `[assembly: CharacterCreationContributor]` | `FactionCreationContributor`: the starting standings of the race's faction. |
| `SceneServer_Systems` label | `FactionSystem.asset` (the aggression toggle request). |
| `ClientWorldGUI_Panels` label | `UIFactions.prefab`; the Factions key still opens it by name. |
| `IDashboardCategoryProvider` | `FactionDashboardCategories`: the Dashboard's Factions and Faction Matrix pages. |
| `[assembly: GameModule]` | `FactionsModule` ("factions"). |

## What stays in the core

`IFactionController` and `FactionAllianceLevel`; an NPC's race (`NPC.RaceTemplate`, in its own payload) and its
`IsAggressive` flag; the harm rule itself (`HarmRule`, `IHarmRelation`); `ArenaTeamRegistry`, which decides sides inside
an arena; and `CharacterDamageController`, which calls `AdjustFaction` on a kill.

## Removing it

Delete `Assets/FishMMO/Modules/Factions` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Characters then have no standings, there is no aggression toggle, and the harm rule decides without the faction
relation. The `character_factions` table and its rows stay in place, unused.

## Reference

### Standings

Each `Faction` holds one `int Value`, clamped to `[FactionTemplate.Minimum, FactionTemplate.Maximum]` (`-10000`,
`10000`). `FactionController` keeps four dictionaries: `Factions` (all), `Allied` (`> 0`), `Neutral` (`== 0`) and
`Hostile` (`< 0`); a change moves the faction between groups.

- `SetFaction(templateID, value, skipEvent)` sets a value; `Add(template, amount)` adds to it. Both return early for an
  NPC, mark the row changed (`Faction.MarkChanged()` sets `PersistenceDirty` and bumps `Version`), and raise
  `IFactionStandings.OnUpdateFaction` unless `skipEvent` is set. `skipEvent: true` is the restore path and leaves the
  row clean.
- `MarkPersisted(version)` refuses to clear a row that changed while its write was in flight.

### A faction's races

A faction lists the races that start in it (`FactionTemplate.StartingRaces`); `FactionTemplate.InitialFor(race)` answers
the other way round. A new character starts allied at `Maximum` with its faction's `DefaultAllied`, neutral with
`DefaultNeutral`, hostile at `Minimum` with `DefaultHostile` (`FactionCreationContributor`).

**NPCs derive their roster.** An NPC never changes faction, so both peers build its table from its race's faction
(`InitializeTemplateFactions`, from `InitializeOnce` on the server and `ReadPayload` on a client) instead of sending it.
`FactionsAreTemplateDerived` reports which case a controller is in. A pet is the exception: `CopyFrom` installs its
owner's standings, which clears the flag, and its roster travels like a player's.

### Alliance level

`GetAllianceLevel(other)` resolves, in order:

1. **Arena:** inside an arena the match's teams decide (`ArenaTeamRegistry`).
2. **Same side:** a pet answers as its owner (`HarmRule.Principal`); an owner and its pets are allies.
3. **Party, guild:** the same party or the same guild (through the core `IPartyController` / `IGuildController`) is Ally.
4. **Wildlife:** wildlife stands outside the factions: Neutral, or Enemy when either side's aggression is on.
5. **Aggression:** either side's aggression on is Enemy.
6. **NPC:** the NPC's race faction is in this character's `Hostile` set → Enemy.
7. **Player:** the other player is allied with one of this character's hostile factions → Enemy.
8. Otherwise Neutral.

`GetStandingAllianceLevel` is the same without the aggression steps; `GetDisplayedAllianceLevel` is what nameplates
show. `GetAllianceLevelColor` gives green (Ally), sky blue (Neutral) and red (Enemy).

### Kills

`CharacterDamageController` calls `AdjustFaction(defender, 0.01, 0.01)` on a kill:

- **NPC killed:** for each of its race faction's `DefaultAllied` the killer loses `Maximum × 1%`; for each
  `DefaultHostile` the killer gains `Maximum × 1%`.
- **Player killed:** the killer loses 1% of the victim's value with each of the victim's allied factions and gains 1%
  of it with each hostile one.

### Aggression

`TrySetAggression` turns a player's toggle on or off (server only). Turning it off is refused for
`AggressionLockSeconds` (300 s) after the character, with aggression on, hit or killed something or was hit; without the
lock a player could strike, turn aggression off and be out of reach of the retaliation. `AutoAggressionAttacks` (3)
different attack casts refused at the same character within `AutoAggressionWindowSeconds` (10 s), which only aggression
would have let land, turn it on. The state reaches peers in the spawn payload and in `FactionAggressionStateBroadcast` /
`FactionAggressionLockBroadcast`.

### On the wire

The payload is framed by a byte count, so `ReadPayload` can resynchronise after rejecting one (every behaviour on an
object shares one unframed buffer). It writes the race template ID (unpacked) and a derived-roster flag; an owned roster
is written per receiver: the **owner** gets exact `Int32` standings, **everyone else** one sign byte per entry.

Changes go to the owner as `FactionUpdateBroadcast` / `FactionUpdateMultipleBroadcast` (exact values) and to observers
as `CharacterObserverFactionUpdateBroadcast` (a sign, -1, 0 or +1, sent only when a standing crosses zero). The only
observer-side reader of a peer's standings is the alliance check, which needs only the sign, and the exact value would
hand a packet-inspecting client a peer's progression. The flush subscribes to `TimeManager.OnTick` only while something
is dirty.

### The faction matrix

`FactionMatrixTemplate` holds a `FactionMatrix` (a flat `Count × Count` array of `FactionAllianceLevel`). In its
inspector (`FactionMatrixEditor`): **Rebuild Matrix** loads every `FactionTemplate`, edits are mirrored, the diagonal is
always Ally, and **Rebuild Factions** writes the matrix back into each template's `DefaultAllied`, `DefaultNeutral` and
`DefaultHostile`.

## License

This project is subject to the FishMMO project license.
