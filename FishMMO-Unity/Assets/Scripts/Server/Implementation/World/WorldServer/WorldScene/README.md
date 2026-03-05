# WorldScene System

## Overview

The WorldScene system routes authenticated world-server players into the correct scene endpoint. It supports both open-world and instance scene flows, maintains waiting queues while scenes are loading, coordinates scene-server assignment, and keeps a live connection-count metric used by world admission logic. Database work is asynchronous; all FishNet/Unity state mutations are marshalled onto the main thread through a dedicated queue container.

It also includes request-debounce, queue-TTL hardening, and a write-through TTL cache layer to reduce database flood risk and prevent stale queue memory growth.

## Directory Structure

```
WorldScene/
├── WorldSceneSystem.cs                    # Queue orchestration, scene routing, DB coordination
├── WorldSceneMappingData.cs               # Runtime queue/state maps for open-world and instance routing
├── WorldSceneSystemRuntimeData.cs         # Runtime state (authenticator, timers, processing gate, caches)
├── WorldSceneSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

Related core contracts:

- `Server/Core/World/WorldServer/WorldScene/IWorldSceneSystem.cs`
- `Server/Core/World/WorldServer/WorldScene/IWorldSceneMappingData.cs`
- `Server/Core/World/WorldServer/WorldScene/IWorldSceneSystemRuntimeData.cs`
- `Server/Core/World/WorldServer/WorldScene/IWorldSceneSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`
- `Server/Core/Collections/ExpiringKeyTracker.cs`
- `Server/Core/Collections/TimedCache.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── WorldSceneSystem : IWorldSceneSystem
```

### Runtime Data Containers

```
RuntimeDataContainer
├── WorldSceneMappingData          : IWorldSceneMappingData<NetworkConnection>
├── WorldSceneSystemRuntimeData    : IWorldSceneSystemRuntimeData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── WorldSceneSystemMainThreadQueueData : IWorldSceneSystemMainThreadQueueData
```

## Runtime Data Container Details

### `WorldSceneMappingData`

Bidirectional queue maps for open-world and instance connection routing. Implements `IWorldSceneMappingData<NetworkConnection>`.

| Property | Type | Purpose |
|----------|------|---------|
| `WaitingOpenWorldConnections` | `Dictionary<string, HashSet<NetworkConnection>>` | Connections waiting for an open-world scene, keyed by scene name |
| `OpenWorldConnectionScenes` | `Dictionary<NetworkConnection, string>` | Reverse index: connection → open-world scene name |
| `WaitingInstanceConnections` | `Dictionary<long, HashSet<NetworkConnection>>` | Connections waiting for an instance scene, keyed by instance ID |
| `InstanceConnectionScenes` | `Dictionary<NetworkConnection, long>` | Reverse index: connection → instance ID |
| `ConnectionCount` | `int` | Total managed connections (DB scene population + waiting queues) |

**Design:** Bidirectional maps enable O(1) add/remove and fast cleanup on disconnect.

**Lifecycle:**
- `InitializeOnce()` — creates empty dictionaries, zeros connection count.
- `Clear()` — clears all dictionaries and resets count.
- `Deinitialize()` — clears all data (references remain as Dictionary types don't need nulling).

### `WorldSceneSystemRuntimeData`

Mutable runtime state for queue processing, debounce, caching, and authenticator references. Implements `IWorldSceneSystemRuntimeData`.

| Property | Type | Purpose |
|----------|------|---------|
| `IsProcessingQueue` | `int` | Processing gate state to prevent overlapping queue-processing cycles |
| `WaitQueueRateSeconds` | `float` | Queue tick interval in seconds |
| `NextWaitingQueueSweep` | `float` | Countdown until stale waiting-queue purge sweep |
| `NextDebounceCleanup` | `float` | Countdown until debounce cleanup sweep |
| `InstanceLookupDebounce` | `ExpiringKeyTracker<string>` | Per-account debounce tracker preventing DB flood from rapid instance lookups |
| `WaitingQueueEnteredUtcByClientId` | `Dictionary<int, DateTime>` | Timestamps tracking when each client entered a waiting queue (for TTL purge) |
| `AvailableSceneCache` | `TimedCache<string, IReadOnlyList<SceneData>>` | Write-through TTL cache of `FetchAvailableAsync` results keyed by scene name |
| `SceneServerAddressCache` | `TimedCache<long, (string, ushort)>` | Write-through TTL cache of scene server addresses keyed by scene server ID |
| `LoginAuthenticator` | `WorldServerAuthenticator` | Reference to the world server authenticator for auth event subscription |
| `NextWaitQueueUpdate` | `float` | Timer countdown until next wait-queue processing tick |

**Lifecycle:**
- `InitializeOnce()` — creates `ExpiringKeyTracker` with `OrdinalIgnoreCase` comparer, empty timestamp dictionary, and both `TimedCache` instances (scene cache uses `OrdinalIgnoreCase` comparer).
- `Clear()` — nulls authenticator, resets timers, clears tracker, timestamps, and both caches.
- `Deinitialize()` — clears and nulls tracker, timestamp dictionary, and both caches.

### `WorldSceneSystemMainThreadQueueData`

Per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` (which inherits from `MainThreadQueueData`). Implements `IWorldSceneSystemMainThreadQueueData`.

Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread.

