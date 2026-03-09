# Scene Channel System

**Short description:** SceneServer subsystem for open-world scene channel listing and switching, aggregating scene instances across scene servers via database queries and handling same-server or cross-server channel transitions with DoS protection and write-through TTL caching.

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

The Scene Channel system is a scene-server subsystem that provides channel selection for open-world scenes. Channels are multiple instances of the same scene on the same world server, potentially hosted across different scene servers. The system aggregates all available instances via database queries (`ISceneService.FetchAvailableAsync`, `ISceneServerService.FetchAsync`) and exposes a channel list to the client. It also handles same-server or cross-server channel switching by updating the character's `SceneHandle` and disconnecting the client for world-server re-route.

The implementation uses a split execution model:
- **Main thread:** request validation, ingress guard checks, cooldown enforcement, network broadcasts, and character state mutations.
- **Async worker:** database reads via `TryEnqueueIngressWork` for scene-instance and scene-server-address queries.
- **Main-thread queue:** marshalling async completion actions back to Unity/FishNet-safe context via `ISceneChannelSystemMainThreadQueueData`.

All database work is asynchronous. Main-thread mutations (FishNet broadcasts, character state changes, disconnects) are marshalled through an isolated main-thread queue container. Two write-through TTL caches reduce repeated database polling for scene-instance and scene-server-address queries.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Channel listing aggregating all available open-world scene instances across scene servers on the same world server via database queries
- Automatic initial channel list dispatch when a character loads into an open-world scene (`OnAfterLoadCharacter`)
- Explicit channel list refresh via `RequestSceneChannelListBroadcast` from the client
- Channel switching with database validation of target handle existence, `OpenWorld` scene type, and capacity
- Character `SceneHandle` update and `CharacterFlags.IsLoaded` disable before disconnect for safe transition
- Client disconnect triggering standard save/despawn/session-release pipeline via `CharacterSystem.OnRemoteConnectionStopped`
- Auto-reconnect through world server load balancing to the scene server hosting the target channel (SceneHandle-aware routing)
- Per-connection, per-operation ingress guard with configurable debounce and in-flight gating (`RequestList`, `SelectChannel`)
- Bounded ingress guard sweep with configurable TTL, interval, and max removals
- Per-connection channel switch cooldown with configurable interval and bounded dictionary (DoS defense)
- Hard cap on cooldown dictionary size (`maxCooldownEntries`) rejecting new entries when saturated
- Periodic cleanup of expired cooldown entries with bounded removal per sweep
- Immediate cooldown entry removal on client disconnect
- Write-through TTL cache for scene-instance query results (`AvailableSceneCache`, keyed by scene name with `OrdinalIgnoreCase` comparer)
- Write-through TTL cache for scene-server address results (`SceneServerAddressCache`, keyed by scene server ID)
- Cache invalidation on fetch failure to prevent routing to dead scene servers
- Periodic expired-cache sweep piggybacking on the cooldown cleanup cycle
- Async worker backpressure via `TryEnqueueIngressWork` (rejects when queue unavailable/full)
- Per-system main-thread queue isolation with configurable drain cap per frame
- Instanced scene rejection for both channel listing and switching
- Graceful failure semantics: invalid requests fail closed with no mutation; capacity/type checks enforced before state changes; async failures logged without blocking main thread

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework
- **FishMMO Server Core** — provides `ServerBehaviour`, `ISceneChannelSystem`, `ISceneChannelSystemRuntimeData`, `ISceneChannelSystemMainThreadQueueData`, broadcast types (`RequestSceneChannelListBroadcast`, `SceneChannelSelectBroadcast`, `SceneChannelListBroadcast`, `ChannelAddress`), `IngressGuard`, `TimedCache`, `AsyncWorkerData`, `ISceneInstanceMappingData`, `ICharacterMappingData<NetworkConnection>`, `ISceneServerSystem<NetworkConnection>`, `ICharacterSystem<NetworkConnection, Scene>`, `CharacterFlags`, and `SceneType`
- **FishMMO Database** — provides `ISceneService`, `ISceneServerService`, `SceneData`, and `DatabaseResult<T>`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side scene-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `SceneChannelSystem` is present on the scene server GameObject (it inherits from `ServerBehaviour` and implements `ISceneChannelSystem`). The asset is created via `Create > FishMMO > Server > SceneServer > Scene Channel System`.
2. Verify that the following data containers are registered in `DataContainerRegistry`:
   - `SceneChannelSystemRuntimeData` → `ISceneChannelSystemRuntimeData`
   - `SceneChannelSystemMainThreadQueueData` → `ISceneChannelSystemMainThreadQueueData`
   - `AsyncWorkerData` (shared async work queue)
