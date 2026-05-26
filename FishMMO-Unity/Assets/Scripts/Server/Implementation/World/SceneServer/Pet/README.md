# Pet System

**Short description:** Server-side pet lifecycle and runtime control system for scene-server player characters, handling pet follow/stay/summon/release requests, ability-driven spawning, character-driven spawn/despawn transitions, ingress guarding, and asynchronous database persistence with main-thread-marshaled broadcasts.

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

The Pet system is the SceneServer authority for pet lifecycle and runtime control. It handles pet follow/stay/summon/release requests from connected players, character-driven pet spawn/despawn transitions, pet summon ability integration, and pet persistence across sessions.

The design separates responsibilities across execution contexts:
- **Main thread:** request validation, ingress guarding, controller/AI updates, spawning/despawning, faction copying, and network broadcasts.
- **Async worker:** database fetch/persist operations dispatched through `IAsyncWorkerData`.
- **Main-thread queue:** marshaling async completion actions back to Unity/FishNet-safe context via `PetSystemMainThreadQueueData`.

All DB writes are queued through `TryEnqueueAsyncWork(...)` to `IAsyncWorkerData` with `entityKey = characterID` for per-character operation ordering. If queueing fails (backpressure/missing dependency), the system logs warnings while keeping gameplay state intact. Broadcasts are only emitted after successful state transitions, ensuring clients never see stale or uncommitted data.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Pet follow, stay, summon (warp), and release commands via network broadcasts with full validation
- Per-connection ingress guarding with debounce and in-flight protection to prevent duplicate operations across all pet control actions
- Ability-driven pet summoning via `AbilityObject.OnPetSummon` with bounding-box randomization and ground sphere cast for spawn positioning
- Automatic pet spawn on character login via async database load and main-thread marshaling
- Automatic pet state capture and async persistence on character despawn or pet death
- Pet AI initialization with follow-target and home-position management via `IAIController`
- Faction data copying from owner to pet via `IFactionController.CopyFrom`
- Shared `SpawnAndInitializePet` helper that handles existing-pet despawn, pooled object retrieval, AI/faction init, scene transfer, network spawn, and broadcast emission
- Optimistic concurrency on pet persistence with version-incremented writes via `ICharacterPetService`
- Time-sliced main-thread queue draining with configurable per-frame action limits to avoid frame spikes
- Periodic ingress guard cleanup sweeps with configurable TTL and max removals per sweep
- Achievement integration for pet summon events via configurable `AchievementTemplate`
- Graceful degradation: logs warnings on persistence queue failures while keeping in-memory gameplay state intact
- Object pool integration: pets are retrieved from and returned to the FishNet object pool to reduce allocation pressure
- Guard release deferred until async operation completes for character spawn pet loads, preventing overlap windows for duplicate operations

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework
- **FishMMO Server Core** — provides `ServerBehaviour`, `IPetSystem`, controller interfaces, broadcast types, `IAsyncWorkerData`, and `IngressGuard`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side scene-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `PetSystem` is present on the scene server GameObject (it inherits from `ServerBehaviour` and implements `IPetSystem`).
2. Verify that `PetSystemMainThreadQueueData` and `PetSystemRuntimeData` data containers are registered (declared via `[RequiresDataContainer]` attributes).
3. Confirm that `AsyncWorkerData` (`IAsyncWorkerData`) is available for non-blocking DB write queuing.
4. Confirm that `ICharacterSystem<NetworkConnection, Scene>` is registered in the behaviour registry.
5. Verify that the database service `ICharacterPetService` is available in the DB service registry.
6. On initialize, `PetSystem` automatically registers broadcast handlers for `PetFollowBroadcast`, `PetStayBroadcast`, `PetSummonBroadcast`, and `PetReleaseBroadcast`, and subscribes to `AbilityObject.OnPetSummon`, `OnSpawnCharacter`, `OnDespawnCharacter`, and `OnPetKilled`; on deinitialize, it unregisters them all.

## Configuration

### Inspector Settings

