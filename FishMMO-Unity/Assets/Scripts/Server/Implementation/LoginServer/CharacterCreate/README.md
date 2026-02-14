# CharacterCreate System

## Overview

The CharacterCreate system handles login-server character creation with strict main-thread safety for Unity API access and asynchronous persistence for database work. It validates requests (name, account, race, spawn), builds initial character state (attributes, factions, abilities, inventory, equipment), writes everything transactionally via Unit of Work, and marshals all network responses back to the main thread.

## Directory Structure

```
CharacterCreate/
├── CharacterCreateSystem.cs                    # Stateless character creation behaviour + async persistence pipeline
├── CharacterCreateSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

Related core contracts:

- `Server/Core/LoginServer/CharacterCreate/ICharacterCreateSystem.cs`
- `Server/Core/LoginServer/CharacterCreate/ICharacterCreateSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`

## Inheritance Hierarchies

### System Behaviour

```
ServerBehaviour
└── CharacterCreateSystem : ICharacterCreateSystem
```

### Main-Thread Queue Data

```
RuntimeDataContainer
└── MainThreadQueueData (abstract)
    └── CharacterCreateSystemMainThreadQueueData : ICharacterCreateSystemMainThreadQueueData
```

## Runtime Data Dependencies

`CharacterCreateSystem` declares runtime container dependencies via attributes:

- `[RequiresDataContainer(typeof(CharacterCreateSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

### Why both containers are required

| Container | Responsibility |
|-----------|----------------|
| `AsyncWorkerData` | Runs DB-heavy work off the network/main thread with bounded backpressure-aware queuing |
| `CharacterCreateSystemMainThreadQueueData` | Marshals thread-unsafe FishNet/Unity operations back to the main thread |

## Request Pipeline

### 1) Network Thread Gate (`OnServerCharacterCreateBroadcastReceived`)

Fast validation only:

1. Verify connection is active.
2. Validate character name (`Constants.Authentication.IsAllowedCharacterName`).
3. Resolve account name from `AccountManager`.
4. Resolve required DB services from `ServiceRegistry`.
5. Perform Unity-thread-only checks:
   - `RaceTemplate.Get(...)`
   - prefab/component validation
   - spawnable prefab validation
6. Enqueue async worker task through `IAsyncWorkerData`.

If enqueue fails (queue pressure), request is rejected immediately with `CharacterCreateResult.Error`.

### 2) Async Worker Processing (`ProcessCharacterCreateAsync`)

Background thread work:

1. Validate world/spawn details from immutable cache.
2. Validate allowed race against selected spawn.
3. Enforce account character count limit (`MaxCharacters`).
4. Construct all DTOs:
   - `CharacterData`
   - `CharacterFactionData`
   - `CharacterAbilityData`
   - `CharacterInventoryData`
   - `CharacterEquipmentData`
   - `CharacterAttributeData`
5. Begin unit of work.
6. Create character row and persist all sub-entities.
7. Commit transaction.

### 3) Main Thread Response Marshalling

All client responses are queued via `EnqueueMainThread(...)` and executed in `OnLateUpdate` via `DrainMainThreadQueue()`.

This guarantees thread-safe calls to:

- `Server.NetworkWrapper.Broadcast(...)`
- `conn.Kick(...)`

## Transactional Consistency

Character creation uses `IUnitOfWorkService`:

1. `BeginAsync()` starts transaction scope.
2. Character row is created first to obtain real `characterID`.
3. All dependent rows are created with that `characterID`.
4. `CommitAsync()` finalizes atomically.

If commit fails, the client receives `CharacterCreateResult.Error` and no partial success is reported.

## Starting Data Composition

`CharacterCreateSystem` combines global starting templates with race-specific templates:

- Factions: from `raceTemplate.InitialFaction`
- Abilities: `StartingAbilities` + `raceTemplate.StartingAbilities`
- Inventory: `StartingInventoryItems` + `raceTemplate.StartingInventoryItems`
- Equipment: `StartingEquipment` + `raceTemplate.StartingEquipment`
- Attributes: from `raceTemplate.InitialAttributes`

Equipment seed generation uses `ItemGenerator` so item-derived stats can be deterministically reconstructed on load.

## Validation and Result Mapping

### Validation gates

- Invalid character name -> `CharacterCreateResult.InvalidCharacterName`
- Invalid account/session binding -> disconnect/kick
- Invalid race/model/prefab/spawn mismatch -> invalid spawn or kick
- Character cap reached -> `CharacterCreateResult.TooMany`
- DB uniqueness/validation failures -> mapped client result

### Database error mapping

| Database Error | Client Result |
|----------------|---------------|
| `AlreadyExists` | `CharacterNameTaken` |
| `ValidationError` | `InvalidCharacterName` |
| Other/unknown | `Error` |

## Main Thread Safety Rules

The system explicitly separates thread-sensitive and thread-safe operations:

- **Main thread only**: Unity object lookups, prefab/component checks, FishNet broadcast/kick calls.
- **Worker thread**: DTO building, collection composition, database calls, transaction handling.

This prevents frame stalls while preserving correctness for Unity/FishNet APIs.

## External Integration Points

- **Account Manager** (`IAccountManager`) — validates connection-to-account ownership.
- **Database Services** (`ICharacterService`, `ICharacterFactionService`, `ICharacterAbilityService`, `ICharacterInventoryService`, `ICharacterEquipmentService`, `ICharacterAttributeService`) — persists character graph.
- **Unit of Work** (`IUnitOfWorkService`) — transactional integrity.
- **World Scene Details** (`WorldSceneDetailsCache`) — validates initial spawn location.
- **Race Templates** (`RaceTemplate`) — validates race/model and supplies starter data.
- **AsyncWorkerData** — background execution with backpressure.
- **MainThreadQueueData** — safe main-thread response dispatch.