3. Verify that the following dependencies resolve from `DataContainerRegistry`:
   - `ISceneInstanceMappingData`
   - `ICharacterMappingData<NetworkConnection>`
4. Verify that `ISceneServerSystem<NetworkConnection>` is registered in `BehaviourRegistry` (used for `WorldSceneDetailsCache.MaxClients` lookups).
5. Verify that `ICharacterSystem<NetworkConnection, Scene>` is registered in `BehaviourRegistry` for character load event subscriptions.
6. Verify that `ISceneService` and `ISceneServerService` are available in `Database.ServiceRegistry`.
7. On initialize, `SceneChannelSystem` registers broadcast handlers for `RequestSceneChannelListBroadcast` and `SceneChannelSelectBroadcast` (both require authentication), subscribes to `OnAfterLoadCharacter` for initial channel list dispatch, subscribes to connection events for cooldown cleanup, and clamps all serialized fields.
8. On deinitialize, it drains the remaining main-thread queue, clears both caches, unregisters broadcast handlers, unsubscribes character and connection callbacks.
9. Clients send `RequestSceneChannelListBroadcast` to refresh the channel list or `SceneChannelSelectBroadcast` to switch channels; the server validates, queries the database, and replies with a `SceneChannelListBroadcast` or disconnects the client for world-server re-route.

## Configuration

### Inspector Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `maxMainThreadActionsPerFrame` | int | 50 | Max queued channel-system actions drained from main-thread queue per frame |
| `ingressDebounceMilliseconds` | int | 500 | Per-connection, per-operation debounce window in milliseconds |
| `ingressSweepIntervalSeconds` | float | 10.0 | Seconds between ingress guard stale-entry sweep passes |
| `ingressEntryTtlSeconds` | float | 60.0 | Maximum age in seconds for ingress entries before sweep eligibility |
| `ingressSweepMaxRemovals` | int | 128 | Maximum ingress entries removed per sweep pass |
| `channelSwitchCooldownSeconds` | float | 10.0 | Minimum seconds between channel switch attempts per connection |
| `cooldownCleanupIntervalSeconds` | float | 30.0 | Seconds between stale cooldown dictionary cleanup sweeps |
| `cooldownCleanupMaxRemovals` | int | 128 | Maximum cooldown entries removed per cleanup sweep |
| `maxCooldownEntries` | int | 5000 | Hard cap on cooldown dictionary size (DoS defense) |
| `sceneInstanceCacheTtlSeconds` | float | 5.0 | TTL for cached scene-instance query results; 0 disables caching |
| `sceneServerCacheTtlSeconds` | float | 10.0 | TTL for cached scene-server address results; 0 disables caching |

### Clamped Minimums

All serialized fields are clamped on initialization to prevent invalid configuration:

| Parameter | Minimum |
|---|---|
| `maxMainThreadActionsPerFrame` | 1 |
| `ingressDebounceMilliseconds` | 50 |
| `ingressSweepIntervalSeconds` | 1.0 |
| `ingressEntryTtlSeconds` | 5.0 |
| `ingressSweepMaxRemovals` | 1 |
| `channelSwitchCooldownSeconds` | 1.0 |
| `cooldownCleanupIntervalSeconds` | 5.0 |
| `cooldownCleanupMaxRemovals` | 1 |
| `maxCooldownEntries` | 100 |
| `sceneInstanceCacheTtlSeconds` | 0.0 |
| `sceneServerCacheTtlSeconds` | 0.0 |

