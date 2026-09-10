# Pet System

**Short description:** Server-side pet lifecycle and runtime control system for scene-server player characters, handling follow/stay/attack/attack-priority/stance/summon/release commands, ability-driven spawning and ability learning, character-driven spawn/despawn transitions, ingress guarding, and asynchronous database persistence with main-thread-marshaled broadcasts.

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

The Pet system is the SceneServer authority for pet lifecycle and runtime control. It handles pet follow/stay/summon/release requests from connected players, character-driven pet spawn/despawn transitions, pet summon ability integration, and pet persistence across sessions.

The design separates responsibilities across execution contexts:
- **Main thread:** request validation, ingress guarding, controller/AI updates, spawning/despawning, faction copying, and network broadcasts.
- **Async worker:** database fetch/persist operations dispatched through `IAsyncWorkerData`.
- **Main-thread queue:** marshaling async completion actions back to Unity/FishNet-safe context via `PetSystemMainThreadQueueData`.

Async work is queued to `IAsyncWorkerData` with `entityKey = characterID` for per-character ordering: reads through `TryEnqueueAsyncWork(...)`, and the one persistence write through `EnqueuePersistence(...)`, which falls back to the thread pool rather than dropping the write when the channel is full. If queueing a read fails (backpressure/missing dependency), the system logs warnings while keeping gameplay state intact. Broadcasts are only emitted after successful state transitions, ensuring clients never see stale or uncommitted data.

### Ownership

Pets are spawned **without an owning connection** — server-owned, exactly like any other NPC. FishNet's `Replicate_Authoritative` only accepts server-produced input for an object with no owner, so handing a pet to the summoner's connection would make that client responsible for supplying input to a brain that does not run there, and the server's own decisions would be discarded. Nothing in the pet system reads the pet's `Owner`, and the pet's `NetworkTransform` is server-authoritative, so server ownership costs nothing.

### Commands and orders

A pet carries three independent pieces of state, all server-authoritative and all written into the spawn payload:

| State | Values | Meaning |
|---|---|---|
| `PetStance` | `Passive`, `Defensive`, `Aggressive` | Whether the pet *initiates*. An explicit attack order works in every stance. |
| `PetMovementOrder` | `Follow`, `Stay` | Whether the pet heels or holds position. |
| `PetAttackPriority` (packed `int`) | a permutation of `PetAttackTarget.Pinned`, `Current`, `HighestThreat` | The order an attack order tries its three ways of choosing a victim. |

The first two are byte-backed enums whose zero values (`Passive`, `Follow`) are the safe defaults, so an unset value never makes a pet pick a fight or strand itself. The priority packs its three steps into one `int` with bit 0 as a set marker, so the 0 an old client or an unset field carries is never a valid order and decodes to `PetAttackPriority.Default` (pinned → current → highest threat). Clients only ever *request* a change; the server confirms with a broadcast.

Movement orders are expressed as orders, not by writing the AI's combat target. `Target` means "the thing I am fighting" throughout the AI, and conflating the two is what previously made a pet told to Stay never move again.

## Supported Platforms

| Platform | Supported | Notes |
|---|---|---|
| Windows | Yes | |
| Linux | Yes | |
| WebGL | N/A | Server-only module |
| Unity 6.3 LTS | Yes | Required engine version |
| IL2CPP | Yes | Supported scripting backend |

## Features

