# Server Select System

**Short description:** Login-server subsystem that serves the world server list to authenticated clients by querying active servers from the database with an idle-timeout filter, mapping rows into network payloads, and dispatching responses on the main thread with per-connection in-flight gating and bounded draining.

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

The Server Select system manages the world server list workflow on the login server. When an authenticated client sends a `RequestServerListBroadcast`, the system validates the connection, applies per-connection in-flight gating and cooldown checks, then offloads the database query onto `AsyncWorkerData` to keep the network handler non-blocking. Active world servers are fetched via `IWorldServerService.FetchActiveAsync` with an `IdleTimeout` filter (default 60 seconds), mapped from `WorldServerData` rows into `WorldServerDetails` DTOs, and the `ServerListBroadcast` response is marshalled back to the main thread through a dedicated queue container (`ServerSelectSystemMainThreadQueueData`).

Bounded main-thread response draining (`maxMainThreadResponsesPerFrame`) prevents frame spikes, while per-connection in-flight gating and a post-release cooldown (`serverListCooldownMilliseconds = 1000`) prevent concurrent and rapid sequential request spam.

### Threading Model

| Thread | Work |
|--------|------|
| Main / Network thread | Broadcast receive, fast validation, enqueue worker task |
| Async worker thread | Database fetch via `IWorldServerService` and DTO mapping |
| Main thread | FishNet `Broadcast` dispatch (thread-safe marshalling via queue) |

`OnUpdate` drains queued main-thread actions every frame, capped by `maxMainThreadResponsesPerFrame`. `OnDeinitialize` drains all remaining actions so clients receive final messages.

### Broadcast Protocol

| Broadcast | Direction | Purpose |
|-----------|-----------|---------|
| `RequestServerListBroadcast` | Client → Server | Request the list of active world servers |
| `ServerListBroadcast` | Server → Client | Response containing `List<WorldServerDetails>` of active world servers |
| `WorldSceneConnectBroadcast` | Server → Client | Provides address and port for connecting to a specific world scene server |

### Failure Response Behaviour

All request flows guarantee a response to the client, even on failure, to prevent indefinite client hangs:

- **World Server Service Unavailable:** An empty `ServerListBroadcast` is sent and a warning is logged.
- **Database Fetch Failure:** An empty `ServerListBroadcast` is sent with the error message logged.
- **Async Enqueue Failure:** The in-flight gate is released immediately and a warning is logged.
- **Unexpected Exception:** Caught in the `ProcessServerListRequestAsync` catch block, logged, and the in-flight gate is released in the `finally` block.

### Authentication Check

Before processing any `RequestServerListBroadcast`, the system verifies the connection is authenticated via `Server.AccountManager.GetAccountNameByConnection`. Unauthenticated connections are immediately kicked with `KickReason.UnusualActivity`.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only subsystem |

**Engine:** Unity 6.3 LTS
**Scripting Backend:** IL2CPP

## Features

- **Active world server list** — async database fetch via `IWorldServerService.FetchActiveAsync`, filtered by configurable `idleTimeout` (default 60s)
- **DTO mapping** — maps `WorldServerData` rows to `WorldServerDetails` (Name, LastPulse, Address, Port, CharacterCount, Locked)
- **Per-connection in-flight gating** — `ConcurrentDictionary<int, byte>` prevents duplicate concurrent server-list requests per connection
- **Post-release cooldown** — configurable `serverListCooldownMilliseconds` (default 1000ms) gap between successive requests enforced via `NextAllowedRequestUtcByClientId`
- **Bounded main-thread draining** — configurable `maxMainThreadResponsesPerFrame` to time-slice response dispatch and avoid frame spikes
- **Authentication enforcement** — verifies connection ownership via `AccountManager.GetAccountNameByConnection` before processing; kicks unauthenticated connections
- **Guaranteed client response** — on any failure (service unavailability, DB error, exception), an empty `ServerListBroadcast` is sent to prevent client hangs
- **Disconnect cleanup** — `OnRemoteConnectionStopped` removes in-flight and cooldown entries for disconnected clients
- **Idle-timeout server filtering** — servers whose last pulse exceeds `idleTimeout` seconds are excluded by the database query

## Prerequisites

- FishMMO server framework with `ServerBehaviour` base class
- FishNet networking library
- PostgreSQL database with Npgsql services implementing:
  - `IWorldServerService` (provides `FetchActiveAsync`)
- `AccountManager` for connection-to-account authentication mapping
- `AsyncWorkerData` runtime data container for background task dispatch
- `DataContainerRegistry` with required containers registered

## Installation / Build

This is an integrated module within the FishMMO server framework. No separate installation is required.

1. The `ServerSelectSystem` ScriptableObject is created via **Assets → Create → FishMMO → Server → LoginServer → Server Select System**.
2. Add the created asset to the login server's `ServerBehaviour` list.
3. The `DataContainerRegistry` automatically creates required runtime data containers declared via `[RequiresDataContainer]` attributes.

