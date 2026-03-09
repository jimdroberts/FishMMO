# Naming System

**Short description:** SceneServer lookup service for resolving entity IDs to names and names back to IDs, handling character and guild naming requests with local cache checks, per-connection debounce, and asynchronous database fallback.

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

The Naming system is the SceneServer lookup service for resolving IDs to names and names back to IDs. It handles client requests for character/guild naming, checks local runtime mappings and TTL caches first, and falls back to asynchronous database lookups when data is not locally available.

The subsystem uses a split execution model:
- **Main thread:** request validation, debounce checks, cache lookups, and network broadcasts.
- **Async worker:** database lookup operations via `TryEnqueueAsyncWork`.
- **Main-thread queue:** marshaling async lookup results back to safe broadcast context via `INamingSystemMainThreadQueueData`.

All database lookups are deduplicated with concurrent in-flight tracking dictionaries (`CharacterNameByIdInFlight`, `GuildNameByIdInFlight`, `CharacterByNameInFlight`) capped at `MaxInFlightLookups` (5 000). TTL-based caches for resolved names, IDs, and negative (missing) results are swept periodically to bound memory. Per-connection request debounce prevents rapid-fire abuse.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Forward naming resolution (ID → Name) for characters and guilds via `NamingBroadcast`
- Reverse naming resolution (Name → ID) for characters via `ReverseNamingBroadcast`
- Local scene-server `ICharacterMappingData<NetworkConnection>` checked before any database call
- TTL-based caches (`CharacterNameByIdCache`, `GuildNameByIdCache`, `CharacterIdByNameCache`, `CharacterNameByNameCache`) with configurable expiry and bounded sweep
- Negative-result cache (`CharacterMissingByNameCache`) prevents repeated DB lookups for nonexistent names
- Per-connection request debounce via configurable `requestDebounceMilliseconds`
- Concurrent in-flight deduplication per lookup key with `MaxInFlightLookups` cap (5 000)
- Async database lookups queued via `TryEnqueueAsyncWork` with backpressure (rejects when queue is unavailable/full, logs warning)
- Per-system main-thread queue isolation via `NamingSystemMainThreadQueueData` with configurable drain cap per frame
- Explicit not-found response (`id = 0`, empty name) for failed reverse character lookups
- Oversized name rejection (names exceeding `Authentication.CharacterNameMaxLength` are dropped before any cache or DB work)
- Graceful failure semantics: null/invalid requests return early; missing services abort safely; broadcasts skipped when connection no longer active

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework
- **FishMMO Server Core** — provides `ServerBehaviour`, `INamingSystem`, `INamingSystemMainThreadQueueData`, `INamingSystemRuntimeData`, `INamingSystemMappingData`, broadcast types, and `AsyncWorkerData`
- **FishMMO Database** — provides `ICharacterService`, `IGuildService`, and `DatabaseResult<T>`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side scene-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `NamingSystem` is present on the scene server GameObject (it inherits from `ServerBehaviour` and implements `INamingSystem<NetworkConnection>`).
2. Verify that the following data containers are registered in `DataContainerRegistry`:
   - `NamingSystemRuntimeData` → `INamingSystemRuntimeData`
   - `NamingSystemMappingData` → `INamingSystemMappingData`
   - `NamingSystemMainThreadQueueData` → `INamingSystemMainThreadQueueData`
   - `AsyncWorkerData` (shared async work queue)
3. On initialize, `NamingSystem` validates all data containers and registers broadcast handlers for `NamingBroadcast` and `ReverseNamingBroadcast`.
4. On deinitialize, it drains the remaining main-thread queue and unregisters the broadcast handlers.
5. Clients send `NamingBroadcast` for forward lookups (ID → Name) or `ReverseNamingBroadcast` for reverse lookups (Name → ID); the server resolves from cache or database and replies on the same broadcast type.

## Configuration

### Inspector Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `maxMainThreadActionsPerFrame` | int | 100 | Max naming-system actions drained from main-thread queue per frame |
| `requestDebounceMilliseconds` | int | 75 | Minimum milliseconds between naming requests per connection |
| `cacheTtlSeconds` | float | 30.0 | Cache TTL in seconds for naming lookup caches |
| `cacheSweepIntervalSeconds` | float | 1.0 | Seconds between bounded naming cache sweeps |
| `cacheSweepMaxScan` | int | 128 | Maximum cache entries scanned per sweep pass |
| `cacheSweepMaxRemove` | int | 128 | Maximum cache entries removed per sweep pass |

### Internal Constants

| Constant | Value | Description |
|---|---|---|
| `MaxInFlightLookups` | 5000 | Maximum concurrent in-flight naming lookups per dictionary before new requests are dropped |

