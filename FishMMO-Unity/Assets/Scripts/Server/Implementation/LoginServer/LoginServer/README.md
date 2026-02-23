# LoginServer System

## Overview

The LoginServer system manages login-server registration and liveness reporting in the database. On startup, it resolves server identity (name + address), registers the login server record, stores the assigned database ID in runtime data, and schedules periodic heartbeat pulses. Heartbeat work is dispatched to `AsyncWorkerData` so update loops stay non-blocking.

## Directory Structure

```
LoginServer/
├── LoginServerSystem.cs       # Login server lifecycle behaviour (register + heartbeat)
├── LoginServerRuntimeData.cs  # Runtime container storing login server DB ID
└── README.md
```

Related core contracts:

- `Server/Core/LoginServer/LoginServer/ILoginServerSystem.cs`
- `Server/Core/LoginServer/LoginServer/ILoginServerRuntimeData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── LoginServerSystem : ILoginServerSystem
```

### Runtime Data

```
RuntimeDataContainer
└── LoginServerRuntimeData : ILoginServerRuntimeData
```

## Runtime Data Dependencies

`LoginServerSystem` declares:

- `[RequiresDataContainer(typeof(LoginServerRuntimeData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

| Container | Responsibility |
|-----------|----------------|
| `LoginServerRuntimeData` | Holds persistent runtime login-server ID assigned by DB |
| `AsyncWorkerData` | Executes heartbeat pulse tasks without blocking main/update threads |

## Initialization Flow

`InitializeOnce()` performs:

1. Validate `Server` and required runtime container availability.
2. Resolve public server address via `AddressProvider.TryGetServerIPAddress(...)`.
3. Read server display name from config key `ServerName`.
4. Resolve `ILoginServerService` from database service registry.
5. Register login server row in DB (`PersistAsync(name, address, port)`).
6. Store returned DB ID in `ILoginServerRuntimeData.ID`.
7. Register periodic callback with `IPeriodicUpdateSystem` at `PulseRate` interval.

If any step fails, initialization returns a failure status and logs error context.

## Heartbeat Pipeline

`OnPeriodicPulse()` runs at configured cadence while server state is `Started`:

1. Read `serverId` from runtime data.
2. Queue async pulse work through `TryEnqueueAsyncWork(...)`.
3. Worker executes `PulseAsync(serverId)`.
4. `ILoginServerService.PulseAsync(serverId)` updates liveness timestamp in DB.
5. Failures are logged (warning/error) without crashing the server loop.

If async queue enqueue fails (backpressure or missing dependency), the pulse is skipped and logged.

## Periodic Callback Integration

The system relies on `IPeriodicUpdateSystem` for fixed-rate callbacks:

- Register: `RegisterPeriodicCallback(PulseRate, OnPeriodicPulse)` in initialize.
- Unregister: `UnregisterPeriodicCallback(OnPeriodicPulse)` in deinitialize.

This keeps pulse cadence independent from frame rate while avoiding per-frame DB calls.

## Configuration Surface

| Field | Type | Default | Purpose |
|---|---|---|---|
| `PulseRate` | `float` | `5.0f` | Periodic heartbeat interval (seconds) |

`PulseRate` is exposed as a read-only property backed by `[SerializeField] private float pulseRate`.

## Shutdown Cleanup

`OnDeinitialize()` performs:

1. Unregisters the periodic pulse callback from `IPeriodicUpdateSystem`.
2. Deletes the login server's DB row (`ILoginServerService.DeleteAsync(serverId)`) with a 5-second timeout.
3. Resets `ILoginServerRuntimeData.ID` to zero.

This prevents ghost rows from persisting in the database after an orderly shutdown.

## Runtime Data Lifecycle

`LoginServerRuntimeData` stores one mutable value:

- `ID` (`long`): login server DB row identifier.

Lifecycle behavior:

- `InitializeOnce()` sets `ID = 0`.
- `Clear()` resets `ID = 0`.
- `Deinitialize()` resets `ID = 0`.

## Threading Model

| Thread | Work |
|--------|------|
| Main / server update | initialization, callback registration, pulse scheduling |
| Async workers | database heartbeat calls (`PulseAsync`) |

This separation ensures heartbeat failures or DB latency do not stall server frame/update loops.

## External Integration Points

- **Address Provider** (`IServerAddressProvider`) — resolves network address and port for registration.
- **Configuration** (`IServerConfiguration`) — supplies `ServerName`.
- **Database Service Registry** — resolves `ILoginServerService`.
- **Periodic Update System** (`IPeriodicUpdateSystem`) — drives heartbeat scheduling.
- **Runtime Data Registry** — provides `ILoginServerRuntimeData` and `IAsyncWorkerData`.