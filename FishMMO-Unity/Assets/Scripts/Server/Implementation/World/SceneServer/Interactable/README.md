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
├── HandlesInteractableAttribute.cs            # Attribute for handler-to-interactable type mapping
├── IInteractableHandler.cs                    # Interaction handler contract
├── IInteractableHandlerInitializer.cs         # Handler registration contract
├── InteractableHandlerInitializer.cs          # Default handler registration ScriptableObject
├── InteractableSystem.cs                      # Main SceneServer interactable subsystem
├── InteractableSystem.AbilityCraft.cs         # Partial: ability craft broadcast handling
├── InteractableSystem.Container.cs            # Partial: container interaction handling
├── InteractableSystem.Dialogue.cs             # Partial: dialogue interaction handling
├── InteractableSystem.DungeonFinder.cs        # Partial: dungeon finder broadcast handling
├── InteractableSystem.Mailbox.cs              # Partial: mailbox interaction handling
├── InteractableSystem.Merchant.cs             # Partial: merchant purchase broadcast handling
├── InteractableSystemMainThreadQueueData.cs   # Main-thread action queue container
├── InteractableSystemRuntimeData.cs           # Runtime state (IngressGuard, handler registry)
└── Handlers/                                  # Concrete IInteractableHandler implementations
    ├── AbilityCrafterHandler.cs               # Ability crafter interaction handler
    ├── BankerHandler.cs                       # Banker interaction handler
    ├── BindstoneHandler.cs                    # Bindstone interaction handler
    ├── DialogueInteractableHandler.cs         # Dialogue interaction handler
    ├── DungeonEntranceHandler.cs              # Dungeon entrance interaction handler
    ├── MerchantHandler.cs                     # Merchant interaction handler
    ├── TeleporterHandler.cs                   # Teleporter interaction handler
    ├── WorldItemHandler.cs                    # World item interaction handler
    ├── CapturePoint/                          # Capture point handler(s)
    ├── Container/                             # Container handler(s)
    ├── GatheringNode/                         # Gathering node handler(s)
    ├── LoreObject/                            # Lore object handler(s)
    ├── Mailbox/                               # Mailbox handler(s)
    ├── Shrine/                                # Shrine handler(s)
    └── Switch/                                # Switch handler(s)
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

## Ingress Guarding Model

The system now uses a shared `IngressGuard` instance (exposed on the runtime data container) to manage per-character, per-operation debounce and in-flight state for all interactable entry points. `IngressGuard` provides:

- bounded debounce timestamps per (connection, operation)
- in-flight acquisition (one active operation key per connection+operation)
- periodic bounded sweep of stale entries

This enforces one active interactable operation per character at a time (interactable, merchant, ability-craft, and dungeon-finder), while still applying operation-specific debounce intervals.

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
- Async enqueue failures are handled explicitly:
  - inventory persistence falls back to direct async persistence with warning logs,
  - known-ability and crafted-ability persistence failures fail closed (no local learn mutation when enqueue is rejected).