- Pet follow, stay, attack, stance, summon (warp), and release commands via network broadcasts with full validation
- Attack orders try three choices in the order the owner set (`PetAttackPriority`, default pinned → current → highest threat): the click carries the pinned and hovered frame ids separately, each verified — spawned character, owner's scene, within `TargetController.MAX_TARGET_DISTANCE` of the owner — with the server's own copy of the reported frame backing the current step, and the highest-threat step resolved from the threat tables (`AggressionDispatcher.TryFindHighestThreatAgainst`, which with pet-credited threat is what the owner and pet have attacked the most); whatever a step yields is re-validated as alive, not the owner, not the pet, and hostile by faction, and the first valid one wins
- The attack priority is session state on the owner's `IPetController` like the stance (`PetAttackPriorityBroadcast` request/confirm, refused unless a permutation of the three steps, applied at summon and sent with `PetAddBroadcast`); the client remembers it in its settings under `PetAttackPriority` and replays it on every summon, so it survives sessions with no database change
- Death is a full reset: a player's pet is dismissed from the kill event through the same `DismissPet` path as a voluntary release
- A pet and its owner share threat both ways (see the AI README): hit either and both are threatened; a pet's hits are credited to its owner as well
- Stance changes reject values outside the enum rather than casting a hostile byte straight in; dropping to `Passive` recalls the pet and interrupts its cast
- Defensive and Aggressive pets answer an attack on their owner via `IPetController.OnOwnerAttacked`, a server-side hook raised from the global damage event
- Pet ability learning: template IDs restored from the database and abilities granted by the `PetAbilityTemplate` are both taught to the pet's `IAbilityController` on spawn, and captured back before persistence
- Per-connection ingress guarding with debounce and in-flight protection to prevent duplicate operations across all pet control actions
- Ability-driven pet summoning via `AbilityObject.OnPetSummon` with bounding-box randomization and ground sphere cast for spawn positioning
- Automatic pet spawn on character login via async database load and main-thread marshaling, restoring version-matched attributes (`FetchPersistedPetAttributesAsync`) and buffs (`FetchPersistedPetBuffsAsync`) alongside the abilities
- Live pet state is written by the character save, not from here: `CharacterSystem.AppendPetData` snapshots the pet row, its attributes and its buffs before the session claim is released, so a zone transfer cannot race a second unordered write. Character despawn therefore tears the pet down and writes nothing
- Dismissal is the one thing this system persists: `PersistPetDismissed` → `SavePetDismissedAsync` records `spawned = false` for a release, an owner's death, or a pet's death, so a pet that died at noon is not waiting alive at the next login
- Pet AI initialization after the scene move and activation, so the NavMeshAgent is warped onto the mesh at its real spawn position rather than being driven while inactive
- A pet's AI `Home` resolves to its owner's live position, so leashing, wandering and return-home all track the player without the pet system having to write anything
- Faction data copying from owner to pet via `IFactionController.CopyFrom`
- Shared `SpawnAndInitializePet` helper that handles existing-pet despawn, pooled object retrieval, AI/faction init, scene transfer, network spawn, and broadcast emission
- Pet rows are versioned from the **owner's** counter (`++owner.Version`) — a pooled pet has no durable identity of its own — and restored attributes and buffs are accepted only when their version matches the pet row's, so a new pet cannot inherit the previous one's values
- Time-sliced main-thread queue draining with configurable per-frame action limits to avoid frame spikes
- Periodic ingress guard cleanup sweeps with configurable TTL and max removals per sweep
- Achievement integration for pet summon events via configurable `AchievementTemplate`
- Graceful degradation: logs warnings on persistence queue failures while keeping in-memory gameplay state intact
- Object pool integration: pets are retrieved from and returned to the FishNet object pool to reduce allocation pressure
- Stale-reference safety: the owner's controller reference is cleared on death and despawn, so a Summon or Follow command cannot re-task a pooled pet that no longer exists
- Guard release deferred until async operation completes for character spawn pet loads, preventing overlap windows for duplicate operations

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — networking framework
- **FishMMO Server Core** — provides `ServerBehaviour`, `IPetSystem`, controller interfaces, broadcast types, `IAsyncWorkerData`, and `IngressGuard`

## Installation / Build

This is an integrated module within FishMMO. It is included as part of the server-side scene-server implementation and does not require separate installation. Ensure the FishMMO Server Core and its dependencies are properly configured in your Unity project.

## Quick Start Guides

