# Server Implementation

## Overview

The `Server` class is the runtime composition root for FishMMO server processes. It wires together core services, network transport/authentication, runtime data containers, server behaviours, periodic callbacks, and shutdown orchestration.

At startup, it constructs and initializes the server stack, then starts FishNet listening. During runtime, it drives behaviour updates and interval-based callbacks. During shutdown, it deinitializes and unregisters subsystems in reverse order and releases network/database resources.

## Directory Structure

```text
Implementation/
├── README.md                         # This document
├── Server.cs                         # Composition root and lifecycle coordinator
├── ServerBehaviour.cs                # Base class for implementation-side server behaviours
├── ServerBehaviourRegistry.cs        # Behaviour registration/initialization orchestration
├── ServerComponentRegistry.cs        # Runtime-data-container registration orchestration
├── FishNetNetworkWrapper.cs          # FishNet adapter implementing network wrapper interface
├── INetworkManagerWrapper.cs         # Network abstraction used by core/implementation
├── MainThreadQueueHelper.cs          # Queues work onto the main Unity thread
├── ServerAddressProvider.cs          # Local/public server address resolution
├── PeriodicCallbackData.cs           # Periodic callback timing state
├── PhysicsTicker.cs                  # Physics tick integration
├── ServerLauncher.cs                 # Server bootstrap helper
├── ServerWindowTitleUpdater.cs       # Window title status updater
├── ServerWindowTitleUpdaterRuntimeData.cs # Runtime state for window title updater
├── RuntimeData/                      # Shared runtime data containers
├── Account/                          # Account/auth related implementation pieces
├── Authentication/                   # Authenticator and auth workflow integration
├── LoginServer/                      # Login-server-specific systems
└── World/                            # World/scene server systems
```

## Class and Interface Relationships

`Server` inherits and implements:

```text
MonoBehaviour
└── Server
    ├── IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>
    ├── IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>
    └── IPeriodicUpdateSystem
```

## Startup Flow

1. `Start()`
   - Resolves `NetworkManager`.
   - Builds `Configuration`, `ServerEvents`, `CoreServer`, and `NetworkWrapper`.
   - Subscribes initialization log handlers.
   - Fetches external IP asynchronously.
2. `OnFinalizeSetup(remoteAddress)`
   - Initializes core server addressing.
   - Creates database orchestrator.
   - Builds `AddressProvider`.
   - Applies transport settings and authenticator wiring.
   - Creates account manager.
   - Discovers, registers, and initializes runtime data containers.
   - Registers and initializes server behaviours.
   - Configures KCC simulation mode.
   - Starts network server.

## Runtime Update Model

### Behaviour Updates

`LateUpdate()` calls `UpdateServerBehaviours(deltaTime)` which snapshots `serverBehaviours` into a reusable scratch list before dispatch. This prevents `InvalidOperationException` if a behaviour's `OnLateUpdate` registers or unregisters behaviours. Only initialized, non-null behaviours are invoked.

### Periodic Callbacks

`Server` exposes `IPeriodicUpdateSystem`:
- `RegisterPeriodicCallback(interval, callback)`
- `UnregisterPeriodicCallback(callback)`
- `UpdateCallbackInterval(callback, newInterval)`

Callbacks are stored in a dictionary. Each frame, `UpdatePeriodicCallbacks` decrements timers and collects ready callbacks into a reusable scratch list. The `foreach` over the dictionary completes before any callback is invoked, so callbacks are free to call `Register`/`Unregister` during dispatch without dictionary mutation during enumeration.

Callbacks receive `data.Interval` (not frame `deltaTime`) so the argument is always the registered period. After invocation, `TimeRemaining` resets to the full interval — no catch-up loop, which is appropriate for DB heartbeats and server-tick work.

`PeriodicCallbackData` caches a `CallbackName` string at construction time so log messages never pay repeated reflection cost on `Callback.Method`. All log paths use the cached name exclusively.

## Connection State Handling

`ServerManager_OnServerConnectionState(...)` maps FishNet connection states to internal `ConnectionState` via `MapConnectionState(...)` and logs local/remote bind details when available.

## Data Container Discovery

`DiscoverAndCreateDataContainers()` scans `ServerBehaviour` instances for `RequiresDataContainerAttribute`:
- clears `dataContainers` first to prevent accumulation if Unity domain reload is disabled,
- deduplicates container types,
- validates constructability through `RuntimeDataContainerFactory`,
- groups by initialization priority,
- creates and stores containers in priority order,
- uses `is RuntimeDataContainer` pattern-match instead of a direct cast for factory return safety.

## Shutdown Flow

`OnDestroy()` and `OnApplicationQuit()` both call `PerformShutdown()` (idempotent via `hasShutdown`).

Shutdown sequence:
1. Clear periodic callbacks.
2. Deinitialize and unregister behaviours (reverse order).
3. Deinitialize and unregister runtime data containers (reverse order).
4. Shutdown authenticator workers.
5. Stop network server.
6. Shutdown database.
7. Deinitialize core server and clear account manager.
8. Unsubscribe server event handlers and connection-state handler.

## Key Dependencies

- **FishNet**: transport, connection states, authenticator integration.
- **Core server layer**: role initialization and lifecycle events.
- **Database orchestrator**: service registry and graceful shutdown.
- **Runtime registries**: behaviour and data-container orchestration.
- **KCC**: deterministic simulation setup (`AutoSimulation = false`).

## Reliability Notes

- Startup fails fast if critical dependencies are missing (`NetworkManager`, remote IP).
- Event handler callbacks use cached delegates to support reliable unsubscribe.
- Cleanup is guarded for null-safe shutdown and only executed once.
- Registry unregistration runs independently from `Initialized` flags to avoid skip-on-shutdown edge cases.
- Periodic callback enumeration completes before invocation, so callbacks can safely register/unregister.
- Behaviour dispatch snapshots the list to avoid mutation during `OnLateUpdate`.
- Periodic callbacks receive `data.Interval`, not frame `deltaTime`, so the argument is always meaningful.
- `PeriodicCallbackData.CallbackName` caches the reflection-derived display name at construction time; no runtime reflection in any log path.
- `RegisterPeriodicCallback` uses `TryGetValue` instead of `ContainsKey` + double-indexer to save a hash lookup.
- Container factory results use `is` pattern-match instead of direct cast for future-safe type checking.
- `dataContainers.Clear()` at start of discovery prevents accumulation when Unity domain reload is disabled.
- `ServerBehaviourRegistry` implements `IServerBehaviourRegistry` directly, enabling clean assignment without `as` cast.