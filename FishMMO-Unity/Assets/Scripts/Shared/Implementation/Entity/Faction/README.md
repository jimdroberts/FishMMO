# Faction System

**Short description:** A data-driven reputation framework that tracks per-character faction standings, resolves alliance levels through a priority chain, and synchronizes reputation changes via FishNet broadcasts.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guide](#quick-start-guide)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The Faction system is a data-driven reputation framework for FishMMO. Each character tracks reputation values against multiple factions, which determines alliance levels (Ally, Neutral, Enemy) for gameplay interactions. The system uses a matrix-based editor for defining default inter-faction relationships, supports dynamic reputation adjustment through combat events, and synchronizes standings via FishNet broadcasts.

## Supported Platforms

| Platform | Status | Notes |
|----------|--------|-------|
| Windows  | ✅ Supported | Primary development platform |
| Linux    | ✅ Supported | Server and client builds |
| WebGL    | ✅ Supported | Via Unity WebGL export |

Built with **Unity 6.3 LTS** using **IL2CPP** scripting backend.

## Features

- **Per-character reputation tracking** against multiple factions with integer standings
- **Alliance level resolution** via priority chain (party → guild → aggression → faction standings)
- **Value model** with Allied/Neutral/Hostile grouping and fast dictionary lookups
- **Matrix-based editor** for configuring default NxN inter-faction relationships
- **Combat-based adjustment** — Killing NPCs or players shifts faction standings proportionally
- **NPC static relationships** — NPCs use `RaceTemplate.InitialFaction` without accumulating changes
- **Network synchronization** — Owner-targeted and observer-targeted FishNet broadcasts
- **Alliance coloring** — Green (Ally), Sky Blue (Neutral), Red (Enemy) for nameplates/frames
- **Clamped reputation** — Values bounded to `[Minimum, Maximum]` (default `[-10000, 10000]`)
- **Static events** — `OnUpdateFaction` fired on every faction change for UI and system hooks

## Prerequisites

- Unity 6.3 LTS
- FishNetworking (FishNet)
- FishMMO Shared Core

## Installation / Build

This system is an integrated module of the FishMMO Unity project. No separate installation is required.

## Quick Start Guide

1. **Define factions** — Create `FactionTemplate` ScriptableObjects via Addressables. Set icon, description, and default relationships.
2. **Configure the matrix** — Open the `FactionMatrixTemplate` asset in the inspector. Click **Rebuild Matrix** to load all factions, edit the NxN grid, then click **Rebuild Factions** to propagate defaults.
3. **Assign NPC factions** — Set `RaceTemplate.InitialFaction` on NPC race templates to link NPCs to their faction identity.
4. **Runtime adjustments** — Faction standings change automatically through combat events (`CharacterDamageController.Kill()` calls `AdjustFaction()` with 1% adjustment). Direct modification is available via `SetFaction()` and `Add()`.
5. **Query alliance** — Call `GetAllianceLevel(otherFactionController)` to determine the relationship between two characters for gameplay logic.

## Configuration

### Template Properties

| Property | Type | Description |
|----------|------|-------------|
| `Icon` | `Sprite` | UI icon for the faction |
| `Description` | `string` | Player-facing description text |
| `Minimum` | `const int` | Minimum reputation value (`-10000`) |
| `Maximum` | `const int` | Maximum reputation value (`10000`) |
| `DefaultAllied` | `FactionHashSet` | Set of factions allied by default |
| `DefaultNeutral` | `FactionHashSet` | Set of factions neutral by default |
| `DefaultHostile` | `FactionHashSet` | Set of factions hostile by default |
| `Name` | `string` | Read-only, from ScriptableObject name |

### Value Model

Each `Faction` instance holds a single `int Value` representing the character's reputation with that faction:

| Range | Alliance Group | Meaning |
|-------|---------------|---------|
| `> 0` | Allied | Positive standing, faction is friendly |
| `= 0` | Neutral | No relationship established |
| `< 0` | Hostile | Negative standing, faction is hostile |

Values are clamped to `[FactionTemplate.Minimum, FactionTemplate.Maximum]` which defaults to `[-10000, 10000]`.

### Controller Dictionaries

The `FactionController` maintains four dictionaries for fast lookups:

| Dictionary | Contents |
|-----------|----------|
| `Factions` | All factions the character has standings with |
| `Allied` | Subset with `Value > 0` |
| `Neutral` | Subset with `Value == 0` |
| `Hostile` | Subset with `Value < 0` |

When a faction value changes, the faction is removed from its old group and inserted into the correct new group.

### Alliance Level Colors

| Level | Color |
|-------|-------|
| Ally | Green |
| Neutral | Sky Blue |
| Enemy | Red |

### Faction Matrix

The `FactionMatrixTemplate` provides an editor tool for configuring default inter-faction relationships:

- **Matrix structure** — `FactionMatrix` stores a flat `FactionAllianceLevel[]` array of size `Count × Count`, indexed as `[x + y * Count]`.
- **Rebuild Matrix** — Loads all `FactionTemplate` assets via Addressables and creates a new `Count × Count` matrix initialized to `Neutral`.
- **Edit Matrix** — The custom inspector (`FactionMatrixEditor`) renders an NxN grid of enum dropdowns. Edits are automatically mirrored (symmetric matrix). The diagonal is always `Ally` (a faction is always allied with itself).
- **Rebuild Factions** — Propagates the matrix values to each `FactionTemplate`'s `DefaultAllied`, `DefaultNeutral`, and `DefaultHostile` sets.

## Usage Examples

### Direct Modification API

| Method | Behavior |
|--------|----------|
| `SetFaction(templateID, value)` | Sets absolute value, updates alliance group, fires event |
| `Add(template, amount)` | Adds amount to current value, clamps to bounds, fires event |

Both methods skip adjustment for NPC characters to prevent NPCs from accumulating faction changes.

### Combat-Based Adjustment

`AdjustFaction(defenderFactionController, alliedPercent, hostilePercent)` is called by `CharacterDamageController.Kill()` with `(0.01, 0.01)` — 1% adjustment:

```
AdjustFaction(defender, alliedPercent, hostilePercent)
  ├── Defender is NPC?
  │   └── For each of defender's race InitialFaction.DefaultAllied:
  │       → Add(-Maximum * alliedPercent) to attacker's standing  (lose rep)
  │   └── For each of defender's race InitialFaction.DefaultHostile:
  │       → Add(+Maximum * hostilePercent) to attacker's standing (gain rep)
  ├── Defender is Player?
  │   └── For each of defender's Allied factions:
  │       → Add(-faction.Value * alliedPercent) to attacker's standing
  │   └── For each of defender's Hostile factions:
  │       → Add(+faction.Value * hostilePercent) to attacker's standing
```

**Effect**: Killing an NPC makes you lose standing with the NPC's allies and gain standing with the NPC's enemies. Killing a player has a proportional effect based on the victim's actual faction standings.

### Network Synchronization

#### Payload Serialization (FishNet Reader/Writer)

- **WritePayload**: Writes `Int32(count)`, then for each faction: `Int32(templateID)`, `Int32(value)`.
- **ReadPayload**: Clears all dictionaries, reads payload, and calls `SetFaction(id, value)` for each entry.

#### Client Broadcast Receivers

| Broadcast | Purpose |
|-----------|---------|
| `FactionUpdateBroadcast` | Owner-targeted single faction update |
| `FactionUpdateMultipleBroadcast` | Owner-targeted bulk faction update |
| `CharacterObserverFactionUpdateBroadcast` | Observer-targeted faction updates with `CharacterID` routing |

Observer-targeted updates resolve the destination controller through `BaseCharacter.ClientCharacters` and apply updates on the resolved `IFactionController` instance.

### Static Events

| Event | Signature | When Fired |
|-------|-----------|------------|
| `OnUpdateFaction` | `Action<ICharacter, Faction>` | After `SetFaction` (unless `skipEvent=true`) or `Add` |

### NPC Faction Handling

NPCs use a different faction path than players:

- **No accumulation**: `SetFaction` and `Add` early-return for NPCs (`Character as NPC != null`) to prevent NPCs from changing standings through combat.
- **Static relationships**: NPC faction relationships are determined by their `RaceTemplate.InitialFaction`, which references a `FactionTemplate` with pre-configured `DefaultAllied`/`DefaultHostile` sets.
- **Alliance checks**: When evaluating alliance level against an NPC, the system checks the NPC's `InitialFaction.ID` directly against the player's `Hostile` dictionary, rather than iterating the NPC's dynamic standings.

### External Integration Points

- **CharacterDamageController** — Calls `AdjustFaction()` on kill with 1% allied/hostile adjustment.
- **Party System** — `GetAllianceLevel` checks party membership for Ally override.
- **Guild System** — `GetAllianceLevel` checks guild membership for Ally override.
- **NPC System** — NPCs use `RaceTemplate.InitialFaction` for static faction identity.
- **Database Layer** — Factions are persisted and restored via `CharacterFactionData` DTO.
- **UI** — `UIFactions` panel subscribes to `OnUpdateFaction` for real-time standing display.
- **Target Frames** — `GetAllianceLevelColor` provides nameplate/frame coloring.

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Faction creation | Create a `FactionTemplate` ScriptableObject and rebuild matrix | Template appears in matrix editor grid |
| Matrix editing | Change a cell in the `FactionMatrixEditor` inspector | Change mirrored symmetrically; diagonal remains Ally |
| Rebuild factions | Click **Rebuild Factions** on `FactionMatrixTemplate` | `DefaultAllied`/`DefaultNeutral`/`DefaultHostile` sets updated on each `FactionTemplate` |
| Set faction standing | Call `SetFaction(templateID, value)` on a player | Value stored in correct dictionary group; `OnUpdateFaction` fired |
| Add reputation | Call `Add(template, amount)` on a player | Value clamped to bounds; dictionary group updated; event fired |
| NPC kill adjustment | Kill an NPC as a player | Lose standing with NPC's allied factions; gain standing with NPC's hostile factions |
| Player kill adjustment | Kill a player character | Proportional faction shifts based on victim's Allied/Hostile standings |
| NPC immunity | Trigger `SetFaction`/`Add` on an NPC | Early return; no faction change applied |
| Alliance resolution | Call `GetAllianceLevel` between two characters | Correct level returned per priority chain (party → guild → aggression → standings) |
| Network sync | Modify a faction on the server | `FactionUpdateBroadcast` received by owner client; observer broadcasts reach nearby clients |
| UI update | Change a faction standing at runtime | `UIFactions` panel reflects the new value via `OnUpdateFaction` event |

## Flow Diagram

### Alliance Resolution

```
GetAllianceLevel(other)
  1. Same party?         → Ally
  2. Same guild?         → Ally
  3. Either aggressive?  → Enemy
  4. Other is NPC?
     └── Check if NPC's InitialFaction is in this character's Hostile dict → Enemy
  5. Other is Player?
     └── For each of this character's Hostile factions:
         └── If the other player is Allied with that faction → Enemy
  6. Default             → Neutral
```

### Faction Adjustment Flow (Combat Kill)

```
CharacterDamageController.Kill()
  └── AdjustFaction(defender, 0.01, 0.01)
        ├── Defender is NPC
        │   ├── For each DefaultAllied of defender's InitialFaction:
        │   │   └── attacker.Add(-Maximum * 0.01)   // lose rep with allies
        │   └── For each DefaultHostile of defender's InitialFaction:
        │       └── attacker.Add(+Maximum * 0.01)   // gain rep with enemies
        └── Defender is Player
            ├── For each of defender's Allied factions:
            │   └── attacker.Add(-faction.Value * 0.01)
            └── For each of defender's Hostile factions:
                └── attacker.Add(+faction.Value * 0.01)
```

### Faction Value Update Flow

```
SetFaction(templateID, value) / Add(template, amount)
  ├── NPC check → early return if NPC
  ├── Clamp value to [Minimum, Maximum]
  ├── Remove from old alliance group dictionary
  ├── Insert into new alliance group dictionary
  └── Fire OnUpdateFaction(character, faction)
        ├── Server: FactionSystem persists to DB, broadcasts to client
        └── Client: UIFactions panel updates display
```

## Project Structure

### Directory Structure

```
Faction/
├── Faction.cs                     # Runtime faction instance (value, template ref)
├── FactionAllianceLevel.cs        # Enum: Ally, Neutral, Enemy
├── FactionController.cs           # Per-entity controller (CharacterBehaviour / NetworkBehaviour)
├── IFactionController.cs          # Faction controller interface + static events
└── Template/
    ├── FactionTemplate.cs             # ScriptableObject blueprint (bounds, default relationships)
    ├── FactionMatrix.cs               # Flat array representing NxN faction relationship grid
    ├── FactionMatrixTemplate.cs       # ScriptableObject wrapping FactionMatrix + editor rebuild tools
    └── Editor/
        └── FactionMatrixEditor.cs         # Custom inspector for visual matrix editing
```

### Related Files (Outside This Directory)

```
Shared/Implementation/Network/Character/FactionBroadcasts.cs            # FishNet broadcast structs for faction updates
Server/Implementation/World/SceneServer/Faction/FactionSystem.cs         # Server-side faction update handling + DB persistence
Shared/Implementation/Entity/CharacterAttribute/CharacterDamageController.cs  # Calls AdjustFaction() on kill events
Shared/Implementation/Entity/BaseCharacter.cs                            # Client-side character cache used for observer routing
Client/UI/Controls/World/Faction/UIFactions.cs                           # Faction UI panel
```

### Inheritance Hierarchies

#### Runtime Instances

```
Faction                            # Standalone class (no inheritance)
```

#### Templates (ScriptableObjects)

```
CachedScriptableObject<FactionTemplate>
└── FactionTemplate                    # Icon, Description, Minimum/Maximum, Default Allied/Neutral/Hostile sets

CachedScriptableObject<FactionMatrixTemplate>
└── FactionMatrixTemplate              # Wraps FactionMatrix, provides RebuildMatrix/RebuildFactions
```

#### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── FactionController : IFactionController
```

#### Supporting Types

```
FactionAllianceLevel               # Enum: Ally (0), Neutral (1), Enemy (2)
FactionMatrix                      # [Serializable] class: flat FactionAllianceLevel[] array
```

## License

This project is subject to the FishMMO project license.
