# Cooldown System

**Short description:** Deterministic, tick-based per-ability cooldown manager using immutable `CooldownInstance` structs with `IPredictableController` (Order=90) integration for FishNet Prediction V2.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features / Capabilities / Security Features](#features--capabilities--security-features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guides](#quick-start-guides)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The Cooldown system manages per-ability cooldowns for FishMMO characters using immutable, tick-based `CooldownInstance` structs. Cooldowns are recorded as `(StartTick, DurationTicks)` pairs and expire via integer comparison: `(currentTick - StartTick) >= DurationTicks`. This design is perfectly deterministic across client and server — no per-tick mutation is needed, eliminating float drift and double-subtract risks during reconcile replay.

`CooldownController` implements `IPredictableController` (Order=90), running after `BuffController` (80) and before `CharacterAttributeController` (95) in the unified prediction pipeline driven by `CharacterPredictionController`. On each tick, it calls `ExpireElapsed()` to remove finished cooldowns. Reconcile snapshots use `CooldownReconcileEntry[]` arrays with index-delta compression for bandwidth-efficient network synchronization.

## Supported Platforms

| Platform | Status | Notes |
|----------|--------|-------|
| Windows  | ✅ Supported | Primary development platform |
| Linux    | ✅ Supported | Server and client builds |
| WebGL    | ✅ Supported | Via Unity WebGL export |

Built with **Unity 6.3 LTS** using **IL2CPP** scripting backend.

## Features / Capabilities / Security Features

### Features

- **Immutable cooldown instances** — `CooldownInstance` is a `readonly struct` with `StartTick` and `DurationTicks`; no per-tick mutation needed
- **Deterministic tick-based expiry** — Integer comparison `(currentTick - StartTick) >= DurationTicks` produces identical results on client and server
- **Zero float drift** — All timing uses `uint` network ticks; seconds are only computed for UI display via `RemainingTime(currentTick)`
- **Prediction V2 integration** — `IPredictableController` (Order=90) with `PopulateInput`, `OnReplicate`, `OnCreateReconcile`, `OnReconcile`
- **Cached reconcile snapshots** — `CreateReconcileSnapshot()` returns the cached array when cooldowns haven't changed (`snapshotDirty` flag), enabling reference-equality shortcutting in the delta serializer
- **Index-delta array compression** — `CooldownReconcileEntry.WriteArrayDelta` transmits only changed entries; reference-equal arrays produce zero network bytes
- **Payload bounds checking** — `Read()` and delta serializer cap entries at 4096 to guard against malformed packets
- **Static events** — `OnAddCooldown`, `OnUpdateCooldown`, `OnRemoveCooldown` for UI integration (owner-only)

### Security Features

- Cooldown state is server-authoritative — reconcile overwrites client state on mismatch
- Payload deserialization caps array sizes at `MaxEntries` (4096) to prevent allocation attacks
- `Read()` discards expired entries based on `currentTick`, preventing stale cooldown injection

## Prerequisites

- **Unity 6.3 LTS** (or compatible version)
- **FishNetworking** with Prediction V2 support
- **FishMMO Shared Core** — `CharacterBehaviour`, `IPredictableController`, `ICooldownController`, `IntBitExtensions`
- **CharacterPredictionController** — drives the unified prediction pipeline

## Installation / Build

The Cooldown system is an integral part of the **FishMMO Unity project**. There is no separate installation step.

1. Clone or update the FishMMO repository.
2. Open the project in Unity 6.3 LTS.
3. The Cooldown system is located at `Assets/Scripts/Shared/Implementation/Entity/Prediction/Ability/Cooldown/`.
4. Ensure all FishMMO dependencies (FishNetworking, Shared Core) are present.

## Quick Start Guides

### Checking and Adding Cooldowns

```csharp
// Check if an ability is on cooldown
uint currentTick = TimeManager.LocalTick;
bool onCooldown = cooldownController.IsOnCooldown(abilityID, currentTick);

// Get remaining cooldown time in seconds (for UI)
if (cooldownController.TryGetCooldown(abilityID, currentTick, out float remaining))
{
    UpdateCooldownBar(remaining);
}

// Add a cooldown from ability activation (seconds → ticks)
float tickDelta = (float)TimeManager.TickDelta;
CooldownInstance cd = new CooldownInstance(currentTick, durationSeconds, tickDelta);
cooldownController.AddCooldown(abilityID, cd);

// Remove a specific cooldown
cooldownController.RemoveCooldown(abilityID);

// Clear all cooldowns
cooldownController.Clear();
```

### Restoring Cooldowns from Database

```csharp
// When loading from DB, cooldowns are stored as total/remaining seconds
CooldownInstance cd = CooldownInstance.FromRemainingSeconds(
    currentTick, totalDurationSeconds, remainingDurationSeconds, tickDelta);
cooldownController.AddCooldown(abilityID, cd);
```

## Configuration

### CooldownController

`CooldownController` extends `CharacterBehaviour` and implements `ICooldownController` and `IPredictableController`. It has no inspector-exposed fields — all configuration is implicit.

| Property | Type | Description |
|----------|------|-------------|
| `Order` | `int` | `90` — runs before `AbilityController` (100) so expired cooldowns are removed before ability start checks |
| `TickDelta` | `float` | Cached `TimeManager.TickDelta` for seconds↔ticks conversion. Falls back to `Time.fixedDeltaTime` if unavailable |

### CooldownInstance

An immutable `readonly struct` — never mutated after construction.

| Field/Property     | Type    | Description                                                    |
|--------------------|---------|----------------------------------------------------------------|
| `StartTick`        | `uint`  | Network tick at which the cooldown started                      |
| `DurationTicks`    | `uint`  | Duration in network ticks (immutable)                           |
| `TotalTime`        | `float` | Duration in seconds (`DurationTicks * tickDelta`), for UI      |
| `RemainingTime(uint)` | `float` | Remaining seconds at given tick, clamped to zero            |
| `RemainingTicks(uint)` | `uint` | Remaining ticks at given tick, clamped to zero              |
| `IsOnCooldown(uint)` | `bool`  | `true` when `(currentTick - StartTick) < DurationTicks`     |

#### Constructors

| Constructor | Use Case |
|-------------|----------|
| `CooldownInstance(uint startTick, float durationSeconds, float tickDelta)` | Gameplay activation — converts seconds to ticks via `ceil(duration / tickDelta)` |
| `CooldownInstance(uint startTick, uint durationTicks, float tickDelta)` | Deserialization / reconcile — pre-computed tick values |
| `CooldownInstance.FromRemainingSeconds(uint currentTick, float totalSeconds, float remainingSeconds, float tickDelta)` | DB restore — derives `StartTick` backwards so `RemainingTicks(currentTick)` is correct |

### CooldownReconcileEntry

Lightweight struct for reconcile snapshot serialization.

| Field          | Type   | Description                              |
|----------------|--------|------------------------------------------|
| `AbilityID`    | `long` | The ability this cooldown is for         |
| `StartTick`    | `uint` | Absolute network tick at cooldown start  |
| `DurationTicks`| `uint` | Duration in ticks                        |

### Static Events (on `ICooldownController`)

| Event              | Signature                         | When Fired                               |
|--------------------|-----------------------------------|------------------------------------------|
| `OnAddCooldown`    | `Action<long, CooldownInstance>`  | Cooldown added (owner-only)              |
| `OnUpdateCooldown` | `Action<long, CooldownInstance>`  | Cooldown replaced/updated (owner-only)   |
| `OnRemoveCooldown` | `Action<long>`                    | Cooldown expired or removed (owner-only) |

## Usage Examples

### Prediction Pipeline Integration

`CooldownController` participates in the unified prediction pipeline:

| Method              | Behaviour                                                                    |
|---------------------|------------------------------------------------------------------------------|
| `PopulateInput`     | No-op — cooldowns have no owner input                                        |
| `OnReplicate`       | Calls `ExpireElapsed(input.GetTick())` — deterministic expiry per tick       |
| `OnCreateReconcile` | Writes `CreateReconcileSnapshot()` → `CooldownReconcileEntry[]`             |
| `OnReconcile`       | Calls `RestoreFromReconcile(entries)` to replace all cooldowns with server state |

### Network Serialization

#### Payload (Full State)

Used by `ReadPayload`/`WritePayload` for initial character spawn:

- **Write**: `int count`, then per cooldown: `long abilityID`, `uint startTick`, `uint durationTicks`
- **Read**: Reads entries and discards any already expired relative to `currentTick`

#### Delta Serialization (Reconcile)

`CooldownReconcileEntry.WriteArrayDelta` / `ReadArrayDelta`:

- **Reference-equality shortcut**: If `ReferenceEquals(prev, next)`, returns `false` (zero bytes)
- **Same-length mode**: Writes negative count, then only indices + entries that differ
- **Different-length / forced**: Writes positive count + full array
- **Null handling**: `null` prev or next encoded as empty array with count 0

### Reconcile Snapshot Caching

`CreateReconcileSnapshot()` uses a dirty flag to avoid unnecessary allocations:

```
AddCooldown / RemoveCooldown / Clear / RestoreFromReconcile
    → snapshotDirty = true

CreateReconcileSnapshot():
    if (cooldowns.Count == 0) → return null
    if (!snapshotDirty && cachedSnapshot != null) → return cachedSnapshot  // same reference
    else → rebuild cachedSnapshot from cooldowns dictionary
```

The delta serializer's `ReferenceEquals(prev, next)` check then skips the per-element comparison entirely when the cached array is reused.

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Cooldown starts | Call `AddCooldown(id, instance)` | `IsOnCooldown(id, currentTick)` returns `true`; `OnAddCooldown` fires |
| Cooldown queries | Call `TryGetCooldown(id, currentTick, out float)` | Returns `true` with remaining seconds > 0 |
| Cooldown expiry | Advance ticks past `StartTick + DurationTicks` | `ExpireElapsed` removes the entry; `OnRemoveCooldown` fires |
| Deterministic replay | Cause a reconcile with active cooldowns | Same cooldowns expire at the same ticks during replay |
| Reconcile restore | Force a mismatch | `RestoreFromReconcile` replaces all cooldowns with server state |
| Snapshot caching | Call `CreateReconcileSnapshot()` twice without changes | Returns same array reference (`snapshotDirty == false`) |
| Delta compression | Monitor network bytes for unchanged cooldowns | Zero bytes when array reference hasn't changed |
| Payload bounds | Send payload with count > 4096 | `Read()` discards the entire payload |
| DB restore | Use `FromRemainingSeconds` to create a cooldown | `RemainingTicks(currentTick)` matches expected remaining ticks |
| Tick conversion | Compare `RemainingTime(tick)` to expected seconds | Matches `RemainingTicks(tick) * tickDelta` |

## Flow Diagram

### Cooldown Lifecycle

```
Ability Activation
        │
        ▼
AbilityController.AddCooldown(ability, currentTick)
        │
        ▼
CooldownController.AddCooldown(abilityID, CooldownInstance)
  ├── Store in SortedDictionary<long, CooldownInstance>
  ├── snapshotDirty = true
  └── Fire OnAddCooldown (owner-only)
        │
        ▼
  ┌─────────────────────────────────────────────┐
  │  Each Prediction Tick (OnReplicate)          │
  │  ExpireElapsed(currentTick):                 │
  │    foreach cooldown:                         │
  │      if (currentTick - StartTick) >= Duration│
  │        → queue for removal                   │
  │    remove expired entries                    │
  │    Fire OnRemoveCooldown for each (owner)    │
  └──────────────────────────┬──────────────────┘
                             │
                             ▼
  ┌─────────────────────────────────────────────┐
  │  Server: OnCreateReconcile                   │
  │  CreateReconcileSnapshot()                   │
  │    → CooldownReconcileEntry[] (cached if     │
  │      snapshotDirty == false)                 │
  │    → Written to CharacterReconcileData       │
  └──────────────────────────┬──────────────────┘
                             │
                             ▼ (on mismatch)
  ┌─────────────────────────────────────────────┐
  │  Client: OnReconcile                         │
  │  RestoreFromReconcile(entries)               │
  │    → Clear all cooldowns                     │
  │    → Rebuild from server entries             │
  │    → Replay ticks from reconcile point       │
  └─────────────────────────────────────────────┘
```

### Immutable Expiry Model

```
                    StartTick                    StartTick + DurationTicks
                        │                                │
  ──────────────────────┼────────────────────────────────┼──────────────
                        │◄─────── DurationTicks ────────►│
                        │                                │
  IsOnCooldown = true   │  (currentTick - StartTick)     │  IsOnCooldown = false
                        │    < DurationTicks             │
                        │                                │
  No mutation needed — expiry is a pure function of currentTick
```

## Project Structure

### Directory Structure

```
Cooldown/
├── CooldownController.cs       # Per-entity cooldown manager (CharacterBehaviour, ICooldownController, IPredictableController Order=90)
├── CooldownInstance.cs          # Immutable readonly struct: StartTick, DurationTicks, tick↔seconds conversion
└── CooldownReconcileEntry.cs    # Reconcile snapshot entry + index-delta array serialization (WriteArrayDelta/ReadArrayDelta)
```

### Related Files

```
Shared/Core/Entity/Prediction/Ability/Cooldown/ICooldownController.cs  # Interface + static events
Shared/Implementation/Entity/Prediction/CharacterPredictionController.cs  # Drives OnReplicate/OnReconcile
Shared/Implementation/Entity/Prediction/CharacterReconcileData.cs  # Contains Cooldowns field
Shared/Implementation/Entity/Prediction/CharacterReconcileDataDeltaSerializer.cs  # Calls WriteArrayDelta/ReadArrayDelta
Shared/Implementation/Entity/Prediction/Ability/AbilityController.cs  # Calls AddCooldown on ability activation
```

### Inheritance Hierarchy

```
CharacterBehaviour
└── CooldownController : ICooldownController, IPredictableController (Order=90)

readonly struct CooldownInstance                # Immutable cooldown data
struct CooldownReconcileEntry : IEquatable<>    # Serialization entry
```

## License

This project is subject to the FishMMO project license.