1. Ensure `PetSystem` is present on the scene server GameObject (it inherits from `ServerBehaviour` and implements `IPetSystem`).
2. Verify that `PetSystemMainThreadQueueData` and `PetSystemRuntimeData` data containers are registered (declared via `[RequiresDataContainer]` attributes).
3. Confirm that `AsyncWorkerData` (`IAsyncWorkerData`) is available for non-blocking DB write queuing.
4. Confirm that `ICharacterSystem<NetworkConnection, Scene>` is registered in the behaviour registry.
5. Verify that the database service `ICharacterPetService` is available in the DB service registry.
6. On initialize, `PetSystem` automatically registers broadcast handlers for `PetFollowBroadcast`, `PetStayBroadcast`, `PetAttackBroadcast`, `PetAttackPriorityBroadcast`, `PetStanceBroadcast`, `PetSummonBroadcast`, and `PetReleaseBroadcast`, and subscribes to `AbilityObject.OnPetSummon`, `ICharacterDamageController.OnKilled`, `OnSpawnCharacter`, `OnDespawnCharacter`, and `OnPetKilled`; on deinitialize, it unregisters them all.
7. Give the pet prefab an AI archetype — one of the six `Pet - *` assets under `Assets/Templates/Entity/NPCs/AI/Archetypes/` — and give the `PetAbilityTemplate` a `PetAbilities` list. A pet with no abilities will follow its owner and never attack.

## Configuration

### Inspector Settings

| Field | Type | Default | Purpose |
|---|---|---|---|
| `maxMainThreadActionsPerFrame` | `int` | `100` | Max pet-system actions drained from main-thread queue per frame |
| `ingressDebounceMilliseconds` | `int` | `80` | Minimum milliseconds between pet control requests per connection |
| `ingressSweepIntervalSeconds` | `float` | `5.0` | Seconds between bounded ingress guard cleanup sweeps |
| `ingressEntryTtlSeconds` | `float` | `30.0` | Seconds before stale ingress guard entries are removed |
| `ingressSweepMaxRemovals` | `int` | `128` | Maximum stale ingress guard entries removed per sweep |
| `PetSummonAchievementTemplate` | `AchievementTemplate` | `null` | Optional achievement template incremented when a pet is summoned |

### Required Data Containers

| Container | Interface | Purpose |
|---|---|---|
| `PetSystemMainThreadQueueData` | `IPetSystemMainThreadQueueData` | Per-system main-thread action queue for marshaling async completions |
| `PetSystemRuntimeData` | `IPetSystemRuntimeData` | Runtime state container for ingress guard |
| `AsyncWorkerData` | `IAsyncWorkerData` | Queued non-blocking async DB work dispatch |

### Database Service Dependencies

| Service | Purpose |
|---|---|
| `ICharacterPetService` | Fetches and persists pet records (spawned state, template, abilities, version) |

### Threading Model

| Thread | Work |
|---|---|
| Main thread | Request validation, ingress guarding, pet AI/faction updates, spawning/despawning, network broadcasts, DTO capture |
| Async worker | Database fetch/persist operations via `IAsyncWorkerData` |

## Usage Examples

### Broadcast Handlers

`PetSystem` registers the following server broadcast handlers on initialize:

- `PetFollowBroadcast` → `OnPetFollowBroadcastReceived`
- `PetStayBroadcast` → `OnPetStayBroadcastReceived`
- `PetAttackBroadcast` → `OnPetAttackBroadcastReceived`
- `PetStanceBroadcast` → `OnPetStanceBroadcastReceived`
- `PetSummonBroadcast` → `OnPetSummonBroadcastReceived`
- `PetReleaseBroadcast` → `OnPetReleaseBroadcastReceived`
- `PetAttackPriorityBroadcast` → `OnPetAttackPriorityBroadcastReceived`

And unregisters them on deinitialize.

### Broadcasts Emitted