| Field | Type | Default | Purpose |
|---|---|---|---|
| `maxMainThreadActionsPerFrame` | `int` | `100` | Max pet-system actions drained from main-thread queue per frame |
| `ingressDebounceMilliseconds` | `int` | `80` | Minimum milliseconds between pet control requests per connection |
| `ingressSweepIntervalSeconds` | `float` | `5.0` | Seconds between bounded ingress guard cleanup sweeps |
| `ingressEntryTtlSeconds` | `float` | `30.0` | Seconds before stale ingress guard entries are removed |
| `ingressSweepMaxRemovals` | `int` | `128` | Maximum stale ingress guard entries removed per sweep |
| `PetSummonAchievementTemplate` | `AchievementTemplate` | `null` | Optional achievement template incremented when a pet is summoned |

### Required Data Containers

| Container | Interface | Purpose |
|---|---|---|
| `PetSystemMainThreadQueueData` | `IPetSystemMainThreadQueueData` | Per-system main-thread action queue for marshaling async completions |
| `PetSystemRuntimeData` | `IPetSystemRuntimeData` | Runtime state container for ingress guard |
| `AsyncWorkerData` | `IAsyncWorkerData` | Queued non-blocking async DB work dispatch |

### Database Service Dependencies

| Service | Purpose |
|---|---|
| `ICharacterPetService` | Fetches and persists pet records (spawned state, template, abilities, version) |

### Threading Model

| Thread | Work |
|---|---|
| Main thread | Request validation, ingress guarding, pet AI/faction updates, spawning/despawning, network broadcasts, DTO capture |
| Async worker | Database fetch/persist operations via `IAsyncWorkerData` |

## Usage Examples

### Broadcast Handlers

`PetSystem` registers the following server broadcast handlers on initialize:

- `PetFollowBroadcast` → `OnPetFollowBroadcastReceived`
- `PetStayBroadcast` → `OnPetStayBroadcastReceived`
- `PetSummonBroadcast` → `OnPetSummonBroadcastReceived`
- `PetReleaseBroadcast` → `OnPetReleaseBroadcastReceived`

And unregisters them on deinitialize.

### Broadcasts Emitted

| Broadcast | Purpose |
|---|---|
| `PetAddBroadcast` | Notify owner of successful pet spawn (includes pet ID) |
| `PetRemoveBroadcast` | Notify owner of pet removal (release or death) |

### External Integration Points

| Integration | Role |
|---|---|
| `IPetController` | Pet ownership state on the character object |
| `IAIController` | Pet AI home position, follow target, agent warping, and initialization |
| `IFactionController` | Faction data copying from owner to pet |
| `ICharacterAttributeController` | Health attribute read during despawn to determine alive/spawned state |
| `ICharacterSystem<NetworkConnection, Scene>` | Character spawn/despawn/pet-killed event subscriptions |
| `AbilityObject.OnPetSummon` | Ability-driven pet summon event integration |
| `IngressGuard` (via `IPetSystemRuntimeData`) | Per-connection debounce and in-flight protection |
| `AsyncWorkerData` (`IAsyncWorkerData`) | Queued non-blocking async persistence |
| `IAchievementController` | Optional achievement increment on pet summon |
| `PetAbilityTemplate` | Template defining pet prefab, spawn bounding box, spawn distance, and ability data |
| Database service (`ICharacterPetService`) | Pet relationship persistence and spawned-state loading |

### Ingress Guarding

Pet ingress uses per-connection operation keys with debounce + in-flight protection. Operation codes are scoped per action type (`Follow`, `Stay`, `Summon`, `Release`, `LoadPet`). For the character-spawn pet load path, guard release is deferred until the async operation completes (not at enqueue-time), preventing overlap windows for duplicate pet loads on rapid reconnect. For synchronous broadcast handlers (follow, stay, summon), guards are released in a `finally` block after the handler completes.

### Async Worker and Backpressure

`TryEnqueueAsyncWork(...)` dispatches all async DB work through `IAsyncWorkerData`.

Behavior:
- Returns `true` when accepted.
- Returns `false` when queue is unavailable or full.
- Logs warnings on rejection/unavailability.
- Uses `entityKey = characterID` for per-character ordering.

This prevents unbounded fire-and-forget tasks and preserves operation order per player.

### Persistence Model

