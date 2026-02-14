# KickRequest System

## Overview

The KickRequest system enforces account disconnect requests issued through the database. It periodically polls for new kick requests, validates whether each request is still relevant, and disconnects matching online connections. Database and polling work run asynchronously, while connection `Kick(...)` operations are marshalled to the Unity main thread through a dedicated queue container.

## Directory Structure

```
KickRequest/
├── KickRequestSystem.cs                    # Polling, validation, and kick execution orchestration
├── KickRequestSystemQueueData.cs           # Polling cursor + processing gate runtime state
├── KickRequestSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

Related core contracts:

- `Server/Core/World/KickRequest/IKickRequestSystem.cs`
- `Server/Core/World/KickRequest/IKickRequestSystemQueueData.cs`
- `Server/Core/World/KickRequest/IKickRequestSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── KickRequestSystem : IKickRequestSystem
```

### Runtime Data Containers

```
RuntimeDataContainer
├── KickRequestSystemQueueData : IKickRequestSystemQueueData
└── MainThreadQueueData (abstract)
    └── KickRequestSystemMainThreadQueueData : IKickRequestSystemMainThreadQueueData
```

## Runtime Data Responsibilities

| Container | Responsibility |
|-----------|----------------|
| `KickRequestSystemQueueData` | Tracks polling state (`LastFetchTime`, `LastPosition`) and overlap gate (`IsProcessing`) |
| `KickRequestSystemMainThreadQueueData` | Executes queued `Kick(...)` actions safely on main thread |
| `AsyncWorkerData` | Runs DB polling and cleanup operations off main/network threads |

## Polling and Processing Flow

### 1) Periodic Trigger

`IPeriodicUpdateSystem` invokes `OnPeriodicUpdate(...)` every `updatePumpRate` seconds.

If server state is started:

- enqueue `ProcessKickRequestsAsync()` via `TryEnqueueAsyncWork(...)`
- log warning if enqueue fails (queue pressure or missing dependency)

### 2) Overlap Protection

`ProcessKickRequestsAsync()` uses `IKickRequestSystemQueueData.IsProcessing` under a lock to prevent concurrent overlapping polls.

```
lock(data)
  if IsProcessing -> return
  IsProcessing = true
...
finally lock(data) IsProcessing = false
```

### 3) Database Fetch Cursor

The system fetches kick requests using cursor pagination:

- `LastFetchTime`
- `LastPosition`
- `UpdateFetchCount`

After each non-empty fetch, cursor advances to the latest row in the batch.

### 4) Stale Request Filtering

For each kick request, the system fetches account last-login timestamps in parallel.

Rule:

- If `lastLogin >= kickRequest.TimeCreated`, request is stale (account already reconnected) -> skip.
- Otherwise, request remains valid -> queue kick action on main thread.

### 5) Main-Thread Kick

Queued action resolves live connection by account name and calls:

- `conn.Kick(FishNet.Managing.Server.KickReason.UnexpectedProblem)`

## Disconnect Cleanup Flow

When a remote connection stops:

1. Resolve account name from `AccountManager`.
2. Enqueue `DeleteKickRequestAsync(accountName)` keyed by account hash for stable ordering.
3. Remove pending kick requests for that account from the database.

This prevents stale kick requests from re-triggering after legitimate disconnect/reconnect cycles.

## Configuration Surface

| Field / Property | Type | Default | Purpose |
|------------------|------|---------|---------|
| `updatePumpRate` / `UpdatePumpRate` | `float` | `5.0f` | Poll interval in seconds |
| `updateFetchCount` / `UpdateFetchCount` | `int` | `100` | Maximum kick requests per fetch |

## Threading Model

| Thread | Work |
|--------|------|
| Main / periodic callback thread | Schedule polling, drain main-thread queue |
| Async worker threads | DB fetch, cursor updates, stale-filter checks |
| Main thread | FishNet `Kick(...)` operations |

## Lifecycle

### InitializeOnce

- Validates `Server`, `ServerManager`, and required data containers.
- Subscribes to `OnRemoteConnectionState`.
- Registers periodic callback with configured pump rate.

### OnDeinitialize

- Drains queued main-thread actions.
- Unsubscribes connection-state event.
- Unregisters periodic callback.

### Queue Data Lifecycle

`KickRequestSystemQueueData` sets/reset values on initialize and clear:

- `IsProcessing = false`
- `LastFetchTime = DateTime.UtcNow`
- `LastPosition = 0`

## External Integration Points

- **KickRequestService** (`IKickRequestService`) — fetch and delete kick requests.
- **AccountService** (`IAccountService`) — last-login checks for stale request filtering.
- **AccountManager** — account-to-connection lookup and reverse cleanup mapping.
- **Periodic Update System** (`IPeriodicUpdateSystem`) — fixed-rate polling cadence.
- **AsyncWorkerData** — bounded async execution with enqueue backpressure.
- **MainThreadQueueData** — thread-safe dispatch of network kicks.