### Ingress Operations

| Operation | Byte Value | Used By |
|---|---|---|
| `RequestList` | 1 | `OnCharacterLoaded`, `OnRequestChannelList` |
| `SelectChannel` | 2 | `OnChannelSelect` |

### Threading Model

| Thread | Work |
|---|---|
| Main thread | Request validation, ingress guards, cooldown enforcement, broadcast dispatch, character state mutations (SceneHandle, CharacterFlags), disconnect, queue drain, ingress sweep, cooldown cleanup, cache sweep |
| Async worker | Database reads (`FetchAvailableScenesAsync`, `FetchSceneServerAddressAsync`) via `TryEnqueueIngressWork` with automatic guard release |

### Scene Instance Caches

Two `TimedCache` instances in `SceneChannelSystemRuntimeData` reduce database polling:

| Cache | Key | Value | Default TTL | Comparer |
|---|---|---|---|---|
| `AvailableSceneCache` | scene name (`string`) | `IReadOnlyList<SceneData>` | 5 s | `OrdinalIgnoreCase` |
| `SceneServerAddressCache` | scene server ID (`long`) | `(string Address, ushort Port)` | 10 s | default |

Cache behaviour:
- On hit (within TTL): returns cached value, skips database query.
- On miss or TTL expired: queries database, stores result in cache.
- On fetch failure (`SceneServerAddressCache`): invalidates stale entry so subsequent calls re-fetch immediately.
- Expired entries are swept periodically during the cooldown cleanup cycle (max 64 scanned, 32 removed per sweep per cache).

## Usage Examples

### Broadcast Protocol

| Broadcast | Direction | Purpose |
|---|---|---|
| `RequestSceneChannelListBroadcast` | Client → Server | Client requests the channel list (empty payload) |
| `SceneChannelSelectBroadcast` | Client → Server | Client selects a target channel (contains `ChannelAddress` with target `SceneHandle`) |
| `SceneChannelListBroadcast` | Server → Client | Server sends the list of available channels (contains `List<ChannelAddress>`) |

Both inbound broadcast handlers require authentication (`requireAuthentication = true`).

### Channel List (Automatic on Load)

`OnCharacterLoaded(conn, character)` — fired by `ICharacterSystem.OnAfterLoadCharacter`:

1. Returns immediately if the character is in an instanced scene.
2. Acquires ingress guard (`RequestList`).
3. Captures `sceneName`, `worldServerID`, and `characterID`.
4. Validates `sceneName` is not null/empty.
5. Resolves `maxClients` from `WorldSceneDetailsCache` (fallback: 500).
6. Enqueues `FetchAndSendChannelListAsync` via `TryEnqueueIngressWork`.

### Channel List (Explicit Request)

`OnRequestChannelList(conn, msg, channel)` — handles `RequestSceneChannelListBroadcast`:

1. Validates connection is active and has a spawned object.
2. Looks up the character from `ICharacterMappingData<NetworkConnection>`.
3. Rejects if character is in an instanced scene.
4. Acquires ingress guard (`RequestList`).
5. Captures `sceneName`, `worldServerID`, and `characterID`.
6. Validates `sceneName` is not null/empty.
7. Resolves `maxClients` from `WorldSceneDetailsCache` (fallback: 500).
8. Enqueues `FetchAndSendChannelListAsync` via `TryEnqueueIngressWork`.

### FetchAndSendChannelListAsync

1. Validates `Database.ServiceRegistry`, `ISceneService`, `ISceneServerService`, and `SceneChannelSystemRuntimeData`.
2. Fetches available scene instances (cache-aware via `FetchAvailableScenesAsync`).
3. Filters to `SceneType.OpenWorld` only.
4. For each scene instance, resolves the scene server's address (cache-aware via `FetchSceneServerAddressAsync`).
5. Builds `List<ChannelAddress>` with `Address`, `Port`, `SceneHandle`, `SceneName`, and `CharacterCount`.
6. Marshals `SceneChannelListBroadcast` to the main thread for transmission.

### Channel Switch