**Why a separate concrete type?** The `DataContainerRegistry` creates independent instances per concrete type, ensuring each system gets its own isolated main-thread queue.

## Required Data Container Attributes

`WorldSceneSystem` declares four required containers:

- `[RequiresDataContainer(typeof(WorldSceneSystemRuntimeData))]`
- `[RequiresDataContainer(typeof(WorldSceneMappingData))]`
- `[RequiresDataContainer(typeof(WorldSceneSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

## Core Responsibilities

| Responsibility | Description |
|---|---|
| Authentication handoff | Subscribes to `WorldServerAuthenticator.OnClientAuthenticationResult` and starts routing flow |
| Open-world routing | Assigns queued players to ready open-world scenes; prefers character's saved `SceneHandle` (channel switch support), falls back to any available instance with capacity; enqueues scene-load requests when needed |
| Instance routing | Routes players to ready instances or falls back to world scene if instance is invalid/stale |
| Queue maintenance | Tracks forward + reverse mappings for open-world and instance waiting queues |
| Connection counting | Aggregates DB scene population + queued connections into `ConnectionCount` |
| Scene instance caching | Write-through TTL cache reduces repeated DB polling for scene-instance and scene-server-address queries |
| Main-thread safety | Uses main-thread queue for Broadcast/Kick and map mutation operations |

## Queue Data Model

`WorldSceneMappingData` maintains four synchronized maps:

| Map | Type | Purpose |
|---|---|---|
| `WaitingOpenWorldConnections` | `Dictionary<string, HashSet<NetworkConnection>>` | waiting connections by open-world scene name |
| `OpenWorldConnectionScenes` | `Dictionary<NetworkConnection, string>` | reverse index for open-world waiting membership |
| `WaitingInstanceConnections` | `Dictionary<long, HashSet<NetworkConnection>>` | waiting connections by instance ID |
| `InstanceConnectionScenes` | `Dictionary<NetworkConnection, long>` | reverse index for instance waiting membership |

This bidirectional model enables fast add/remove and cleanup on disconnect.

Queue membership timestamps are tracked by `ClientId` to enforce stale-wait TTL cleanup.

## Processing Loop

`OnUpdate` performs:

1. Resolve `WorldSceneSystemRuntimeData` once (cached for entire frame).
2. Drain main-thread action queue.
3. Sweep stale waiting queue entries (TTL purge) — `runtimeData` passed through to avoid redundant lookups.
4. Sweep expired instance-lookup debounce entries and scene caches (`SweepSceneCaches`).
5. Tick wait-queue timer (`WaitQueueRateSeconds`, min 0.5 s, default 2 s).
6. Snapshot current open-world scene keys + pending instance connections (capacity-hint lists).
7. Acquire runtime processing gate.
8. Enqueue async queue-processing worker task.

A runtime-data processing gate prevents overlapping queue cycles.

## Security and DoS Hardening

### Instance-routing debounce (DB flood protection)

- Instance routing lookups are debounced per account name.
- Rapid reconnect attempts for the same account within the debounce window are dropped.
- This reduces repeated `FetchByAccountAsync`/instance lookups and protects DB pool capacity.
- Debounce TTL entries are managed by shared `ExpiringKeyTracker<string>` with bounded head-first expiry sweeps.

### Waiting queue TTL purge (ghost queue protection)

- Connections in waiting maps are timestamped when queued.
- Connections that remain queued beyond TTL are purged from forward/reverse maps.
- Active stale waiters are kicked; inactive entries are cleaned silently.
- Periodic sweeps prevent unbounded growth of waiting `Dictionary<..., HashSet<NetworkConnection>>` structures.

### Async routing race guard

- The snapshot phase removes connections from waiting maps before going async.
- If a connection reconnects and is re-added to the queue during async processing, the broadcast lambda detects the re-queue and skips the stale routing.
- Both open-world and instance routing check `OpenWorldConnectionScenes.ContainsKey` / `InstanceConnectionScenes.ContainsKey` before broadcasting.
- This prevents stale routing from reaching connections that were already re-queued for fresh processing.

### Scene server cache invalidation

- Cached scene-server addresses are invalidated (via `TimedCache.Invalidate`) when a fetch fails (`!result.IsSuccess`).
- Batch scene-server resolution invalidates all requested IDs when the batch call fails.
- This prevents up-to-TTL stale routing to dead scene servers.
- Default `sceneServerCacheTtlSeconds` is 10 s (short enough to limit blast radius, long enough to absorb bursts).

## Open-World Routing Flow

`ProcessOpenWorldQueueAsync(sceneName)`:

1. Fetch available ready scene instances (cache-aware via `FetchAvailableScenesAsync`).
2. Resolve scene server addresses using cache-first, batch-fetch-miss strategy via `FetchSceneServersByIDsAsync`. Build per-handle capacity tracker.
3. Snapshot and dequeue all waiting connections on the main thread.
4. **Fetch phase** — batch-load character data via `FetchSelectedCharactersByAccountsAsync`; results are mapped back to connections by account name.
5. **Routing phase** — for each connection with valid character data:
   - Prefer the character's saved `SceneHandle` (enables channel switching via `SceneChannelSystem`).
   - Fall back to any instance with remaining capacity.
   - No capacity → re-queue for the next cycle.
   - Persist updated `SceneHandle` if changed.
   - Broadcast `WorldSceneConnectBroadcast` with race guard (skip if connection was re-queued during async work).
6. `CleanupAndEnqueueNewSceneIfNeededAsync` removes empty waiting buckets and enqueues a DB scene-load request if connections are still waiting.

`GetMaxClients(sceneName)` uses `WorldSceneDetailsCache` and clamps to `[1, MAX_CLIENTS_PER_INSTANCE]`.

## Instance Routing Flow

`ProcessInstanceConnectionAsync(conn)`:

1. Validate connection/account on main thread.
2. **Remove from instance queue immediately** (prevents re-processing, erroneous TTL kicks, and stale routing during the async window).
3. Fetch selected character.
4. If not in instance → fallback to world-scene queue.
5. If instance flag is stale/invalid → clear flag in DB, fallback to world scene.
6. Fetch instance scene row:
   - `Ready` → fetch scene server and broadcast connect endpoint (with race guard). If scene server fetch fails → delete stale scene entry and fall back to world scene.
   - `Pending/Loading` → **re-add** to instance waiting map.
   - Unknown/terminal status → clear flag and fall back to world scene.

## Fallback Behavior

`FallbackToWorldSceneAsync` resolves the selected character’s world scene and re-queues the connection into open-world waiting maps, removing it from instance maps first.

## Connection Count Update

`UpdateConnectionCountAsync` computes:

$$
\text{ConnectionCount} = \text{DB scene character total} + \text{waiting open-world} + \text{waiting instance}
$$

The final write to `IWorldSceneMappingData.ConnectionCount` is marshalled to the main thread.

## Event Wiring and Lifecycle

### InitializeOnce

- Validates dependencies and containers.
- Resolves `WorldServerAuthenticator`.
- Subscribes to:
  - `ServerManager.OnRemoteConnectionState`
  - `LoginAuthenticator.OnClientAuthenticationResult`

### OnDeinitialize

- Drains pending main-thread actions.
- Clears debounce tracker, waiting-queue timestamps, and both scene caches.
- Unsubscribes both events.
- Deletes world-scene rows for this world server from DB as shutdown cleanup.

## Threading Model

| Thread | Work |
|---|---|
| Main thread | queue/map mutations, FishNet broadcasts, kicks, event callbacks |
| Async worker | DB fetch/update/persist operations and queue orchestration |

All thread-sensitive operations are marshalled via `WorldSceneSystemMainThreadQueueData`.

## Scene Instance Cache

Two `TimedCache` instances in `WorldSceneSystemRuntimeData` reduce database polling:

| Cache | Key | Value | Default TTL |
|---|---|---|---|
| `AvailableSceneCache` | scene name (`string`) | `IReadOnlyList<SceneData>` | 5 s |
| `SceneServerAddressCache` | scene server ID (`long`) | `(string Address, ushort Port)` | 10 s |

- **Write-through**: every successful DB fetch populates the cache.
- **Reads do NOT extend lifetime**: entries expire relative to write time.
- **Invalidation on failure**: a failed `FetchAsync` call invalidates the corresponding cache entry so the next caller re-fetches immediately.
- **Sweep**: `SweepSceneCaches` runs during the debounce cleanup cycle with bounded head-first traversal (max 64 scan, 32 remove).
- **Disable**: set TTL to 0 to bypass caching entirely.

### Helper Methods

| Method | Purpose |
|---|---|
| `FetchAvailableScenesAsync` | Cache-aware wrapper around `ISceneService.FetchAvailableAsync` |
| `FetchSceneServerAddressAsync` | Cache-aware wrapper around `ISceneServerService.FetchAsync` (used by instance routing; invalidates on failure) |
| `SweepSceneCaches` | Bounded expiry sweep for both caches |

### Batch DB Integration

Open-world routing uses batch DB methods to eliminate N+1 query overhead:

| Batch Method | Replaces | Used In |
|---|---|---|
| `ICharacterService.FetchSelectedCharactersByAccountsAsync` | N × `FetchByAccountAsync` | `ProcessOpenWorldQueueAsync` fetch phase |
| `ISceneServerService.FetchSceneServersByIDsAsync` | N × `FetchSceneServerAddressAsync` (for cache misses) | `ProcessOpenWorldQueueAsync` address resolution |

- **Character batch**: builds a `List<string>` of account names from waiting connections, issues a single DB round-trip, and maps results back via `CharacterData.Account`.
- **Server address batch**: checks `SceneServerAddressCache` per server ID first; only cache-miss IDs are collected and fetched in one batch call. Results populate the cache for future hits.

## External Integration Points

- **WorldServerAuthenticator**: auth success trigger for scene routing.
- **SceneService / SceneServerService / CharacterService**: scene lookup, batch character fetch, batch address resolution, assignment persistence, endpoint lookup.
- **WorldServerSystemRuntimeData**: world server ID context for DB scene queries.
- **WorldSceneDetailsCache**: per-scene max client metadata.
- **AsyncWorkerData**: bounded background execution with enqueue backpressure.
- **TimedCache**: write-through TTL cache reducing scene-instance and scene-server-address DB polling.