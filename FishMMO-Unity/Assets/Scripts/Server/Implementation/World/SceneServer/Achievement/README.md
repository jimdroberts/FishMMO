# Achievement System

## Overview

The Achievement system handles server-side achievement progression and reward payout for scene-server player characters. It listens to achievement update/completion events, pushes immediate client UI updates via broadcasts, applies gameplay rewards synchronously (abilities/items), and persists reward side effects asynchronously through `AsyncWorkerData` to avoid blocking gameplay flow.

## Directory Structure

```
Achievement/
├── AchievementSystem.cs   # Event-driven achievement updates and reward processing
└── README.md
```

Related core contract:

- `Server/Core/World/SceneServer/Achievement/IAchievementSystem.cs`

## Inheritance Hierarchy

```
ServerBehaviour
└── AchievementSystem : IAchievementSystem
```

## Event Wiring

`AchievementSystem` subscribes to static controller events on initialize:

- `IAchievementController.OnUpdateAchievement`
- `IAchievementController.OnCompleteAchievement`

And unsubscribes on deinitialize.

## Runtime Flow

### 1) Progress Update

`IAchievementController_OnUpdateAchievement(...)`:

1. Validates character/achievement objects.
2. Confirms the character is an `IPlayerCharacter`.
3. Broadcasts `AchievementUpdateBroadcast` to the owner with:
   - template ID
   - current value
   - current tier

### 2) Completion Rewards

`IAchievementController_HandleAchievementRewards(...)`:

1. Validates character/template/tier.
2. Resolves optional persistence services from DB registry:
   - `ICharacterKnownAbilityService`
   - `ICharacterInventoryService`
   - `ICharacterBankService`
3. Applies reward groups:
   - ability rewards
   - ability-event rewards
   - item rewards (inventory-first, bank fallback)

Reward application is immediate in memory; persistence is queued asynchronously.

## Reward Categories

### Ability Rewards

Uses generic helper `HandleAbilityGenericRewards<...>` for both:

- `BaseAbilityTemplate` rewards
- `AbilityEvent` rewards

Behavior:

1. Skip known abilities/events.
2. Learn unknown rewards via `IAbilityController`.
3. Queue DB persist (`PersistKnownAbilityAsync`) per learned template.
4. Broadcast single/multi known-ability add payloads.

### Item Rewards

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

## Broadcasts Emitted

| Broadcast | Purpose |
|---|---|
| `AchievementUpdateBroadcast` | Notify current progress/tier updates |
| `KnownAbilityAddMultipleBroadcast` | Notify newly learned base abilities |
| `KnownAbilityEventAddMultipleBroadcast` | Notify newly learned ability events |
| `InventorySetMultipleItemsBroadcast` | Notify inventory item reward changes |
| `BankSetMultipleItemsBroadcast` | Notify bank item reward changes |

## Async Persistence Strategy

All DB writes are queued through `TryEnqueueAsyncWork(...)` to `IAsyncWorkerData`:

- known ability inserts
- inventory slot persistence
- bank slot persistence

If queueing fails (backpressure/missing dependency), the system logs warnings with character/slot/template context while keeping gameplay state intact.

## Threading Model

| Thread | Work |
|---|---|
| Main thread | Event callbacks, gameplay mutations, broadcasts, DTO capture |
| Async worker | DB persistence of reward side effects |

This design keeps player feedback immediate while deferring I/O latency.

## External Integration Points

- **AchievementController** (`IAchievementController`) — event source.
- **AbilityController** (`IAbilityController`) — ability learn/known checks.
- **InventoryController / BankController** — item placement and slot changes.
- **AsyncWorkerData** (`IAsyncWorkerData`) — queued non-blocking persistence.
- **Database services** — known ability/inventory/bank persistence.