`OnChannelSelect(conn, msg, channel)` — handles `SceneChannelSelectBroadcast`:

1. Validates connection is active with a spawned object.
2. Looks up the character from `ICharacterMappingData<NetworkConnection>`.
3. Rejects if character is in an instanced scene.
4. Rejects if `currentHandle == targetHandle` (already on target channel).
5. Enforces per-connection cooldown:
   - Rejects if within cooldown window.
   - Rejects if dictionary is saturated (`>= maxCooldownEntries`) and client has no existing entry.
6. Acquires ingress guard (`SelectChannel`).
7. Records cooldown timestamp immediately to prevent rapid re-entry during async window.
8. Resolves `maxClients` from `WorldSceneDetailsCache`.
9. Enqueues `ValidateAndSwitchChannelAsync` via `TryEnqueueIngressWork`.

### ValidateAndSwitchChannelAsync

1. Validates `Database.ServiceRegistry` and `ISceneService`.
2. Fetches available instances (cache-aware) and verifies target handle exists, is `OpenWorld`, and has capacity (`CharacterCount < maxClients`).
3. Marshals to main thread:
   - Re-validates the character is still mapped to the connection.
   - Sets `character.SceneHandle = targetHandle`.
   - Disables `CharacterFlags.IsLoaded` to prevent gameplay during transition.
   - Calls `conn.Disconnect(false)`.
4. The disconnect triggers `CharacterSystem.OnRemoteConnectionStopped` → save/despawn/session-release pipeline.
5. `BuildCharacterData` captures the updated `SceneHandle` so the character is persisted with the target channel.
6. The client auto-reconnects to the world server, which routes through `ProcessOpenWorldQueueAsync` to the scene server hosting the target channel (SceneHandle-aware routing).

### Failure Semantics

- Invalid requests fail closed with no mutation.
- Instanced scene requests are silently rejected.
- Capacity and scene-type checks are enforced before state changes.
- Async failures are caught, logged, and do not block the main thread.
- Main-thread completion paths revalidate connection activity and character mapping before mutating or broadcasting.
- Ingress guards are always released via `TryEnqueueIngressWork` deferred release (on completion or failure).
- Cache invalidation on fetch failure prevents stale routing.

## Operational Checks

| Check | How to Verify |
|---|---|
| Initialization success | Confirm `SceneChannelSystem` logs "Initialized (Debounce=500ms, SwitchCooldown=10s)" without errors on server startup |
| Data containers available | Verify `ISceneChannelSystemRuntimeData`, `ISceneChannelSystemMainThreadQueueData`, and `AsyncWorkerData` all resolve from `DataContainerRegistry` |
| Dependencies available | Verify `ISceneInstanceMappingData`, `ICharacterMappingData<NetworkConnection>`, and `ISceneServerSystem<NetworkConnection>` resolve from their respective registries |
| Database services available | Verify `ISceneService` and `ISceneServerService` resolve from `Database.ServiceRegistry` |
| Initial channel list on load | Load a character into an open-world scene; confirm `SceneChannelListBroadcast` is sent automatically with available channels |
| Instanced scene rejection (load) | Load a character into an instanced scene; confirm no channel list is sent |
| Explicit channel list request | Send `RequestSceneChannelListBroadcast` from an authenticated client in an open-world scene; confirm `SceneChannelListBroadcast` reply with `List<ChannelAddress>` |
| Instanced scene rejection (request) | Send `RequestSceneChannelListBroadcast` from a character in an instanced scene; confirm request is silently dropped |
| Channel switch | Send `SceneChannelSelectBroadcast` with a valid target handle; confirm character `SceneHandle` is updated and client is disconnected |
| Same-channel rejection | Send `SceneChannelSelectBroadcast` with `targetHandle == currentHandle`; confirm request is silently dropped |
| Target validation | Send `SceneChannelSelectBroadcast` with a non-existent or full target handle; confirm request is rejected after async validation |
| Channel switch cooldown | Send rapid consecutive `SceneChannelSelectBroadcast` requests; confirm excess requests are dropped within `channelSwitchCooldownSeconds` |
| Cooldown dictionary saturation | Saturate the cooldown dictionary to `maxCooldownEntries`; confirm new entries from unknown clients are rejected |
| Ingress debounce | Send rapid consecutive `RequestSceneChannelListBroadcast` from the same connection; confirm excess requests are dropped |
| Ingress in-flight guard | Send overlapping async requests; confirm only one is processed at a time per operation type |
| Ingress sweep | Wait for `ingressSweepIntervalSeconds`; confirm stale guard entries are cleaned up |
| Cooldown cleanup | Wait for `cooldownCleanupIntervalSeconds`; confirm expired cooldown entries are removed |
| Disconnect cleanup | Disconnect a client; confirm cooldown entry is removed immediately via `OnRemoteConnectionStopped` |
| Scene instance cache hit | Request channel list twice within `sceneInstanceCacheTtlSeconds`; confirm second request uses cached data (no DB query) |
| Scene instance cache expiry | Wait beyond `sceneInstanceCacheTtlSeconds`; confirm next request queries the database |
| Scene server cache invalidation | Simulate a scene server fetch failure; confirm cached address is invalidated |
| Cache sweep | Wait for `cooldownCleanupIntervalSeconds`; confirm expired cache entries are swept |
| Main-thread queue drain | Confirm queued async results are dispatched on the main thread within `maxMainThreadActionsPerFrame` per frame |
| World scene details fallback | Remove scene from `WorldSceneDetailsCache`; confirm `maxClients` falls back to 500 |
| Deinitialize cleanup | Trigger deinitialize; confirm broadcast handlers unregistered, character and connection callbacks unsubscribed, caches cleared, and main-thread queue drained |

