# Buff System

## Overview

The Buff system is a data-driven, template-based framework for applying temporary (or permanent) effects to FishMMO characters. It supports duration-based expiration, tick-based periodic effects, stacking, attribute modification, FX instantiation, and FishNet network synchronization. Buffs and debuffs share the same pipeline, distinguished only by an `IsDebuff` flag on the template.

## Directory Structure

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

### Related Files (Outside This Directory)

```
Shared/Network/Character/BuffBroadcasts.cs   # FishNet broadcast structs for buff add/remove
```

## Inheritance Hierarchies

### Runtime Instances

```
Buff                               # Standalone class (no inheritance)
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<BaseBuffTemplate>
└── BaseBuffTemplate                   # Abstract: Duration, TickRate, MaxStacks, IsPermanent, IsDebuff
    └── AttributeBuffTemplate          # Concrete: Applies BonusAttributes via AddModifier()
```

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── BuffController : IBuffController
```

### Configuration Types

```
BuffAttributeTemplate              # [Serializable] class: Value + CharacterAttributeTemplate reference
```

## Buff Lifecycle

### 1. Application

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

### 2. Ticking

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

### 3. Removal

```
Remove(buffID)
  → buff.Remove(Character)                       // Template.OnRemove → AddModifier(-V) per attribute
  → Remove from dictionary
  → Fire OnRemoveBuff / OnRemoveDebuff
```

`RemoveAll(ignoreInvokeRemove)` iterates a snapshot copy, skipping `IsPermanent` buffs.

`RemoveRandom(rng, includeBuffs, includeDebuffs)` attempts up to 10 random selections, skipping permanent buffs and checking buff/debuff inclusion flags.

## Stacking Model

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

## Attribute Modification

`AttributeBuffTemplate` is the concrete template type that modifies character attributes. It holds a `List<BuffAttributeTemplate>` where each entry pairs a `CharacterAttributeTemplate` with an `int Value`.

| Hook | Effect |
|------|--------|
| `OnApply` | For each `BonusAttribute`: `characterAttribute.AddModifier(+Value)` |
| `OnRemove` | For each `BonusAttribute`: `characterAttribute.AddModifier(-Value)` |
| `OnApplyStack` | Delegates to `OnApply` (adds another `+Value`) |
| `OnRemoveStack` | Delegates to `OnRemove` (adds another `-Value`) |
| `OnTick` | No-op for attribute buffs |

All modifications go through `CharacterAttribute.AddModifier()`, which operates on the `ExternalModifier` layer. This ensures buff bonuses are never overwritten by the formula recalculation system (see CharacterAttribute README).

## Template Properties

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

## Network Synchronization

### Payload Serialization (FishNet Reader/Writer)

- **WritePayload**: Writes `Int32(count)`, then for each buff: `Int32(templateID)`, `Single(remainingTime)`, `Single(tickTime)`, `Int32(stacks)`.
- **ReadPayload**: Reads the payload and calls `Apply(Buff buff)` for each entry, which re-applies all attribute modifiers.

### Client Broadcast Receivers

| Broadcast | Purpose |
|-----------|---------|
| `BuffAddBroadcast` | Server tells client to apply a single buff by template ID |
| `BuffAddMultipleBroadcast` | Server tells client to apply multiple buffs at once |
| `BuffRemoveBroadcast` | Server tells client to remove a single buff by template ID |
| `BuffRemoveMultipleBroadcast` | Server tells client to remove multiple buffs at once |

Client broadcasts use `Apply(BaseBuffTemplate)` (the gameplay path), not `Apply(Buff buff)`, because they represent new game events rather than state restoration.

## Static Events

All events are defined on `IBuffController`:

| Event | Signature | When Fired |
|-------|-----------|------------|
| `OnAddTime` | `Action<Buff>` | When time is added to a buff |
| `OnSubtractTime` | `Action<Buff>` | Every frame during `Update()` |
| `OnAddBuff` | `Action<Buff>` | When a non-debuff is applied |
| `OnRemoveBuff` | `Action<Buff>` | When a non-debuff is removed |
| `OnAddDebuff` | `Action<Buff>` | When a debuff is applied |
| `OnRemoveDebuff` | `Action<Buff>` | When a debuff is removed |

## Creating New Buff Types

To create a new buff template type:

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

## External Integration Points

The buff system is consumed by and interacts with:

- **Ability System** — Abilities apply buffs/debuffs to targets via `BuffController.Apply(template)`.
- **CharacterAttribute System** — `AttributeBuffTemplate` modifies attributes via `AddModifier()` on the `ExternalModifier` layer.
- **CharacterDamageController** — `RemoveAll()` is called on kill to clear all non-permanent buffs.
- **Item System** — Items may apply buffs on use or equip.
- **Database Layer** — Buffs are persisted and restored via `CharacterBuffData` DTO and loaded through `ReadPayload` → `Apply(Buff buff)`.
- **UI** — Buff icons, tooltips, and timers are driven by `OnAddBuff`/`OnRemoveBuff`/`OnSubtractTime` events.

## Notes

- **FX Prefabs**: `BaseBuffTemplate.OnApplyFX` instantiates `FXPrefab` as a child of the character's `MeshRoot` (or `Transform`). FX prefabs are expected to be self-destroying — they manage their own lifetime and clean up after their effect ends.
