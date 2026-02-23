# ServerSelect System

## Overview

The ServerSelect system serves the login-phase world server list to clients. It accepts `RequestServerListBroadcast` requests, queries active world servers from the database with an idle-timeout filter, maps database rows into network payloads, and sends responses on the main thread. Database work is offloaded to `AsyncWorkerData` to keep request handlers non-blocking.

The system additionally applies per-connection in-flight request gating and bounded main-thread response draining to reduce request spam impact and frame spikes.

## Directory Structure

```
ServerSelect/
├── ServerSelectSystem.cs                    # Login server behaviour for world server list requests
├── ServerSelectSystemRuntimeData.cs         # Per-connection in-flight gate container
├── ServerSelectSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

Related core contracts:

- `Server/Core/LoginServer/ServerSelect/IServerSelectSystem.cs`
- `Server/Core/LoginServer/ServerSelect/IServerSelectSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── ServerSelectSystem : IServerSelectSystem
```

### Runtime Data Containers

```
RuntimeDataContainer
├── ServerSelectSystemRuntimeData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── ServerSelectSystemMainThreadQueueData : IServerSelectSystemMainThreadQueueData
```

## Runtime Data Container Details

### `ServerSelectSystemRuntimeData`

Mutable runtime state for the server selection system.

| Property | Type | Purpose |
|----------|------|---------|
| `InFlightRequests` | `ConcurrentDictionary<int, byte>` | Per-connection in-flight gate preventing duplicate concurrent server-list requests |
| `NextAllowedRequestUtcByClientId` | `ConcurrentDictionary<int, DateTime>` | Per-connection post-release cooldown timestamp; enforces `serverListCooldownMilliseconds` gap between successive list requests |

**Thread Safety:** `ConcurrentDictionary` allows safe access from both network and worker threads.

**Lifecycle:**
- `InitializeOnce()` — creates empty `ConcurrentDictionary`.
- `Clear()` — clears dictionary entries.
- `Deinitialize()` — clears and nulls reference.

### `ServerSelectSystemMainThreadQueueData`

Per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` (which inherits from `MainThreadQueueData`). Implements `IServerSelectSystemMainThreadQueueData`.

Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread.

**Why a separate concrete type?** The `DataContainerRegistry` creates independent instances per concrete type, ensuring each system gets its own isolated main-thread queue.

## Runtime Data Dependencies

`ServerSelectSystem` requires:

- `[RequiresDataContainer(typeof(ServerSelectSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(ServerSelectSystemRuntimeData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

| Container | Purpose |
|-----------|---------|
| `ServerSelectSystemRuntimeData` | Per-connection in-flight gate for server-list request deduplication |
| `AsyncWorkerData` | Executes database fetches on worker threads |
| `ServerSelectSystemMainThreadQueueData` | Marshals FishNet broadcasts to the Unity main thread |

## Request Flow

### 1) Request Entry

`OnServerRequestServerListBroadcastReceived`:

1. Verifies connection is active.
2. Attempts to acquire per-connection in-flight gate.
3. Enqueues async processing via `TryEnqueueAsyncWork(...)`.
4. Logs warning if queueing fails (backpressure/missing dependency).
5. Releases gate if enqueue fails.

### 2) Async Processing

`ProcessServerListRequestAsync`:

1. Resolves `IWorldServerService` from the database service registry.
2. Queries active servers using `FetchActiveAsync(IdleTimeout)`.
3. Maps `WorldServerData` rows to `WorldServerDetails` DTOs.
4. Enqueues a main-thread response action.
5. Releases in-flight gate in `finally`.

On failure (service unavailable or fetch failure), an empty `ServerListBroadcast` is sent to prevent indefinite client hangs.

### 3) Main-Thread Dispatch

`OnLateUpdate` drains the queue each frame through `DrainMainThreadQueue()`, then sends:

- `ServerListBroadcast` containing `List<WorldServerDetails>`.

`DrainMainThreadQueue()` is bounded by `maxMainThreadResponsesPerFrame` during normal updates and drains all during deinitialize.

## Operational Safeguards

- **Per-connection in-flight gate (`ServerSelectSystemRuntimeData.InFlightRequests`)**
    - Prevents one connection from stacking multiple concurrent server-list tasks.
    - Gate is released on both enqueue failure and async completion (`finally`).
- **Post-release cooldown (`serverListCooldownMilliseconds`, default `1000`)**
    - After an in-flight request completes, the connection must wait the configured cooldown before another server-list request is accepted.
    - Tracked via `NextAllowedRequestUtcByClientId` in runtime data.
    - Prevents rapid sequential spam after each request completes.
    - Entries are cleaned up on disconnect.
- **Bounded main-thread response draining (`maxMainThreadResponsesPerFrame`)**
    - Time-slices queued responses to avoid frame-time spikes.

## Filtering Behavior

`IdleTimeout` controls active-server visibility:

- Servers whose last pulse exceeds `IdleTimeout` seconds are excluded by the DB query.
- Default value is `60` seconds.

## Threading Model

| Thread | Work |
|--------|------|
| Main / Network thread | Broadcast receive, fast validation, enqueue worker task |
| Async worker thread | Database fetch and DTO mapping |
| Main thread | FishNet broadcast dispatch (thread-safe marshalling) |

## Lifecycle

### InitializeOnce

- Validates required dependencies (`Server`, main-thread queue container).
- Registers `RequestServerListBroadcast` handler.
- Logs initialization state with timeout config.

### OnDeinitialize

- Drains queued response actions.
- Unregisters broadcast handler.

## External Integration Points

- **Database Service Registry**: resolves `IWorldServerService`.
- **WorldServerService**: provides active world server rows filtered by pulse age.
- **AsyncWorkerData**: centralized bounded background work execution.
- **ServerSelectSystemMainThreadQueueData**: safe dispatch point for network operations.
- **FishNet NetworkWrapper**: receives request broadcasts and sends `ServerListBroadcast` responses.