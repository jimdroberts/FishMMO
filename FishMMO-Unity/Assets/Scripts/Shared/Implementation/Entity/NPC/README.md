# NPC System

## Overview

The NPC system is a server-authoritative, template-driven framework for non-player characters in FishMMO. It provides AI state-machine behavior via NavMeshAgent navigation, deterministic attribute generation with seeded RNG, pet entity management, NPC guild templates, and FishNet network synchronization. NPCs are spawned by `ObjectSpawner`, receive network payloads for client-side reproduction, and use a pluggable ScriptableObject-based state machine for all behavior logic.

## Directory Structure

```
NPC/
├── NPC.cs                          # Core NPC class (BaseCharacter + ISpawnable)
├── AI/
│   ├── AIController.cs             # NavMeshAgent-based AI state machine controller
│   ├── IAIController.cs            # Composite AI controller interface (inherits IAINavigation, IAIStateMachine, IAIWaypoints)
│   ├── IAINavigation.cs            # Navigation sub-interface (Agent, speeds, movement)
│   ├── IAIStateMachine.cs          # State machine sub-interface (state transitions, updates)
│   ├── IAIWaypoints.cs             # Waypoints sub-interface (waypoint list, current index)
│   ├── BaseAIState.cs              # Abstract ScriptableObject base for all AI states
│   ├── AgentAvoidancePriority.cs   # Enum for NavMesh agent avoidance levels
│   └── States/
│       ├── BaseAttackingState.cs   # Combat state (target picking, range, attack)
│       ├── GetBehindState.cs       # Flanking movement behind a target
│       ├── IdleState.cs            # Idle/wait with randomized update rate
│       ├── OrbitState.cs           # Orbit around a target at a radius
│       ├── PatrolState.cs          # Waypoint-based patrol movement
│       ├── PetIdleState.cs         # Pet follow-owner idle behavior
│       ├── RetreatState.cs         # Flee from target to safe distance
│       ├── ReturnHomeState.cs      # Return to home position with healing
│       └── WanderState.cs          # Random wandering within a radius
├── Attribute/
│   ├── NPCAttribute.cs            # NPC attribute definition (scalar, random, range)
│   └── NPCAttributeDatabase.cs    # ScriptableObject database of NPC attributes
├── NPCGuild/
│   └── NPCGuildTemplate.cs        # NPC guild template with archetypes and requirements
└── Pet/
    ├── IPetController.cs           # Pet controller interface with events
    ├── Pet.cs                      # Pet NPC (extends NPC with owner and abilities)
    └── PetController.cs            # Per-character pet management controller
```

## Inheritance Hierarchies

### Runtime Entities

```
BaseCharacter (NetworkBehaviour)
└── NPC : ISceneObject, ISpawnable
    └── Pet
```

### AI States (ScriptableObjects)

```
CachedScriptableObject<BaseAIState>
└── BaseAIState (abstract)
    ├── BaseAttackingState
    ├── GetBehindState
    ├── IdleState
    ├── OrbitState
    ├── PatrolState
    ├── PetIdleState
    ├── RetreatState
    ├── ReturnHomeState
    └── WanderState
```

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
├── AIController   : IAIController (inherits IAINavigation, IAIStateMachine, IAIWaypoints)
└── PetController  : IPetController
```

## AI State Machine

The AI system uses a **ScriptableObject-based state machine** pattern. Each state is a `BaseAIState` asset assigned to the `AIController` via the Unity Inspector. The controller manages transitions, timers, and the NavMeshAgent.

### State Lifecycle

| Method          | Description                                                             |
|-----------------|-------------------------------------------------------------------------|
| `Enter()`       | Called when transitioning into the state. Setup and destination setting. |
| `UpdateState()` | Called on a timer-based interval (`GetUpdateRate()`). Core logic.       |
| `Exit()`        | Called when transitioning out of the state. Cleanup and reset.          |

### State Transitions

```
┌──────────┐   enemies detected   ┌───────────────────┐
│  Idle    │─────────────────────>│  BaseAttackingState │
└──────────┘                      └───────────────────┘
     ▲                                     │
     │  target lost                        │ target killed / lost
     │                                     ▼
     │                            ┌──────────────────────┐
     └────────────────────────────│ TransitionToRandom    │
                                  │  MovementState()      │
                                  └──────────────────────┘
                                       │         │
                           ┌───────────┘         └───────────┐
                           ▼                                 ▼
                    ┌────────────┐                   ┌──────────────┐
                    │  Wander    │                   │   Patrol     │
                    └────────────┘                   └──────────────┘
                           │                                 │
                           └──────────┐    ┌─────────────────┘
                                      ▼    ▼
                              ┌──────────────────┐
                              │  ReturnHome      │──> (leash exceeded)
                              └──────────────────┘

