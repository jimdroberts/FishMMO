# ServerSelect System

## Overview

The ServerSelect system serves the login-phase world server list to clients. It accepts `RequestServerListBroadcast` requests, queries active world servers from the database with an idle-timeout filter, maps database rows into network payloads, and sends responses on the main thread. Database work is offloaded to `AsyncWorkerData` to keep request handlers non-blocking.

## Directory Structure

```
ServerSelect/
├── ServerSelectSystem.cs                    # Login server behaviour for world server list requests
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

### Main-Thread Queue Data

```
RuntimeDataContainer
└── MainThreadQueueData (abstract)
    └── ServerSelectSystemMainThreadQueueData : IServerSelectSystemMainThreadQueueData
```

## Runtime Data Dependencies

`ServerSelectSystem` requires:

- `[RequiresDataContainer(typeof(ServerSelectSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

| Container | Purpose |
|-----------|---------|
| `AsyncWorkerData` | Executes database fetches on worker threads |
| `ServerSelectSystemMainThreadQueueData` | Marshals FishNet broadcasts to the Unity main thread |

## Request Flow

### 1) Request Entry

`OnServerRequestServerListBroadcastReceived`:

1. Verifies connection is active.
2. Enqueues async processing via `TryEnqueueAsyncWork(...)`.
3. Logs warning if queueing fails (backpressure/missing dependency).

### 2) Async Processing

`ProcessServerListRequestAsync`:

1. Resolves `IWorldServerService` from the database service registry.
2. Queries active servers using `FetchActiveAsync(IdleTimeout)`.
3. Maps `WorldServerData` rows to `WorldServerDetails` DTOs.
4. Enqueues a main-thread response action.

### 3) Main-Thread Dispatch

`OnLateUpdate` drains the queue each frame through `DrainMainThreadQueue()`, then sends:

- `ServerListBroadcast` containing `List<WorldServerDetails>`.

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