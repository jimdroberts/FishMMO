# Server Implementation

**Short description:** Concrete server-side runtime layer that composes core services, FishNet networking, database access, authentication, and modular server behaviours into running Login, World, and Scene server processes.

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

The `Server` class (`Server.cs`) is the composition root for every FishMMO server process. It inherits `MonoBehaviour` and implements `IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>`, `IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>`, and `IPeriodicUpdateSystem`.

At startup the composition root:

1. Resolves a `NetworkManager` from the scene.
2. Builds `IServerConfiguration`, `IServerEvents`, `ICoreServer`, and `INetworkManagerWrapper`.
3. Fetches the external IP address asynchronously.
4. Once the IP is available, initialises the core server, database orchestrator, address provider, authenticator, account manager, runtime data containers (auto-discovered via `RequiresDataContainerAttribute`), and server behaviours.
5. Starts `VerifyDatabaseSchema` concurrently with behaviour initialization and joins it just before the transport opens: pending migrations abort startup, a check that could not run is only a warning.
6. Disables KCC auto-simulation and starts the FishNet server.

During runtime, `LateUpdate` drives two loops: the behaviour update loop (snapshot-safe against registration mutations) and the periodic callback system (enumeration-complete-before-invoke pattern). Shutdown is idempotent and tears down subsystems in reverse order.

The Implementation layer sits between the abstract `Server.Core` interfaces and the concrete FishNet/Unity runtime. Each server type (Login, World, Scene) is loaded as an Addressable scene; `ServerLauncher` selects which scene(s) to load based on command-line arguments or a configurable boot list.

### Sub-System Organisation

| Sub-System | Description |
|---|---|
| **Account** | Account management strategies — SRP-based (login) and token-based (world/scene). |
| **Authentication** | Server authenticator hierarchy — base, SRP (`ServerAuthenticator`), and token (`TokenServerAuthenticator`), plus `AccountVerificationPolicy` and `SigningKeyKekProvider`. |
| **RuntimeData** | Shared runtime data containers, factory, and registry for mutable per-system state. |
| **Smtp** | `SmtpService` (`ISmtpService`) — outbound mail for account verification/recovery, configured from server config with `FISHMMO_SMTP_*` environment overrides. |
| **KickRequest** | `KickRequestSystem` — cross-server kick propagation. Lives at the root of `Implementation/`, not under `World/`. |
| **LoginServer** | Login-server-specific systems: account creation, character create/select, server select, the login server lifecycle, and `LoginQueueSystem` (FIFO admission queue when auth capacity is saturated). |
| **World** | World and scene server systems organised into `WorldServer/` and `SceneServer/`. |

### Scene-Server System Inventory

Everything under `World/SceneServer/`. All are `ServerBehaviour` ScriptableObject assets except
the two noted otherwise. Each has its own README with the detail; this table is the index.

| System | Type | Notes |
|---|---|---|
| Scene server lifecycle | `SceneServerSystem` (+ `.AdminCommands`, `.ServerControl` partials) | Scene load/unload, instance placement via `SceneServerPlacementPolicy`. |
| Character | `CharacterSystem` (+ `.Connection`, `.Loading`, `.Saving`, `.Social`, `.Instance`, `.CombatLogout` partials) | Spawn/despawn, persistence, combat-logout. |
| Character inventory | `CharacterInventorySystem` | Inventory/equipment/bank/bag operations and the exchange funnel. |
| Interactable | `InteractableSystem` (+ 11 partials) | Merchants, containers, dialogue, mailbox, corpses, ability crafting, waypoints — **and arenas and the group/dungeon finders** (see below). |
| Chat | `ChatSystem` (+ `.LocalChat`, `.WorldChat`, `.TellChat`, `.GroupChat`, `.ArenaChat` partials) | |
| Guild | `GuildSystem` (+ `.Authority`, `.Ranks`, `.Recruitment` partials) | Rank ladder, authority checks, invites/applications. |
| Party | `PartySystem` | Membership, plus `PartyCombatMeterData` server-side meters. |
| Friend | `FriendSystem` | |
| Trade | `TradeSystem` (+ `.Handlers`, `.Commit` partials) | Player-to-player trade; commits through `ICharacterInventorySystem.TryRunExchange`, which enqueues `CharacterInventorySystem.RunExchangeAsync`. |
| Housing | `HousingSystem` (+ `.Access`, `.Building`, `.Network`, `.Plots`, `.Sync`, `.Tax`, `.Vault` partials) | **Server-only, and off by default** (see below). |
| Quest | `QuestSystem` | |
| Achievement | `AchievementSystem` | |
| Pet | `PetSystem` | |
| Hotkey | `HotkeySystem` | |
| Naming | `NamingSystem` | Name resolution/caching for IDs the client has not seen. |
| Scene channel | `SceneChannelSystem` | |
| Waypoint | `WaypointPersistence`, `WaypointSceneAudit` (static helpers, not behaviours) | Discovered-waypoint pages, OR-merged; the audit compares live `WaypointRegistry` indices against baked `WorldSceneDetails.Waypoints` once per loaded scene. |
| Scene-server auth | `SceneServerAuthenticator` | Not a behaviour — a `TokenServerAuthenticator` subclass attached to the `NetworkManager`. |

