# SceneChannel System

## Overview

The SceneChannel system is a scene-server subsystem that provides channel selection for open-world scenes. Channels are multiple instances of the same scene on the same world server, potentially hosted across different scene servers. The system aggregates all available instances via database queries and exposes a channel list to the client. It also handles same-server or cross-server channel switching by updating the character's `SceneHandle` and disconnecting the client for world-server re-route.

All database work is asynchronous. Main-thread mutations (FishNet broadcasts, character state changes, disconnects) are marshalled through an isolated main-thread queue container.

## Directory Structure

```
SceneChannel/
├── SceneChannelSystem.cs                    # Channel listing, switching, DoS protection, cache
├── SceneChannelSystemRuntimeData.cs         # Runtime state (ingress guard, cooldowns, caches)
├── SceneChannelSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

Related core contracts:

- `Server/Core/World/SceneServer/SceneChannel/ISceneChannelSystem.cs`
- `Server/Core/World/SceneServer/SceneChannel/ISceneChannelSystemRuntimeData.cs`
- `Server/Core/World/SceneServer/SceneChannel/ISceneChannelSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`
- `Server/Core/Collections/TimedCache.cs`
- `Server/Core/Collections/IngressGuard.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── SceneChannelSystem : ISceneChannelSystem
```

### Runtime Data Containers

```
RuntimeDataContainer
├── SceneChannelSystemRuntimeData    : ISceneChannelSystemRuntimeData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── SceneChannelSystemMainThreadQueueData : ISceneChannelSystemMainThreadQueueData
```

## Runtime Data Container Details

### `SceneChannelSystemRuntimeData`

Mutable runtime state for ingress protection, cooldowns, and caching. Implements `ISceneChannelSystemRuntimeData`.

| Property | Type | Purpose |
|----------|------|---------|
| `IngressGuard` | `IngressGuard` | Per-connection, per-operation debounce and in-flight gating |
| `ChannelSwitchCooldownByClientId` | `Dictionary<int, DateTime>` | Tracks last channel switch time per client for cooldown enforcement |
| `NextCooldownCleanup` | `float` | Countdown until next stale cooldown dictionary cleanup sweep |
| `AvailableSceneCache` | `TimedCache<string, IReadOnlyList<SceneData>>` | Write-through TTL cache of `FetchAvailableAsync` results keyed by scene name |
| `SceneServerAddressCache` | `TimedCache<long, (string, ushort)>` | Write-through TTL cache of scene server addresses keyed by scene server ID |

**Lifecycle:**
- `InitializeOnce()` — creates `IngressGuard`, empty cooldown dictionary, and both `TimedCache` instances (scene cache uses `OrdinalIgnoreCase` comparer).
- `Clear()` — clears guard, cooldowns, resets timer, and clears both caches.
- `Deinitialize()` — clears and nulls all references.

### `SceneChannelSystemMainThreadQueueData`

Per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` (which inherits from `MainThreadQueueData`). Implements `ISceneChannelSystemMainThreadQueueData`.

Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread.

**Why a separate concrete type?** The `DataContainerRegistry` creates independent instances per concrete type, ensuring each system gets its own isolated main-thread queue separate from other subsystems.

## Required Data Container Attributes

`SceneChannelSystem` declares three required containers:

