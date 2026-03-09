# Login Server System

**Short description:** Manages login-server database registration, HMAC signing-key provisioning, authenticator configuration, and periodic heartbeat liveness reporting for a FishMMO login server instance.

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

The Login Server system manages the full lifecycle of a FishMMO login server instance inside the database. On startup it resolves the server's identity (name + public address), registers a login-server row in the database, generates an HMAC signing key for authentication token issuance, persists that key, and configures the `ServerAuthenticator` so it can sign tokens. A periodic heartbeat pulse is then scheduled to keep the server's liveness timestamp current.

All database work during initialization is executed via `Task.Run` with a 30-second timeout to avoid deadlocks from Unity's `SynchronizationContext` when blocking on async during init. Heartbeat pulses are dispatched through `AsyncWorkerData` so the main update loop stays non-blocking.

On shutdown the system performs an orderly cleanup: it zeros the in-memory signing key, deletes the signing-key row from the database, deregisters the login-server record, and resets runtime state.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Fully supported as a server host |
| Linux    | Yes       | Fully supported as a server host |
| WebGL    | N/A       | Server-only component; not applicable to browser builds |

**Engine:** Unity 6.3 LTS  
**Scripting backend:** IL2CPP

## Features

- **Database registration** — Persists a login-server row (name, address, port) via `ILoginServerService.PersistAsync` with a 30-second timeout.
- **HMAC signing-key generation** — Generates a cryptographically random HMAC key (`CryptoHelper.GenerateKey`) and persists it via `ILoginServerSigningKeyService.UpsertAsync`.
- **Authenticator configuration** — Injects the HMAC key and login-server ID into the `ServerAuthenticator` so it can issue signed authentication tokens.
- **Periodic heartbeat** — Schedules `OnPeriodicPulse` through `IPeriodicUpdateSystem` at a configurable cadence (default 5 s).
- **Async heartbeat dispatch** — Heartbeat database calls are enqueued to `AsyncWorkerData` via `TryEnqueueAsyncWork`, keeping the main thread free.
- **Graceful shutdown** — Zeros the in-memory signing key with `CryptographicOperations.ZeroMemory`, deletes the signing-key DB row, deregisters the login-server DB row, and resets runtime data.
- **Timeout-guarded initialization** — All blocking database operations use explicit timeouts to prevent indefinite hangs.
- **Structured error logging** — Every failure path logs contextual error/warning messages through `FishMMO.Logging.Log`.

## Prerequisites

- Unity 6.3 LTS (IL2CPP scripting backend)
- FishNet networking framework (for `ServerManager` / `ServerAuthenticator`)
- FishMMO server core assemblies (`FishMMO.Server.Core`, `FishMMO.Server.Core.LoginServer`)
- FishMMO database layer (`FishMMO.Database`, `FishMMO.Database.Npgsql`) with a reachable PostgreSQL instance
- `ILoginServerService` and `ILoginServerSigningKeyService` registered in the database service registry
- A valid server address provider (`IServerAddressProvider`) and configuration entry for `ServerName`

## Installation / Build

This is an integrated module within the FishMMO Unity project. No separate installation is required.

1. Ensure the FishMMO Unity project is open in the Unity Editor.
2. The `LoginServerSystem` ScriptableObject asset can be created via **Assets → Create → FishMMO → Server → LoginServer → Login Server System**.
3. Add the created asset to the login server's behaviour list so it is initialized during server startup.
4. Ensure `LoginServerRuntimeData` and `AsyncWorkerData` runtime data containers are registered in the server's `DataContainerRegistry`.

## Quick Start Guides

### Running a Login Server Locally

1. Configure `ServerName` in the server configuration file (e.g., `"LoginServer-Dev"`).
2. Ensure the address provider returns a valid IP and port for the login server.
3. Verify the database is reachable and `ILoginServerService` / `ILoginServerSigningKeyService` are registered.
4. Start the server — `LoginServerSystem.InitializeOnce()` will register in the DB, generate the HMAC key, and begin heartbeating.
5. Confirm initialization by checking the debug log for: `Initialized (ServerID=<id>, Address=<addr>:<port>, PulseRate=5s)`.

### Verifying Heartbeat

1. After startup, query the login-server table in the database.
2. The `LastPulse` (or equivalent timestamp column) should update every `PulseRate` seconds.
3. If heartbeat stops, check logs for `"Failed to enqueue pulse work item"` or `"Pulse failed"` warnings.

## Configuration

| Field | Type | Default | Source | Purpose |
|-------|------|---------|--------|---------|
| `pulseRate` | `float` | `5.0f` | `[SerializeField]` on `LoginServerSystem` | Interval in seconds between database heartbeat pulses |
| `ServerName` | `string` | — | Server configuration (`IServerConfiguration`) | Display name registered in the database for this login server |
| Server address / port | `ServerAddress` | — | `IServerAddressProvider` | Public network endpoint registered in the database |

`PulseRate` is exposed as a read-only property backed by `[SerializeField] private float pulseRate`.

### Inspector Fields

| Inspector Field | Type | Default | Description |
|-----------------|------|---------|-------------|
| `Pulse Rate` | `float` | `5.0` | How often (in seconds) the server sends a heartbeat to the database |

## Usage Examples

### Accessing the Login Server ID at Runtime

```csharp
if (Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
{
    long loginServerId = runtimeData.ID;
    // Use loginServerId for cross-server identification, token signing, etc.
}
```

### Creating the ScriptableObject Asset

```
Assets → Create → FishMMO → Server → LoginServer → Login Server System
```