| Broadcast | Purpose |
|---|---|
| `PetAddBroadcast` | Notify owner of successful pet spawn (pet ID, stance, attack priority, movement order) |
| `PetRemoveBroadcast` | Notify owner of pet removal (release or death) |
| `PetStanceBroadcast` | Confirm the authoritative stance after a change request |
| `PetMovementOrderBroadcast` | Confirm the authoritative movement order after Follow or Stay |
| `PetAttackPriorityBroadcast` | Confirm the authoritative attack priority — sent for every request, including a refused one, so the panel cannot be left out of step |

The UI does not paint a requested stance optimistically — it waits for the server's confirming broadcast, so the highlighted button always reflects what the pet is really doing rather than what was last clicked.

### External Integration Points

| Integration | Role |
|---|---|
| `IPetController` | Pet ownership state, stance/order mirror, and the `OnOwnerAttacked` server hook |
| `ITargetController` | Backs the `Current` step when the click names nothing (`HasClientSelectedTarget` / `ClientSelectedTargetObjectId`), and supplies `MAX_TARGET_DISTANCE`, the bound every claimed target id is checked against |
| `AggressionDispatcher` | `TryFindHighestThreatAgainst` resolves the `HighestThreat` step from the server's threat tables |
| `ICharacterDamageController.OnKilled` | Static kill event; a player's death dismisses their pet through `DismissPet` |
| `IAbilityController` | Pet ability learning on spawn and capture before persistence |
| `IAIController` | Pet AI home position, follow target, agent warping, and initialization |
| `IFactionController` | Faction data copying from owner to pet |
| `ICharacterAttributeController` | Health attribute read during despawn to determine alive/spawned state |
| `ICharacterSystem<NetworkConnection, Scene>` | Character spawn/despawn/pet-killed event subscriptions |
| `AbilityObject.OnPetSummon` | Ability-driven pet summon event integration |
| `IngressGuard` (via `IPetSystemRuntimeData`) | Per-connection debounce and in-flight protection |
| `AsyncWorkerData` (`IAsyncWorkerData`) | Queued non-blocking async persistence |
| `IAchievementController` | Optional achievement increment on pet summon |
| `PetAbilityTemplate` | Template defining pet prefab, spawn bounding box, spawn distance, and ability data |
| Database service (`ICharacterPetService`) | Pet relationship persistence and spawned-state loading |

### Ingress Guarding

Pet ingress uses per-connection operation keys with debounce + in-flight protection. Operation codes are scoped per action type (`Follow`, `Stay`, `Summon`, `Release`, `LoadPet`, `Attack`, `Stance`, `AttackPriority`). For the character-spawn pet load path, guard release is deferred until the async operation completes (not at enqueue-time), preventing overlap windows for duplicate pet loads on rapid reconnect. For synchronous broadcast handlers (follow, stay, summon), guards are released in a `finally` block after the handler completes.

### Async Worker and Backpressure

`TryEnqueueAsyncWork(...)` dispatches all async DB work through `IAsyncWorkerData`.

Behavior:
- Returns `true` when accepted.
- Returns `false` when queue is unavailable or full.
- Logs warnings on rejection/unavailability.
- Uses `entityKey = characterID` for per-character ordering.

This prevents unbounded fire-and-forget tasks and preserves operation order per player.

### Persistence Model

A **live** pet is not persisted from this system at all. `CharacterSystem.AppendPetData` snapshots the pet row, its attributes and its buffs as part of the owning character's save, which is what gets that state into the database before the character's session claim is released — and therefore before the destination scene server reads it.

`PersistPetDismissed(owner, pet)` — the only write this system makes:
- Runs `Pet.CaptureKnownAbilities()` first, so abilities granted at summon time from the `PetAbilityTemplate` are persisted rather than lost on the next log in.
- Stamps the row with `++owner.Version`: the counter only has to increase, not be contiguous, and sharing the owner's stream keeps this write ordered against the character saves.
- Skips the write and logs a warning if `templateID` is invalid (≤ 0) — the row then still lists the pet as out.
- Hands `SavePetDismissedAsync` to `EnqueuePersistence`, keyed by `characterID`, which writes `CharacterPetData(..., spawned: false)` through `ICharacterPetService.PersistAsync`.