`SavePetAsync(...)`:
- Fetches the existing pet row for optimistic version increment.
- Persists `(characterID, templateID, abilities, spawned)` state with `version + 1`.
- Uses per-character keyed queueing to preserve operation order.
- Skips persistence and logs a warning if `templateID` is invalid (≤ 0).

`LoadAndSpawnPetAsync(...)`:
- Fetches the currently spawned pet record via `ICharacterPetService.FetchSpawnedAsync`.
- Marshals pet reconstruction/spawn back to the main thread via `TryEnqueueMainThread`.
- Re-validates connection, character, and pet template before spawning.

## Operational Checks

| Check | How to Verify |
|---|---|
| System initialization | Confirm `PetSystem` initializes without errors; broadcast handlers are registered |
| Pet follow | Send a `PetFollowBroadcast` with an active pet; verify AI target is set to owner transform |
| Pet stay | Send a `PetStayBroadcast` with an active pet; verify AI home is set to pet position and target is cleared |
| Pet summon (warp) | Send a `PetSummonBroadcast` with an active pet; verify AI agent warps to owner position |
| Pet release | Send a `PetReleaseBroadcast` with an active pet; verify pet is despawned, `PetRemoveBroadcast` reaches client, and async save is queued with `spawned=false` |
| Character spawn pet load | Spawn a character with a previously saved pet; verify async load triggers and pet is instantiated with correct template and abilities |
| Character despawn persistence | Despawn a character with an active pet; verify pet state is captured and async save is queued with correct alive/spawned flag |
| Pet killed | Trigger pet death; verify despawn persistence flow runs and `PetRemoveBroadcast` reaches client |
| Ability-driven summon | Execute a pet summon ability; verify pet spawns at ground-cast position with AI initialized and `PetAddBroadcast` emitted |
| Ingress debounce | Send rapid duplicate pet control requests; confirm only the first is processed within the debounce window |
| Async persistence | Check logs for successful `TryEnqueueAsyncWork` calls after release/despawn/death operations |
| Main-thread queue draining | Verify queued actions are executed each frame within `maxMainThreadActionsPerFrame` limit |
| Persistence failure graceful degradation | Simulate persistence queue failure; confirm warning is logged and in-memory state remains unchanged |
| Achievement increment | Configure `PetSummonAchievementTemplate`; summon a pet and verify achievement counter increments |
| Ingress guard cleanup | Verify stale ingress guard entries are removed during periodic sweeps |
| Optimistic concurrency | Verify pet save logs a warning on version conflict or DB error |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart LR
    Owner[Character] -->|summon / dismiss| Sys[PetSystem]
    Sys -->|load pet state| DB[(PostgreSQL Pets)]
    Sys -->|spawn AI| Pet[Pet entity]
    Pet -->|tick / commands| Sys
    Sys -->|persist diffs| DB
    Sys -->|broadcast| Clients[Nearby Clients]
