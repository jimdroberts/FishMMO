# WorldScene System

## Overview

The WorldScene system routes authenticated world-server players into the correct scene endpoint. It supports both open-world and instance scene flows, maintains waiting queues while scenes are loading, coordinates scene-server assignment, and keeps a live connection-count metric used by world admission logic. Database work is asynchronous; all FishNet/Unity state mutations are marshalled onto the main thread through a dedicated queue container.

## Directory Structure

```
WorldScene/
├── WorldSceneSystem.cs                    # Queue orchestration, scene routing, DB coordination
├── WorldSceneMappingData.cs               # Runtime queue/state maps for open-world and instance routing
├── WorldSceneSystemRuntimeData.cs         # Runtime state (authenticator ref, next queue tick)
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
    └── WorldSceneSystemMainThreadQueueData : IWorldSceneSystemMainThreadQueueData
```

## Core Responsibilities

| Responsibility | Description |
|---|---|
| Authentication handoff | Subscribes to `WorldServerAuthenticator.OnClientAuthenticationResult` and starts routing flow |
| Open-world routing | Assigns queued players to ready open-world scenes, enqueues scene-load requests when needed |
| Instance routing | Routes players to ready instances or falls back to world scene if instance is invalid/stale |
| Queue maintenance | Tracks forward + reverse mappings for open-world and instance waiting queues |
| Connection counting | Aggregates DB scene population + queued connections into `ConnectionCount` |
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

## Processing Loop

`OnLateUpdate` performs:

1. Drain main-thread action queue.
2. Tick wait-queue timer (`waitQueueRate`, default 2s).
3. Snapshot current open-world scene keys + pending instance connections.
4. Enqueue async queue-processing worker task.

An atomic processing gate prevents overlapping queue cycles.

## Open-World Routing Flow

`ProcessOpenWorldQueueAsync(sceneName)`:

1. Fetch available ready scenes for the requested world scene.
2. For each candidate scene server:
   - snapshot queued connections on main thread,
   - dequeue valid connections up to max capacity,
   - persist selected character scene handle,
   - broadcast `WorldSceneConnectBroadcast` with target scene server endpoint.
3. Remove empty waiting buckets.
4. If queue still has waiters, enqueue DB scene-load request.

`GetMaxClients(sceneName)` uses `WorldSceneDetailsCache` and clamps to `[1, MAX_CLIENTS_PER_INSTANCE]`.

## Instance Routing Flow

`ProcessInstanceConnectionAsync(conn)`:

1. Validate connection/account.
2. Fetch selected character.
3. If not in instance -> fallback to world-scene queue.
4. If instance flag is stale/invalid -> clear flag in DB, fallback to world scene.
5. Fetch instance scene row:
   - `Ready` -> fetch scene server and broadcast connect endpoint.
   - `Pending/Loading` -> enqueue in instance waiting map.
   - invalid/missing scene server -> cleanup stale scene row when needed.

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
- Unsubscribes both events.
- Deletes world-scene rows for this world server from DB as shutdown cleanup.

## Threading Model

| Thread | Work |
|---|---|
| Main thread | queue/map mutations, FishNet broadcasts, kicks, event callbacks |
| Async worker | DB fetch/update/persist operations and queue orchestration |

All thread-sensitive operations are marshalled via `WorldSceneSystemMainThreadQueueData`.

## External Integration Points

- **WorldServerAuthenticator**: auth success trigger for scene routing.
- **SceneService / SceneServerService / CharacterService**: scene lookup, assignment persistence, endpoint lookup.
- **WorldServerSystemRuntimeData**: world server ID context for DB scene queries.
- **WorldSceneDetailsCache**: per-scene max client metadata.
- **AsyncWorkerData**: bounded background execution with enqueue backpressure.