`LoadAndSpawnPetAsync(...)`:
- Fetches the currently spawned pet record via `ICharacterPetService.FetchSpawnedAsync`.
- Fetches attributes and buffs stamped with the same version as that row (`FetchPersistedPetAttributesAsync`, `FetchPersistedPetBuffsAsync`); rows at an older version belong to a pet that has since been dismissed and are discarded.
- Marshals pet reconstruction/spawn back to the main thread via `TryEnqueueMainThread`.
- Re-validates the connection, that `character.ID` still equals the `characterID` the fetch was for, and that `conn.FirstObject` is still that character's `NetworkObject` — the character object is pooled and may have been handed to a different player during the round trip — then resolves the template and spawns.

## Operational Checks

| Check | How to Verify |
|---|---|
| System initialization | Confirm `PetSystem` initializes without errors; broadcast handlers are registered |
| Pet follow | Send a `PetFollowBroadcast` with an active pet; verify AI target is set to owner transform |
| Pet stay | Send a `PetStayBroadcast` with an active pet; verify the pet holds position and `PetMovementOrderBroadcast` reaches the client |
| Pet attack | Target a hostile, send a `PetAttackBroadcast`; verify the pet engages it. Target a friendly or the pet itself; verify the order is refused |
| Attack priority order | Reorder the steps in the pet panel, then attack with nothing pinned; verify the `Current` step is used, and with a pinned target that the pinned one wins under the default order |
| Out-of-range target id | Send a `PetAttackBroadcast` naming a spawned character beyond `TargetController.MAX_TARGET_DISTANCE` or in another scene; verify the step is skipped rather than honoured |
| Attack priority confirm | Send a `PetAttackPriorityBroadcast` with a non-permutation value; verify it is ignored and the order in force is broadcast back |
| Owner death dismissal | Kill a player with a pet out; verify the pet is despawned, `PetRemoveBroadcast` reaches the client, and the dismissal is persisted |
| Pet stance | Send each `PetStanceBroadcast` value; verify the confirming broadcast, and that Passive recalls a fighting pet |
| Defensive response | With a Defensive pet, have a hostile attack the owner; verify the pet engages the attacker |
| Pet abilities | Summon a pet whose `PetAbilityTemplate.PetAbilities` is populated; verify the pet's `IAbilityController` knows them and the pet attacks |
| Ability persistence | Summon, release, and re-log; verify the pet returns with the same abilities |
| Pet summon (warp) | Send a `PetSummonBroadcast` with an active pet; verify AI agent warps to owner position |
| Pet release | Send a `PetReleaseBroadcast` with an active pet; verify pet is despawned, `PetRemoveBroadcast` reaches client, and async save is queued with `spawned=false` |
| Character spawn pet load | Spawn a character with a previously saved pet; verify async load triggers and pet is instantiated with correct template and abilities |
| Character despawn teardown | Despawn a character with an active pet; verify the pet is returned to the pool and the controller reference cleared, and that **no** pet write is issued from `PetSystem` — the preceding character save carries it |
| Pet killed | Trigger pet death; verify `PersistPetDismissed` writes `spawned = false`, `PetRemoveBroadcast` reaches the client, and the dismiss triggers fire |
| Ability-driven summon | Execute a pet summon ability; verify pet spawns at ground-cast position with AI initialized and `PetAddBroadcast` emitted |
| Ingress debounce | Send rapid duplicate pet control requests; confirm only the first is processed within the debounce window |
| Async persistence | Check logs for `EnqueuePersistence` accepting `SavePetDismissedAsync` after release/owner-death/pet-death operations |
| Main-thread queue draining | Verify queued actions are executed each frame within `maxMainThreadActionsPerFrame` limit |
| Persistence failure graceful degradation | Simulate persistence queue failure; confirm warning is logged and in-memory state remains unchanged |
| Achievement increment | Configure `PetSummonAchievementTemplate`; summon a pet and verify achievement counter increments |
| Ingress guard cleanup | Verify stale ingress guard entries are removed during periodic sweeps |
| Version-matched restore | Dismiss a pet, summon a different one, and re-log; verify the new pet does not inherit the previous pet's attributes or buffs |

