# Character System

## Overview

The Character system is the scene-server authority for player character lifecycle: loading selected characters, claiming/releasing character sessions, spawning/despawning network objects, handling scene validation/teleport transitions, periodic persistence, and runtime mapping for fast lookups. Unity/FishNet state changes execute on the main thread; database operations are dispatched asynchronously through `AsyncWorkerData`.

## Directory Structure

```
Character/
├── CharacterSystem.cs                    # Character lifecycle orchestration, persistence, scene validation
├── CharacterSystem.Connection.cs         # Partial: connection/disconnection handlers
├── CharacterSystem.Loading.cs            # Partial: character loading and spawning
├── CharacterSystem.Saving.cs             # Partial: periodic and on-demand character persistence
├── CharacterSystem.Social.cs             # Partial: social sync (guild, party, friend)
├── CharacterMappingData.cs               # Runtime mapping caches (connection, ID, name, world, waiting load, session tokens)
├── CharacterSystemRuntimeData.cs         # Runtime state container
├── CharacterSystemMainThreadQueueData.cs # Per-system main-thread queue container
└── README.md
```

Related core contracts:

- `Server/Core/World/SceneServer/Character/ICharacterSystem.cs`
- `Server/Core/World/SceneServer/Character/ICharacterMappingData.cs`
- `Server/Core/World/SceneServer/Character/ICharacterSystemMainThreadQueueData.cs`
- `Server/Core/World/SceneServer/Character/CharacterSessionInfo.cs`
- `Server/Core/RuntimeData/IAsyncWorkerData.cs`
- `Server/Core/RuntimeData/IMainThreadQueueData.cs`

## Inheritance Hierarchies

### Behaviour

```
ServerBehaviour
└── CharacterSystem : ICharacterSystem<NetworkConnection, Scene>
```

### Runtime Data Containers

```
RuntimeDataContainer
├── CharacterMappingData : ICharacterMappingData<NetworkConnection>
└── MainThreadQueueData (abstract)
    └── CharacterSystemMainThreadQueueData : ICharacterSystemMainThreadQueueData
```

## Runtime Mapping Model

`CharacterMappingData` maintains synchronized runtime indices:

| Map | Purpose |
|---|---|
| `CharactersByID` | characterID → `IPlayerCharacter` |
| `CharactersByLowerCaseName` | lowercase name → `IPlayerCharacter` |
| `CharactersByWorld` | worldID → (characterID → `IPlayerCharacter`) |
| `ConnectionCharacters` | connection → active spawned character |
| `WaitingSceneLoadCharacters` | connection → character loaded but not fully scene-validated |
| `SessionTokens` | characterID → claimed session ownership (`Token`, `ServerID`) |

## Character Load Pipeline

### 1) Authentication callback

`Authenticator_OnClientAuthenticationResult(...)` validates account/session prerequisites and enqueues `LoadCharacterAsync(...)`.

### 2) Async DB snapshot + claim

`LoadCharacterAsync(...)`:

1. Begins UnitOfWork.
2. Fetches selected character for account.
3. Claims character session (`TryClaimAsync`) before heavy hydration.
4. Fetches sub-entity data (inventory, bank, equipment, attributes, abilities, known abilities, achievements, friends, guild, party, hotkeys, buffs, factions).
5. Commits read transaction.
6. Marshals to main thread for instantiation.

### 3) Main-thread instantiate + hydrate

`InstantiateAndLoadCharacter(...)`:

1. Instantiates race prefab.
2. Populates base character fields.
3. Hydrates all controllers from fetched DTOs.
4. Attempts scene load via `ISceneServerSystem`.
5. On success: stores session token + moves to `WaitingSceneLoadCharacters`.
6. On failure: releases claimed session + disconnects.

### 4) Scene validation + spawn

- `SceneManager_OnClientLoadedStartScenes(...)` verifies scene validity and sends `ClientValidatedSceneBroadcast`.
- `OnClientValidatedSceneBroadcastReceived(...)` promotes character into active maps, spawns network object, sends initial non-DB payloads, and asynchronously fetches social overlays.

## Session Ownership and Release

Character sessions are explicitly claimed/released to prevent dual-server ownership:

- Claim: `TryClaimAsync(characterID, serverID)` during load.
- Lease refresh: `RefreshSessionLeaseAsync(...)` during periodic save loop.
- Release: `ReleaseAsync(characterID, serverID, token)` on disconnect/teleport/error/deinit.

`CharacterSessionInfo` in `SessionTokens` ensures release uses the correct token+server pair.

## Periodic Operations

### Periodic Save

`OnPeriodicSave(...)` snapshots character DTOs and enqueues `SaveAllCharactersAsync(...)`.

`SaveAllCharactersAsync(...)`:

- persists each character
- refreshes session leases even if persistence fails
- uses processing guard to prevent overlapping save cycles

### Out-of-Bounds Check

`OnPeriodicOutOfBoundsCheck(...)` verifies each character remains within scene boundaries and teleports invalid positions to respawn points.

## Teleport/Death Flow

### Teleport

`IPlayerCharacter_OnTeleport(...)`:

- validates teleporter
- unloads current scene
- sets immortality
- updates destination scene/position
- disables instance flag
- saves + releases session, forcing reconnect flow through world routing

### Death

`CharacterDamageController_OnKilled(...)`:

- player: heals/respawns or returns to bind scene (with reconnect path if needed)
- NPC: despawn
- pet: raises `OnPetKilled`

## Initial Payloads Sent After Spawn

### Immediate non-DB payloads

`SendNonDbCharacterData(...)` broadcasts:

- known abilities/events
- achievements
- inventory
- bank
- hotkeys

### Async social payloads

`SendAllCharacterDataAsync(...)` fetches and broadcasts:

- guild members
- party members
- friend online status

## Events Exposed

`CharacterSystem` exposes lifecycle events for cross-system coordination:

- `OnBeforeLoadCharacter`
- `OnAfterLoadCharacter`
- `OnConnect`
- `OnDisconnect`
- `OnSpawnCharacter`
- `OnDespawnCharacter`
- `OnPetKilled`

## Threading and Queueing

| Thread | Responsibilities |
|---|---|
| Main thread | Unity/FishNet object lifecycle, map mutations, broadcasts, scene checks |
| Async worker | DB load/save/session operations and social fetches |

Main-thread actions are marshalled through `CharacterSystemMainThreadQueueData`. Async DB tasks are enqueued through `IAsyncWorkerData`; enqueue failures are logged and handled on critical paths.

## External Integration Points

- **SceneServerAuthenticator**: emits auth-success signal for load entry.
- **SceneServerSystem**: validates/loads target scenes.
- **CharacterService + sub-entity services**: persistence/hydration/session claim/lease/release.
- **World boundaries + teleporters**: movement safety and transitions.
- **FishNet SceneManager / ServerManager**: spawn/despawn and scene lifecycle callbacks.