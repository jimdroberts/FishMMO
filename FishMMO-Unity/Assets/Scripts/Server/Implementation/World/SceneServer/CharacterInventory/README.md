# CharacterInventory System

## Overview

The CharacterInventory system is the SceneServer authority for item movement across player inventory, equipment, and bank containers. It validates incoming client broadcasts, applies runtime container mutations, persists resulting state through database services, and echoes successful operations back to the originating client.

The system is designed to keep item state deterministic by:
- Executing container mutations on the main thread.
- Building DTO snapshots immediately after mutation.
- Enqueueing persistence work through the centralized async worker.
- Using optimistic concurrency versions on item and attribute records.

## Directory Structure

```text
CharacterInventory/
├── CharacterInventorySystem.cs     # SceneServer implementation and async persistence orchestration
└── README.md                       # System documentation
```

## Public Contract

This implementation satisfies:
- `ICharacterInventorySystem` from [Assets/Scripts/Server/Core/World/SceneServer/CharacterInventory/ICharacterInventorySystem.cs](../../../../Core/World/SceneServer/CharacterInventory/ICharacterInventorySystem.cs)

Public contract surface:
- `SwapContainerItems(container, fromIndex, toIndex, out affectedItems)`
- `SwapContainerItems(from, to, fromIndex, toIndex, out affectedFromItems, out deletedFromSlots, out affectedToItems)`

These utility methods are used by broadcast handlers to normalize same-container and cross-container swap behavior.

## Broadcast Handling

`InitializeOnce()` registers these server-side handlers:

### Inventory
- `InventoryRemoveItemBroadcast`
- `InventorySwapItemSlotsBroadcast`

### Equipment
- `EquipmentEquipItemBroadcast`
- `EquipmentUnequipItemBroadcast`

### Bank
- `BankRemoveItemBroadcast`
- `BankSwapItemSlotsBroadcast`

`OnDeinitialize()` unregisters all handlers.

## Core Validation Rules

Before any mutation, handlers validate:
1. Connection and spawned player object exist.
2. Character is present and not teleporting.
3. Required controller exists (`IInventoryController`, `IEquipmentController`, `IBankController`).
4. For bank operations, banker interaction is valid via `ValidateBankerSceneObject(...)`:
   - Scene object exists.
   - Banker is in the same scene as the character.
   - Character is in interaction range.
   - Interactable is a banker.

If validation fails, the request exits early with no mutation.

## Container Mutation Model

### Same-container swap
Uses `SwapItemSlots(...)` and returns affected items for persistence.

### Cross-container move/swap
Uses explicit get/set behavior:
- Reads source item.
- If destination occupied, swaps destination item back into source.
- If destination empty, clears source and marks source slot for delete persistence.
- Writes source item into destination slot.

The handler then persists:
- Updated slot DTOs.
- Deleted slot records (with `long.MaxValue` when item reference is no longer available).

## Persistence Pipeline

DTO builders create persistence payloads and increment `Version` for optimistic concurrency:
- `CharacterInventoryData`
- `CharacterBankData`
- `CharacterEquipmentData`
- `CharacterAttributeData`

Persistence services used:
- `ICharacterInventoryService`
- `ICharacterBankService`
- `ICharacterEquipmentService`
- `ICharacterAttributeService`

All database operations are queued through `TryEnqueueAsyncWork(...)`.

## Async Worker and Backpressure

`TryEnqueueAsyncWork(...)` resolves `IAsyncWorkerData` from the runtime data container registry and attempts enqueue.

Behavior:
- Returns `true` when accepted.
- Returns `false` when queue is unavailable or full.
- Logs warnings when work cannot be queued.
- Uses `entityKey = characterID` for per-character ordered processing.

This helps preserve operation order and prevents uncontrolled fire-and-forget task bursts.

## Equipment and Attribute Coupling

Equip/unequip operations persist both:
- Item movement updates (inventory/bank/equipment records).
- Character attribute snapshots after equipment changes.

This ensures derived combat/stat state remains synchronized with equipment state in storage.

## Failure Semantics

- Validation failures: no state change, no broadcast.
- Mutation success: client receives original success broadcast payload.
- Persistence enqueue/service failures: logged for operational visibility.

Runtime state remains authoritative in-memory; persistence failures are observable through logs and should be monitored operationally.