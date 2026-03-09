# Kick Request System

**Short description:** Periodically polls the database for account kick requests, filters stale entries via last-login timestamps, and disconnects matching connections on the main thread through a dedicated queue container.

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

The Kick Request system enforces account disconnect requests issued through the database. It runs on the World Server as a `ServerBehaviour` and periodically polls for new kick requests using cursor-based pagination. Each request is validated against account last-login timestamps to filter stale entries (accounts that have already reconnected). Valid kick actions are marshalled to the Unity main thread through a dedicated `MainThreadQueueData` container, since FishNet's `NetworkConnection.Kick(...)` is not thread-safe.

The system is split into a Core interface layer and an Implementation layer:

- **Core layer** — Defines `IKickRequestSystem`, `IKickRequestSystemQueueData`, and `IKickRequestSystemMainThreadQueueData` as engine-agnostic contracts. Other systems can query or modify runtime pump parameters (`UpdatePumpRate`, `UpdateFetchCount`) without referencing the implementation.
- **Implementation layer** — Provides three concrete classes:
  - **`KickRequestSystem`** — Orchestrates polling, validation, and kick execution. Extends `ServerBehaviour` and implements `IKickRequestSystem`.
  - **`KickRequestSystemQueueData`** — Tracks polling cursor state (`LastFetchTime`, `LastPosition`) and an overlap gate (`IsProcessing`). Extends `RuntimeDataContainer`.
  - **`KickRequestSystemMainThreadQueueData`** — Per-system main-thread action queue container. Extends `SystemMainThreadQueueData`.

Database and polling work run asynchronously via `AsyncWorkerData`, while all FishNet connection operations are dispatched through the main-thread queue to guarantee thread safety.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Fully supported as a server host |
| Linux    | Yes       | Fully supported as a server host |
| WebGL    | N/A       | Server-only component; not applicable to browser builds |

**Engine:** Unity 6.3 LTS
**Scripting backend:** IL2CPP

## Features

- **Cursor-based database polling** — Fetches kick requests using `LastFetchTime` and `LastPosition` for efficient pagination. After each non-empty fetch the cursor advances to the latest row in the batch, ensuring no requests are missed or re-processed.
- **Stale request filtering** — For each kick request, the system fetches the account's last-login timestamp. If the last login occurred after the kick request was created, the request is considered stale (account already reconnected) and is skipped.
- **Batched last-login checks** — Login timestamp queries are batched in groups of 10 using `Task.WhenAll` to avoid saturating the database connection pool while still running concurrently within each batch.
- **Overlap protection** — `IKickRequestSystemQueueData.IsProcessing` is checked under a lock before each poll to prevent concurrent overlapping database fetches.
- **Main-thread kick dispatch** — All `conn.Kick(KickReason.UnexpectedProblem)` calls are enqueued via `TryEnqueueMainThread<IKickRequestSystemMainThreadQueueData>` and drained on the main thread each frame, respecting `maxMainThreadActionsPerFrame` to avoid frame spikes.
- **Disconnect cleanup** — When a remote connection stops, the system asynchronously deletes any pending kick request for that account from the database, preventing stale requests from re-triggering after legitimate disconnect/reconnect cycles.
- **Configurable pump rate** — Poll interval (`updatePumpRate`) and fetch count (`updateFetchCount`) are exposed as serialized fields and runtime properties, tunable via the Unity Inspector or code.
- **Periodic update integration** — Registers with `IPeriodicUpdateSystem` for fixed-rate polling cadence rather than relying on per-frame checks.
- **Graceful shutdown drain** — On deinitialization, the system drains all remaining main-thread actions so clients receive their final disconnect messages.
- **Async worker backpressure** — All database operations are submitted via `TryEnqueueAsyncWork`, which returns `false` when the worker pool is full. Warnings are logged on enqueue failure.

## Prerequisites

- Unity 6.3 LTS (IL2CPP scripting backend)
- FishNet networking framework (`FishNet.Connection.NetworkConnection`)
- FishMMO Database layer with `IKickRequestService` and `IAccountService` implementations
- FishMMO Server Core (`ServerBehaviour`, `RuntimeDataContainer`, `AsyncWorkerData`, `SystemMainThreadQueueData`)
- A running PostgreSQL (or compatible) database with kick request and account tables

## Installation / Build

The Kick Request system is an integrated module within the FishMMO server architecture. It is included automatically when building the World Server and requires no separate installation steps.

1. Ensure the FishMMO Unity project is set up with all dependencies resolved.
2. Create a `KickRequestSystem` ScriptableObject asset via **Assets → Create → FishMMO → Server → WorldServer → Kick Request System**.
3. Assign the asset to the World Server's system list.
4. Create `KickRequestSystemQueueData` and `KickRequestSystemMainThreadQueueData` data container assets and register them in the `DataContainerRegistry`.

