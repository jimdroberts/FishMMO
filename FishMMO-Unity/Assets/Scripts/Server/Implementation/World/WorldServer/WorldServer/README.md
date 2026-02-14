# WorldServer System

## Overview

The WorldServer system is responsible for registering the running world server instance in the database and keeping its liveness/population state up to date through periodic heartbeat pulses. It persists world endpoint metadata (name/address/port), tracks server identity (`ServerId`) in runtime data, and sends pulse updates with current character counts gathered from world-scene mapping.

## Directory Structure

```
WorldServer/
├── WorldServerSystem.cs          # World server registration + heartbeat orchestration
├── WorldServerSystemRuntimeData.cs # Runtime world server ID + lock state container
└── README.md
```

Related core contracts:

- `Server/Core/World/WorldServer/WorldServer/IWorldServerSystem.cs`
- `Server/Core/World/WorldServer/WorldServer/IWorldServerSystemRuntimeData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── WorldServerSystem : IWorldServerSystem
```

### Runtime Data

```
RuntimeDataContainer
└── WorldServerSystemRuntimeData : IWorldServerSystemRuntimeData
```

## Runtime Data Model

`WorldServerSystemRuntimeData` stores mutable world-server state:

| Property | Type | Description |
|---|---|---|
| `ID` | `long` | Database identifier for this world server instance |
| `IsLocked` | `bool` | Admission lock flag used by world authentication/gates |

Lifecycle behavior:

- `InitializeOnce()` sets `ID = 0`, `IsLocked = false`
- `Clear()` resets both values
- `Deinitialize()` delegates to `Clear()`

## Initialization Flow

`WorldServerSystem.InitializeOnce()` performs:

1. Validates base dependencies (`Server`, DB service registry).
2. Verifies required DB service (`IWorldServerService`).
3. Validates required config (`ServerName`).
4. Resolves network endpoint via `AddressProvider.TryGetServerIPAddress(...)`.
5. Resolves `IWorldSceneSystem` and current connection count from `IWorldSceneMappingData<NetworkConnection>`.
6. Calls `Register(address, port, characterCount)`.
7. Registers periodic callback (`PulseRate`) through `IPeriodicUpdateSystem`.

If required dependencies fail, initialization returns an explicit failure status.

## Registration Logic

`Register(serverAddress, port, characterCount)`:

1. Resolves runtime data container (`IWorldServerSystemRuntimeData`).
2. Resolves `IWorldServerService` from DB service registry.
3. Reads `ServerName` from server configuration.
4. Persists world server row in DB using:
   - name
   - address
   - port
   - initial character count
   - current lock state (`IsLocked`)
5. Caches returned `ServerId` into runtime data.

The registration call is executed through `Task.Run(...).GetAwaiter().GetResult()` during startup to avoid deadlocking Unity’s synchronization context while still guaranteeing registration completes before startup continues.

## Pulse / Heartbeat Pipeline

### Trigger

`OnPeriodicPulse(deltaTime)` runs on periodic callback cadence (`PulseRate`) when:

- server state is `Started`
- behaviour is initialized
- `IWorldSceneSystem` is available

### Dispatch

It reads current `ConnectionCount` from `IWorldSceneMappingData<NetworkConnection>` and calls `Pulse(characterCount)`.

### Async Execution

`Pulse(characterCount)` enqueues `PulseAsync(serverId, characterCount)` onto `AsyncWorkerData`.

If enqueue fails (backpressure/missing dependency), it logs a warning and skips the pulse cycle.

### Database Update

`PulseAsync(...)` resolves `IWorldServerService` and executes `PulseAsync(serverId, characterCount)` to update server liveness/population in the database.

## Threading Model

| Thread | Work |
|---|---|
| Main / periodic callback | validation, registration call orchestration, pulse scheduling |
| Async worker | non-blocking database pulse updates |

This separation avoids blocking frame/update loops during normal operation while still guaranteeing deterministic registration at startup.

## Configuration Surface

| Field | Type | Default | Purpose |
|---|---|---|---|
| `PulseRate` | `float` | `5.0f` | Periodic heartbeat interval (seconds) |

## External Integration Points

- **AddressProvider** (`IServerAddressProvider`) — resolves public endpoint for registration.
- **Configuration** (`IServerConfiguration`) — supplies `ServerName`.
- **WorldSceneMappingData** (`IWorldSceneMappingData<NetworkConnection>`) — supplies live character count.
- **WorldServerService** (`IWorldServerService`) — persists world record and pulse updates.
- **PeriodicUpdateSystem** (`IPeriodicUpdateSystem`) — drives pulse cadence.
- **AsyncWorkerData** — executes pulse DB writes asynchronously with bounded queueing.