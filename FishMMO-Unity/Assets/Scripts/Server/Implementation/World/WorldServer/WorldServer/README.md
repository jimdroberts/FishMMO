# World Server System

**Short description:** WorldServer subsystem responsible for registering the running world server instance in the database and maintaining its liveness and population state through periodic heartbeat pulses.

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

The WorldServer system is responsible for registering the running world server instance in the database and keeping its liveness/population state up to date through periodic heartbeat pulses. It persists world endpoint metadata (name, address, port), tracks server identity (`ServerId`) in runtime data, and sends pulse updates with current character counts gathered from world-scene mapping.

The implementation uses a split execution model:

- **Main thread / periodic callback:** dependency validation, registration call orchestration, pulse scheduling, and connection count reads from `IWorldSceneMappingData<NetworkConnection>`.
- **Async worker:** non-blocking database pulse updates dispatched via `TryEnqueueAsyncWork` onto `AsyncWorkerData`.
- **Startup registration:** executed through `Task.Run(...).GetAwaiter().GetResult()` to avoid deadlocking Unity's synchronization context while guaranteeing registration completes before startup continues.

This separation avoids blocking frame/update loops during normal operation while still guaranteeing deterministic registration at startup.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- World server registration in the database on startup with endpoint metadata (name, address, port) and initial character count
- Periodic heartbeat (pulse) updates to the database with live character count from `IWorldSceneMappingData<NetworkConnection>`
- Configurable pulse interval via inspector field `PulseRate` (default 5 seconds)
- Runtime data container (`WorldServerSystemRuntimeData`) storing mutable server ID and admission lock state independently from system logic
- Async worker integration for non-blocking database writes during pulse cycles
- Orderly shutdown cleanup that deletes the world server DB row with a 5-second timeout, preventing ghost records
- `CreateAssetMenu` support for ScriptableObject-based instantiation (`FishMMO/Server/WorldServer/World Server System`)
- Explicit dependency validation at initialization for `Server`, `Database.ServiceRegistry`, `IWorldServerService`, `ServerName` config, and `IWorldSceneSystem`
- Admission lock flag (`IsLocked`) persisted with each registration, usable by world authentication gates

## Prerequisites

- Unity 6.3 LTS (or later)
- FishNet networking framework
- FishMMO Server Core assemblies (`FishMMO.Server.Core`)
- FishMMO Database layer with Npgsql services (`FishMMO.Database`, `FishMMO.Database.Npgsql`)
- A running PostgreSQL instance with the FishMMO schema (world server table)
- `IWorldServerService` registered in the database service registry
- `IWorldSceneSystem` registered in the server behaviour registry
- `IWorldSceneMappingData<NetworkConnection>` registered in the data container registry
- `IPeriodicUpdateSystem` implemented by the server (for heartbeat scheduling)
- `AsyncWorkerData` data container available for async work dispatch
- `ServerName` configured in the server configuration

## Installation / Build

This is an integrated module within the FishMMO project. No separate installation is required.

1. Ensure the FishMMO Unity project is set up with all server-side assemblies.
2. The `WorldServerSystem` ScriptableObject can be created via the Unity menu: **Assets → Create → FishMMO → Server → WorldServer → World Server System**.
3. Attach the created asset to the server's behaviour registry so it is initialized during server startup.
4. Ensure `WorldServerSystemRuntimeData` is registered as a data container (enforced by the `[RequiresDataContainer]` attribute).

## Quick Start Guides

### Minimal Server Setup

1. Create a `WorldServerSystem` ScriptableObject asset from the Unity menu.
2. Configure `ServerName` in the server configuration file or provider.
3. Set the `PulseRate` field in the inspector (default: `5.0` seconds).
4. Ensure the database connection string points to a valid PostgreSQL instance with the FishMMO schema.
5. Start the world server — `InitializeOnce()` will register the server in the database and begin periodic heartbeat pulses.

### Verifying Registration

1. Start the world server and check the console for `Initialized (PulseRate=5s)`.
2. Query the world server table in the database to confirm a row exists with the expected name, address, and port.
3. Wait one pulse interval and verify the `LastPulse` or character count column is updating.

## Configuration

| Field | Type | Default | Source | Purpose |
|---|---|---|---|---|
| `pulseRate` | `float` | `5.0f` | `[SerializeField]` inspector | Interval in seconds between heartbeat pulses to the database |
| `ServerName` | `string` | *(required)* | `IServerConfiguration` | Human-readable name persisted with the world server record |
| Server address/port | `string` / `ushort` | *(auto-resolved)* | `IServerAddressProvider` | Public endpoint metadata stored in the database |

### Runtime Data Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ID` | `long` | `0` | Database identifier for this world server instance, set after registration |
| `IsLocked` | `bool` | `false` | Admission lock flag used by world authentication gates to reject new connections |

## Usage Examples

### Programmatic Registration

```csharp
// WorldServerSystem.Register is called automatically during InitializeOnce(),
// but can also be invoked directly through the IWorldServerSystem interface:
if (server.BehaviourRegistry.TryGet(out IWorldServerSystem worldServer))
{
    worldServer.Register("192.168.1.100", 7770, 0);
}
```

### Manual Pulse Trigger

```csharp
// Pulses are sent automatically on the periodic callback, but can be triggered manually:
if (server.BehaviourRegistry.TryGet(out IWorldServerSystem worldServer))
{
    int currentPlayers = GetCurrentCharacterCount();
    worldServer.Pulse(currentPlayers);
}
```

### Checking Runtime State