## Quick Start Guides

### Inspector Setup

1. Select the `KickRequestSystem` ScriptableObject asset.
2. Configure **Update Pump Rate** (default `5.0` seconds) — how often the system polls the database.
3. Configure **Update Fetch Count** (default `100`) — maximum kick requests fetched per poll.
4. Configure **Max Main Thread Actions Per Frame** (default `100`) — limits frame spikes from large kick batches.

### Runtime Tuning

```csharp
// Adjust poll interval at runtime
if (server.TryGetSystem<IKickRequestSystem>(out var kickSystem))
{
    kickSystem.UpdatePumpRate = 2.0f;   // poll every 2 seconds
    kickSystem.UpdateFetchCount = 50;   // fetch up to 50 per poll
}
```

### Issuing a Kick Request

Kick requests are inserted into the database by external systems (e.g., admin tools, login server). The `KickRequestSystem` will pick them up on the next poll cycle and disconnect the matching connection if the account is still online.

## Configuration

| Field / Property | Type | Default | Purpose |
|------------------|------|---------|---------|
| `maxMainThreadActionsPerFrame` | `int` | `100` | Maximum kick actions drained from the main-thread queue per frame. Clamped to a minimum of 1. |
| `updatePumpRate` / `UpdatePumpRate` | `float` | `5.0f` | Database poll interval in seconds. Controls how frequently the system checks for new kick requests. |
| `updateFetchCount` / `UpdateFetchCount` | `int` | `100` | Maximum number of kick requests fetched per database poll. |

### Runtime Data State

| Container | Field | Type | Default | Purpose |
|-----------|-------|------|---------|---------|
| `KickRequestSystemQueueData` | `IsProcessing` | `bool` | `false` | Overlap gate preventing concurrent polls |
| `KickRequestSystemQueueData` | `LastFetchTime` | `DateTime` | `DateTime.UtcNow` | Cursor timestamp for pagination |
| `KickRequestSystemQueueData` | `LastPosition` | `long` | `0` | Cursor row ID for pagination |

## Usage Examples

### Querying Kick System State

```csharp
if (server.TryGetSystem<IKickRequestSystem>(out var kickSystem))
{
    Debug.Log($"Poll rate: {kickSystem.UpdatePumpRate}s, Fetch count: {kickSystem.UpdateFetchCount}");
}
```

### Inserting a Kick Request (Database Side)

```csharp
// From an admin tool or another server system
if (database.ServiceRegistry.TryGet<IKickRequestService>(out var kickService))
{
    await kickService.InsertAsync(new KickRequestData
    {
        AccountName = "targetAccount",
        TimeCreated = DateTime.UtcNow
    });
}
```

### Connection Disconnect Lifecycle

```csharp
// Automatically handled by KickRequestSystem:
// 1. Poll detects kick request for "targetAccount"
// 2. Last-login check confirms account hasn't reconnected
// 3. Main-thread queue receives kick action
// 4. On next frame drain: conn.Kick(KickReason.UnexpectedProblem)
// 5. OnRemoteConnectionStopped fires → kick request deleted from DB
```

## Operational Checks

| Check | How to verify | Expected |
|-------|---------------|----------|
| System initialized | Log output: `"Initialized (UpdatePumpRate=5s, FetchCount=100)"` | Appears once at server start |
| Polling active | Monitor database query logs for periodic `FetchAsync` calls | Queries every `updatePumpRate` seconds |
| Kick executed | Watch for client disconnects after inserting a kick request | Client disconnected within one poll cycle |
| Stale filtering | Insert kick request, then log the account in again before next poll | Account stays connected; request skipped |
| Disconnect cleanup | Disconnect an account with a pending kick request | Kick request deleted from database |
| Overlap protection | Set `updatePumpRate` very low with slow DB | Only one `ProcessKickRequestsAsync` runs at a time |
| Main-thread drain | Insert many kick requests at once | Kicks processed at `maxMainThreadActionsPerFrame` per frame |
| Enqueue backpressure | Saturate async worker pool | Warning logged: `"Failed to enqueue..."` |
| Graceful shutdown | Stop the server with pending kick actions | All queued actions drain before shutdown completes |

