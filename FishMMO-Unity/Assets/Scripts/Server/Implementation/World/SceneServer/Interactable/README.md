# Interactable System

## Overview

The Interactable system validates and processes player interactions with world interactables on the SceneServer. It handles interaction dispatch, merchant purchases, ability crafting/learning, and dungeon finder instance assignment.

It is designed with:
- strict server-side validation (connection, character, scene, object, range),
- main-thread marshalling for Unity/FishNet state mutation,
- asynchronous database persistence through the shared async worker queue.

## Directory Structure

```text
Interactable/
├── README.md                                  # This document
├── IInteractableHandler.cs                    # Interaction handler contract
├── IInteractableHandlerInitializer.cs         # Handler registration contract
├── InteractableHandlerInitializer.cs          # Default handler registration ScriptableObject
├── InteractableSystem.cs                      # Main SceneServer interactable subsystem
└── InteractableSystemMainThreadQueueData.cs   # Main-thread action queue container
```

## Core Responsibilities

### 1) Interaction Dispatch

`InteractableSystem` receives `InteractableBroadcast`, validates sender and scene object, resolves the runtime interactable type, and dispatches to the registered `IInteractableHandler`.

### 2) Merchant Purchases

`MerchantPurchaseBroadcast` is validated against:
- character + inventory availability,
- merchant template existence,
- scene and object ownership/range,
- currency affordability,
- safe tab index bounds.

Successful purchases send inventory/ability updates to the client and persist changes asynchronously.

### 3) Ability Learning and Crafting

The system supports:
- direct template/event learning (`LearnAbilityTemplate`, `LearnAbilityEvent`),
- crafted ability creation from a base ability plus selected events (`AbilityCraftBroadcast`).

Validation includes duplicate-event rejection, known-event checks, max learned ability count, and currency cost calculation.

### 4) Dungeon Finder Routing

`DungeonFinderBroadcast` validates entrance/range and destination scene metadata, then asynchronously:
- checks existing character instance,
- checks party member instance conflicts,
- enqueues a new scene request when needed.

Final character instance state changes and disconnect are marshalled back to the main thread.

## Threading Model

- **Async worker queue** (`IAsyncWorkerData`): used for DB persistence and dungeon-finder async flows.
- **Main-thread queue** (`IInteractableSystemMainThreadQueueData`): used for Unity/FishNet-safe mutations (instance flags/position/disconnect).

Queue submissions use checked enqueue semantics with warning logs when work is rejected or the queue is unavailable.

## Data and Service Dependencies

`InteractableSystem` depends on:
- `WorldSceneDetailsCache` for scene validation and respawn details,
- `InteractableHandlerInitializer` for handler wiring,
- `CurrencyTemplate` for price validation,
- database services such as:
  - `ICharacterInventoryService`,
  - `ICharacterKnownAbilityService`,
  - `ICharacterAbilityService`,
  - `ISceneService`,
  - `ICharacterPartyService`.

## Network Contracts

Inbound broadcasts:
- `InteractableBroadcast`
- `MerchantPurchaseBroadcast`
- `AbilityCraftBroadcast`
- `DungeonFinderBroadcast`

Outbound broadcasts include inventory and known-ability updates, and crafted ability add notifications.

## Handler Registration

`InteractableHandlerInitializer` registers concrete handlers for built-in interactables (ability crafter, banker, merchant, world item, bindstone, teleporter, dungeon entrance). Handlers can be replaced by updating registration order or injecting a custom initializer asset.

## Reliability Notes

- Scene/object/range checks prevent cross-scene and spoofed interaction attempts.
- Merchant tab accesses use explicit index bounds checks.
- Ability craft event selection rejects duplicates and unknown/unowned events.
- Async enqueue failures are logged to surface backpressure or lifecycle ordering issues.