- `[RequiresDataContainer(typeof(SceneChannelSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(SceneChannelSystemRuntimeData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

## Core Responsibilities

| Responsibility | Description |
|---|---|
| Channel listing | Aggregates all available instances of a scene across scene servers via DB, sends list to client |
| Channel switching | Validates target channel, updates character `SceneHandle`, disconnects for world-server re-route |
| DoS protection | Per-connection ingress guard with debounce + in-flight gating; per-connection cooldown with bounded dictionary |
| Scene instance caching | Write-through TTL cache reduces repeated DB polling for scene-instance and scene-server-address queries |
| Main-thread safety | Uses isolated main-thread queue for Broadcast/Disconnect and character state mutations |

## Broadcast Protocol

| Broadcast | Direction | Purpose |
|---|---|---|
| `RequestSceneChannelListBroadcast` | Client → Server | Client requests the channel list (empty payload) |
| `SceneChannelSelectBroadcast` | Client → Server | Client selects a target channel (contains `ChannelAddress` with target `SceneHandle`) |
| `SceneChannelListBroadcast` | Server → Client | Server sends the list of available channels (contains `List<ChannelAddress>`) |

Both inbound broadcast handlers require authentication (`requireAuthentication = true`).

## Serialized Configuration

| Field | Default | Purpose |
|---|---|---|
| `maxMainThreadActionsPerFrame` | 50 | Max queued actions drained per frame |
| `ingressDebounceMilliseconds` | 500 | Per-connection, per-operation debounce window |
| `ingressSweepIntervalSeconds` | 10 | Seconds between stale ingress entry sweeps |
| `ingressEntryTtlSeconds` | 60 | Maximum age of ingress entries before sweep eligibility |
| `ingressSweepMaxRemovals` | 128 | Max ingress entries removed per sweep |
| `channelSwitchCooldownSeconds` | 10 | Minimum seconds between channel switch attempts per connection |
| `cooldownCleanupIntervalSeconds` | 30 | Seconds between stale cooldown dictionary cleanup sweeps |
| `cooldownCleanupMaxRemovals` | 128 | Max cooldown entries removed per cleanup sweep |
| `maxCooldownEntries` | 5000 | Hard cap on cooldown dictionary size (DoS defense) |
| `sceneInstanceCacheTtlSeconds` | 5 | TTL for cached scene-instance query results (0 = disabled) |
| `sceneServerCacheTtlSeconds` | 10 | TTL for cached scene-server address results (0 = disabled) |

## Processing Loop

`OnUpdate` performs:

1. Drain main-thread action queue.
2. Sweep stale ingress guard entries (`IngressGuard.Sweep`).
3. Decrement cooldown cleanup timer:
   - Cleanup expired cooldown entries (`CleanupExpiredCooldownEntries`).
   - Sweep expired scene caches (`SweepSceneCaches`).

## Security and DoS Hardening

### Ingress guard (per-connection, per-operation rate limiting)

- Every inbound broadcast handler acquires an ingress guard before enqueuing async work.
- The guard enforces a configurable debounce window per `(connectionId, operation)` pair.
- In-flight gating prevents a second request while a prior async operation is still running.
- The guard key is released automatically when the async work completes or fails (`TryEnqueueIngressWork`).
- Stale entries are swept periodically to bound memory growth.

### Channel switch cooldown

- Each connection has a per-client cooldown tracked in `ChannelSwitchCooldownByClientId`.
- Requests within the cooldown window are silently dropped.
- The cooldown is recorded immediately before async work to prevent rapid re-entry during the async window.
- The dictionary is bounded by `maxCooldownEntries`; new entries are rejected when saturated (DoS defense).
- Expired cooldown entries are cleaned up periodically with bounded removal per sweep.
- Entries are removed immediately on connection disconnect.

### Scene server cache invalidation

- Cached scene-server addresses are invalidated (via `TimedCache.Invalidate`) when a fetch fails.
- This prevents routing clients to dead scene servers for up to the full TTL window.

## Channel List Flow

Two entry points produce the same result:

1. **`OnCharacterLoaded`** — fired when a character loads into an open-world scene. Sends the initial channel list automatically. Instanced scenes are ignored.
2. **`OnRequestChannelList`** — handles explicit `RequestSceneChannelListBroadcast` from the client. Validates the connection and character, rejects instance scenes.

Both paths:

1. Validate connection and character.
2. Reject if character is in an instanced scene.
3. Acquire ingress guard.
4. Enqueue `FetchAndSendChannelListAsync`:
   - Fetch available scene instances (cache-aware via `FetchAvailableScenesAsync`).
   - Filter to `SceneType.OpenWorld` only.
   - Resolve each scene server's address (cache-aware via `FetchSceneServerAddressAsync`).
   - Build `List<ChannelAddress>` with address, port, scene handle, scene name, and character count.
   - Marshal `SceneChannelListBroadcast` to main thread for transmission.

## Channel Switch Flow

`OnChannelSelect` → `ValidateAndSwitchChannelAsync`:

1. Validate connection, character, and reject instance scenes.
2. Reject if already on the target channel (`currentHandle == targetHandle`).
3. Enforce per-connection cooldown (reject if within window or dictionary saturated).
4. Acquire ingress guard.
5. Record cooldown timestamp immediately.
6. Enqueue `ValidateAndSwitchChannelAsync`:
   - Fetch available instances (cache-aware) and verify target handle exists, is `OpenWorld`, and has capacity.
   - Marshal to main thread:
     - Re-validate character is still mapped.
     - Set `character.SceneHandle = targetHandle`.
     - Disable `CharacterFlags.IsLoaded` to prevent gameplay during transition.
     - Disconnect the client (`conn.Disconnect(false)`).
7. The disconnect triggers `CharacterSystem.OnRemoteConnectionStopped` → save/despawn/session-release pipeline.
8. `BuildCharacterData` captures the updated `SceneHandle` so the character is persisted with the target channel.
9. The client auto-reconnects to the world server, which routes through `ProcessOpenWorldQueueAsync` to the scene server hosting the target channel (SceneHandle-aware routing).

## Scene Instance Cache

Two `TimedCache` instances in `SceneChannelSystemRuntimeData` reduce database polling:

| Cache | Key | Value | Default TTL |
|---|---|---|---|
| `AvailableSceneCache` | scene name (`string`) | `IReadOnlyList<SceneData>` | 5 s |
| `SceneServerAddressCache` | scene server ID (`long`) | `(string Address, ushort Port)` | 10 s |

- **Write-through**: every successful DB fetch populates the cache.
- **Reads do NOT extend lifetime**: entries expire relative to write time.
- **Invalidation on failure**: a failed `FetchAsync` call invalidates the corresponding cache entry so the next caller re-fetches immediately.
- **Sweep**: `SweepSceneCaches` runs during the cooldown cleanup cycle with bounded head-first traversal (max 64 scan, 32 remove).
- **Disable**: set TTL to 0 to bypass caching entirely.

### Helper Methods

| Method | Purpose |
|---|---|
| `FetchAvailableScenesAsync` | Cache-aware wrapper around `ISceneService.FetchAvailableAsync` |
| `FetchSceneServerAddressAsync` | Cache-aware wrapper around `ISceneServerService.FetchAsync` (invalidates on failure) |
| `SweepSceneCaches` | Bounded expiry sweep for both caches |
| `GetMaxClients` | Reads max clients from `ISceneServerSystem.WorldSceneDetailsCache`, falls back to 500 |

## Event Wiring and Lifecycle

### InitializeOnce

- Validates all dependencies and data containers.
- Registers broadcast handlers:
  - `RequestSceneChannelListBroadcast` → `OnRequestChannelList`
  - `SceneChannelSelectBroadcast` → `OnChannelSelect`
- Subscribes to `ICharacterSystem.OnAfterLoadCharacter` → `OnCharacterLoaded`.
- Subscribes to `ServerManager.OnRemoteConnectionState` for disconnect cleanup.
- Clamps all serialized fields to safe minimums.

### OnDeinitialize

- Drains pending main-thread actions.
- Clears both scene caches.
- Unregisters broadcast handlers.
- Unsubscribes from character load and connection events.

## Threading Model

| Thread | Work |
|---|---|
| Main thread | broadcast handlers, ingress guard acquire/release, cooldown checks, character state changes, disconnects, queue drain |
| Async worker | DB fetch operations (scene instances, scene servers, character validation), channel list assembly |

All thread-sensitive operations are marshalled via `SceneChannelSystemMainThreadQueueData`.

## External Integration Points

- **ICharacterSystem**: `OnAfterLoadCharacter` event for initial channel list push.
- **ISceneServerSystem**: `WorldSceneDetailsCache` for max-clients metadata.
- **ICharacterMappingData**: connection → character lookup for validation.
- **ISceneInstanceMappingData**: local scene instance tracking.
- **ISceneService / ISceneServerService**: scene lookup and server address resolution.
- **AsyncWorkerData**: bounded background execution with enqueue backpressure.
- **TimedCache**: write-through TTL cache reducing scene-instance and scene-server-address DB polling.
- **IngressGuard**: per-connection, per-operation rate limiting and in-flight gating.