## Flow Diagram

### High-Level Overview

```mermaid
flowchart LR
    Owner[Character] -->|summon / dismiss| Sys[PetSystem]
    Sys -->|load pet state| DB[(PostgreSQL Pets)]
    Sys -->|spawn AI| Pet[Pet entity]
    Pet -->|tick / commands| Sys
    Sys -->|persist diffs| DB
    Sys -->|broadcast| Clients[Nearby Clients]
```

### Pet Follow / Stay

```
OnPetFollowBroadcastReceived / OnPetStayBroadcastReceived
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate IPetController exists with active pet
├─ 4. SetMovementOrder(conn, petController, Follow | Stay)
│      ├── Pet.MovementOrder = order
│      ├── Stay  → AI Home pinned to the pet's current position
│      ├── Follow → AI Home resolves to the owner again
│      └── Broadcast PetMovementOrderBroadcast to the owner
└─ 5. Release ingress guard (finally)
```

### Pet Attack

```
OnPetAttackBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate IPetController exists with active pet
├─ 4. Decode petController.AttackPriority (falls back to PetAttackPriority.Default)
├─ 5. Walk the three steps in order, stopping at the first valid target:
│      ├── Pinned        → TryResolveFrameTarget(msg.PinnedTargetObjectID)
│      ├── Current       → TryResolveFrameTarget(msg.HoveredTargetObjectID)
│      │                   else the server's own ClientSelectedTargetObjectId
│      └── HighestThreat → AggressionDispatcher.TryFindHighestThreatAgainst(owner, filter)
│      TryResolveFrameTarget: id ≠ 0, spawned on the server, same scene as the owner,
│      within TargetController.MAX_TARGET_DISTANCE of the OWNER (not the pet)
├─ 6. IsValidPetTarget on whatever a step yields: alive, not the owner, not the pet,
│      hostile by faction — an invalid result hands over to the next step
├─ 7. CommandPetAttack → set AI target, clear any Stay, enter the attacking state
└─ 8. Release ingress guard (finally)
```

### Pet Attack Priority

```
OnPetAttackPriorityBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection, spawned object, and CharacterStateValidation.CanAct
├─ 2. Acquire ingress guard (AttackPriority)
├─ 3. Validate IPetController exists (a live pet is NOT required)
├─ 4. PetAttackPriority.IsValid(msg.Priority) → apply to the controller and,
│      when one is out, to the Pet as well; anything else is ignored
├─ 5. Broadcast PetAttackPriorityBroadcast with the order actually in force
└─ 6. Release ingress guard (finally)
```

Session state only: nothing here touches the database. The client keeps the order in its own
settings (`UITKPetControl`, key `PetAttackPriority`) and replays it on every summon, which is
what makes it survive a log out.

### Pet Stance

```
OnPetStanceBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Reject a stance outside the enum
├─ 3. Acquire ingress guard (debounce + in-flight)
├─ 4. Validate IPetController exists with active pet
├─ 5. Apply stance to both the Pet and the controller mirror
├─ 6. Passive → RecallPet (clear target, interrupt cast, return to idle)
├─ 7. Broadcast PetStanceBroadcast to confirm
└─ 8. Release ingress guard (finally)
```

### Owner Attacked → Defensive Response

```
IPetController.OnOwnerAttacked(petController, attacker)
│
├─ 1. Ignore if the pet is Passive
├─ 2. Ignore if the pet is already in its attacking state (do not thrash its target)
├─ 3. IsValidPetTarget(attacker)
└─ 4. CommandPetAttack(pet, attacker)
```

### Pet Summon (Warp)

```
OnPetSummonBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate IPetController exists with active pet
├─ 4. Warp IAIController.Agent to owner position
└─ 5. Release ingress guard (finally)
```