```

### Pet Follow

```
OnPetFollowBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate IPetController exists with active pet
├─ 4. Set IAIController.Home to owner position
├─ 5. Set IAIController.Target to owner transform
└─ 6. Release ingress guard (finally)
```

### Pet Stay

```
OnPetStayBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate IPetController exists with active pet
├─ 4. Set IAIController.Home to pet's current position
├─ 5. Clear IAIController.Target (null)
└─ 6. Release ingress guard (finally)
```

### Pet Summon (Warp)

```
OnPetSummonBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate IPetController exists with active pet
├─ 4. Warp IAIController.Agent to owner position
└─ 5. Release ingress guard (finally)
```

### Pet Release

```
OnPetReleaseBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate database services available
├─ 4. Validate IPetController exists with active pet
├─ 5. Capture immutable DTO (characterID, templateID, abilities)
├─ 6. Despawn pet network object to pool
├─ 7. Clear pet owner and controller references
├─ 8. Broadcast PetRemoveBroadcast to owner
├─ 9. Enqueue async SavePetAsync(spawned=false) keyed by characterID
└─ 10. Release ingress guard (finally)
```

### Character Spawn → Pet Load

```
CharacterSystem_OnSpawnCharacter(conn, character, scene)
│
├─ 1. Validate character and IPetController
├─ 2. Validate database services available
├─ 3. Acquire ingress guard (LoadPet, in-flight only)
├─ 4. Enqueue async LoadAndSpawnPetAsync(...)
│      │
│      ├─ Resolve ICharacterPetService
│      ├─ FetchSpawnedAsync pet record from DB
│      └─ Enqueue main-thread completion action:
│           ├── Re-validate connection/character/network state
│           ├── Resolve PetAbilityTemplate from templateID
│           ├── Retrieve pooled pet NetworkObject at owner position
│           ├── SpawnAndInitializePet(...)
│           │    ├── Despawn any existing pet
│           │    ├── Initialize Pet component (owner, template, abilities)
│           │    ├── Initialize AI (home, follow target)
│           │    ├── Move to owner scene
│           │    ├── Activate and network-spawn
│           │    ├── Copy faction from owner
│           │    ├── Broadcast PetAddBroadcast
│           │    └── Increment achievement (if configured)
│           └── Guard release (finally, deferred)
│
└─ On enqueue failure: release guard immediately
```

### Character Despawn → Pet Save

```
CharacterSystem_OnDespawnCharacter(conn, character)
│
├─ 1. Validate character and IPetController
├─ 2. Validate database services available
├─ 3. Read pet health to determine alive/spawned state
├─ 4. Capture immutable DTO (characterID, templateID, abilities, spawned)
├─ 5. Despawn pet network object to pool (if spawned)
└─ 6. Enqueue async SavePetAsync(characterID, templateID, abilities, spawned)
```

### Pet Killed

```
CharacterSystem_OnPetKilled(conn, character)
│
├─ 1. Reuse CharacterSystem_OnDespawnCharacter flow (captures state + async save)
└─ 2. Broadcast PetRemoveBroadcast to owner
```

### Ability-Driven Pet Summon

```
AbilityObject_OnPetSummon(petAbilityTemplate, caster)
│
├─ 1. Validate template, caster, IPetController, and pet prefab
├─ 2. Get physics scene from caster scene
├─ 3. Generate random spawn origin within bounding box
├─ 4. Sphere cast downward to find ground position
├─ 5. Retrieve pooled pet NetworkObject at ground position
└─ 6. SpawnAndInitializePet(...)
       ├── Despawn any existing pet
       ├── Initialize Pet component (owner, template)
       ├── Initialize AI (spawn position, follow target)
       ├── Move to caster scene
       ├── Activate and network-spawn
       ├── Copy faction from caster
       ├── Broadcast PetAddBroadcast
       └── Increment achievement (if configured)
```

### Failure Semantics

```
Error Handling
│
├─ Invalid requests → ignored without state mutation
├─ DB/service lookup failures → abort async operation safely
├─ Queue rejection/unavailability → logged, work skipped
├─ Async exceptions → caught and logged with character context
├─ Invalid templateID (≤ 0) → save skipped with warning
├─ Optimistic concurrency conflict → logged with version details
├─ Pooled object missing Pet component → returned to pool, spawn aborted
└─ Broadcasts → only emitted after successful state transitions
```

## Project Structure

### Directory Structure

```
Pet/
├── PetSystem.cs                       # Pet lifecycle orchestration, broadcast handlers, and async persistence
├── PetSystemRuntimeData.cs            # Runtime state container for ingress guard
├── PetSystemMainThreadQueueData.cs    # Per-system main-thread action queue container
└── README.md                          # System documentation
```

### Related Core Contracts

- `Server/Core/World/SceneServer/Pet/IPetSystem.cs`
- `Server/Core/World/SceneServer/Pet/IPetSystemMainThreadQueueData.cs`
- `Server/Core/World/SceneServer/Pet/IPetSystemRuntimeData.cs`

### Inheritance Hierarchy

```
ServerBehaviour
└── PetSystem : IPetSystem

RuntimeDataContainer
└── PetSystemRuntimeData : IPetSystemRuntimeData

SystemMainThreadQueueData
└── PetSystemMainThreadQueueData : IPetSystemMainThreadQueueData
```

## License

This project is subject to the FishMMO project license.
