# CharacterSelect System

## Overview

The CharacterSelect system handles character listing, deletion, and selection on the login server. It keeps the network handler path lightweight by queuing database-heavy work onto `AsyncWorkerData`, then marshals all FishNet responses back onto the Unity main thread through a dedicated main-thread queue container.

## Directory Structure

```
CharacterSelect/
├── CharacterSelectSystem.cs                    # Stateless login-server character list/delete/select behaviour
├── CharacterSelectSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

Related core contracts:

- `Server/Core/LoginServer/CharacterSelect/ICharacterSelectSystem.cs`
- `Server/Core/LoginServer/CharacterSelect/ICharacterSelectSystemMainThreadQueueData.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── CharacterSelectSystem : ICharacterSelectSystem
```

### Main-Thread Queue Data

```
RuntimeDataContainer
└── MainThreadQueueData (abstract)
    └── CharacterSelectSystemMainThreadQueueData : ICharacterSelectSystemMainThreadQueueData
```

## Runtime Data Dependencies

`CharacterSelectSystem` declares two required containers:

- `[RequiresDataContainer(typeof(CharacterSelectSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

| Container | Responsibility |
|-----------|----------------|
| `AsyncWorkerData` | Executes database operations in background worker threads |
| `CharacterSelectSystemMainThreadQueueData` | Marshals network-safe response actions to main thread |

## Request Flows

### Character List (`CharacterRequestListBroadcast`)

1. Validate account ownership via `AccountManager` (`connection -> accountName`).
2. Queue async fetch (`ICharacterService.FetchManyAsync(accountName)`).
3. Map `CharacterData` rows to `CharacterDetails`.
4. Enqueue main-thread response action.
5. Send `CharacterListBroadcast`.

### Character Delete (`CharacterDeleteBroadcast`)

1. Validate connection + account binding.
2. Queue async deletion pipeline.
3. Open Unit of Work transaction.
4. Fetch character and verify account ownership.
5. If `KeepDeleteData == false`, delete all sub-entity tables (abilities, achievements, attributes, bank, buffs, equipment, factions, friends, hotkeys, inventory, known abilities, pets).
6. Soft-delete character row via `ICharacterService.DeleteAsync(...)`.
7. Commit transaction.
8. Enqueue main-thread response action.
9. Send `CharacterDeleteBroadcast`.

### Character Select (`CharacterSelectBroadcast`)

1. Validate connection + account binding.
2. Queue async select pipeline.
3. Open Unit of Work transaction.
4. Fetch character and verify ownership.
5. Set selected character (`SetSelectedAsync(accountName, characterId)`).
6. Commit transaction.
7. Fetch active world servers.
8. Map rows to `WorldServerDetails`.
9. Enqueue main-thread response action.
10. Send `ServerListBroadcast`.

## Transaction Boundaries

Both delete and select operations run under `IUnitOfWorkService` to preserve consistency:

- Begin transaction
- Validate ownership + mutate data
- Commit

If any stage fails, no success message is sent and logs are emitted with failure context.

## Backpressure and Queueing

Async work dispatch uses `TryEnqueueAsyncWork(...)` and returns `bool`:

- `true`: request accepted by async queue.
- `false`: request rejected under pressure or missing queue dependency.

Rejected enqueue attempts are logged with account context to aid operational monitoring.

## Threading Model

| Thread | Work |
|--------|------|
| Network/Main | Broadcast receive, fast validation, enqueue async work |
| Async Workers | Database fetch/delete/select operations |
| Main Thread | FishNet `Broadcast` and `Kick` operations via queued actions |

`OnLateUpdate` drains queued main-thread actions every frame.

## Deletion Retention Policy

`KeepDeleteData` controls whether sub-entity character records are preserved when deleting a character:

- `true`: keep sub-entity rows (character row still soft-deleted).
- `false`: remove sub-entity rows before character delete.

This allows operational choice between retention/auditing and full cleanup.

## External Integration Points

- **AccountManager**: validates connection ownership (`GetAccountNameByConnection`).
- **Database Service Registry**: resolves all character/world/unit-of-work services.
- **CharacterService**: fetch list, fetch by name, set selected, delete character.
- **WorldServerService**: supplies active world server list after successful selection.
- **AsyncWorkerData**: centralized background task queue.
- **CharacterSelectSystemMainThreadQueueData**: guarantees main-thread-safe network dispatch.