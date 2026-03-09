# Buff System

**Short description:** The Buff system is a data-driven, template-based framework for applying temporary (or permanent) effects to FishMMO characters.

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

The Buff system is a data-driven, template-based framework for applying temporary (or permanent) effects to FishMMO characters. It supports duration-based expiration, tick-based periodic effects, stacking, attribute modification, FX instantiation, and FishNet network synchronization. Buffs and debuffs share the same pipeline, distinguished only by an `IsDebuff` flag on the template.

## Supported Platforms

| Platform | Status | Notes |
|----------|--------|-------|
| Windows  | ✅ Supported | Primary development platform |
| Linux    | ✅ Supported | Server and client builds |
| WebGL    | ✅ Supported | Via Unity WebGL export |

Built with **Unity 6.3 LTS** using **IL2CPP** scripting backend.

## Features

- **Duration-based expiration** — Buffs automatically expire after a configurable duration
- **Tick-based periodic effects** — Periodic `OnTick` callbacks at configurable intervals
- **Stacking** — Buffs support up to `MaxStacks` with symmetric modifier accounting
- **Attribute modification** — `AttributeBuffTemplate` grants bonus attributes via `AddModifier()` on the `ExternalModifier` layer
- **FX instantiation** — Client-side visual effect prefabs attached to character mesh
- **FishNet network synchronization** — Full broadcast-based sync for owners and observers
- **Permanent buffs** — `IsPermanent` flag protects buffs from mass-removal operations
- **Buff/debuff distinction** — Unified pipeline with `IsDebuff` flag for categorization, events, and UI
- **Static events** — `OnAddBuff`, `OnRemoveBuff`, `OnAddDebuff`, `OnRemoveDebuff`, `OnSubtractTime`, `OnAddTime` for UI and other systems
- **Database persistence** — Buffs are serialized/deserialized via payload methods for save/load

## Prerequisites

- Unity 6.3 LTS
- FishNetworking (FishNet)
- FishMMO Shared Core

## Installation / Build

This system is an integrated module of the FishMMO Unity project. No separate installation is required. It is automatically included when the project is opened in Unity.

## Quick Start Guide

**Applying a buff from gameplay (ability, item, region):**

```csharp
// Get the target's BuffController
IBuffController buffController = target.GetComponent<BuffController>();

// Apply a buff template (handles stacking, FX, events)
buffController.Apply(myBuffTemplate);
```

**Removing a buff:**

```csharp
// Remove a specific buff by template ID
buffController.Remove(buffTemplateID);

// Remove all non-permanent buffs
buffController.RemoveAll(ignoreInvokeRemove: false);

// Remove a random buff (with inclusion flags)
buffController.RemoveRandom(rng, includeBuffs: true, includeDebuffs: true);
```

`RemoveAll(ignoreInvokeRemove)` iterates a snapshot copy, skipping `IsPermanent` buffs.

`RemoveRandom(rng, includeBuffs, includeDebuffs)` attempts up to 10 random selections, skipping permanent buffs and checking buff/debuff inclusion flags.

**Creating a new buff template type:**

1. Create a new class extending `BaseBuffTemplate` in `Template/Types/`.
2. Add a `[CreateAssetMenu]` attribute for Unity's asset creation menu.
3. Implement the five abstract methods:
   - `OnApply(Buff buff, ICharacter target)` — Initial application effect.
   - `OnRemove(Buff buff, ICharacter target)` — Cleanup when the buff is fully removed.
   - `OnApplyStack(Buff buff, ICharacter target)` — Effect when a stack is added.
   - `OnRemoveStack(Buff buff, ICharacter target)` — Effect when a stack is removed.
   - `OnTick(Buff buff, ICharacter target)` — Periodic effect each tick interval.
4. Optionally override `SecondaryTooltip(Utf16ValueStringBuilder)` for custom tooltip content.
5. Optionally override `OnApplyFX(Buff, ICharacter)` for custom visual effects.