Special states:
  RetreatState  ──  flee from target to SafeDistance
  GetBehindState ── flank behind target
  OrbitState     ── circle around target at radius
  PetIdleState   ── follow owner, warp if path invalid
```

### Movement States

The `AIController` builds a list of available movement states from non-null inspector references (`WanderState`, `PatrolState`, `ReturnHomeState`, `IdleState`). `TransitionToRandomMovementState()` picks one at random for naturalistic behavior.

### Enemy Detection

`BaseAIState.SweepForEnemies()` uses `PhysicsScene.OverlapSphere()` with configurable `DetectionRadius` and `EnemyLayers`. Results are filtered by:
1. Ignoring the NPC's own collider.
2. Faction alliance check — only `FactionAllianceLevel.Enemy` targets pass.
3. Line-of-sight raycast via `HasLineOfSight()` using `LineOfSightBlockingLayers`.

Enemy sweeps occur on a timer (`EnemySweepRate`, default 1.5s) and are skipped when already attacking or returning home.

### Leash System

The `AIController.CheckLeash()` method prevents NPCs from wandering too far:

| Condition                                  | Action                              |
|--------------------------------------------|-------------------------------------|
| Distance² > `MaxLeashRange²`               | Warp home, full heal                |
| Distance² > `MinLeashRange²`               | Transition to `ReturnHomeState`     |
| `LeashUpdateRate <= 0` or already returning | Skip leash check                   |

Leash parameters are defined per-state on `BaseAIState`, allowing different leash ranges for different behaviors.

## NPC Spawning and Initialization

### Server-Side

1. `ObjectSpawner` instantiates the NPC prefab.
2. `NPC.OnAwake()` runs server path (`#if UNITY_SERVER`):
   - Registers as a `SceneObject`.
   - Generates a deterministic seed via static `System.Random`.
   - Creates a per-NPC `System.Random` from that seed.
   - Calls `AddNPCAttributes(true)` to apply attribute bonuses and set `CurrentValue` for resources.
3. `AIController.Initialize(home, waypoints)` sets home position, configures NavMeshAgent dimensions from collider, and enters the initial state.

### Client-Side

1. `NPC.ReadPayload()` reads `ID` and `npcSeed` from the network.
2. Instantiates a local `System.Random` with the received seed.
3. Calls `AddNPCAttributes(false)` — applies the same modifier values deterministically, but does not set `CurrentValue` (server synchronizes that separately).
4. Uses the RNG to pick a model index from `RaceTemplate.Models` for visual consistency.

### Network Payload

| Field       | Type    | Description                            |
|-------------|---------|----------------------------------------|
| `ID`        | `long`  | Unique scene object identifier         |
| `npcSeed`   | `int`   | RNG seed for deterministic attributes  |

`Pet` extends the payload with an additional `ownerID` (`long`) written after the base NPC payload.

## NPC Attribute System

### NPCAttribute

Each `NPCAttribute` defines how a single attribute bonus is applied:

| Field       | Type                        | Description                                              |
|-------------|-----------------------------|----------------------------------------------------------|
| `IsScalar`  | `bool`                      | If true, value is a percentage of the current attribute   |
| `IsRandom`  | `bool`                      | If true, value is randomized between Min and Max          |
| `Min`       | `int`                       | Minimum random value                                     |
| `Max`       | `int`                       | Maximum value (used directly if not random)               |
| `Template`  | `CharacterAttributeTemplate`| The attribute type to modify                              |

### NPCAttributeDatabase

A `ScriptableObject` (`CreateAssetMenu`) containing a `List<NPCAttribute>`. Assigned to the NPC prefab via the `AttributeBonuses` field. Supports lookup by template name via `TryGetNPCAttribute()`.

### Attribute Application