## Quick Start Guides

### Server Operator

1. Create the `ServerSelectSystem` ScriptableObject asset via the Unity menu.
2. Assign it to the login server behaviour list.
3. Configure inspector fields (see [Configuration](#configuration)).
4. Start the login server — the system registers the `RequestServerListBroadcast` handler on `InitializeOnce`.
5. Authenticated clients can now request the world server list.

### Developer

1. `ServerSelectSystem` extends `ServerBehaviour` and implements `IServerSelectSystem`.
2. The broadcast handler is registered in `InitializeOnce` and unregistered in `OnDeinitialize`.
3. All async database work is dispatched via `TryEnqueueAsyncWork` onto `AsyncWorkerData`.
4. All FishNet broadcasts are marshalled back to the main thread via `TryEnqueueMainThread<IServerSelectSystemMainThreadQueueData>`.
5. Per-connection state is stored in `ServerSelectSystemRuntimeData` — always release in-flight gates in `finally` blocks.
6. `OnUpdate` calls `DrainMainThreadQueue(drainAll: false)` every frame; `OnDeinitialize` calls `DrainMainThreadQueue(drainAll: true)`.

## Configuration

### Inspector Fields (`ServerSelectSystem`)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `maxMainThreadResponsesPerFrame` | `int` | `100` | Maximum queued main-thread response actions processed per frame. Clamped to minimum of 1 on initialization. |
| `idleTimeout` | `float` | `60` | Idle timeout in seconds for world servers to be considered active. Servers whose last pulse exceeds this value are excluded. Minimum value of 1 enforced via `[Min(1f)]`. |
| `serverListCooldownMilliseconds` | `int` | `1000` | Minimum interval in milliseconds between successive server-list requests from the same connection. Prevents sequential spam after each request completes. |

### Runtime Data: `ServerSelectSystemRuntimeData`

| Property | Type | Purpose |
|----------|------|---------|
| `InFlightRequests` | `ConcurrentDictionary<int, byte>` | Per-connection in-flight gate preventing duplicate concurrent server-list requests |
| `NextAllowedRequestUtcByClientId` | `ConcurrentDictionary<int, DateTime>` | Per-connection post-release cooldown timestamp; enforces `serverListCooldownMilliseconds` gap between successive requests |

**Thread Safety:** `ConcurrentDictionary` allows safe access from both network and worker threads.

**Lifecycle:**
- `InitializeOnce()` — creates empty `ConcurrentDictionary` instances.
- `Clear()` — clears dictionary entries.
- `OnDeinitialize()` — clears and nulls references.

### Runtime Data Dependencies

`ServerSelectSystem` declares three required containers via attributes:

| Container | Responsibility |
|-----------|----------------|
| `ServerSelectSystemRuntimeData` | Per-connection in-flight gate and cooldown for server-list request deduplication |
| `AsyncWorkerData` | Executes database operations in background worker threads |
| `ServerSelectSystemMainThreadQueueData` | Marshals network-safe response actions to the main thread |

## Usage Examples

### Server List Request (Client → Server → Client)

```
Client sends: RequestServerListBroadcast { }
Server validates authentication, checks in-flight gate and cooldown
Server enqueues async work → fetches active servers from DB (filtered by idleTimeout)
Server maps WorldServerData rows to WorldServerDetails DTOs
Server sends: ServerListBroadcast { Servers = [ { Name, LastPulse, Address, Port, CharacterCount, Locked }, ... ] }
```

### Server List Request — Failure (Service Unavailable)

```
Client sends: RequestServerListBroadcast { }
Server validates authentication, acquires in-flight gate
Server enqueues async work → IWorldServerService unavailable
Server sends: ServerListBroadcast { Servers = [] }    // empty list, client does not hang
Server releases in-flight gate
```

### Server List Request — Cooldown Rejection

```
Client sends: RequestServerListBroadcast { }
Server checks NextAllowedRequestUtcByClientId → cooldown not expired
Request silently dropped (client must wait for cooldown to expire)
```

### Server List Request — Unauthenticated

```
Client sends: RequestServerListBroadcast { }
Server checks AccountManager → connection not authenticated
Server kicks connection with KickReason.UnusualActivity
```

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| System initializes | Login server startup logs | `"ServerSelectSystem: Initialized (idleTimeout=60s)"` in debug log |
| Server list works | Authenticate and request server list | Client receives `ServerListBroadcast` with active world server entries |
| Idle timeout filtering | Set `idleTimeout` low, stop a world server | Stopped server excluded from list after timeout expires |
| In-flight gating | Rapid-fire requests from same connection | Only one request processed at a time; subsequent requests silently dropped |
| Cooldown enforcement | Send request immediately after previous completes | Request rejected until 1-second cooldown expires |
| Authentication enforcement | Send request without authenticating | Connection kicked with `KickReason.UnusualActivity` |
| Disconnect cleanup | Client disconnects mid-flow | In-flight and cooldown entries removed for that connection |
| Failure responses | DB service unavailable or query failure | Client receives empty `ServerListBroadcast` (never hangs) |
| Deinitialize drain | Shut down login server with pending responses | All queued responses dispatched before shutdown completes |
| Locked server display | Lock a world server in the database | Server appears in list with `Locked = true` |

## Flow Diagram

### Server List Request Flow

```
Client                    LoginServer                        Database
  |                           |                                 |
  |-- RequestServerList ----->|                                 |
  |                           |-- Validate authentication       |
  |                           |-- Check cooldown                |
  |                           |-- Acquire in-flight gate        |
  |                           |-- Enqueue async work            |
  |                           |       |                         |
  |                           |       |-- FetchActiveAsync ---->|
  |                           |       |   (idleTimeout filter)  |
  |                           |       |<-- WorldServerData[] ---|
  |                           |       |                         |
  |                           |       |-- Map to DTOs           |
  |                           |       |   WorldServerDetails[]  |
  |                           |       |                         |
  |                           |<- Enqueue main-thread response  |
  |<-- ServerListBroadcast    |                                 |
  |                           |-- Release in-flight gate        |
  |                           |-- Set cooldown timestamp        |
```

### Failure Flow

```
Client                    LoginServer                        Database
  |                           |                                 |
  |-- RequestServerList ----->|                                 |
  |                           |-- Validate authentication       |
  |                           |-- Check cooldown                |
  |                           |-- Acquire in-flight gate        |
  |                           |-- Enqueue async work            |
  |                           |       |                         |
  |                           |       |-- FetchActiveAsync ---->|
  |                           |       |<-- Error / null --------|
  |                           |       |                         |
  |                           |<- Enqueue empty response        |
  |<-- ServerListBroadcast    |   (Servers = [])                |
  |                           |-- Release in-flight gate        |
  |                           |-- Set cooldown timestamp        |
```

### Main-Thread Drain Cycle

```
OnUpdate (every frame)
  |
  |-- DrainMainThreadQueue(drainAll: false)
  |       |
  |       |-- Dequeue up to maxMainThreadResponsesPerFrame actions
  |       |-- Execute each action (FishNet Broadcast calls)
  |
OnDeinitialize
  |
  |-- DrainMainThreadQueue(drainAll: true)
  |       |
  |       |-- Dequeue ALL remaining actions
  |       |-- Execute each action (final client messages)
```

## Project Structure

### Directory Tree

```
Server/Implementation/LoginServer/ServerSelect/
├── ServerSelectSystem.cs                    # Login-server behaviour for world server list requests
├── ServerSelectSystemRuntimeData.cs         # Per-connection in-flight gate and cooldown container
├── ServerSelectSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md

Server/Core/LoginServer/ServerSelect/
├── IServerSelectSystem.cs                   # Engine-agnostic public API interface
└── IServerSelectSystemMainThreadQueueData.cs # Main-thread queue data interface

Shared/Implementation/Network/ServerSelect/
├── ServerSelectBroadcasts.cs                # Network broadcast structs (RequestServerListBroadcast, ServerListBroadcast, WorldSceneConnectBroadcast)
└── ServerAddress.cs                         # Serializable server address with HTTPS formatting

Shared/Implementation/Network/
└── WorldServerDetails.cs                    # Serializable world server details DTO
```

### Inheritance Hierarchies

#### Behaviour

```
ServerBehaviour
└── ServerSelectSystem : IServerSelectSystem
```

#### Runtime Data Containers

```
RuntimeDataContainer
├── ServerSelectSystemRuntimeData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── ServerSelectSystemMainThreadQueueData : IServerSelectSystemMainThreadQueueData
```

#### Broadcast Structs

```
IBroadcast
├── RequestServerListBroadcast      → (empty, no fields)
├── ServerListBroadcast             → List<WorldServerDetails>
└── WorldSceneConnectBroadcast      → string Address, ushort Port
```

#### Shared Data Classes

```
WorldServerDetails
├── Name            : string
├── LastPulse       : DateTime
├── Address         : string
├── Port            : ushort
├── CharacterCount  : int
└── Locked          : bool

ServerAddress
├── Address         : string
├── Port            : ushort
└── HTTPSAddress()  : string    // formats as https://Address:Port/

ServerAddresses
└── Addresses       : List<ServerAddress>
```

### External Integration Points

| Dependency | Purpose |
|------------|---------|
| `AccountManager` | Validates connection authentication via `GetAccountNameByConnection` |
| `Database Service Registry` | Resolves `IWorldServerService` |
| `IWorldServerService` | Provides active world server rows filtered by pulse age (`FetchActiveAsync`) |
| `AsyncWorkerData` | Centralized bounded background work execution queue |
| `ServerSelectSystemMainThreadQueueData` | Guarantees main-thread-safe network dispatch |
| `FishNet NetworkWrapper` | Receives `RequestServerListBroadcast` and sends `ServerListBroadcast` responses |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