**Important**: Ensure `OnApply`/`OnRemove` and `OnApplyStack`/`OnRemoveStack` are symmetric — every effect applied must be fully reversed on removal to avoid modifier leaks.

## Configuration

### Template Properties

`BaseBuffTemplate` exposes the following configurable fields:

| Property | Type | Description |
|----------|------|-------------|
| `FXPrefab` | `GameObject` | Visual effect prefab instantiated on the character (client-side only) |
| `Description` | `string` | Tooltip description text |
| `Icon` | `Sprite` | UI icon |
| `Duration` | `float` | Total duration in seconds (0 = permanent or event-driven) |
| `TickRate` | `float` | Interval in seconds between `OnTick` calls |
| `UseCount` | `uint` | Number of times the buff can be triggered |
| `MaxStacks` | `uint` | Maximum stack count (0 = no stacking) |
| `IsPermanent` | `bool` | If true, `RemoveAll` and `RemoveRandom` skip this buff |
| `IsDebuff` | `bool` | Determines buff vs debuff categorization for events and UI |

### Attribute Modification

`AttributeBuffTemplate` is the concrete template type that modifies character attributes. It holds a `List<BuffAttributeTemplate>` where each entry pairs a `CharacterAttributeTemplate` with an `int Value`.

| Hook | Effect |
|------|--------|
| `OnApply` | For each `BonusAttribute`: `characterAttribute.AddModifier(+Value)` |
| `OnRemove` | For each `BonusAttribute`: `characterAttribute.AddModifier(-Value)` |
| `OnApplyStack` | Delegates to `OnApply` (adds another `+Value`) |
| `OnRemoveStack` | Delegates to `OnRemove` (adds another `-Value`) |
| `OnTick` | No-op for attribute buffs |

All modifications go through `CharacterAttribute.AddModifier()`, which operates on the `ExternalModifier` layer. This ensures buff bonuses are never overwritten by the formula recalculation system (see CharacterAttribute README).

### Static Events

All events are defined on `IBuffController`:

| Event | Signature | When Fired |
|-------|-----------|------------|
| `OnAddTime` | `Action<Buff>` | When time is added to a buff |
| `OnSubtractTime` | `Action<Buff>` | Every frame during `Update()` |
| `OnAddBuff` | `Action<Buff>` | When a non-debuff is applied |
| `OnRemoveBuff` | `Action<Buff>` | When a non-debuff is removed |
| `OnAddDebuff` | `Action<Buff>` | When a debuff is applied |
| `OnRemoveDebuff` | `Action<Buff>` | When a debuff is removed |

### External Integration Points

The buff system is consumed by and interacts with:

- **Ability System** — Abilities apply buffs/debuffs to targets via `BuffController.Apply(template)`.
- **CharacterAttribute System** — `AttributeBuffTemplate` modifies attributes via `AddModifier()` on the `ExternalModifier` layer.
- **CharacterDamageController** — `RemoveAll()` is called on kill to clear all non-permanent buffs.
- **Item System** — Items may apply buffs on use or equip.
- **Database Layer** — Buffs are persisted and restored via `CharacterBuffData` DTO and loaded through `ReadPayload` → `Apply(Buff buff)`.
- **UI** — Buff icons, tooltips, and timers are driven by `OnAddBuff`/`OnRemoveBuff`/`OnSubtractTime` events.

### Notes

- **FX Prefabs**: `BaseBuffTemplate.OnApplyFX` instantiates `FXPrefab` as a child of the character's `MeshRoot` (or `Transform`). FX prefabs are expected to be self-destroying — they manage their own lifetime and clean up after their effect ends.

## Usage Examples

### Network Synchronization

#### Payload Serialization (FishNet Reader/Writer)

- **WritePayload**: Writes `Int32(count)`, then for each buff: `Int32(templateID)`, `Single(remainingTime)`, `Single(tickTime)`, `Int32(stacks)`.
- **ReadPayload**: Reads the payload and calls `Apply(Buff buff)` for each entry, which re-applies all attribute modifiers.

#### Client Broadcast Receivers

