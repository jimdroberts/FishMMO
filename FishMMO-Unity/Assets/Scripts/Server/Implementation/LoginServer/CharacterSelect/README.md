# CharacterSelect System

## Overview

The CharacterSelect system handles character listing, deletion, and selection on the login server. It keeps the network handler path lightweight by queuing database-heavy work onto `AsyncWorkerData`, then marshals all FishNet responses back onto the Unity main thread through a dedicated main-thread queue container.

It also applies per-connection in-flight gating for select/delete requests and bounded main-thread response draining to reduce DoS pressure and frame spikes.

## Directory Structure

```
CharacterSelect/
├── CharacterSelectSystem.cs                    # Stateless login-server character list/delete/select behaviour
├── CharacterSelectSystemRuntimeData.cs         # Per-connection in-flight gate container
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

### Runtime Data Containers

```
RuntimeDataContainer
├── CharacterSelectSystemRuntimeData
└── MainThreadQueueData (abstract)
    └── SystemMainThreadQueueData (abstract)
        └── CharacterSelectSystemMainThreadQueueData : ICharacterSelectSystemMainThreadQueueData
```

## Runtime Data Container Details

### `CharacterSelectSystemRuntimeData`

Mutable runtime state for the character selection system.

| Property | Type | Purpose |
|----------|------|---------|
| `InFlightRequests` | `ConcurrentDictionary<int, byte>` | Per-connection in-flight gate preventing duplicate concurrent list/select/delete operations |
| `NextAllowedRequestUtc` | `ConcurrentDictionary<int, DateTime>` | Per-connection post-release cooldown timestamp; enforces `RequestCooldownMilliseconds` gap between successive requests |

**Thread Safety:** `ConcurrentDictionary` allows safe access from both network and worker threads.

**Lifecycle:**
- `InitializeOnce()` — creates empty `ConcurrentDictionary`.
- `Clear()` — clears dictionary entries.
- `Deinitialize()` — clears and nulls reference.

### `CharacterSelectSystemMainThreadQueueData`

Per-system main-thread action queue. Inherits from `SystemMainThreadQueueData` (which inherits from `MainThreadQueueData`). Implements `ICharacterSelectSystemMainThreadQueueData`.

Provides `Enqueue(Action)` and `Drain(int)` methods for marshalling async worker responses back to the Unity main thread.

**Why a separate concrete type?** The `DataContainerRegistry` creates independent instances per concrete type, ensuring each system gets its own isolated main-thread queue.

## Runtime Data Dependencies

`CharacterSelectSystem` declares three required containers:

- `[RequiresDataContainer(typeof(CharacterSelectSystemMainThreadQueueData))]`
- `[RequiresDataContainer(typeof(CharacterSelectSystemRuntimeData))]`
- `[RequiresDataContainer(typeof(AsyncWorkerData))]`

| Container | Responsibility |
|-----------|----------------|
| `CharacterSelectSystemRuntimeData` | Per-connection in-flight gate and cooldown for list/select/delete request deduplication |
| `AsyncWorkerData` | Executes database operations in background worker threads |
| `CharacterSelectSystemMainThreadQueueData` | Marshals network-safe response actions to main thread |

## Operational Safeguards

- **Per-connection in-flight gate (`CharacterSelectSystemRuntimeData.InFlightRequests`)**
    - Applied to `CharacterRequestListBroadcast`, `CharacterDeleteBroadcast`, and `CharacterSelectBroadcast`.
    - Prevents a single connection from queueing multiple concurrent list/select/delete operations.
    - Gate is always released in `finally` after async processing.
- **Post-release cooldown (`RequestCooldownMilliseconds`, constant `2000`)**
    - After an in-flight request completes, the connection must wait the configured cooldown before another request is accepted.
    - Tracked via `NextAllowedRequestUtc` in runtime data.
    - Prevents rapid sequential spam after each request completes.
    - Entries are cleaned up on disconnect.
- **Bounded main-thread response draining (`maxMainThreadResponsesPerFrame`)**
    - `OnLateUpdate` drains up to `maxMainThreadResponsesPerFrame` actions each frame.
    - `OnDeinitialize` drains all remaining actions.

## Request Flows

### Character List (`CharacterRequestListBroadcast`)

1. Validate account ownership via `AccountManager` (`connection -> accountName`).
2. Queue async fetch (`ICharacterService.FetchManyAsync(accountName)`).
3. Map `CharacterData` rows to `CharacterDetails`.
4. Enqueue main-thread response action.
5. Send `CharacterListBroadcast`.

### Character Delete (`CharacterDeleteBroadcast`)

1. Validate connection + account binding.
2. Acquire per-connection in-flight gate.
3. Queue async deletion pipeline.
3. Open Unit of Work transaction.
4. Fetch character and verify account ownership.
5. If `KeepDeleteData == false`, delete all sub-entity tables (abilities, achievements, attributes, bank, buffs, equipment, factions, friends, hotkeys, inventory, known abilities, pets).
6. Soft-delete character row via `ICharacterService.DeleteAsync(...)`.
7. Commit transaction.
8. Enqueue main-thread response action.
9. Send `CharacterDeleteBroadcast`.
10. Release in-flight gate in `finally`.

### Character Select (`CharacterSelectBroadcast`)

1. Validate connection + account binding.
2. Acquire per-connection in-flight gate.
3. Queue async select pipeline.
3. Open Unit of Work transaction.
4. Fetch character and verify ownership.
5. Set selected character (`SetSelectedAsync(accountName, characterId)`).
6. Verify selected ownership state for defense-in-depth (`FetchByAccountAsync(accountName, selected: true)`).
7. Commit transaction.
8. Fetch active world servers.
9. Map rows to `WorldServerDetails`.
10. Enqueue main-thread response action.
11. Send `ServerListBroadcast`.
12. Release in-flight gate in `finally`.

## Failure Response Behavior

All request flows guarantee a response to the client, even on failure, to prevent indefinite client hangs:

- **Character List**: On DB service unavailability or fetch failure, an empty `CharacterListBroadcast` is sent.
- **Character Select**: On any failure (service unavailability, UoW failure, ownership mismatch, commit failure, or world server fetch failure), an empty `ServerListBroadcast` is sent.
- **Character Delete**: On failure, no `CharacterDeleteBroadcast` echo is sent — the character stays in the client's list. The in-flight guard releases so the user can retry.

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

`OnLateUpdate` drains queued main-thread actions every frame, capped by `maxMainThreadResponsesPerFrame`.

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