# Guild System

## Overview

The Guild system is the SceneServer social subsystem for guild lifecycle and membership management. It handles guild creation, invitations, invite acceptance/decline, member leave/remove flows, rank changes, and periodic guild membership synchronization.

The system separates execution contexts:
- Main thread: validation, in-memory controller state changes, tracker updates, and network broadcasts.
- Async worker: database reads/writes and cross-server guild update markers.
- Main-thread queue: marshaling async completion actions back to Unity/FishNet-safe execution.

## Directory Structure

```text
Guild/
├── GuildSystem.cs                 # Core guild orchestration, handlers, async persistence, and sync pump
├── GuildSystemRuntimeData.cs      # Pending invitation map and last guild-update fetch timestamp
├── GuildSystemMainThreadQueueData.cs # Per-system main-thread action queue container
├── GuildCharacterMappingData.cs   # Guild-to-membership tracking for online/local and known members
└── README.md                      # System documentation
```

## Core Contracts

Implementation targets:
- `IGuildSystem<NetworkConnection>`
- `IGuildSystemRuntimeData`
- `IGuildSystemMainThreadQueueData`
- `IGuildCharacterMappingData`

Primary runtime properties:
- `MaxGuildSize`
- `MaxGuildNameLength`
- `UpdatePumpRate`

## Runtime Data Containers

### `GuildSystemRuntimeData`
Stores:
- `PendingInvitations` (`TargetCharacterID -> GuildID`)
- `LastFetchTime` for guild update polling cursor

### `GuildCharacterMappingData`
Stores:
- `GuildCharacterTracker` (online/local characters by guild)
- `GuildMemberTracker` (latest known members by guild from update fetches)

### `GuildSystemMainThreadQueueData`
Per-system queue for actions that must execute on main thread after async DB operations.

## Initialization and Lifecycle

`InitializeOnce()`:
1. Validates required server/data/behavior dependencies.
2. Registers guild chat commands (`/gi`, `/ginvite`).
3. Registers guild network handlers.
4. Subscribes to character connect/disconnect hooks.
5. Registers periodic guild update callback.

`OnDeinitialize()`:
1. Drains pending main-thread queue actions.
2. Unregisters guild handlers.
3. Unsubscribes character hooks.
4. Unregisters periodic callback.

`OnLateUpdate()` drains queued main-thread actions every frame.

## Guild Tracking Model

`AddGuildCharacterTracker(guildID, characterID)` and `RemoveGuildCharacterTracker(...)` maintain per-guild online membership for this SceneServer.

When a guild no longer has local members, both local tracker and cached member tracker entries are removed to prevent stale memory growth.

## Periodic Synchronization Pump

`OnPeriodicUpdate(...)` dispatches async fetch work when server is started.

`FetchAndProcessGuildUpdatesAsync()`:
1. Reads tracked guild IDs + last fetch time.
2. Fetches guild update rows from database.
3. For each updated guild, fetches current member rows.
4. Marshals to main thread:
   - updates `LastFetchTime`
   - computes removed members and sends leave broadcasts where applicable
   - refreshes cached member tracker
   - broadcasts full guild member snapshots (`GuildAddMultipleBroadcast`) to local online members

## Command and Broadcast Flows

### Chat command
- `/gi` and `/ginvite` resolve target by lowercase name and route to invite broadcast flow.

### Create guild
- Validates name rules and non-membership.
- Async checks name uniqueness and creates guild.
- Persists creator as leader.
- Main-thread updates controller/tracker and sends success broadcast.

### Invite / Accept / Decline
- Invite validates inviter permissions and guild capacity.
- Pending invitation stores target -> guild mapping.
- Accept validates pending invite, persists membership, pushes guild-update marker, updates local state.
- Decline clears pending invitation.

### Leave / Remove / Rank change
- Leave immediately updates in-memory requester state and broadcasts leave, then async handles leader transfer, member delete, and guild delete/update marker.
- Remove validates requester rank and target rank constraints, then async deletes target and triggers guild update marker.
- Rank change is leader-only and updates rank asynchronously, then triggers update marker.

## Database Dependencies

Primary services:
- `IGuildService`
- `ICharacterGuildService`
- `IGuildUpdateService`

All mutating flows trigger guild-update persistence when needed so other servers can reconcile member lists.

## Async Worker and Backpressure

All async DB tasks are queued through `TryEnqueueAsyncWork(...)`:
- returns `true` when accepted
- returns `false` when queue unavailable/full
- logs warning on rejection
- supports entity-keyed ordering for per-character/per-guild sequencing

This prevents unbounded fire-and-forget task growth and preserves operation ordering.

## Failure Semantics

- Invalid requests are ignored safely.
- Permission/rank/guild-capacity checks fail closed.
- Async failures are logged and do not block main thread.
- Main-thread completion paths revalidate connection/object/controller state before mutating or broadcasting.