| Broadcast | Purpose |
|-----------|---------|
| `BuffAddBroadcast` | Owner-targeted add buff update |
| `BuffAddMultipleBroadcast` | Owner-targeted bulk add buff update |
| `BuffRemoveBroadcast` | Owner-targeted remove buff update |
| `BuffRemoveMultipleBroadcast` | Owner-targeted bulk remove buff update |
| `CharacterObserverBuffAddBroadcast` | Observer-targeted add buff updates with `CharacterID` routing |
| `CharacterObserverBuffRemoveBroadcast` | Observer-targeted remove buff updates with `CharacterID` routing |

Client broadcasts use `Apply(BaseBuffTemplate)` (the gameplay path), not `Apply(Buff buff)`, because they represent new game events rather than state restoration.

Observer-targeted messages resolve the destination character via `BaseCharacter.ClientCharacters[msg.CharacterID]`, then apply changes through the resolved `IBuffController`.

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Buff applies correctly | Apply a buff template via `BuffController.Apply(template)` | Buff appears in controller dictionary; `OnAddBuff`/`OnAddDebuff` event fires |
| Stacking works | Apply same buff multiple times (up to `MaxStacks`) | `Stacks` increments; attribute modifiers accumulate |
| Duration expiration | Wait for `Duration` seconds after apply | Stacks decrement one at a time; buff removed when stacks reach 0 |
| Tick fires | Apply buff with non-zero `TickRate` | `OnTick` called at each tick interval |
| Removal cleans up | Call `Remove(buffID)` | All modifiers reversed; `OnRemoveBuff`/`OnRemoveDebuff` fires |
| Permanent buff protection | Call `RemoveAll()` with `IsPermanent` buff active | Permanent buff remains |
| Network sync (owner) | Apply buff on server | `BuffAddBroadcast` received on owning client; buff applied locally |
| Network sync (observer) | Apply buff on server with nearby observers | `CharacterObserverBuffAddBroadcast` received; buff visible on observed character |
| DB persistence | Save character with active buffs, reload | Buffs restored via `ReadPayload` → `Apply(Buff buff)` with correct stacks/time |
| FX instantiation | Apply buff with `FXPrefab` set | FX prefab spawned as child of `MeshRoot`; self-destroys after effect |
| Modifier balance | Apply and fully remove a stacked buff | Net modifier change is zero (every `+V` paired with `-V`) |

## Flow Diagram

### Buff Lifecycle

#### 1. Application

A buff enters the system through one of two `Apply` overloads on `BuffController`:

| Overload | Entry Point | Use Case |
|----------|------------|----------|
| `Apply(BaseBuffTemplate)` | Gameplay trigger (ability, item, region) | Creates a new `Buff`, calls `buff.Apply(Character)`, handles stacking + FX |
| `Apply(Buff buff)` | DB load / network payload (`ReadPayload`) | Receives pre-constructed `Buff` with existing `Stacks`, calls `buff.Apply(Character)` + re-applies stack modifiers without incrementing `Stacks` |

**Application flow** (`Apply(BaseBuffTemplate)`):

```
Apply(template)
  ├── Buff already exists?
  │   └── No  → Create Buff(template.ID)
  │            → buff.Apply(Character)           // Template.OnApply → AddModifier(+V) per attribute
  │            → Add to dictionary
  │            → Fire OnAddBuff / OnAddDebuff
  ├── Stacking allowed? (MaxStacks > 0 && Stacks < MaxStacks)
  │   └── Yes → buff.AddStack(Character)         // Template.OnApplyStack → AddModifier(+V) again
  │            → ++Stacks
  │            → ResetDuration
  │   └── No  → ResetDuration only
  └── Template.OnApplyFX(buff, Character)        // Client-side FX instantiation
```

**Restoration flow** (`Apply(Buff buff)`) — for DB load / network sync:

