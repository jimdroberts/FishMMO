# Naming System

## Overview

The Naming system is the SceneServer lookup service for resolving IDs to names and names back to IDs. It handles client requests for character/guild naming, checks local runtime mappings first, and falls back to asynchronous database lookups when data is not locally available.

The subsystem uses a split execution model:
- Main thread: request validation, cache checks, and network broadcasts.
- Async worker: database lookup operations.
- Main-thread queue: marshaling async lookup results back to safe broadcast context.

## Directory Structure

```text
Naming/
├── NamingSystem.cs                 # Naming/reverse-naming handlers, cache checks, async DB lookup orchestration
├── NamingSystemMappingData.cs      # Character name ↔ ID mapping data container
├── NamingSystemRuntimeData.cs      # Runtime state container
├── NamingSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md                       # System documentation
```

## Core Contracts

Implementation target:
- `INamingSystem<NetworkConnection>`

Supporting runtime contract:
- `INamingSystemMainThreadQueueData`

## Lifecycle

### InitializeOnce()
1. Validates server dependency.
2. Validates required main-thread queue data container.
3. Registers network handlers:
   - `NamingBroadcast`
   - `ReverseNamingBroadcast`

### OnDeinitialize()
1. Drains pending main-thread queue actions.
2. Unregisters naming handlers.

### OnLateUpdate()
- Drains queued main-thread actions each frame.

## Forward Naming Flow (ID -> Name)

Handled by `OnServerNamingBroadcastReceived(...)`.

### Character name
1. Check local `ICharacterMappingData<NetworkConnection>.CharactersByID`.
2. If found, reply immediately.
3. If not found, queue async DB lookup (`FetchCharacterNameAsync(...)`).

### Guild name
1. Queue async DB lookup (`FetchGuildNameAsync(...)`).
2. Reply on completion if a valid name is found.

## Reverse Naming Flow (Name -> ID)

Handled by `OnServerReverseNamingBroadcastReceived(...)`.

### Character name
1. Normalize input to lowercase invariant.
2. Check local `CharactersByLowerCaseName` mapping.
3. If not found, queue async DB lookup (`FetchCharacterByNameAsync(...)`).
4. Send not-found payload (`id = 0`, empty name) when resolution fails.

### Guild name
- Not currently implemented in reverse path.

## Main-Thread Queue

`NamingSystemMainThreadQueueData` provides per-system queue isolation.

Usage:
- Async database tasks enqueue completion actions with `EnqueueMainThread(...)`.
- Queue drains in `OnLateUpdate()` and deinitialize.

This ensures all broadcasts execute on main thread.

## Async Worker and Backpressure

All DB lookups are queued via `TryEnqueueAsyncWork(...)`:
- returns `true` when accepted
- returns `false` when queue unavailable/full
- logs warning when rejected

This prevents unbounded fire-and-forget task growth under load.

## Database Dependencies

Primary services:
- `ICharacterService`
- `IGuildService`

Local cache dependency:
- `ICharacterMappingData<NetworkConnection>`

## Failure Semantics

- Null/invalid requests return early with no exceptions.
- Missing services abort lookup safely.
- Failed lookups produce explicit not-found response in reverse character path.
- Broadcasts are skipped when connection is no longer active.

These semantics provide stable, deterministic naming behavior under partial failure.