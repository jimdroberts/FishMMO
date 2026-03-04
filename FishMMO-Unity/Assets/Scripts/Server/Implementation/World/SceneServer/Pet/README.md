# Pet System

## Overview

The Pet system is the SceneServer authority for pet lifecycle and runtime control. It handles pet follow/stay/summon/release requests, character-driven pet spawn/despawn transitions, pet summon ability integration, and pet persistence across sessions.

The subsystem separates execution responsibilities:
- Main thread: network handlers, controller/AI updates, spawning/despawning, and broadcasts.
- Async worker: pet database fetch/persist operations.
- Main-thread queue: marshaling async completion actions back to Unity/FishNet-safe execution.

## Directory Structure

```text
Pet/
├── PetSystem.cs                   # Pet lifecycle orchestration, broadcast handlers, and async persistence
├── PetSystemRuntimeData.cs        # Runtime state container
├── PetSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md                      # System documentation
```

## Core Contracts

Implemented interfaces:
- `IPetSystem`
- `IPetSystemMainThreadQueueData`

Required runtime data containers:
- `IPetSystemMainThreadQueueData`
- `IAsyncWorkerData`

## Lifecycle

### InitializeOnce()
1. Validates dependencies.
2. Registers pet-related network handlers:
   - `PetFollowBroadcast`
   - `PetStayBroadcast`
   - `PetSummonBroadcast`
   - `PetReleaseBroadcast`
3. Subscribes to ability event:
   - `AbilityObject.OnPetSummon`
4. Subscribes to character system events:
   - `OnSpawnCharacter`
   - `OnDespawnCharacter`
   - `OnPetKilled`

### OnDeinitialize()
1. Drains pending main-thread queue actions.
2. Unregisters pet network handlers.
3. Unsubscribes ability and character callbacks.

### OnLateUpdate()
- Drains queued main-thread actions each frame.

## Player-Control Broadcast Flows

### Follow
`OnPetFollowBroadcastReceived(...)`
- Validates owner and pet controller.
- Updates pet AI home/target so pet follows the owner.

### Stay
`OnPetStayBroadcastReceived(...)`
- Validates owner and pet controller.
- Updates pet AI home to current pet position and clears follow target.

### Summon
`OnPetSummonBroadcastReceived(...)`
- Validates owner and pet controller.
- Warps AI agent to owner position.

### Release
`OnPetReleaseBroadcastReceived(...)`
- Captures pet snapshot for persistence.
- Despawns pet network object.
- Clears owner/controller references.
- Broadcasts `PetRemoveBroadcast`.
- Queues async persistence with `spawned=false`.

## Character Event Flows

### Character spawn
`CharacterSystem_OnSpawnCharacter(...)`
- Queues async load of spawned pet record.
- On success, main-thread path instantiates pooled pet, initializes AI/faction, moves pet to owner scene, spawns pet network object, and broadcasts `PetAddBroadcast`.

### Character despawn
`CharacterSystem_OnDespawnCharacter(...)`
- Captures pet state (template, abilities, alive/spawned state).
- Despawns pet object if present.
- Queues async persistence for next login/session restore.

### Pet killed
`CharacterSystem_OnPetKilled(...)`
- Reuses despawn persistence flow.
- Sends immediate `PetRemoveBroadcast` to owner.

## Ability Integration

`AbilityObject_OnPetSummon(...)` handles pet summoning from ability execution:
1. Validates template/pet prefab/controller.
2. Selects spawn position using bounding-box randomization + ground sphere cast.
3. Retrieves pooled pet object and initializes runtime fields.
4. Initializes AI follow target and faction copy from caster.
5. Moves to caster scene and spawns network object.
6. Broadcasts `PetAddBroadcast`.

## Persistence Model

`SavePetAsync(...)`:
- Reads existing pet row for optimistic version increment.
- Persists `(characterID, templateID, abilities, spawned)` state.
- Uses per-character keyed queueing to preserve operation order.

`LoadAndSpawnPetAsync(...)`:
- Fetches currently spawned pet record.
- Marshals pet reconstruction/spawn back to main thread.

Primary DB dependency:
- `ICharacterPetService`

## Async Worker and Backpressure

All async pet DB tasks are dispatched through `TryEnqueueAsyncWork(...)`:
- Returns `true` when accepted.
- Returns `false` when queue unavailable/full.
- Logs warnings on rejection/unavailability.
- Uses `entityKey = characterID` for ordered per-character pet operations.

This prevents unbounded fire-and-forget task growth and reduces race risk between release/spawn/despawn persistence paths.

## Failure Semantics

- Invalid handlers exit early with no mutation.
- Async DB/service failures are logged and do not block main thread.
- Main-thread completion paths revalidate connection/object/controller state before mutation/broadcast.
- Broadcasts are owner-scoped and sent only when state transitions are valid.