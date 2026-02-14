# RuntimeData Container System

## Overview

The RuntimeData Container system provides a clean separation of concerns between server logic and mutable runtime state in FishMMO. `ServerBehaviour` ScriptableObjects handle business logic, event subscriptions, and configuration, while `RuntimeDataContainer` classes hold all mutable runtime data (dictionaries, trackers, timestamps). Containers are automatically discovered via the `[RequiresDataContainer]` attribute, deduplicated, priority-ordered, and registered in a global `DataContainerRegistry` for type-safe access.

## Directory Structure

```
Server/
├── Core/RuntimeData/
│   ├── IRuntimeDataContainer.cs              # Marker + generic data container interface
│   ├── IRuntimeDataContainerFactory.cs       # Factory interface for container creation
│   ├── IRuntimeDataContainerRegistry.cs      # Registry interface for container management
│   └── RequiresDataContainerAttribute.cs     # Attribute for declaring container dependencies
│
└── Implementation/RuntimeData/
    ├── RuntimeDataContainer.cs               # Abstract base class for all containers
    ├── RuntimeDataContainerFactory.cs        # Reflection-based container factory
    └── RuntimeDataContainerRegistry.cs       # Concrete registry with lifecycle management
```

Concrete per-system containers (e.g., `PartyRuntimeData`, `CharacterMappingData`) live alongside their respective system implementations under `Server/Implementation/World/` and `Server/Implementation/LoginServer/`.

## Separation of Concerns

### ServerBehaviour (Logic)

- `ScriptableObject`-based, added to the server via Unity Inspector.
- Contains immutable configuration (e.g., `MaxPartySize`, `SaveRate`).
- Implements business logic, validation, and algorithms.
- Handles network broadcasts, events, and callbacks.
- **Stateless** — all mutable state lives in containers.

### RuntimeDataContainer (Data)

- Non-serializable runtime classes created via reflection.
- Contains mutable runtime state (dictionaries, trackers, timestamps).
- **No business logic** — pure data storage.
- Automatically created via attribute-based discovery.
- Registered with `DataContainerRegistry` for global access.

### Why This Separation?

| Benefit            | Description                                                             |
|--------------------|-------------------------------------------------------------------------|
| **Testability**    | Mock data containers without mocking entire systems                     |
| **Reusability**    | Multiple behaviours can share the same data container                   |
| **Clarity**        | Clear ownership — behaviours own logic, containers own state            |
| **Thread Safety**  | Data containers can be locked independently                             |
| **Serialization**  | ScriptableObjects (logic) can be serialized; runtime data cannot        |
| **Hot Reload**     | Unity can reload ScriptableObjects without losing runtime state         |

## Inheritance Hierarchies

### Core Interfaces

```
IServerComponent
└── IRuntimeDataContainer (marker)
    └── IRuntimeDataContainer<TNetworkManager, TServerManager, TConnection, TDataContainer>
            • Initialize(server, serverManager) → ServerComponentInitializationStatus
            • Clear()

IServerComponentRegistry<TNetworkManager, TConnection, TDataContainer>
└── IRuntimeDataContainerRegistry<TNetworkManager, TConnection, TDataContainer>

IRuntimeDataContainerFactory
    • CreateContainer(Type) → IRuntimeDataContainer
    • IsValidContainerType(Type) → bool
```

### Concrete Implementations

```
RuntimeDataContainer (abstract)
    : IRuntimeDataContainer<INetworkManagerWrapper, ServerManager, NetworkConnection, IRuntimeDataContainer>
    │
    ├── PartyRuntimeData
    ├── GuildRuntimeData
    ├── ChatRuntimeData
    ├── CharacterMappingData
    ├── SceneServerRuntimeData
    ├── WorldServerRuntimeData
    └── ... (per-system containers)

ServerComponentRegistry<...>
└── RuntimeDataContainerRegistry
        : IServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>

RuntimeDataContainerFactory
    : IRuntimeDataContainerFactory
```

## Automatic Container Discovery

### RequiresDataContainer Attribute

Declare container dependencies directly on `ServerBehaviour` classes:

