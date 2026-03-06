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
├── SceneServerRuntimeData.cs           # Scene server identity, lock state, and reusable buffers
├── SceneServerSystemMainThreadQueueData.cs # Per-system main-thread queue container
├── SceneInstanceMappingData.cs         # World/scene/handle mapping, flat handle lookup, and pending tracking
├── SceneInstanceDetails.cs             # Per-instance runtime metadata and character count tracking
├── PendingSceneInfo.cs                 # Struct combining SceneData + enqueue timestamp (Core)
└── README.md                           # System documentation
```

## Core Contracts

Primary interfaces and models:
- `ISceneServerSystem<NetworkConnection>`
- `ISceneServerRuntimeData`
- `ISceneServerSystemMainThreadQueueData`
- `ISceneInstanceMappingData`
- `ISceneInstanceDetails`
- `PendingSceneInfo` (readonly struct)

Primary runtime properties:
- `PulseRate`
- `WorldSceneDetailsCache`

## Configuration

| Field | Default | Description |
|-------|---------|-------------|
| `maxMainThreadActionsPerFrame` | 100 | Max queued scene-server actions drained from main-thread queue per frame |
| `pendingSceneTimeoutSeconds` | 60.0 | Max age (seconds) before a pending scene load request is failed |
| `pendingSceneSweepIntervalSeconds` | 2.0 | Interval between bounded pending-scene cleanup sweeps |
| `pendingSceneSweepMaxRemovals` | 64 | Max expired pending scenes removed per sweep pass |
| `maxScenesLoadedPerPulse` | 3 | Max scene load requests dequeued from DB per pulse cycle |
| `pulseRate` | 5.0 | Interval (seconds) between heartbeat pulses to the database |

All config values are clamped to safe minimums in `InitializeOnce()`.

## Runtime Data Containers

### `SceneServerRuntimeData`
Stores:
- `ID` (database scene server identifier)
- `IsLocked` (admission/availability state)
- Reusable main-thread buffers (zero-allocation pulse collection):
  - `ScenePulseDataBuffer` (simplified `(Handle, CharacterCount)` tuples — stale metadata stays local)
  - `ScenesToUnloadBuffer`, `SceneGroupValuesBuffer`, `SceneDetailsValuesBuffer`
  - `ExpiredSceneIdsBuffer`, `UnloadedHandlesBuffer`

### `SceneInstanceMappingData`
Stores:
- `WorldScenes` (world → scene name → handle → instance details) — nested hierarchy
- `SceneInstanceByHandle` (handle → instance details) — flat O(1) lookup kept in sync with `WorldScenes`
- `SceneNameByHandle` (handle → scene name) — reverse name lookup
- `PendingScenes` (`Dictionary<long, PendingSceneInfo>`) — each entry bundles `SceneData` + `EnqueuedUtc` in a single struct, eliminating the previous dual-map sync risk

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
6. Clamping of all config values to safe minimums.

## Periodic Pulse Model

`OnPeriodicPulse(...)` collects:
- current character count
- per-scene pulse payload (via reusable buffers — no per-pulse GC allocations)
- stale-scene unload candidates

Buffer population uses manual `foreach` loops instead of `AddRange(collection.Values)` to avoid boxing `Dictionary.ValueCollection` struct enumerators through `IEnumerable<T>`.

The `ScenePulseDataBuffer` uses a simplified `(Handle, CharacterCount)` tuple — stale-pulse detection (`StalePulse`, `TimeSinceLastExit`) stays local on the main thread and is never sent to the async method. This eliminates unused fields from the async payload.

Main-thread stale scenes are unloaded immediately. The pulse buffer is **snapshotted** before passing to the async worker, preventing a fragile shared-buffer pattern where a future refactor could clear the reusable buffer while async is still reading it:

```csharp
var pulseSnapshot = new List<(int, int)>(runtimeData.ScenePulseDataBuffer);
```

`PeriodicPulseAsync(...)` then:
1. Pulses scene server status
2. Passes the snapshot directly to `PulseBatchAsync` (no conversion list needed)
3. Dequeues up to `maxScenesLoadedPerPulse` pending scene-load requests (bounded loop, breaks on first empty dequeue)
4. Marshals each load request back to main thread via `TryEnqueueMainThread`

## Scene Load/Unload Lifecycle

### Load request processing
`ProcessSceneLoadRequest(...)`:
- Validates scene cache entry
- **Atomic duplicate guard**: `TryAdd` rejects if `PendingScenes` already contains the scene ID — prevents race if two queued callbacks target the same scene
- Stores a `PendingSceneInfo` struct (SceneData + EnqueuedUtc) in the merged map
- Issues FishNet scene load with server params carrying DB scene ID

### Load completion
`SceneManager_OnLoadEnd(...)`:
- Resolves `PendingSceneInfo` from the merged map, extracts `SceneData`
- Single `PendingScenes.Remove()` cleans both scene data and timestamp atomically
- On failure: marks scene status failed
- On success: calls `ProcessScene(...)` which populates both the nested `WorldScenes` hierarchy and the flat `SceneInstanceByHandle` map, then marks the row ready via DB
- PhysicsTicker GameObjects are created with `HideFlags.DontSave` for explicit cleanup safety

### Unload completion
`SceneManager_OnUnloadEnd(...)`:
- Uses O(1) `SceneInstanceByHandle` lookup per unloaded handle
- Removes from both the flat map and the targeted nested dictionary entry
- **Cleans up empty containers**: removes empty scene-name dictionaries and empty world-server entries to prevent memory leak from scene churn
- Eliminates collection-modification race risk (no longer iterates `WorldScenes.Values`)

### Explicit unload
`UnloadScene(handle)`:
- Queues DB delete-by-handle (with result check)
- Requests FishNet unload

## Character Count Integration

Character events update scene instance counts through `AdjustSceneCharacterCount(...)`:
- connect/load increments
- disconnect decrements

`SceneInstanceDetails.AddCharacterCount(...)` updates `LastExit` when the scene becomes empty, enabling stale-scene detection.

## Connection Routing Helpers

`TryGetSceneInstanceDetails(...)` resolves instance metadata via O(1) `SceneInstanceByHandle` lookup, then validates the caller's `worldServerID`/`sceneName` expectations match the stored details as a correctness check.

`TryLoadSceneForConnection(...)` loads a target instance scene for a connection when available and loaded.

`UnloadSceneForConnection(...)` unloads a named scene for a connection.

## Async Worker and Backpressure

All DB work is queued via `TryEnqueueAsyncWork(...)`:
- Returns `true` when accepted
- Returns `false` when queue unavailable/full
- **Every call site checks the return value** and logs a warning on failure
- **Fire-and-forget fallback**: when enqueue fails, critical DB updates (status failures, ready marks, deletes) are fired directly via `_ = SomeAsync(...)` so the database is never left with stale state
- Supports entity-keyed ordering (server/scene scoped)

This prevents unbounded fire-and-forget workload during high scene churn while ensuring the DB always receives critical state transitions.

## Database Dependencies

Primary services:
- `ISceneServerService`
- `ISceneService`

Pulse and readiness flows rely on these services to keep world routing state synchronized with live scene-server state.

## Scalability Features

| Feature | Description |
|---------|-------------|
| **Flat handle→details map** | `SceneInstanceByHandle` provides O(1) lookup for unload, routing, and character count adjustment — replaces O(worlds × scenes) nested iteration |
| **Scene load rate limiting** | `maxScenesLoadedPerPulse` bounds dequeue loop to prevent DB flood from overwhelming the scene server |
| **Duplicate load guard** | `TryAdd` atomically rejects duplicate scene IDs — prevents race from two queued callbacks targeting the same scene |
| **Zero-allocation pulse collection** | Reusable buffers + manual `foreach` (no `AddRange` enumerator boxing) eliminate per-pulse GC pressure |
| **Pulse snapshot** | `ScenePulseDataBuffer` is snapshotted before async dispatch — eliminates fragile shared-buffer pattern |
| **Simplified pulse data** | Stale metadata stays on main thread; async only receives `(Handle, CharacterCount)` — no conversion alloc |
| **PendingSceneInfo struct** | Merges `SceneData` + `EnqueuedUtc` into a single map entry — eliminates dual-map sync risk |
| **Empty container cleanup** | Unload handler prunes empty scene-name and world-server dictionaries — prevents memory leak from churn |
| **Async enqueue fallback** | Every `TryEnqueueAsyncWork` call is checked; failures fire the async method directly so the DB is never left stale |
| **PhysicsTicker HideFlags** | `HideFlags.DontSave` ensures explicit cleanup of scene-bound GameObjects |
| **Scene handle reuse safety** | Handle mappings are always removed on unload; documented at both Add and Remove sites |
| **Enumerate-then-remove safety** | `SweepExpiredPendingScenes` collects IDs before removal; pattern documented inline |
| **Pending scene TTL** | Bounded sweep with configurable timeout, interval, and max removals per pass |
| **Atomic pulse gate** | `Interlocked.CompareExchange` prevents overlapping async pulses |

## Failure Semantics

- Invalid dependencies fail initialization with explicit status/error logging.
- Failed scene loads are marked in database and skipped safely.
- Duplicate scene load requests are rejected atomically via `TryAdd` with a warning log.
- Async DB failures are logged and do not block main thread scene operations.
- Failed `TryEnqueueAsyncWork` calls fire the async method directly as a fallback — the DB always receives critical state transitions.
- Main-thread marshaled callbacks revalidate runtime dependencies before mutation.
- Pending scene requests that exceed `pendingSceneTimeoutSeconds` are failed and cleaned up in bounded sweeps.
- Unity scene handles may be reused after unload; all handle→details mappings are removed on unload to prevent stale resolution.