### Threading Model

| Thread | Work |
|---|---|
| Main thread | Request validation, debounce checks, cache lookups, broadcast dispatch, queue drain, cache sweep |
| Async worker | Database lookups (`FetchCharacterNameAsync`, `FetchGuildNameAsync`, `FetchCharacterByNameAsync`) |

## Usage Examples

### Broadcast Handlers

`NamingSystem` registers the following server-side broadcast handlers on initialize:

| Broadcast | Handler | Purpose |
|---|---|---|
| `NamingBroadcast` | `OnServerNamingBroadcastReceived` | Forward lookup: resolve ID → Name |
| `ReverseNamingBroadcast` | `OnServerReverseNamingBroadcastReceived` | Reverse lookup: resolve Name → ID |

### Forward Naming Path (ID → Name)

`OnServerNamingBroadcastReceived(conn, msg, channel)`:

1. Validates connection and spawned player object.
2. Checks per-connection request debounce.
3. Resolves `INamingSystemRuntimeData` and `INamingSystemMappingData`.
4. **Character name:**
   - Check local `ICharacterMappingData<NetworkConnection>.CharactersByID`; if found, upsert cache and reply immediately.
   - Else check `CharacterNameByIdCache`; if hit, reply immediately.
   - Else enqueue async DB lookup (`FetchCharacterNameAsync`) with in-flight deduplication.
5. **Guild name:**
   - Check `GuildNameByIdCache`; if hit, reply immediately.
   - Else enqueue async DB lookup (`FetchGuildNameAsync`) with in-flight deduplication.

### Reverse Naming Path (Name → ID)

`OnServerReverseNamingBroadcastReceived(conn, msg, channel)`:

1. Validates connection and spawned player object.
2. Checks per-connection request debounce.
3. Rejects null/empty names with immediate not-found response.
4. Rejects oversized names (exceeding `Authentication.CharacterNameMaxLength`).
5. Normalizes input to lowercase invariant.
6. **Character name:**
   - Check local `CharactersByLowerCaseName` mapping; if found, upsert caches, clear missing cache, reply immediately.
   - Else check `CharacterMissingByNameCache`; if hit, reply with not-found.
   - Else check `CharacterIdByNameCache` + `CharacterNameByNameCache`; if both hit, reply immediately.
   - Else enqueue async DB lookup (`FetchCharacterByNameAsync`) with in-flight deduplication.
   - If database unavailable, send not-found response immediately.
7. **Guild name:** Not currently implemented in reverse path.

### Failure Semantics

- Null/invalid requests return early (silent no-op).
- Missing services abort lookup safely without crashing.
- Failed reverse character lookups produce explicit not-found response (`id = 0`, empty name).
- Successful DB lookups for nonexistent names populate `CharacterMissingByNameCache` to avoid repeated queries.
- Broadcasts are skipped when the connection is no longer active.
- `TryEnqueueAsyncWork` returns `false` when the queue is unavailable or full; a warning is logged and the in-flight slot is released.

## Operational Checks

| Check | How to Verify |
|---|---|
| Initialization success | Confirm `NamingSystem` logs "Initialized" without errors on server startup |
| Data containers available | Verify `INamingSystemRuntimeData`, `INamingSystemMappingData`, and `INamingSystemMainThreadQueueData` all resolve from `DataContainerRegistry` |
| Forward character lookup (cached) | Send `NamingBroadcast` with `CharacterName` type for a locally present character; confirm immediate reply with correct name |
| Forward character lookup (DB) | Send `NamingBroadcast` for a character not on the local scene; confirm async DB fetch and delayed reply |
| Forward guild lookup | Send `NamingBroadcast` with `GuildName` type; confirm cache check then async DB fetch and reply |
| Reverse character lookup (cached) | Send `ReverseNamingBroadcast` for a locally present character name; confirm immediate reply with correct ID |
| Reverse character lookup (DB) | Send `ReverseNamingBroadcast` for a name not locally present; confirm async DB fetch and reply |
| Reverse not-found response | Send `ReverseNamingBroadcast` for a nonexistent character name; confirm reply with `id = 0` and empty name |
| Negative cache hit | Repeat the not-found lookup; confirm no second DB query and immediate not-found reply |
| Oversized name rejection | Send `ReverseNamingBroadcast` with a name exceeding `CharacterNameMaxLength`; confirm no processing occurs |
| Request debounce | Send rapid consecutive naming requests from the same connection; confirm excess requests are dropped |
| In-flight deduplication | Send duplicate forward lookups for the same ID concurrently; confirm only one DB query is issued |
| In-flight cap enforcement | Saturate `MaxInFlightLookups` (5 000); confirm additional lookups are rejected and warning is logged |
| Cache sweep | Wait for sweep interval; confirm stale cache entries are removed without errors |
| Main-thread queue drain | Confirm queued async results are dispatched on the main thread within `maxMainThreadActionsPerFrame` per frame |
| Deinitialize cleanup | Trigger deinitialize; confirm broadcast handlers are unregistered and main-thread queue is drained |