## Flow Diagram

### Channel List (Load / Request)

```
OnCharacterLoaded(conn, character) / OnRequestChannelList(conn, msg, channel)
│
├─ 1. Validate connection + character
├─ 2. Reject if instanced scene
├─ 3. Acquire ingress guard (RequestList)
├─ 4. Capture sceneName, worldServerID, characterID
├─ 5. Resolve maxClients from WorldSceneDetailsCache (fallback: 500)
└─ 6. TryEnqueueIngressWork → FetchAndSendChannelListAsync
       │
       ├─ FetchAvailableScenesAsync(worldServerID, sceneName, maxClients)
       │    └─ Cache hit → return cached; miss → ISceneService.FetchAvailableAsync → cache result
       ├─ Filter to SceneType.OpenWorld
       ├─ For each instance:
       │    └─ FetchSceneServerAddressAsync(sceneServerID)
       │         └─ Cache hit → return cached; miss → ISceneServerService.FetchAsync → cache result
       │              └─ Failure → invalidate cache entry
       ├─ Build List<ChannelAddress> (Address, Port, SceneHandle, SceneName, CharacterCount)
       └─ TryEnqueueMainThread
              └─ Validate conn still active → Broadcast SceneChannelListBroadcast
```

### Channel Switch

```
OnChannelSelect(conn, msg, channel)
│
├─ 1. Validate connection + character
├─ 2. Reject if instanced scene
├─ 3. Reject if currentHandle == targetHandle
├─ 4. Enforce per-connection cooldown
│      ├─ Reject if within cooldown window
│      └─ Reject if dictionary saturated (>= maxCooldownEntries)
├─ 5. Acquire ingress guard (SelectChannel)
├─ 6. Record cooldown timestamp immediately
├─ 7. Capture sceneName, worldServerID, characterID, maxClients
└─ 8. TryEnqueueIngressWork → ValidateAndSwitchChannelAsync
       │
       ├─ FetchAvailableScenesAsync (cache-aware)
       ├─ Verify targetHandle exists, is OpenWorld, has capacity
       └─ TryEnqueueMainThread
              ├─ Re-validate character still mapped to connection
              ├─ character.SceneHandle = targetHandle
              ├─ character.DisableFlags(CharacterFlags.IsLoaded)
              └─ conn.Disconnect(false)
                     │
                     └─ Triggers CharacterSystem.OnRemoteConnectionStopped
                            ├─ RemoveCharacterConnectionMapping
                            ├─ SaveAndDespawnCharacter (saves with updated SceneHandle)
                            └─ Client auto-reconnects → world server re-routes to target channel
```

