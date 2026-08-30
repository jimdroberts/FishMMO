# Achievement System

**Short description:** Server-side achievement progression and reward payout system for scene-server player characters, handling event-driven updates, immediate client broadcasts, synchronous reward application, and asynchronous persistence.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
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

The Achievement system handles server-side achievement progression and reward payout for scene-server player characters. It listens to achievement update/completion events, pushes immediate client UI updates via broadcasts, applies gameplay rewards synchronously (abilities/items), and persists reward side effects asynchronously through `AsyncWorkerData` to avoid blocking gameplay flow.

All DB writes are queued through `TryEnqueueAsyncWork(...)` to `IAsyncWorkerData`. If queueing fails (backpressure/missing dependency), the system logs warnings with character/slot/template context while keeping gameplay state intact. This design keeps player feedback immediate while deferring I/O latency.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Event-driven achievement progress tracking via `IAchievementController.OnUpdateAchievement` and `IAchievementController.OnCompleteAchievement`
- Real-time client notification of achievement progress and tier updates via `AchievementUpdateBroadcast`
- Ability reward processing: learns unknown base abilities and ability events, skips already-known entries, broadcasts additions
- Item reward processing with inventory-first placement and automatic bank fallback when inventory capacity is insufficient
- Batched slot broadcast payloads for inventory and bank item changes
- Asynchronous persistence of all reward side effects through `IAsyncWorkerData` to avoid blocking gameplay
- Per-learned-template DB persistence queuing for known abilities
- Per-modified-slot DTO capture on main thread with async persistence queuing for items
- Graceful degradation: logs warnings on persistence queue failures while keeping in-memory gameplay state intact

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework
- **FishMMO Server Core** — provides `ServerBehaviour`, `IAchievementSystem`, controller interfaces, broadcast types, and `IAsyncWorkerData`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side scene-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `AchievementSystem` is present on the scene server GameObject (it inherits from `ServerBehaviour` and implements `IAchievementSystem`).
2. Verify that `IAchievementController` is registered and firing `OnUpdateAchievement` and `OnCompleteAchievement` events.
3. Confirm that the required persistence services (`ICharacterKnownAbilityService`, `ICharacterItemService`) are registered in the DB registry for reward persistence.
4. Confirm that `IAsyncWorkerData` is available for non-blocking DB write queuing.
5. On initialize, `AchievementSystem` automatically subscribes to the controller events; on deinitialize, it unsubscribes.

## Configuration

### Reward Resolution Services

The following optional persistence services are resolved from the DB registry at reward processing time:

| Service | Purpose |
|---|---|
| `ICharacterKnownAbilityService` | Persists newly learned abilities and ability events |
| `ICharacterItemService` | Persists inventory item slot changes from rewards |

### Threading Model

| Thread | Work |
|---|---|
| Main thread | Event callbacks, gameplay mutations, broadcasts, DTO capture |
| Async worker | DB persistence of reward side effects |

## Usage Examples

### Event Wiring

`AchievementSystem` subscribes to static controller events on initialize:

- `IAchievementController.OnUpdateAchievement`
- `IAchievementController.OnCompleteAchievement`

And unsubscribes on deinitialize.

### Broadcasts Emitted

| Broadcast | Purpose |
|---|---|
| `AchievementUpdateBroadcast` | Notify current progress/tier updates |
| `KnownAbilityAddMultipleBroadcast` | Notify newly learned base abilities |
| `KnownAbilityEventAddMultipleBroadcast` | Notify newly learned ability events |
| `InventorySetMultipleItemsBroadcast` | Notify inventory item reward changes |
| `BankSetMultipleItemsBroadcast` | Notify bank item reward changes |

### External Integration Points

| Integration | Role |
|---|---|
| `AchievementController` (`IAchievementController`) | Event source for achievement updates and completions |
| `AbilityController` (`IAbilityController`) | Ability learn/known checks |
| `InventoryController` / `BankController` | Item placement and slot changes |
| `AsyncWorkerData` (`IAsyncWorkerData`) | Queued non-blocking persistence |
| Database services | Known ability/inventory/bank persistence |

### Reward Categories

#### Ability Rewards