## Flow Diagram

### Forward Naming (ID → Name)

```
OnServerNamingBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection + spawned object
├─ 2. Check per-connection request debounce
├─ 3. Resolve INamingSystemRuntimeData + INamingSystemMappingData
│
├─ CharacterName:
│  ├─ 4a. Check local CharactersByID mapping
│  │      └── Hit → upsert cache, SendNamingBroadcast (immediate)
│  ├─ 4b. Check CharacterNameByIdCache
│  │      └── Hit → SendNamingBroadcast (immediate)
│  └─ 4c. Check in-flight cap → TryEnqueueAsyncWork(FetchCharacterNameAsync)
│         └── Async: DB fetch → upsert cache → TryEnqueueMainThread
│                    └── Main thread: SendNamingBroadcast
│
└─ GuildName:
   ├─ 5a. Check GuildNameByIdCache
   │      └── Hit → SendNamingBroadcast (immediate)
   └─ 5b. Check in-flight cap → TryEnqueueAsyncWork(FetchGuildNameAsync)
          └── Async: DB fetch → upsert cache → TryEnqueueMainThread
                     └── Main thread: SendNamingBroadcast
```

### Reverse Naming (Name → ID)

```
OnServerReverseNamingBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection + spawned object
├─ 2. Check per-connection request debounce
├─ 3. Reject null/empty name → SendReverseNamingBroadcast(id=0, empty)
├─ 4. Reject oversized name (> CharacterNameMaxLength)
├─ 5. Normalize name to lowercase invariant
│
├─ CharacterName:
│  ├─ 6a. Check local CharactersByLowerCaseName mapping
│  │      └── Hit → upsert caches, clear missing cache, SendReverseNamingBroadcast
│  ├─ 6b. Check CharacterMissingByNameCache
│  │      └── Hit → SendReverseNamingBroadcast(id=0, empty)
│  ├─ 6c. Check CharacterIdByNameCache + CharacterNameByNameCache
│  │      └── Both hit → SendReverseNamingBroadcast
│  └─ 6d. Check in-flight cap → TryEnqueueAsyncWork(FetchCharacterByNameAsync)
│         ├── Async: DB fetch found → upsert caches, clear missing cache
│         │          → TryEnqueueMainThread → SendReverseNamingBroadcast
│         └── Async: DB fetch not found → upsert missing cache
│                    → TryEnqueueMainThread → SendReverseNamingBroadcast(id=0, empty)
│
└─ GuildName:
   └── Not currently implemented in reverse path
```

### Cache Sweep (OnUpdate)

```
OnUpdate(deltaTime)
│
├─ 1. DrainMainThreadQueue (up to maxMainThreadActionsPerFrame)
└─ 2. SweepCaches()
       ├── Check if sweep interval has elapsed
       ├── Sweep ConnectionRequestTracker (debounce entries)
       └── Sweep all naming mapping caches (TTL-based, bounded scan/remove)
```

## Project Structure

### Directory Structure

```
Naming/
├── NamingSystem.cs                    # Naming/reverse-naming handlers, debounce, cache checks, async DB lookup orchestration
├── NamingSystemMappingData.cs         # Character/guild name ↔ ID TTL cache data container
├── NamingSystemRuntimeData.cs         # Runtime state: in-flight tracking, debounce tracker, sweep timer
├── NamingSystemMainThreadQueueData.cs # Per-system main-thread action queue container
└── README.md
```

### Related Core Contracts

- `Server/Core/World/SceneServer/Naming/INamingSystem.cs`
- `Server/Core/World/SceneServer/Naming/INamingSystemRuntimeData.cs`
- `Server/Core/World/SceneServer/Naming/INamingSystemMappingData.cs`
- `Server/Core/World/SceneServer/Naming/INamingSystemMainThreadQueueData.cs`

### Inheritance Hierarchy

```
ServerBehaviour
└── NamingSystem : INamingSystem<NetworkConnection>

RuntimeDataContainer
├── NamingSystemRuntimeData : INamingSystemRuntimeData
└── NamingSystemMappingData : INamingSystemMappingData

SystemMainThreadQueueData
└── NamingSystemMainThreadQueueData : INamingSystemMainThreadQueueData
```

## License

This project is subject to the FishMMO project license.