```csharp
if (server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData runtimeData))
{
    long serverId = runtimeData.ID;       // Database ID assigned after registration
    bool locked = runtimeData.IsLocked;   // Admission lock state
}
```

### Locking the Server

```csharp
// Set the lock flag before the next registration or pulse persists it:
if (server.DataContainerRegistry.TryGet(out IWorldServerSystemRuntimeData runtimeData))
{
    runtimeData.IsLocked = true;
}
```

## Operational Checks

| Check | Method | Expected Result |
|---|---|---|
| Server registered in DB | Query world server table after startup | Row exists with correct name, address, port |
| Heartbeat updating | Query world server table after one `PulseRate` interval | Character count and/or timestamp updated |
| Console initialization log | Check server console output | `Initialized (PulseRate=5s)` message present |
| Shutdown cleanup | Stop server and query world server table | Row deleted within 5 seconds |
| Missing `ServerName` config | Omit `ServerName` from configuration | `InitializeOnce` returns `FailedToFindRequiredDependency` |
| Missing `IWorldServerService` | Remove service from DB registry | `InitializeOnce` returns `FailedToGetDbContext` |
| Pulse backpressure | Flood async worker queue | Warning log: `Failed to enqueue world server pulse work item.` |
| Runtime data reset on shutdown | Inspect `IWorldServerSystemRuntimeData.ID` after stop | Value is `0` |

## Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        SERVER STARTUP                               │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
                               ▼
                 ┌─────────────────────────────┐
                 │     InitializeOnce()         │
                 │  1. Validate Server, DB,     │
                 │     IWorldServerService,     │
                 │     ServerName config        │
                 │  2. Resolve address/port     │
                 │     via AddressProvider      │
                 │  3. Read initial connection  │
                 │     count from scene mapping │
                 │  4. Call Register(...)        │
                 │  5. Register periodic pulse  │
                 │     callback (PulseRate)     │
                 └──────────────┬──────────────┘
                                │
                ┌───────────────┴───────────────┐
                │                               │
                ▼                               ▼
  ┌───────────────────────┐      ┌──────────────────────────────┐
  │   Register(addr,      │      │ IPeriodicUpdateSystem        │
  │         port, count)  │      │ registers OnPeriodicPulse    │
  │                       │      │ at PulseRate interval        │
  │  Task.Run → DB        │      └──────────────┬───────────────┘
  │  PersistAsync(...)    │                      │
  │  → runtimeData.ID =  │                      │  every PulseRate seconds
  │    result.ServerId    │                      ▼
  └───────────────────────┘      ┌──────────────────────────────┐
                                 │   OnPeriodicPulse(deltaTime) │
                                 │  1. Guard: Initialized,      │
                                 │     Started, Server != null  │
                                 │  2. Read ConnectionCount     │
                                 │     from scene mapping data  │
                                 │  3. Call Pulse(count)         │
                                 └──────────────┬───────────────┘
                                                │
                                                ▼
                                 ┌──────────────────────────────┐
                                 │   Pulse(characterCount)      │
                                 │  TryEnqueueAsyncWork(        │
                                 │    PulseAsync(serverId,      │
                                 │              count))         │
                                 └──────────────┬───────────────┘
                                                │
                                                ▼
                                 ┌──────────────────────────────┐
                                 │  AsyncWorkerData (bg thread) │
                                 │  PulseAsync(serverId, count) │
                                 │  → IWorldServerService       │
                                 │    .PulseAsync(id, count)    │
                                 └──────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       SERVER SHUTDOWN                                │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
                               ▼
                 ┌─────────────────────────────┐
                 │      OnDeinitialize()        │
                 │  1. Unregister periodic      │
                 │     pulse callback           │
                 │  2. Delete world server DB   │
                 │     row (5s timeout)         │
                 │  3. Reset runtimeData.ID = 0 │
                 └─────────────────────────────┘
```

## Project Structure

```
WorldServer/WorldServer/
├── WorldServerSystem.cs              # World server registration + heartbeat orchestration
├── WorldServerSystemRuntimeData.cs   # Runtime world server ID + lock state container
└── README.md                         # This file
```

### Related Core Contracts

```
Server/Core/World/WorldServer/WorldServer/
├── IWorldServerSystem.cs             # Core-facing interface: Register(), Pulse()
└── IWorldServerSystemRuntimeData.cs  # Core-facing interface: ID, IsLocked
```

### Related Data Containers

```
Server/Core/RuntimeData/
└── IAsyncWorkerData.cs               # Async worker queue for background DB operations
```

### Inheritance Hierarchies

**Behaviour**

```
ServerBehaviour
└── WorldServerSystem : IWorldServerSystem
```

**Runtime Data**

```
RuntimeDataContainer
└── WorldServerSystemRuntimeData : IWorldServerSystemRuntimeData
```

### External Integration Points

| Dependency | Interface | Purpose |
|---|---|---|
| Address Provider | `IServerAddressProvider` | Resolves public endpoint for registration |
| Configuration | `IServerConfiguration` | Supplies `ServerName` |
| World Scene Mapping | `IWorldSceneMappingData<NetworkConnection>` | Supplies live character count via `ConnectionCount` |
| World Server Service | `IWorldServerService` | Persists world record, pulse updates, and deletion |
| Periodic Update System | `IPeriodicUpdateSystem` | Drives pulse cadence at `PulseRate` interval |
| Async Worker Data | `AsyncWorkerData` | Executes pulse DB writes asynchronously with bounded queueing |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