### Pet Release

```
OnPetReleaseBroadcastReceived(conn, msg, channel)
│
├─ 1. Validate connection and spawned object
├─ 2. Acquire ingress guard (debounce + in-flight)
├─ 3. Validate database services available
├─ 4. Validate IPetController exists with active pet
├─ 5. DismissPet(player, petController, conn) — the shared dismissal path
│      ├── PersistPetDismissed: CaptureKnownAbilities, then EnqueuePersistence
│      │    (SavePetDismissedAsync, keyed by characterID) with ++owner.Version
│      ├── Despawn pet network object to pool
│      ├── Clear pet owner and controller references, unsubscribe OnOwnerAttacked
│      ├── Broadcast PetRemoveBroadcast to owner
│      └── Fire the server-side OnPetDismissTriggers
└─ 6. Release ingress guard (finally)
```

### Character Spawn → Pet Load

```
CharacterSystem_OnSpawnCharacter(conn, character, scene)
│
├─ 1. Validate character and IPetController
├─ 2. Validate database services available
├─ 3. Acquire ingress guard (LoadPet, in-flight only)
├─ 4. Enqueue async LoadAndSpawnPetAsync(...)
│      │
│      ├─ Resolve ICharacterPetService
│      ├─ FetchSpawnedAsync pet record from DB
│      └─ Enqueue main-thread completion action:
│           ├── Re-validate connection/character/network state
│           ├── Resolve PetAbilityTemplate from templateID
│           ├── Retrieve pooled pet NetworkObject at owner position
│           ├── SpawnAndInitializePet(..., spawnPosition, ...)
│           │    ├── Despawn any existing pet
│           │    ├── Initialize Pet component (owner, template, stance, orders)
│           │    ├── Build the ability list (persisted IDs + template grants)
│           │    ├── Move to owner scene
│           │    ├── Activate
│           │    ├── Initialize AI — warps the agent onto the NavMesh at spawnPosition
│           │    ├── Network-spawn with NO owning connection (server-owned)
│           │    │     └── Pet.OnStartServer learns the ability list
│           │    ├── Copy faction from owner
│           │    ├── Subscribe the defensive OnOwnerAttacked hook
│           │    ├── Broadcast PetAddBroadcast (id, stance, order)
│           │    └── Increment achievement (if configured)
│           └── Guard release (finally, deferred)
│
└─ On enqueue failure: release guard immediately
```

### Character Despawn → Pet Teardown

```
CharacterSystem_OnDespawnCharacter(conn, character)
│
├─ 1. Validate character and IPetController
├─ 2. No live pet → just unsubscribe OnOwnerAttacked and return
├─ 3. Despawn pet network object to pool (if spawned)
└─ 4. Clear Pet.PetOwner / IPetController.Pet, unsubscribe OnOwnerAttacked
```

Deliberately writes nothing: the character save that precedes this event has already
snapshotted the live pet through `CharacterSystem.AppendPetData`, and a second write from here
would be an unordered race against the one that matters. Clearing the reference is what stops a
later Summon or Follow re-tasking a pooled pet that no longer exists.

### Owner Killed → Pet Dismissed

```
ICharacterDamageController.OnKilled(killer, victim)
│
├─ 1. Ignore unless the victim is an IPlayerCharacter with a live pet
├─ 2. Resolve the owner's connection (null when it is no longer active)
└─ 3. DismissPet(owner, petController, conn)
       ├── PersistPetDismissed (snapshots abilities first)
       ├── Despawn the pet NetworkObject to the pool
       ├── Clear Pet.PetOwner / IPetController.Pet, unsubscribe OnOwnerAttacked
       ├── Broadcast PetRemoveBroadcast (only when there is a connection to tell)
       └── InvokePetTriggers(OnPetDismissTriggers)
```