```
Apply(buff)
  ├── Already tracked? → skip
  └── Not tracked:
      → buff.Apply(Character)                    // Base application: OnApply → AddModifier(+V)
      → Add to dictionary
      → Loop buff.Stacks times:
      │   → Template.OnApplyStack(buff, Character) // Re-apply each stack's modifiers
      │   (Stacks NOT incremented — already set from source)
      → Fire OnAddBuff / OnAddDebuff
```

#### 2. Ticking

`BuffController.Update()` runs every frame:

```
foreach buff:
  SubtractTime(deltaTime)
  Fire IBuffController.OnSubtractTime
  if RemainingTime > 0:
    SubtractTickTime(deltaTime)
    if TickTime <= 0:
      Template.OnTick(buff, Character)
      ResetTickTime()
  else:
    if Stacks > 0:
      RemoveStack(Character)                     // Template.OnRemoveStack → AddModifier(-V)
      --Stacks
      ResetDuration                              // Continue with remaining stacks
    else:
      Queue for removal
```

#### 3. Removal

```
Remove(buffID)
  → buff.Remove(Character)                       // Template.OnRemove → AddModifier(-V) per attribute
  → Remove from dictionary
  → Fire OnRemoveBuff / OnRemoveDebuff
```

### Stacking Model

Each buff can have up to `Template.MaxStacks` stacks. The modifier accounting works as follows:

| Action | Modifier Calls | Stacks After |
|--------|---------------|-------------|
| Initial apply | `OnApply` → `AddModifier(+V)` × 1 | 0 |
| Add 1st stack | `OnApplyStack` → `AddModifier(+V)` × 1, then `++Stacks` | 1 |
| Add 2nd stack | `OnApplyStack` → `AddModifier(+V)` × 1, then `++Stacks` | 2 |
| Duration expires (Stacks=2) | `OnRemoveStack` → `AddModifier(-V)` × 1, then `--Stacks`, reset duration | 1 |
| Duration expires (Stacks=1) | `OnRemoveStack` → `AddModifier(-V)` × 1, then `--Stacks`, reset duration | 0 |
| Duration expires (Stacks=0) | `Remove` → `OnRemove` → `AddModifier(-V)` × 1 | removed |

**Total modifiers applied**: `1 (base) + N (stacks)` = `N + 1` calls to `AddModifier(+V)`.
**Total modifiers removed**: `N (stack expirations) + 1 (final remove)` = `N + 1` calls to `AddModifier(-V)`.

The system is balanced: every `+V` is paired with a `-V`.

## Project Structure

### Directory Structure

```
Buff/
├── Buff.cs                        # Runtime buff instance (time, stacks, template ref)
├── BuffController.cs              # Per-entity controller (CharacterBehaviour / NetworkBehaviour)
├── IBuffController.cs             # Buff controller interface + static events
└── Template/
    ├── BaseBuffTemplate.cs            # Abstract ScriptableObject base for all buff templates
    ├── BuffAttributeTemplate.cs       # Serializable attribute+value pair for template configuration
    ├── BuffTemplateDatabase.cs        # Name-to-template lookup database (ScriptableObject)
    └── Types/
        └── AttributeBuffTemplate.cs       # Concrete template: grants bonus attributes
```

#### Related Files (Outside This Directory)

```
Shared/Implementation/Network/Character/BuffBroadcasts.cs   # FishNet broadcast structs for buff add/remove
Shared/Implementation/Entity/BaseCharacter.cs               # Client-side character cache used for observer routing
```

### Inheritance Hierarchies

#### Runtime Instances

```
Buff                               # Standalone class (no inheritance)
```

#### Templates (ScriptableObjects)

```
CachedScriptableObject<BaseBuffTemplate>
└── BaseBuffTemplate                   # Abstract: Duration, TickRate, MaxStacks, IsPermanent, IsDebuff
    └── AttributeBuffTemplate          # Concrete: Applies BonusAttributes via AddModifier()
```

#### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── BuffController : IBuffController
```

#### Configuration Types

```
BuffAttributeTemplate              # [Serializable] class: Value + CharacterAttributeTemplate reference
```

## License

This project is subject to the FishMMO project license.
