# Party System

## Overview

The Party system is the SceneServer social subsystem for party lifecycle and member synchronization. It handles party creation, invitations, invitation accept/decline, leaving, member removal, rank transfer, periodic membership reconciliation, and party chat invite commands.

The implementation uses a split execution model:
- Main thread: request validation, in-memory controller/tracker updates, and network broadcasts.
- Async worker: database reads/writes and party update marker persistence.
- Main-thread queue: marshaling async completion actions back to Unity/FishNet-safe context.

## Directory Structure

```text
Party/
├── PartySystem.cs                  # Core party orchestration, handlers, async persistence, and update pump
├── PartySystemRuntimeData.cs       # Pending invitation map and last update fetch cursor
├── PartySystemMainThreadQueueData.cs # Per-system main-thread action queue container
├── PartyCharacterMappingData.cs    # Party online/cached membership trackers
└── README.md                       # System documentation
```

## Core Contracts

Implementation targets:
- `IPartySystem<NetworkConnection>`
- `IPartySystemRuntimeData`
- `IPartySystemMainThreadQueueData`
- `IPartyCharacterMappingData`

Primary runtime settings:
- `MaxPartySize`
- `UpdatePumpRate`

## Runtime Data Containers

### `PartySystemRuntimeData`
Stores:
- `PendingInvitations` (`TargetCharacterID -> PartyID`)
- `LastFetchTime` for periodic party-update polling cursor

### `PartyCharacterMappingData`
Stores:
- `PartyCharacterTracker` (online/local characters by party)
- `PartyMemberTracker` (latest known members by party from update fetches)

### `PartySystemMainThreadQueueData`
Dedicated queue for actions that must execute on main thread after async DB work.

## Initialization and Lifecycle

`InitializeOnce()`:
1. Validates required server/data/behavior dependencies.
2. Registers party chat commands (`/pi`, `/invite`).
3. Registers party network handlers.
4. Subscribes to character connect/disconnect callbacks.
5. Registers periodic update callback.

`OnDeinitialize()`:
1. Drains queued main-thread actions.
2. Unregisters network handlers.
3. Unsubscribes character callbacks.
4. Unregisters periodic callback.

`OnLateUpdate()` drains queued main-thread actions each frame.

## Tracking and Synchronization

`AddPartyCharacterTracker(...)` and `RemovePartyCharacterTracker(...)` maintain local online party membership.

When a party no longer has local online members, both online tracker and cached member tracker entries are removed to avoid stale memory.

`FetchAndProcessPartyUpdatesAsync()` periodically:
1. Fetches party updates by tracked IDs and `LastFetchTime`.
2. Fetches current members for updated parties.
3. Marshals to main thread:
   - updates `LastFetchTime`
   - computes removed members and sends leave broadcasts
   - refreshes cached member sets
   - broadcasts `PartyAddMultipleBroadcast` snapshots to local online members

## Command and Broadcast Flows

### Chat invite command
- `/pi` and `/invite` resolve target by lowercase name and route to invite flow.

### Create party
- Validates requester not already in a party.
- Async creates party + persists leader membership.
- Main-thread applies controller/tracker state and sends create broadcast.

### Invite / Accept / Decline
- Invite validates inviter leadership and capacity.
- Pending invitations store target -> party mapping.
- Accept validates pending invite, persists membership, triggers update marker, and applies local member state.
- Decline removes pending invite entry.

### Leave / Remove / Rank change
- Leave immediately updates requester local state, broadcasts leave, then async handles leadership transfer/member delete/party delete-or-update.
- Remove validates leader permissions, asynchronously removes target, updates trackers, and triggers update marker.
- Rank change swaps leader/member roles in DB and triggers update marker.

## Database Dependencies

Primary services:
- `IPartyService`
- `ICharacterPartyService`
- `IPartyUpdateService`

All party mutations emit update marker persistence so other servers can reconcile party lists.

## Async Worker and Backpressure

All async DB work is dispatched through `TryEnqueueAsyncWork(...)`:
- returns `true` when accepted
- returns `false` when queue unavailable/full
- logs warnings on rejection/unavailability
- supports entity-keyed ordering for per-character sequencing

This prevents unbounded fire-and-forget task growth and improves operational stability under load.

## Failure Semantics

- Invalid requests fail closed with no mutation.
- Permission/capacity checks are enforced before persistence.
- Async failures are logged and do not block main thread.
- Main-thread completion paths revalidate runtime state before mutating or broadcasting.