### OnUpdate Sweep

```
OnUpdate(deltaTime)
│
├─ 1. DrainMainThreadQueue (up to maxMainThreadActionsPerFrame)
├─ 2. SweepIngressGuard()
│      └── IngressGuard.Sweep(interval, ttl, maxRemovals)
└─ 3. Decrement NextCooldownCleanup timer
       └── When elapsed:
              ├─ Reset timer to cooldownCleanupIntervalSeconds
              ├─ CleanupExpiredCooldownEntries()
              │    └── Remove entries where (UtcNow - lastSwitch) > cooldownSeconds
              │         (bounded by cooldownCleanupMaxRemovals)
              └─ SweepSceneCaches()
                     ├─ AvailableSceneCache.SweepExpired(ttl, maxScan=64, maxRemove=32)
                     └─ SceneServerAddressCache.SweepExpired(ttl, maxScan=64, maxRemove=32)
```

## Project Structure

### Directory Structure

```
SceneChannel/
├── SceneChannelSystem.cs                    # Channel listing, switching, DoS protection, cache
├── SceneChannelSystemRuntimeData.cs         # Runtime state (ingress guard, cooldowns, caches)
├── SceneChannelSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md                                # System documentation
```

### Related Core Contracts

- `Server/Core/World/SceneServer/SceneChannel/ISceneChannelSystem.cs`
- `Server/Core/World/SceneServer/SceneChannel/ISceneChannelSystemRuntimeData.cs`
- `Server/Core/World/SceneServer/SceneChannel/ISceneChannelSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`
- `Server/Core/Collections/TimedCache.cs`
- `Server/Core/Collections/IngressGuard.cs`

### Inheritance Hierarchy

```
ServerBehaviour
└── SceneChannelSystem : ISceneChannelSystem

RuntimeDataContainer
├── SceneChannelSystemRuntimeData : ISceneChannelSystemRuntimeData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── SceneChannelSystemMainThreadQueueData : ISceneChannelSystemMainThreadQueueData
```

### Runtime Data Container Details

**`SceneChannelSystemRuntimeData`** — mutable runtime state for ingress protection, cooldowns, and caching. Implements `ISceneChannelSystemRuntimeData`.

| Property | Type | Purpose |
|---|---|---|
| `IngressGuard` | `IngressGuard` | Per-connection, per-operation debounce and in-flight gating |
| `ChannelSwitchCooldownByClientId` | `Dictionary<int, DateTime>` | Tracks last channel switch time per client for cooldown enforcement |
| `NextCooldownCleanup` | `float` | Countdown until next stale cooldown dictionary cleanup sweep |
| `AvailableSceneCache` | `TimedCache<string, IReadOnlyList<SceneData>>` | Write-through TTL cache of `FetchAvailableAsync` results keyed by scene name |
| `SceneServerAddressCache` | `TimedCache<long, (string, ushort)>` | Write-through TTL cache of scene server addresses keyed by scene server ID |

Lifecycle:
- `InitializeOnce()` — creates `IngressGuard`, empty cooldown dictionary, and both `TimedCache` instances (scene cache uses `OrdinalIgnoreCase` comparer).
- `Clear()` — clears guard, cooldowns, resets timer, and clears both caches.
- `OnDeinitialize()` — clears and nulls all references.

**`SceneChannelSystemMainThreadQueueData`** — per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` (which inherits from `MainThreadQueueData`). Implements `ISceneChannelSystemMainThreadQueueData`. Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread. The `DataContainerRegistry` creates independent instances per concrete type, ensuring each system gets its own isolated main-thread queue separate from other subsystems.

### Required Data Container Attributes

`SceneChannelSystem` declares three required containers:

- `[RequiresDataContainer(typeof(SceneChannelSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(SceneChannelSystemRuntimeData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

## License

This project is subject to the FishMMO project license.