**Arenas and the group/dungeon finder are not separate systems.** They are partials of
`InteractableSystem`: `InteractableSystem.Arena.cs`, `InteractableSystem.ArenaMatch.cs`,
`InteractableSystem.GroupFinder.cs` and `InteractableSystem.DungeonFinder.cs`. There is no
`ArenaSystem` or `GroupFinderSystem` type. Arenas do have a client UI (`Client/GUI/World/Arena/`).

**Housing ships disabled and has no client UI.** `HousingSystem.ownershipMode` defaults to
`HousingOwnershipMode.Neither`, so `IsHousingEnabled` is false and `InitializeOnce` returns
early — a stock server runs no housing at all. There is no housing UI anywhere under
`Assets/Scripts/Client/`; the system is server-side plumbing (plots, building, taxes, vaults,
access lists) with no way for a player to reach it yet.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | Full support including console title via `SetConsoleTitle` (kernel32) |
| Linux | Yes | Process name set via `prctl` (libc.so.6) |
| macOS | Yes | Process title set via `setproctitle` (libc.dylib) |
| WebGL | N/A | Server-only — not applicable |

| Requirement | Version |
|---|---|
| Unity | 6.3 LTS |
| Scripting Backend | IL2CPP |
| FishNet | Runtime dependency |

## Features