The asset menu path is defined by:

```csharp
[CreateAssetMenu(fileName = "LoginServerSystem",
                 menuName = "FishMMO/Server/LoginServer/Login Server System",
                 order = 1)]
```

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| DB registration | Query login-server table after startup | Row exists with correct name, address, port |
| HMAC key persistence | Query signing-key table for login-server ID | Row exists with a non-null key blob |
| Authenticator configured | Inspect `ServerAuthenticator.TokenSigningKey` at runtime | Non-null byte array matching generated key |
| Heartbeat active | Monitor login-server table timestamp | Updates every ~5 s (or configured `PulseRate`) |
| Graceful shutdown — key zeroed | Breakpoint or log after `OnDeinitialize` | `TokenSigningKey` is null, memory zeroed |
| Graceful shutdown — DB cleanup | Query login-server and signing-key tables after stop | Both rows deleted |
| Initialization timeout | Block DB access and start server | Log error after 30 s: `"Login server DB registration timed out"` |
| Pulse failure logging | Simulate DB error during heartbeat | Warning: `"Pulse failed: <error>"` |

## Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                    LoginServerSystem Lifecycle                      │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  InitializeOnce()                                                   │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ 1. Validate Server + DataContainerRegistry                    │ │
│  │ 2. Resolve server address (IServerAddressProvider)            │ │
│  │ 3. Read ServerName from IServerConfiguration                  │ │
│  │ 4. Resolve ILoginServerService from DB service registry       │ │
│  │ 5. Task.Run → PersistAsync(name, address, port) [30s timeout] │ │
│  │ 6. Store returned DB ID → ILoginServerRuntimeData.ID         │ │
│  │ 7. Resolve ILoginServerSigningKeyService                      │ │
│  │ 8. CryptoHelper.GenerateKey(HmacKeyLength)                   │ │
│  │ 9. Task.Run → UpsertAsync(serverId, hmacKey) [30s timeout]   │ │
│  │ 10. Configure ServerAuthenticator (TokenSigningKey, ID)       │ │
│  │ 11. Register periodic callback (PulseRate, OnPeriodicPulse)   │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                              │                                      │
│                              ▼                                      │
│  OnPeriodicPulse(deltaTime)  ── every PulseRate seconds ──         │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ Guard: Initialized && Server != null && State == Started      │ │
│  │ Read serverId from ILoginServerRuntimeData                    │ │
│  │ TryEnqueueAsyncWork → PulseAsync(serverId)                   │ │
│  │   └─► ILoginServerService.PulseAsync(serverId)               │ │
│  │       └─► Updates liveness timestamp in DB                   │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                              │                                      │
│                              ▼                                      │
│  OnDeinitialize()                                                   │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ 1. Unregister periodic callback                               │ │
│  │ 2. Zero authenticator signing key (CryptographicOperations)   │ │
│  │ 3. Set TokenSigningKey = null                                 │ │
│  │ 4. Delete signing-key row from DB [5s timeout]                │ │
│  │ 5. Delete login-server row from DB [5s timeout]               │ │
│  │ 6. Reset ILoginServerRuntimeData.ID = 0                      │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## Project Structure

### Directory Tree

```
LoginServer/
├── LoginServerSystem.cs         # Login server lifecycle behaviour (register, key gen, heartbeat)
├── LoginServerRuntimeData.cs    # Runtime container storing login server DB ID
└── README.md                    # This file
```

### Related Core Contracts

```
Server/Core/LoginServer/LoginServer/
├── ILoginServerSystem.cs        # Interface: IServerBehaviour marker for login server system
└── ILoginServerRuntimeData.cs   # Interface: exposes long ID { get; set; }

Server/Core/RuntimeData/
└── IAsyncWorkerData.cs          # Async worker queue for off-thread DB operations
```

### Inheritance Hierarchies

**Behaviour**

```
ServerBehaviour
└── LoginServerSystem : ILoginServerSystem
```

**Runtime Data**

```
RuntimeDataContainer
└── LoginServerRuntimeData : ILoginServerRuntimeData
```

### Runtime Data Dependencies

`LoginServerSystem` declares:

- `[RequiresDataContainer(typeof(LoginServerRuntimeData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

| Container | Responsibility |
|-----------|----------------|
| `LoginServerRuntimeData` | Holds the persistent runtime login-server ID assigned by the database |
| `AsyncWorkerData` | Executes heartbeat pulse tasks without blocking main/update threads |

### Threading Model

| Thread | Work |
|--------|------|
| Main / server update | Initialization, callback registration, pulse scheduling |
| Async workers (`AsyncWorkerData`) | Database heartbeat calls (`PulseAsync`), signing-key persistence |

### External Integration Points

| Dependency | Interface | Role |
|------------|-----------|------|
| Address Provider | `IServerAddressProvider` | Resolves public network address and port for DB registration |
| Configuration | `IServerConfiguration` | Supplies `ServerName` config key |
| Database Service Registry | `ILoginServerService` | Login-server CRUD and heartbeat pulse |
| Database Service Registry | `ILoginServerSigningKeyService` | HMAC signing-key upsert and delete |
| Periodic Update System | `IPeriodicUpdateSystem` | Drives fixed-rate heartbeat scheduling |
| Network Authenticator | `ServerAuthenticator` | Receives HMAC key and login-server ID for token signing |
| Runtime Data Registry | `ILoginServerRuntimeData`, `IAsyncWorkerData` | Server-scoped state containers |

## License

This module is part of the FishMMO project and is subject to the FishMMO project license.