`DismissPet` is the one path every dismissal takes: a voluntary `PetReleaseBroadcast` and a
forced dismissal on the owner's death run exactly the same code, so both persist and notify
identically. Without the death hook the summon outlived its owner, leashed to a corpse and
still fighting whatever had killed it. A pet's *own* death is `CharacterSystem_OnPetKilled`'s
concern, and NPC deaths are not this handler's at all.

### Pet Killed

```
CharacterSystem_OnPetKilled(conn, character)
│
├─ 1. Read OnPetDismissTriggers BEFORE the teardown clears the controller's pet reference
├─ 2. PersistPetDismissed (spawned = false; no character save is coming for a living owner)
├─ 3. Reuse CharacterSystem_OnDespawnCharacter for the despawn and reference clearing
├─ 4. Broadcast PetRemoveBroadcast to owner (when there is a connection)
└─ 5. InvokePetTriggers(OnPetDismissTriggers)
```

### Ability-Driven Pet Summon

```
AbilityObject_OnPetSummon(petAbilityTemplate, caster)
│
├─ 1. Validate template, caster, IPetController, and pet prefab
├─ 2. Get physics scene from caster scene
├─ 3. Generate random spawn origin within bounding box
├─ 4. Sphere cast downward to find ground position
├─ 5. Retrieve pooled pet NetworkObject at ground position
└─ 6. SpawnAndInitializePet(...)
       ├── Despawn any existing pet
       ├── Initialize Pet component (owner, template, stance, orders)
       ├── Build the ability list from PetAbilityTemplate.PetAbilities
       ├── Move to caster scene
       ├── Activate
       ├── Initialize AI — warps the agent onto the NavMesh at the ground-cast position
       ├── Network-spawn with NO owning connection (server-owned)
       │     └── Pet.OnStartServer learns the ability list
       ├── Copy faction from caster
       ├── Subscribe the defensive OnOwnerAttacked hook
       ├── Broadcast PetAddBroadcast (id, stance, order)
       └── Increment achievement (if configured)
```

### Failure Semantics

```
Error Handling
│
├─ Invalid requests → ignored without state mutation
├─ DB/service lookup failures → abort async operation safely
├─ Queue rejection/unavailability → logged, work skipped
├─ Async exceptions → caught and logged with character context
├─ Invalid templateID (≤ 0) → save skipped with warning
├─ Pet row write failure → logged with character ID and version
├─ Pooled object missing Pet component → returned to pool, spawn aborted
└─ Broadcasts → only emitted after successful state transitions
```

## Known Limitations

| Limitation | Detail |
|---|---|
| Stance is not persisted | `PetStance` lives on the pet and the controller for the session. A player who sets Aggressive and logs out returns to the `Defensive` default. Persisting it needs a `character_pet` schema change. |
| Attack priority is not persisted server-side | The order lives on `IPetController` and the `Pet` for the session. It survives a log out only because the client stores it (`UITKPetControl`, settings key `PetAttackPriority`) and replays it; a fresh client install starts at `PetAttackPriority.Default`. |

## Project Structure

### Directory Structure

```
Pet/
├── PetSystem.cs                       # Pet lifecycle orchestration, broadcast handlers, and async persistence
├── PetSystemRuntimeData.cs            # Runtime state container for ingress guard
├── PetSystemMainThreadQueueData.cs    # Per-system main-thread action queue container
└── README.md                          # System documentation
```

### Related Core Contracts

- `Server/Core/World/SceneServer/Pet/IPetSystem.cs`
- `Server/Core/World/SceneServer/Pet/IPetSystemMainThreadQueueData.cs`
- `Server/Core/World/SceneServer/Pet/IPetSystemRuntimeData.cs`

### Inheritance Hierarchy

```
ServerBehaviour
└── PetSystem : IPetSystem

RuntimeDataContainer
└── PetSystemRuntimeData : IPetSystemRuntimeData

SystemMainThreadQueueData
└── PetSystemMainThreadQueueData : IPetSystemMainThreadQueueData
```

## License

This project is subject to the FishMMO project license.