## Flow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    KickRequestSystem                        │
│                                                             │
│  ┌───────────────────┐    ┌──────────────────────────────┐  │
│  │  InitializeOnce   │    │       OnDeinitialize         │  │
│  │  ─ Validate deps  │    │  ─ DrainMainThreadQueue(all) │  │
│  │  ─ Subscribe conn │    │  ─ Unsubscribe events        │  │
│  │  ─ Register pump  │    │  ─ Unregister periodic       │  │
│  └───────────────────┘    └──────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Periodic Poll (every N seconds)         │   │
│  │                                                      │   │
│  │  OnPeriodicUpdate(deltaTime)                         │   │
│  │    │                                                 │   │
│  │    ▼                                                 │   │
│  │  TryEnqueueAsyncWork(ProcessKickRequestsAsync)       │   │
│  │    │                                                 │   │
│  │    ▼  [Async Worker Thread]                          │   │
│  │  ProcessKickRequestsAsync()                          │   │
│  │    │                                                 │   │
│  │    ├─ lock(data) → check IsProcessing                │   │
│  │    │                                                 │   │
│  │    ├─ kickRequestService.FetchAsync(                  │   │
│  │    │    LastFetchTime, LastPosition, FetchCount)      │   │
│  │    │                                                 │   │
│  │    ├─ Update cursor (LastFetchTime, LastPosition)     │   │
│  │    │                                                 │   │
│  │    ├─ Batch last-login checks (10 per batch)         │   │
│  │    │    accountService.FetchLastLoginAsync(...)       │   │
│  │    │                                                 │   │
│  │    ├─ For each valid request:                         │   │
│  │    │    lastLogin < kickRequest.TimeCreated?          │   │
│  │    │      ├─ Yes → TryEnqueueMainThread(kick action) │   │
│  │    │      └─ No  → Skip (stale)                      │   │
│  │    │                                                 │   │
│  │    └─ finally: lock(data) → IsProcessing = false     │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │           Main-Thread Drain (every frame)            │   │
│  │                                                      │   │
│  │  OnUpdate(deltaTime)                                 │   │
│  │    │                                                 │   │
│  │    ▼                                                 │   │
│  │  DrainMainThreadQueue(drainAll: false)               │   │
│  │    │                                                 │   │
│  │    ▼  Up to maxMainThreadActionsPerFrame:            │   │
│  │  AccountManager.GetConnectionByAccountName(name)     │   │
│  │    │                                                 │   │
│  │    ▼                                                 │   │
│  │  conn.Kick(KickReason.UnexpectedProblem)             │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │           Disconnect Cleanup                         │   │
│  │                                                      │   │
│  │  OnRemoteConnectionStopped(conn)                     │   │
│  │    │                                                 │   │
│  │    ├─ AccountManager.GetAccountNameByConnection(conn) │   │
│  │    │                                                 │   │
│  │    ▼  [Async Worker Thread]                          │   │
│  │  TryEnqueueAsyncWork(DeleteKickRequestAsync)         │   │
│  │    │  keyed by accountName.GetHashCode()             │   │
│  │    │                                                 │   │
│  │    ▼                                                 │   │
│  │  kickRequestService.DeleteAsync(accountName)         │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## Project Structure

### Directory Tree

```
Server/
├── Core/
│   └── World/
│       └── KickRequest/
│           ├── IKickRequestSystem.cs                    # Engine-agnostic public API (UpdatePumpRate, UpdateFetchCount)
│           ├── IKickRequestSystemQueueData.cs           # Polling state contract (IsProcessing, LastFetchTime, LastPosition)
│           └── IKickRequestSystemMainThreadQueueData.cs # Main-thread queue marker interface
└── Implementation/
    └── World/
        └── KickRequest/
            ├── KickRequestSystem.cs                     # Polling, validation, and kick execution orchestration
            ├── KickRequestSystemQueueData.cs            # Polling cursor + processing gate runtime state
            ├── KickRequestSystemMainThreadQueueData.cs  # Per-system main-thread action queue container
            └── README.md
```

### Inheritance Hierarchies

#### Behaviour

```
ServerBehaviour
└── KickRequestSystem : IKickRequestSystem
```

#### Runtime Data Containers

```
RuntimeDataContainer
├── KickRequestSystemQueueData : IKickRequestSystemQueueData
└── SystemMainThreadQueueData (abstract)
    └── KickRequestSystemMainThreadQueueData : IKickRequestSystemMainThreadQueueData
```

### External Integration Points

| Dependency | Interface | Role |
|------------|-----------|------|
| Kick Request Service | `IKickRequestService` | Fetch and delete kick requests from the database |
| Account Service | `IAccountService` | Last-login timestamp checks for stale request filtering |
| Account Manager | `AccountManager` | Account-to-connection lookup and reverse cleanup mapping |
| Periodic Update System | `IPeriodicUpdateSystem` | Fixed-rate polling cadence registration |
| Async Worker Data | `AsyncWorkerData` | Bounded async execution with enqueue backpressure |
| Main Thread Queue Data | `IKickRequestSystemMainThreadQueueData` | Thread-safe dispatch of network kick operations |

### Threading Model

| Thread | Work |
|--------|------|
| Main / periodic callback thread | Schedule polling, drain main-thread queue |
| Async worker threads | DB fetch, cursor updates, stale-filter checks, kick request deletion |
| Main thread (via queue) | FishNet `Kick(...)` operations |

## License

This module is part of the FishMMO project and is distributed under the FishMMO project license. See the repository root for full license terms.