`NPC.AddNPCAttributes(bool asServer)`:

1. Iterates the `AttributeBonuses.Attributes` list.
2. Determines value: `npcRNG.Next(Min, Max)` if random, otherwise `Max`.
3. Finds the matching `CharacterAttribute` or `CharacterResourceAttribute`.
4. If scalar: calculates `value.GetPercentOf(currentValue)` and sets modifier as the delta.
5. If flat: sets modifier as `value - currentValue`.
6. For resource attributes on server: also sets `CurrentValue`.

## Pet System

### Pet Entity

`Pet` extends `NPC` with:
- `PetOwner` — reference to the owning `ICharacter`.
- `PetAbilityTemplate` — defines pet abilities.
- `Abilities` — runtime list of learned ability IDs.
- Network payload includes `ownerID` after base NPC data.

### PetController

Attached to the **owner** character (not the pet). Manages:
- `Pet` reference — the active pet instance.
- Client broadcast listeners for `PetAddBroadcast` / `PetRemoveBroadcast`.
- Invokes `IPetController.OnPetSummoned` and `IPetController.OnPetDestroyed` events.

### Pet AI (PetIdleState)

- Runs at `RunSpeed` to keep up with the owner.
- Calculates follow distance as `Agent.radius * 1.5f`.
- Warps to owner if path is invalid.
- Uses `GetNearestPositionOnSphere()` to find a position near the owner.

## NPC Guild System

`NPCGuildTemplate` is a `CachedScriptableObject` defining:

| Field              | Type                      | Description                                    |
|--------------------|---------------------------|------------------------------------------------|
| `Icon`             | `Sprite`                  | Guild icon for UI                              |
| `Description`      | `string`                  | Guild description                              |
| `Archetypes`       | `List<ArchetypeTemplate>` | Archetypes associated with this guild          |
| `GuildRequirements`| `BaseCondition`           | Condition a player must meet to join/interact  |

`MeetsRequirements(IPlayerCharacter)` evaluates the condition, returning `true` if no requirements are set.

## Agent Configuration

### AgentAvoidancePriority

| Value      | Byte | Description                                |
|------------|------|--------------------------------------------|
| `None`     | 0    | Lowest priority, yields to everyone        |
| `Low`      | 25   | Yields to higher priority agents           |
| `Medium`   | 50   | Default for most agents                    |
| `High`     | 75   | Actively avoids, less likely to yield      |
| `Critical` | 100  | Avoids at all costs, rarely yields         |

### NavMeshAgent Setup

- `height` and `radius` are auto-set from the NPC's collider dimensions via `TryGetDimensions()`.
- Falls back to `height = 2.0f`, `radius = 0.5f` if no collider is found.
- Speed toggles between `Constants.Character.WalkSpeed` (non-combat) and `Constants.Character.RunSpeed` (combat/return home).

## Static Events

| Event (on `IPetController`)   | Signature            | Description                        |
|-------------------------------|----------------------|------------------------------------|
| `OnPetSummoned`               | `Action<Pet>`        | Fired when a pet is summoned       |
| `OnPetDestroyed`              | `Action`             | Fired when a pet is destroyed      |

| Event (on `Pet`)              | Signature              | Description                              |
|-------------------------------|------------------------|------------------------------------------|
| `OnReadID`                    | `Action<long, Pet>`    | Fired when pet owner ID is read from net |

## External Integration Points

The NPC system is consumed by many other systems:

- **ObjectSpawner** — Spawns and despawns NPC instances, provides home position and waypoints.
- **CharacterAttribute System** — NPC attributes are applied as modifiers via `SetModifier()`.
- **Faction System** — `FactionController` determines enemy/ally for AI detection and combat.
- **Ability System** — `BaseAttackingState` interrupts abilities on exit; NPCs use abilities for combat.
- **Damage System** — `CharacterDamageController` handles NPC death, healing, and leash recovery.
- **Buff System** — NPCs can receive buffs that modify their attributes.
- **Scene System** — `SceneObject.Register()` / `Unregister()` for scene-level tracking.
- **Race System** — `RaceTemplate.Models` used for client-side model instantiation.
- **UI System** — Pet summoned/destroyed events update client UI.
- **Database Layer** — Pet owner IDs are synchronized via network payloads.