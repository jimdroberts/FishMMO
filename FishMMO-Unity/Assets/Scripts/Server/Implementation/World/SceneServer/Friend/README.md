# Friend System

## Overview

The Friend system is the SceneServer social subsystem for friend list management. It handles add/remove friend requests from connected players, validates constraints (self-add, max capacity), persists friend relationships asynchronously, and relays successful updates back to the owning client.

The design separates responsibilities across execution contexts:
- Main thread: request validation, in-memory controller updates, and network broadcasts.
- Async worker: database fetch/persist/delete operations.
- Main-thread queue: marshaling async completion actions back to Unity/FishNet-safe context.

## Directory Structure

```text
Friend/
├── FriendSystem.cs                  # Friend add/remove orchestration and async persistence dispatch
├── FriendSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md                        # System documentation
```

## Core Contracts

Implemented interfaces:
- `IFriendSystem`
- `IFriendSystemMainThreadQueueData`

FriendSystem runtime dependencies:
- `ICharacterSystem<NetworkConnection, Scene>`
- `IFriendSystemMainThreadQueueData`
- `IAsyncWorkerData`

Database service dependencies:
- `ICharacterService`
- `ICharacterFriendService`

## Lifecycle

### InitializeOnce()
1. Validates server dependency.
2. Validates required queue data container.
3. Validates required character system dependency.
4. Registers network handlers:
   - `FriendAddNewBroadcast`
   - `FriendRemoveBroadcast`

### OnDeinitialize()
1. Drains pending main-thread actions.
2. Unregisters friend network handlers.

### OnLateUpdate()
- Drains queued main-thread actions each frame.

## Add Friend Flow

Request handler: `OnServerFriendAddNewBroadcastReceived(...)`

Validation path:
1. Connection and spawned object exist.
2. `IFriendController` exists.
3. Current count is below `MaxFriends`.
4. Database services are available.
5. Request is not self-friending.

Async path (`AddFriendAsync(...)`):
1. Resolve target character from database.
2. Persist friend relationship in database.
3. Enqueue main-thread completion action.
4. Revalidate connection/controller state.
5. Recheck max capacity and duplicate status.
6. Add friend to in-memory controller.
7. Broadcast `FriendAddBroadcast` with online state.

## Remove Friend Flow

Request handler: `OnServerFriendRemoveBroadcastReceived(...)`

Processing path:
1. Validate connection/object/controller.
2. Confirm friend exists in in-memory set.
3. Remove friend from in-memory state immediately.
4. Broadcast `FriendRemoveBroadcast` to requester.
5. Enqueue async delete persistence (`DeleteFriendAsync(...)`).

## Async Worker and Backpressure

`TryEnqueueAsyncWork(...)` dispatches all async DB work through `IAsyncWorkerData`.

Behavior:
- Returns `true` when accepted.
- Returns `false` when queue is unavailable or full.
- Logs warnings on rejection/unavailability.
- Uses `entityKey = characterID` for per-character ordering.

This prevents unbounded fire-and-forget tasks and preserves operation order per player.

## Main-Thread Queue

`FriendSystemMainThreadQueueData` provides a dedicated queue container for this subsystem.

Usage:
- Async tasks enqueue completion actions via `EnqueueMainThread(...)`.
- Queue is drained in `OnLateUpdate()` and during deinitialize.

This ensures network broadcasts and controller mutations execute in main-thread-safe context.

## Failure Semantics

- Invalid requests are ignored without state mutation.
- DB/service lookup failures abort async operations safely.
- Queue rejection/unavailability is logged and work is skipped.
- Broadcasts occur only after successful in-memory state changes.

These semantics prioritize server stability and deterministic in-memory social state.