Uses generic helper `HandleAbilityGenericRewards<...>` for both `BaseAbilityTemplate` rewards and `AbilityEvent` rewards.

Behavior:

1. Skip known abilities/events.
2. Learn unknown rewards via `IAbilityController`.
3. Queue DB persist (`PersistKnownAbilityAsync`) per learned template.
4. Broadcast single/multi known-ability add payloads.

#### Item Rewards

`HandleItemRewards(...)`:

1. Checks reward list.
2. Attempts inventory route first if sufficient free slots.
3. Falls back to bank route if inventory capacity is insufficient and bank has room.
4. For each modified slot:
   - build DTO on main thread
   - queue async persistence (`PersistInventorySlotAsync` / `PersistBankSlotAsync`)
   - add slot update broadcast payload
5. Sends batched slot broadcasts:
   - `InventorySetMultipleItemsBroadcast`
   - `BankSetMultipleItemsBroadcast`

## Operational Checks

| Check | How to Verify |
|---|---|
| Event subscription active | Confirm `AchievementSystem` initializes without errors; controller events are wired |
| Progress broadcast delivery | Trigger an achievement update and verify `AchievementUpdateBroadcast` reaches the client |
| Ability reward learn | Complete an achievement with ability rewards; confirm `KnownAbilityAddMultipleBroadcast` is sent and ability is learned |
| Ability event reward learn | Complete an achievement with ability-event rewards; confirm `KnownAbilityEventAddMultipleBroadcast` is sent |
| Item reward — inventory route | Complete an achievement with item rewards when inventory has free slots; verify `InventorySetMultipleItemsBroadcast` |
| Item reward — bank fallback | Complete an achievement with item rewards when inventory is full but bank has room; verify `BankSetMultipleItemsBroadcast` |
| Async persistence queuing | Check logs for successful `TryEnqueueAsyncWork` calls after reward application |
| Persistence failure graceful degradation | Simulate persistence queue failure; confirm warning is logged and gameplay state remains intact |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart LR
    Trigger[Game event] --> Sys[AchievementSystem]
    Sys -->|check definitions| Defs[Achievement defs]
    Sys -->|update progress| DB[(PostgreSQL Achievements)]
    Sys -->|completed?| Reward[Grant rewards]
    Reward --> Client[Unity Client]
    Sys -->|broadcast| Client
```

### Progress Update

```
IAchievementController_OnUpdateAchievement(...)
│
├─ 1. Validate character/achievement objects
├─ 2. Confirm character is an IPlayerCharacter
└─ 3. Broadcast AchievementUpdateBroadcast to owner
       ├── Template ID
       ├── Current value
       └── Current tier
```

### Completion Rewards

```
IAchievementController_HandleAchievementRewards(...)
│
├─ 1. Validate character/template/tier
├─ 2. Resolve optional persistence services from DB registry
│      ├── ICharacterKnownAbilityService
│      └── ICharacterItemService
│
├─ 3. Apply reward groups
│      ├── Ability rewards (HandleAbilityGenericRewards<BaseAbilityTemplate>)
│      │    └── Skip known → Learn unknown → Queue DB persist → Broadcast
│      │
│      ├── Ability-event rewards (HandleAbilityGenericRewards<AbilityEvent>)
│      │    └── Skip known → Learn unknown → Queue DB persist → Broadcast
│      │
│      └── Item rewards (HandleItemRewards)
│           ├── Try inventory route (if free slots)
│           ├── Fallback to bank route (if inventory full)
│           ├── Build DTO on main thread per modified slot
│           ├── Queue async persistence per slot
│           └── Send batched InventorySetMultipleItemsBroadcast / BankSetMultipleItemsBroadcast
│
└─ Reward application: immediate in memory
   Persistence: queued asynchronously via IAsyncWorkerData
```

## Project Structure

### Directory Structure

```
Achievement/
├── AchievementSystem.cs   # Event-driven achievement updates and reward processing
└── README.md
```

### Related Core Contract

- `Server/Core/World/SceneServer/Achievement/IAchievementSystem.cs`

### Inheritance Hierarchy

```
ServerBehaviour
└── AchievementSystem : IAchievementSystem
```

## License

This project is subject to the FishMMO project license.
- **Database services** — known ability/inventory/bank persistence.