- **Composition-root architecture** — `Server` wires core services, networking, database, authentication, behaviours, and data containers in a single orchestrated startup.
- **Modular server behaviours** — `ServerBehaviour` (ScriptableObject-based) provides a plug-in lifecycle: `InitializeOnce` / `InitializeOnceAsync`, `OnUpdate`, `OnDeinitialize`.
- **Non-blocking startup** — behaviours are initialized one at a time through `InitializeAllAsync`, awaited from a coroutine in `Server`, and the transport starts from the completion callback. Failed initialization retries with exponential backoff and then exits the process rather than leaving a live server that never binds its port.
- **Auto-discovered runtime data containers** — behaviours declare `[RequiresDataContainer(typeof(T))]`; the server discovers, deduplicates, priority-sorts, and creates containers automatically.
- **Generic component registries** — `ServerComponentRegistry<TNet, TConn, TComponent>` registers components under concrete type and all `IServerComponent`-derived interfaces for dependency lookup.
- **Network abstraction** — `INetworkManagerWrapper` / `FishNetNetworkWrapper` decouple the server from FishNet internals, exposing start/stop, transport config, broadcast registration, and authenticator attachment.
- **Per-connection broadcast budget** — `FishNetNetworkWrapper.RegisterBroadcast<T>` wraps every handler in a token-bucket admission check (`AdmitBroadcast`) before dispatch. FishNet drains all pending packets each frame on the main thread and has no per-connection message-rate limit of its own, so this is the transport-agnostic backstop: `BroadcastMaxMessagesPerSecond` (default 100) with `BroadcastMessageBurst` (default 200); a connection that keeps sending past an empty bucket is kicked with `KickReason.ExploitExcessiveData` after 200 overflows. The wrapper delegate is remembered in `broadcastWrappers` so `UnregisterBroadcast` hands FishNet the same instance it registered.
- **Transport-level inbound limits** — WebTransport gets `SetInboundRateLimit(TransportMaxInboundMessagesPerSecond, TransportInboundMessageBurst)` enforced on the socket receive thread, and `SetNativeLimits(...)` for connection-level caps applied inside the native library before the QUIC handshake (`TransportConnectIntervalMs`, `TransportMaxConnectionsPerIP`, `TransportMaxHalfOpenConnections`, `TransportMaxQueuedDatagramsPerConnection`, `TransportMaxH3StreamsPerConnection`; `-1` keeps the library default, `0` disables). Applied to both the standalone and Multipass-child transports.
- **Startup schema verification** — `VerifyDatabaseSchema` runs concurrently with behaviour initialization and joins just before the transport opens. Pending migrations are fatal; a check that could not run at all is only a warning (unverified, not known-bad). It verifies *applied migrations only* — an entity changed with no migration generated for it leaves nothing pending and passes (issue #162).
- **Login admission queue** — `LoginQueueSystem` holds a FIFO of connections arriving at auth capacity (`ArrivalOrderTracker<NetworkConnection>`), keeps them connected at the QUIC layer, and pushes `LoginQueuePositionBroadcast` on a server-controlled interval. Update rate and admission rate are server-authoritative (`LoginQueueUpdateRateSeconds`, `LoginQueueAdmissionRatePerSecond`); clients cannot ask for faster updates.
- **Bounded sync-over-async, shutdown only** — `UnitySyncOverAsync` is the one sanctioned way to block on a `Task` from `OnDestroy`/`OnApplicationQuit`, where nothing can yield. Startup must not use it: any `await` in the call chain that captures Unity's `SynchronizationContext` can never resume while the main thread sits in `GetResult()`, and the transport would never bind.
- **Periodic callback system** — `IPeriodicUpdateSystem` with register/unregister/update-interval; enumeration-safe dispatch; callbacks receive their registered interval, not frame delta.
- **Main-thread queue helper** — generic `MainThreadQueueHelper.Drain<T>` / `TryEnqueue<T>` for marshalling async work back to Unity's main thread.
- **Address resolution** — `ServerAddressProvider` resolves IPv4/IPv6 bind addresses from the transport layer with optional overrides.
- **Physics ticker** — `PhysicsTicker` hooks FishNet's `OnPrePhysicsSimulation` to manually advance a scene's `PhysicsScene`.
- **Server launcher** — `ServerLauncher` loads Addressable scenes by command-line argument (`LOGIN`, `WORLD`, `SCENE`) or a configurable boot list.
- **Window title updater** — `ServerWindowTitleUpdater` periodically sets the OS process/console title with transport type, connection state, and client count (Windows, Linux, macOS).
- **Dual account manager strategy** — `SrpAccountManager` for SRP-authenticated login servers; `TokenAccountManager` for token-authenticated world/scene servers.
- **Dual authenticator strategy** — `ServerAuthenticator` (SRP, login server) and `TokenServerAuthenticator` selected at startup based on scene type; the world and scene servers use its subclasses `WorldServerAuthenticator` and `SceneServerAuthenticator`.
- **Idempotent shutdown** — `PerformShutdown` runs once via `hasShutdown` flag, cleaning up behaviours, containers, authenticator workers, network, database, and core server in reverse order.
- **KCC integration** — `KinematicCharacterSystem.AutoSimulation` set to `false` for deterministic server-driven simulation.
- **Snapshot-safe behaviour dispatch** — behaviour list is snapshotted before `OnLateUpdate` dispatch to prevent `InvalidOperationException` if behaviours register or unregister during update.
- **Cached delegate names** — `PeriodicCallbackData.CallbackName` caches the reflection-derived display name at construction time; no runtime reflection in any log path.

## Prerequisites

- Unity 6.3 LTS with IL2CPP scripting backend.
- FishNet networking framework (imported via Plugins).
- KinematicCharacterController package.
- PostgreSQL database (configured via `appsettings.json`).
- ZString for zero-allocation string formatting.
- Addressable Assets for scene and template loading.
- `FishMMO.Server.Core` and `FishMMO.Shared` assemblies.

## Installation / Build

This is an integrated module within the FishMMO Unity project. No separate installation is required.

1. Open the FishMMO-Unity project in Unity 6.3 LTS.
2. Ensure all dependencies (FishNet, KCC, ZString, Addressables) are imported.
3. Configure `appsettings.json` for database connection strings.
4. Build server executables via Unity Build Settings with the desired server scenes.
5. Alternatively, enter Play Mode with the `ServerLauncher` bootstrap scene to run locally.

## Quick Start Guides

### Running in the Editor

1. Open the bootstrap scene containing `ServerLauncher`.
2. Ensure the `BootList` field on `ServerLauncher` includes the desired server scenes (default: `LoginServer`, `WorldServer`, `SceneServer`).
3. Enter Play Mode. The launcher loads scenes as Addressables and each `Server` MonoBehaviour self-initialises.

### Running a Standalone Build

```bash
# Launch all servers (default boot list)
./FishMMO-Server

# Launch a specific server type
./FishMMO-Server LOGIN
./FishMMO-Server WORLD
./FishMMO-Server SCENE
```

The second command-line argument selects the server type. If no argument is provided, all scenes in the boot list are loaded.

### Adding a New Server Behaviour

1. Create a class extending `ServerBehaviour`.
2. Implement `InitializeOnce()`, `OnDeinitialize()`, and optionally `OnUpdate(float deltaTime)`.
   - If initialization needs I/O (database registration, for example), override
     `InitializeOnceAsync(CancellationToken)` instead and `await` the work. Never block the
     Unity main thread: it is the thread that drains async continuations, so blocking it can
     deadlock startup before the transport binds. Overrides run on the main thread and, because
     they await without `ConfigureAwait(false)`, resume on it — Unity APIs stay safe to use
     after an await.
3. If the behaviour needs mutable runtime state, create a `RuntimeDataContainer` subclass and annotate the behaviour with `[RequiresDataContainer(typeof(YourData))]`.
4. Create a ScriptableObject asset for the behaviour and add it to the `Server` component's `serverBehaviours` list.

## Configuration

| Setting | Source | Description |
|---|---|---|
| `AddressOverride` | `Server` inspector field | Optional bind address override |
| `PortOverride` | `Server` inspector field | Optional bind port override |
| `BootList` | `ServerLauncher` inspector field | Array of scene names to load at startup |
| `updateRate` | `ServerWindowTitleUpdater` inspector field | Window title refresh interval (seconds, default 15) |
| Database connection | `appsettings.json` | PostgreSQL connection string |
| Environment | `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT` | Selects `appsettings.{env}.json` overlay |
| Transport settings | `IServerConfiguration`, backed by `LoginServer.cfg` / `WorldServer.cfg` / `SceneServer.cfg` | Bind address, port, max clients applied via `ApplyTransportConfiguration` |
| `EnableIPv6` / `IPv6Address` | Server `.cfg` | Dual-stack bind on the same port |
| `BroadcastMaxMessagesPerSecond` / `BroadcastMessageBurst` | Server `.cfg` | Transport-agnostic per-connection broadcast budget (defaults 100 / 200). `0` disables |
| `TransportMaxInboundMessagesPerSecond` / `TransportInboundMessageBurst` | Server `.cfg` | WebTransport receive-thread budget (defaults 200 / 400) |
| `TransportConnectIntervalMs`, `TransportMaxConnectionsPerIP`, `TransportMaxHalfOpenConnections`, `TransportMaxQueuedDatagramsPerConnection`, `TransportMaxH3StreamsPerConnection` | Server `.cfg` | Native WebTransport connection limits applied pre-handshake. `-1` = library default, `0` = disabled |
| `LoginQueueUpdateRateSeconds` / `LoginQueueAdmissionRatePerSecond` | Server `.cfg` | `LoginQueueSystem` position-broadcast interval and admission smoothing |
| `FISHMMO_SMTP_HOST`, `FISHMMO_SMTP_PORT`, … | Environment | Override the configured SMTP settings for container/orchestration deployments |

## Usage Examples

### Registering a Periodic Callback

```csharp
// Inside a ServerBehaviour's InitializeOnce:
Server.RegisterPeriodicCallback(5.0f, OnHeartbeat);

private void OnHeartbeat(float interval)
{
    // interval == 5.0f (the registered period, not frame deltaTime)
    Database.SendHeartbeat();
}

// In OnDeinitialize:
Server.UnregisterPeriodicCallback(OnHeartbeat);
```

### Enqueuing Main-Thread Work from an Async Worker

```csharp
MainThreadQueueHelper.TryEnqueue<MySystemMainThreadQueueData>(
    Server,
    () => ProcessResult(result));
```

### Draining a Main-Thread Queue in OnUpdate

```csharp
public override void OnUpdate(float deltaTime)
{
    MainThreadQueueHelper.Drain<MySystemMainThreadQueueData>(Server, maxActions: 10, drainAll: false);
}
```

### Looking Up a Behaviour or Data Container

```csharp
if (Server.BehaviourRegistry.TryGet<IMyBehaviour>(out var behaviour))
{
    behaviour.DoWork();
}

if (Server.DataContainerRegistry.TryGet<MyRuntimeData>(out var data))
{
    data.Counter++;
}
```

## Operational Checks

| Check | Method | Expected Result |
|---|---|---|
| Server starts | Enter Play Mode with `ServerLauncher` | Log: `"Server is starting..."` followed by `"Initialization Complete"` |
| External IP resolved | Startup sequence | No exception at `OnFinalizeSetup` |
| Database connected | Startup sequence | Log: `"Initializing Database with Environment: ..."` |
| Login server initialised | `ServerEvents.OnLoginServerInitialized` fires | Log: `"LoginServer initialized."` |
| World server initialised | `ServerEvents.OnWorldServerInitialized` fires | Log: `"WorldServer initialized."` |
| Scene server initialised | `ServerEvents.OnSceneServerInitialized` fires | Log: `"SceneServer initialized."` |
| Behaviours initialised | `BehaviourRegistry.InitializeAll` | No `"failed to initialize"` warnings |
| Data containers created | `DiscoverAndCreateDataContainers` | Log: `"Auto-created RuntimeDataContainer: ..."` for each type |
| Schema verified | `VerifyDatabaseSchema` join before transport start | No pending-migration abort; at worst a warning that the check could not run |
| Network listening | `ServerManager_OnServerConnectionState` | Log: `"Local: ... Remote: ... - Started"` |
| Broadcast budget active | Flood a connection past `BroadcastMaxMessagesPerSecond` | Log: `"... exceeded the broadcast budget ..."` then a `KickReason.ExploitExcessiveData` kick |
| Waypoint authoring drift | Scene-server scene load | `WaypointSceneAudit` logs any live-only / baked-only waypoint indices; agreement logs nothing to fix |
| Window title updated | 15-second cycle (default) | OS process/console title reflects server status |
| Graceful shutdown | Stop Play Mode or `Ctrl+C` | All subsystems deinitialised in reverse order, no errors |
| Periodic callbacks fire | Register a callback with known interval | Callback invoked on schedule with correct interval argument |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart TD
    Main[Server entry] --> Boot[Bootstrap]
    Boot --> Auth[Authentication]
    Boot --> Acct[Account]
    Boot --> RT[RuntimeData]
    Boot --> Kick[KickRequest]
    Boot --> Login[LoginServer subsystems]
    Boot --> World[WorldServer subsystems]
    Boot --> Scene[SceneServer subsystems]
    Auth --> DB[(PostgreSQL)]
    Acct --> DB
    RT --> Cache[Runtime registries]
```

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ServerLauncher                               │
│  (Bootstrap: parse CLI args → load Addressable server scenes)       │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ loads scene(s)
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     Server (MonoBehaviour)                           │
│                     Composition Root                                │
│                                                                     │
│  Start()                                                            │
│   ├─ Resolve NetworkManager                                         │
│   ├─ Create Configuration, ServerEvents, CoreServer                 │
│   ├─ Create FishNetNetworkWrapper                                   │
│   └─ FetchExternalIPAddress → OnFinalizeSetup(remoteAddress)        │
│                                                                     │
│  OnFinalizeSetup()                                                  │
│   ├─ CoreServer.Initialize(remoteAddress, sceneName)                │
│   ├─ Build Database (Npgsql from appsettings.json)                 │
│   ├─ Create ServerAddressProvider                                   │
│   ├─ Apply transport config + attach authenticator                  │
│   ├─ Create AccountManager (SRP or Token based on authenticator)    │
│   ├─ Discover + create RuntimeDataContainers (priority-sorted)      │
│   ├─ Register + initialise data containers                          │
│   ├─ Register + initialise ServerBehaviours                         │
│   ├─ Join VerifyDatabaseSchema (pending migrations = fatal)         │
│   ├─ KCC AutoSimulation = false                                     │
│   └─ NetworkWrapper.StartServer()                                   │
│                                                                     │
│  LateUpdate()                                                       │
│   ├─ UpdateServerBehaviours(deltaTime)   ← snapshot-safe dispatch   │
│   └─ UpdatePeriodicCallbacks(deltaTime)  ← enum-then-invoke         │
│                                                                     │
│  PerformShutdown() [idempotent]                                     │
│   ├─ Clear periodic callbacks                                       │
│   ├─ Deinitialise + unregister behaviours (reverse order)           │
│   ├─ Deinitialise + unregister data containers (reverse order)      │
│   ├─ Shutdown authenticator workers                                 │
│   ├─ Stop network server                                            │
│   ├─ Shutdown database                                              │
│   ├─ Deinitialise core server + clear account manager               │
│   └─ Unsubscribe event handlers                                     │
└─────────────────────────────────────────────────────────────────────┘
          │                    │                     │
          ▼                    ▼                     ▼
   ┌──────────────┐  ┌────────────────┐  ┌──────────────────────┐
   │ LoginServer  │  │  WorldServer   │  │     SceneServer      │
   │  Scene       │  │   Scene        │  │      Scene           │
   │              │  │                │  │                      │
   │ Systems:     │  │ Systems:       │  │ Systems:             │
   │ · Login      │  │ · WorldServer  │  │ · SceneServer        │
   │ · LoginQueue │  │ · WorldScene   │  │ · Character          │
   │ · AcctCreate │  │ · Auth         │  │ · CharacterInventory │
   │ · CharCreate │  │                │  │ · Interactable       │
   │ · CharSelect │  └────────────────┘  │    (+ Arena,         │
   │ · ServerSel  │                      │     Group/Dungeon    │
   └──────────────┘                      │     Finder partials) │
                                         │ · Chat / Guild       │
   KickRequestSystem polls the database  │ · Party / Friend     │
   for kick rows and disconnects the     │ · Trade              │
   matching accounts. It lives at        │ · Quest / Achievement│
   Implementation/KickRequest/.          │ · Pet / Hotkey       │
                                         │ · Naming             │
                                         │ · SceneChannel       │
                                         │ · Housing (disabled) │
                                         └──────────────────────┘
```

## Project Structure

```
Implementation/
├── README.md                                    # This document
├── Server.cs                                    # Composition root and lifecycle coordinator
├── ServerBehaviour.cs                           # Base class for server-side behaviours (ScriptableObject)
├── ServerBehaviourRegistry.cs                   # Behaviour registration/initialisation orchestration
├── ServerComponentRegistry.cs                   # Generic component registry base class
├── FishNetNetworkWrapper.cs                     # FishNet adapter + broadcast budget + transport limits
├── INetworkManagerWrapper.cs                    # Network abstraction interface
├── MainThreadQueueHelper.cs                     # Static helper for main-thread queue drain/enqueue
├── UnitySyncOverAsync.cs                        # Bounded, deadlock-safe blocking wait (shutdown paths only)
├── ServerAddressProvider.cs                     # Local/public server address resolution
├── PeriodicCallbackData.cs                      # Periodic callback timing state with cached name
├── PhysicsTicker.cs                             # Physics tick integration via FishNet TimeManager
├── ServerLauncher.cs                            # Bootstrap: CLI args -> Addressable scene loading
├── ServerWindowTitleUpdater.cs                  # OS-native window/process title updater
├── ServerWindowTitleUpdaterRuntimeData.cs       # Runtime state for window title updater
│
├── Account/                                     # Account management strategies
│   ├── AccountManager.cs                        #   Base/interface for account managers
│   ├── SrpAccountManager.cs                     #   SRP-based account manager (login server)
│   └── TokenAccountManager.cs                   #   Token-based account manager (world/scene)
│
├── Authentication/                              # Server authenticator hierarchy
│   ├── IServerAuthenticator.cs                  #   Authenticator interface
│   ├── BaseServerAuthenticator.cs               #   Shared authenticator base class
│   ├── ServerAuthenticator.cs                   #   SRP authenticator (login server)
│   ├── TokenServerAuthenticator.cs              #   Token authenticator (world/scene)
│   ├── AccountVerificationPolicy.cs             #   Whether an unverified account may log in
│   └── SigningKeyKekProvider.cs                 #   Key-encryption-key source for token signing keys
│
├── Smtp/                                        # Outbound mail
│   └── SmtpService.cs                           #   ISmtpService over System.Net.Mail
│
├── RuntimeData/                                 # Shared runtime data container framework
│   ├── RuntimeDataContainer.cs                  #   Base class for runtime data containers
│   ├── RuntimeDataContainerFactory.cs           #   Factory for creating containers by type
│   ├── RuntimeDataContainerRegistry.cs          #   Registry for container lifecycle management
│   ├── AsyncWorkerData.cs                       #   Base for async worker thread data
│   ├── MainThreadQueueData.cs                   #   Base for main-thread queue data containers
│   └── SystemMainThreadQueueData.cs             #   System-level main-thread queue data
│
├── KickRequest/                                 # Cross-server kick request handling
│   ├── KickRequestSystem.cs
│   ├── KickRequestSystemMainThreadQueueData.cs
│   └── KickRequestSystemQueueData.cs
│
├── LoginServer/                                 # Login-server-specific systems
│   ├── LoginQueueSystem.cs                      #   FIFO admission queue at auth capacity
│   ├── AccountCreation/                         #   Account creation workflow
│   │   ├── AccountCreationSystem.cs
│   │   ├── AccountCreationSystemMainThreadQueueData.cs
│   │   ├── AccountCreationSystemMappingData.cs
│   │   ├── AccountCreationSystemRuntimeData.cs
│   │   └── IAccountCreationPuzzleProvider.cs
│   ├── CharacterCreate/                         #   Character creation workflow
│   ├── CharacterSelect/                         #   Character selection workflow
│   ├── LoginServer/                             #   Login server lifecycle
│   └── ServerSelect/                            #   Server selection workflow
│
└── World/                                       # World and scene server systems
    ├── SceneServer/                             #   Scene-server-specific systems
    │   ├── Achievement/                         #     AchievementSystem
    │   ├── Authentication/                      #     SceneServerAuthenticator
    │   ├── Character/                           #     CharacterSystem + 6 partials
    │   ├── CharacterInventory/                  #     CharacterInventorySystem, ItemContainerMapping
    │   ├── Chat/                                #     ChatSystem + Local/World/Tell/Group/Arena partials
    │   ├── Friend/                              #     FriendSystem
    │   ├── Guild/                               #     GuildSystem + Authority/Ranks/Recruitment, GuildAuthority
    │   ├── Hotkey/                              #     HotkeySystem
    │   ├── Housing/                             #     HousingSystem + 7 partials (off by default, no client UI)
    │   ├── Interactable/                        #     InteractableSystem + 11 partials
    │   │                                        #       (Merchant, Container, Dialogue, Mailbox, Corpse,
    │   │                                        #        AbilityCraft, Waypoint, Arena, ArenaMatch,
    │   │                                        #        GroupFinder, DungeonFinder)
    │   ├── Naming/                              #     NamingSystem
    │   ├── Party/                               #     PartySystem, PartyCombatMeterData
    │   ├── Pet/                                 #     PetSystem
    │   ├── Quest/                               #     QuestSystem
    │   ├── SceneChannel/                        #     SceneChannelSystem
    │   ├── Trade/                               #     TradeSystem + Handlers/Commit, TradeSession, TradeExchange
    │   ├── Waypoint/                            #     WaypointPersistence, WaypointSceneAudit (static helpers)
    │   └── SceneServer/                         #     Scene server lifecycle
    │       ├── SceneServerSystem.cs
    │       ├── SceneServerSystem.AdminCommands.cs
    │       ├── SceneServerSystem.ServerControl.cs
    │       ├── SceneServerPlacementPolicy.cs
    │       ├── SceneServerRuntimeData.cs
    │       ├── SceneServerSystemMainThreadQueueData.cs
    │       ├── SceneInstanceDetails.cs
    │       └── SceneInstanceMappingData.cs
    └── WorldServer/                             #   World-server-specific systems
        ├── Authentication/                      #     WorldServerAuthenticator.cs
        ├── WorldScene/                          #     World scene management
        │   ├── WorldSceneSystem.cs
        │   ├── WorldSceneSystemRuntimeData.cs
        │   ├── WorldSceneSystemMainThreadQueueData.cs
        │   └── WorldSceneMappingData.cs
        └── WorldServer/                         #     World server lifecycle
            ├── WorldServerSystem.cs
            └── WorldServerSystemRuntimeData.cs
```

### Inheritance Hierarchy

```
MonoBehaviour
└── Server : IServer<...IServerBehaviour>, IServer<...IRuntimeDataContainer>, IPeriodicUpdateSystem

ScriptableObject
└── ServerBehaviour : IServerBehaviour<INetworkManagerWrapper, ServerManager, NetworkConnection, IServerBehaviour>
    ├── LoginServerSystem
    ├── LoginQueueSystem
    ├── AccountCreationSystem
    ├── CharacterCreateSystem
    ├── CharacterSelectSystem
    ├── ServerSelectSystem
    ├── WorldServerSystem
    ├── WorldSceneSystem
    ├── SceneServerSystem            (partial: .AdminCommands, .ServerControl)
    ├── KickRequestSystem
    ├── ServerWindowTitleUpdater
    └── (scene-server systems: CharacterSystem, CharacterInventorySystem, InteractableSystem,
         ChatSystem, GuildSystem, PartySystem, FriendSystem, TradeSystem, HousingSystem,
         QuestSystem, AchievementSystem, PetSystem, HotkeySystem, NamingSystem,
         SceneChannelSystem — several of them `partial`)

RuntimeDataContainer : IRuntimeDataContainer
├── LoginServerRuntimeData
├── AccountCreationSystemRuntimeData / ...MappingData
├── CharacterCreateSystemRuntimeData
├── CharacterSelectSystemRuntimeData
├── ServerSelectSystemRuntimeData
├── WorldServerSystemRuntimeData
├── WorldSceneSystemRuntimeData
├── SceneServerRuntimeData
├── ServerWindowTitleUpdaterRuntimeData
├── KickRequestSystemQueueData
├── PartyCombatMeterData
├── CharacterSystemRuntimeData / CharacterMappingData
├── GuildSystemRuntimeData / GuildCharacterMappingData
├── PartySystemRuntimeData / PartyCharacterMappingData
├── NamingSystemRuntimeData / NamingSystemMappingData
├── SceneInstanceMappingData / WorldSceneMappingData
└── *SystemMainThreadQueueData variants

INetworkManagerWrapper
└── FishNetNetworkWrapper

IAccountManager<NetworkConnection>
├── SrpAccountManager
└── TokenAccountManager

IServerAuthenticator
└── BaseServerAuthenticator
    ├── ServerAuthenticator        (SRP — login server)
    └── TokenServerAuthenticator   (token — world/scene servers)
        ├── WorldServerAuthenticator
        └── SceneServerAuthenticator

ServerComponentRegistry<TNet, TConn, TComponent>
├── ServerBehaviourRegistry  : IServerBehaviourRegistry<...>
└── RuntimeDataContainerRegistry

Static helpers (no base type)
├── WaypointPersistence     — dirty-page collection + OR merge, shared by unlock and save paths
├── WaypointSceneAudit      — live vs. baked waypoint index comparison
├── ItemContainerMapping    — the one cast between InventoryType and ItemContainerType
│                            (pinned numerically identical by ItemContainerTypeParityTests)
├── SceneServerPlacementPolicy
├── MainThreadQueueHelper
└── UnitySyncOverAsync
```

## License

This module is part of the FishMMO project and is distributed under the FishMMO project license. See the repository root for full license terms.
