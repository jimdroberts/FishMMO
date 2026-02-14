# SceneServer System

## Overview

The SceneServer system is the SceneServer node orchestration layer responsible for scene instance lifecycle, scene-server heartbeat updates, scene readiness tracking, and connection-scene routing. It coordinates Unity/FishNet scene loading with database scene metadata so world services can discover and route players to active scene instances.

The subsystem uses a split execution model:
- Main thread: scene manager event handling, mapping updates, routing, and broadcast-safe state changes.
- Async worker: database pulse/update/delete operations.
- Main-thread queue: marshaling async completion actions that must mutate Unity/FishNet state.

## Directory Structure

```text
SceneServer/
├── SceneServerSystem.cs                # Core scene server orchestration, pulses, load/unload processing
├── SceneServerRuntimeData.cs           # Scene server identity and lock state
├── SceneServerSystemMainThreadQueueData.cs # Per-system main-thread queue container
├── SceneInstanceMappingData.cs         # World/scene/handle mapping and pending scene-load tracking
├── SceneInstanceDetails.cs             # Per-instance runtime metadata and character count tracking
└── README.md                           # System documentation
```

## Core Contracts

Primary interfaces and models:
- `ISceneServerSystem<NetworkConnection>`
- `ISceneServerRuntimeData`
- `ISceneServerSystemMainThreadQueueData`
- `ISceneInstanceMappingData`
- `ISceneInstanceDetails`

Primary runtime properties:
- `PulseRate`
- `WorldSceneDetailsCache`

## Runtime Data Containers

### `SceneServerRuntimeData`
Stores:
- `ID` (database scene server identifier)
- `IsLocked` (admission/availability state)

### `SceneInstanceMappingData`
Stores:
- `WorldScenes` (world -> scene name -> handle -> instance details)
- `SceneNameByHandle` (reverse lookup)
- `PendingScenes` (queued DB scene-load requests by scene row ID)

### `SceneServerSystemMainThreadQueueData`
Dedicated queue for actions that must run on main thread after async database work.

## Initialization and Registration

`InitializeOnce()` performs:
1. Dependency validation for server services, data containers, scene manager, and character system.
2. Event subscription:
   - Scene load/unload completion
   - Character connect/disconnect/load callbacks
3. Database registration of this scene server node (`PersistAsync`).
4. Cleanup of stale scene rows from previous runs (`DeleteBySceneServerAsync`).
5. Registration of periodic pulse callback.

## Periodic Pulse Model

`OnPeriodicPulse(...)` collects:
- current character count
- per-scene pulse payload
- stale-scene unload candidates

Main-thread stale scenes are unloaded immediately. Remaining pulse data is queued to async worker (`PeriodicPulseAsync(...)`) which:
1. pulses scene server status
2. pulses active scene rows
3. dequeues pending scene-load requests
4. marshals load request processing back to main thread

## Scene Load/Unload Lifecycle

### Load request processing
`ProcessSceneLoadRequest(...)`:
- validates scene cache entry
- tracks pending scene row
- issues FishNet scene load with server params carrying DB scene ID

### Load completion
`SceneManager_OnLoadEnd(...)`:
- resolves pending DB scene row
- on failure: marks scene status failed
- on success: calls `ProcessScene(...)` and marks row ready via DB

### Unload completion
`SceneManager_OnUnloadEnd(...)`:
- removes unloaded handles from runtime mappings

### Explicit unload
`UnloadScene(handle)`:
- queues DB delete-by-handle
- requests FishNet unload

## Character Count Integration

Character events update scene instance counts through `AdjustSceneCharacterCount(...)`:
- connect/load increments
- disconnect decrements

`SceneInstanceDetails.AddCharacterCount(...)` updates `LastExit` when the scene becomes empty, enabling stale-scene detection.

## Connection Routing Helpers

`TryGetSceneInstanceDetails(...)` resolves instance metadata by world/scene/handle.

`TryLoadSceneForConnection(...)` loads a target instance scene for a connection when available and loaded.

`UnloadSceneForConnection(...)` unloads a named scene for a connection.

## Async Worker and Backpressure

All DB work is queued via `TryEnqueueAsyncWork(...)`:
- returns `true` when accepted
- returns `false` when queue unavailable/full
- logs warning on rejection/unavailability
- supports entity-keyed ordering (server/scene scoped)

This prevents unbounded fire-and-forget workload during high scene churn.

## Database Dependencies

Primary services:
- `ISceneServerService`
- `ISceneService`

Pulse and readiness flows rely on these services to keep world routing state synchronized with live scene-server state.

## Failure Semantics

- Invalid dependencies fail initialization with explicit status/error logging.
- Failed scene loads are marked in database and skipped safely.
- Async DB failures are logged and do not block main thread scene operations.
- Main-thread marshaled callbacks revalidate runtime dependencies before mutation.