```csharp
[RequiresDataContainer(typeof(PartyRuntimeData))]
public class PartySystem : ServerBehaviour
{
    public override ServerComponentInitializationStatus InitializeOnce()
    {
        // Container is guaranteed to exist and be initialized
        if (!Server.DataContainerRegistry.TryGet<IPartyRuntimeData>(out var data))
            return ServerComponentInitializationStatus.FailedToGetDataContainer;

        // Use the container...
        return ServerComponentInitializationStatus.Initialized;
    }
}
```

### Deduplication

Multiple systems can require the same container — only one instance is created:

```csharp
[RequiresDataContainer(typeof(CharacterMappingData))]
public class CharacterSystem : ServerBehaviour { }

[RequiresDataContainer(typeof(CharacterMappingData))]  // Same container
public class PartySystem : ServerBehaviour { }

[RequiresDataContainer(typeof(CharacterMappingData))]  // Same container
public class FriendSystem : ServerBehaviour { }

// Result: Only ONE CharacterMappingData instance is created
```

### Priority Ordering

Handle container initialization dependencies:

```csharp
[RequiresDataContainer(typeof(CharacterMappingData), InitializationPriority = 0)]
[RequiresDataContainer(typeof(CharacterInventoryData), InitializationPriority = 10)]
public class CharacterInventorySystem : ServerBehaviour { }

// CharacterMappingData initialized first (priority 0)
// CharacterInventoryData initialized second (priority 10)
```

## Container Lifecycle

### RuntimeDataContainer Base Class

All concrete containers extend `RuntimeDataContainer`, which provides:

| Member              | Type / Signature                               | Description                                      |
|---------------------|-------------------------------------------------|--------------------------------------------------|
| `Initialized`       | `bool`                                          | Whether the container has been initialized       |
| `Server`            | `IServer<...>`                                  | Reference to the server instance                 |
| `ServerManager`     | `ServerManager`                                 | Reference to FishNet's server manager            |
| `Initialize()`      | `→ ServerComponentInitializationStatus`         | Sets references, calls `InitializeOnce()`        |
| `InitializeOnce()`  | `abstract`                                      | One-time initialization (override in subclass)   |
| `Clear()`           | `abstract`                                      | Resets all mutable state                         |
| `Deinitialize()`    | `abstract`                                      | Cleanup on shutdown                              |

### RuntimeDataContainerFactory

Creates container instances via reflection. Validates that the type is:
- Non-null, non-abstract, not an interface.
- Assignable to `IRuntimeDataContainer`.
- Has a public parameterless constructor.

### RuntimeDataContainerRegistry

Manages all container instances. Provides:
- `Register<T>()` / `Unregister<T>()` — Add/remove containers by interface type.
- `TryGet<T>(out T)` / `Get<T>()` — Type-safe container lookup.
- `InitializeAll(server)` — Initializes all registered containers with the server instance.
- `DeinitializeAll()` — Clears and deinitializes all containers, then empties the registry.

## Initialization Order

```
Server.Start()
    │
    └── OnFinalizeSetup(remoteAddress)
            │
            ├── DataContainerRegistry = new RuntimeDataContainerRegistry()
            │
            ├── DiscoverAndCreateDataContainers()
            │   ├── Scan all ServerBehaviours for [RequiresDataContainer] attributes
            │   ├── Create container instances via RuntimeDataContainerFactory
            │   ├── Deduplicate by Type
            │   └── Order by InitializationPriority
            │
            ├── RegisterAllDataContainers()
            │   └── Register each container in DataContainerRegistry
            │
            ├── DataContainerRegistry.InitializeAll(this)
            │   └── Call container.Initialize() → container.InitializeOnce()
            │       for each container (marks as Initialized)
            │
            ├── BehaviourRegistry.InitializeAll(this)
            │   └── Call behaviour.InitializeOnce() for each behaviour
            │       (behaviours can now access initialized containers)
            │
            └── NetworkWrapper.StartServer()
                └── Server Running ✓
```

## Example: Party System

### 1. Core Interface (Engine-Agnostic)

```csharp
public interface IPartyRuntimeData : IRuntimeDataContainer
{
    Dictionary<long, HashSet<long>> PartyMemberTracker { get; }
    Dictionary<long, HashSet<long>> PartyCharacterTracker { get; }
    Dictionary<long, long> PendingInvitations { get; }
    DateTime LastFetchTime { get; set; }
}
```

