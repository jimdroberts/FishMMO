# Faction System

## Overview

The Faction system is a data-driven reputation framework for FishMMO. Each character tracks reputation values against multiple factions, which determines alliance levels (Ally, Neutral, Enemy) for gameplay interactions. The system uses a matrix-based editor for defining default inter-faction relationships, supports dynamic reputation adjustment through combat events, and synchronizes standings via FishNet broadcasts.

## Directory Structure

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

## Inheritance Hierarchies

### Runtime Instances

```
Faction                            # Standalone class (no inheritance)
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<FactionTemplate>
└── FactionTemplate                    # Icon, Description, Minimum/Maximum, Default Allied/Neutral/Hostile sets

CachedScriptableObject<FactionMatrixTemplate>
└── FactionMatrixTemplate              # Wraps FactionMatrix, provides RebuildMatrix/RebuildFactions
```

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── FactionController : IFactionController
```

### Supporting Types

```
FactionAllianceLevel               # Enum: Ally (0), Neutral (1), Enemy (2)
FactionMatrix                      # [Serializable] class: flat FactionAllianceLevel[] array
```

## Value Model

Each `Faction` instance holds a single `int Value` representing the character's reputation with that faction:

| Range | Alliance Group | Meaning |
|-------|---------------|---------|
| `> 0` | Allied | Positive standing, faction is friendly |
| `= 0` | Neutral | No relationship established |
| `< 0` | Hostile | Negative standing, faction is hostile |

Values are clamped to `[FactionTemplate.Minimum, FactionTemplate.Maximum]` which defaults to `[-10000, 10000]`.

The `FactionController` maintains four dictionaries for fast lookups:

| Dictionary | Contents |
|-----------|----------|
| `Factions` | All factions the character has standings with |
| `Allied` | Subset with `Value > 0` |
| `Neutral` | Subset with `Value == 0` |
| `Hostile` | Subset with `Value < 0` |

When a faction value changes, the faction is removed from its old group and inserted into the correct new group.

## Alliance Level Resolution

`GetAllianceLevel(otherFactionController)` determines the relationship between two characters using a priority chain:

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

### Alliance Colors

| Level | Color |
|-------|-------|
| Ally | Green |
| Neutral | Sky Blue |
| Enemy | Red |

## Faction Adjustment

### Direct Modification

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

## Faction Matrix

The `FactionMatrixTemplate` provides an editor tool for configuring default inter-faction relationships:

### Matrix Structure

`FactionMatrix` stores a flat `FactionAllianceLevel[]` array of size `Count × Count`, indexed as `[x + y * Count]`.

### Editor Workflow

1. **Rebuild Matrix** — Loads all `FactionTemplate` assets via Addressables and creates a new `Count × Count` matrix initialized to `Neutral`.
2. **Edit Matrix** — The custom inspector (`FactionMatrixEditor`) renders an NxN grid of enum dropdowns. Edits are automatically mirrored (symmetric matrix). The diagonal is always `Ally` (a faction is always allied with itself).
3. **Rebuild Factions** — Propagates the matrix values to each `FactionTemplate`'s `DefaultAllied`, `DefaultNeutral`, and `DefaultHostile` sets.

## Template Properties

`FactionTemplate` exposes the following configurable fields:

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

## Network Synchronization

### Payload Serialization (FishNet Reader/Writer)

- **WritePayload**: Writes `Int32(count)`, then for each faction: `Int32(templateID)`, `Int32(value)`.
- **ReadPayload**: Clears all dictionaries, reads payload, and calls `SetFaction(id, value)` for each entry.

### Client Broadcast Receivers

| Broadcast | Purpose |
|-----------|---------|
| `FactionUpdateBroadcast` | Owner-targeted single faction update |
| `FactionUpdateMultipleBroadcast` | Owner-targeted bulk faction update |
| `CharacterObserverFactionUpdateBroadcast` | Observer-targeted faction updates with `CharacterID` routing |

Observer-targeted updates resolve the destination controller through `BaseCharacter.ClientCharacters` and apply updates on the resolved `IFactionController` instance.

## Static Events

| Event | Signature | When Fired |
|-------|-----------|------------|
| `OnUpdateFaction` | `Action<ICharacter, Faction>` | After `SetFaction` (unless `skipEvent=true`) or `Add` |

## NPC Faction Handling

NPCs use a different faction path than players:

- **No accumulation**: `SetFaction` and `Add` early-return for NPCs (`Character as NPC != null`) to prevent NPCs from changing standings through combat.
- **Static relationships**: NPC faction relationships are determined by their `RaceTemplate.InitialFaction`, which references a `FactionTemplate` with pre-configured `DefaultAllied`/`DefaultHostile` sets.
- **Alliance checks**: When evaluating alliance level against an NPC, the system checks the NPC's `InitialFaction.ID` directly against the player's `Hostile` dictionary, rather than iterating the NPC's dynamic standings.

## External Integration Points

The faction system is consumed by and interacts with:

- **CharacterDamageController** — Calls `AdjustFaction()` on kill with 1% allied/hostile adjustment.
- **Party System** — `GetAllianceLevel` checks party membership for Ally override.
- **Guild System** — `GetAllianceLevel` checks guild membership for Ally override.
- **NPC System** — NPCs use `RaceTemplate.InitialFaction` for static faction identity.
- **Database Layer** — Factions are persisted and restored via `CharacterFactionData` DTO.
- **UI** — `UIFactions` panel subscribes to `OnUpdateFaction` for real-time standing display.
- **Target Frames** — `GetAllianceLevelColor` provides nameplate/frame coloring.