### 2. Implementation (Concrete Data)

```csharp
public class PartyRuntimeData : RuntimeDataContainer, IPartyRuntimeData
{
    private readonly Dictionary<long, HashSet<long>> partyMemberTracker = new();
    private readonly Dictionary<long, HashSet<long>> partyCharacterTracker = new();
    private readonly Dictionary<long, long> pendingInvitations = new();

    public Dictionary<long, HashSet<long>> PartyMemberTracker => partyMemberTracker;
    public Dictionary<long, HashSet<long>> PartyCharacterTracker => partyCharacterTracker;
    public Dictionary<long, long> PendingInvitations => pendingInvitations;
    public DateTime LastFetchTime { get; set; }

    public override ServerComponentInitializationStatus InitializeOnce()
    {
        LastFetchTime = DateTime.UtcNow;
        return ServerComponentInitializationStatus.Initialized;
    }

    public override void Clear()
    {
        partyMemberTracker.Clear();
        partyCharacterTracker.Clear();
        pendingInvitations.Clear();
        LastFetchTime = DateTime.UtcNow;
    }

    public override void Deinitialize() => Clear();
}
```

### 3. System Logic (Behaviour)

```csharp
[CreateAssetMenu(fileName = "PartySystem", menuName = "FishMMO/Server/SceneServer/Party System")]
[RequiresDataContainer(typeof(PartyRuntimeData))]
public class PartySystem : ServerBehaviour, IPartySystem<NetworkConnection>
{
    // Immutable configuration — no mutable state here
    public int MaxPartySize = 6;
    public float UpdatePumpRate = 1.0f;

    public override ServerComponentInitializationStatus InitializeOnce()
    {
        if (!Server.DataContainerRegistry.TryGet<IPartyRuntimeData>(out var runtimeData))
            return ServerComponentInitializationStatus.FailedToGetDataContainer;

        Server.NetworkWrapper.RegisterBroadcast<PartyCreateBroadcast>(
            OnServerPartyCreateBroadcastReceived, true);

        return ServerComponentInitializationStatus.Initialized;
    }

    public void OnServerPartyCreateBroadcastReceived(
        NetworkConnection conn, PartyCreateBroadcast msg, Channel channel)
    {
        if (!Server.DataContainerRegistry.TryGet<IPartyRuntimeData>(out var runtimeData))
            return;

        if (ValidatePartyCreation(conn))
        {
            long partyId = CreatePartyInDatabase();
            runtimeData.PartyMemberTracker[partyId] = new HashSet<long> { characterId };
            runtimeData.PartyCharacterTracker[partyId] = new HashSet<long> { characterId };
        }
    }
}
```

## Best Practices

### ServerBehaviour — DO

- Store configuration values (rates, limits, thresholds).
- Implement business logic and validation.
- Subscribe to events and broadcasts.
- Access containers via `Server.DataContainerRegistry.TryGet<T>()`.
- Keep stateless — no mutable collections.

### RuntimeDataContainer — DO

- Store all mutable runtime state.
- Provide read-only public interfaces for collections.
- Implement `Clear()` to reset state.
- Use proper collection types (`Dictionary`, `HashSet`, `Queue`, etc.).

### ServerBehaviour — DON'T

- Don't store mutable collections (`Dictionary`, `List`, `HashSet`, `Queue`).
- Don't cache data references in fields.
- Don't implement data storage logic.
- Don't create containers manually.

### RuntimeDataContainer — DON'T

- Don't implement business logic.
- Don't subscribe to events or broadcasts.
- Don't call other systems directly.
- Don't make it a ScriptableObject.

## External Integration Points

- **Server** — Owns the `DataContainerRegistry`, drives discovery, creation, and initialization during startup.
- **ServerBehaviour** — All server systems declare container dependencies via `[RequiresDataContainer]` and access them through the registry.
- **IServerComponent / IServerComponentRegistry** — The container system extends the unified server component architecture.
- **FishNet** — `RuntimeDataContainer` receives `ServerManager` during initialization for network access.
- **Database Layer** — Containers often store data fetched from or destined for the database (e.g., `LastFetchTime` for periodic DB sync).