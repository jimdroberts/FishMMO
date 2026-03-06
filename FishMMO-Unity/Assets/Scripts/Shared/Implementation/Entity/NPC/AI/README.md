# AI System

## Overview

The AI system is a server-authoritative, multi-layered architecture that drives all NPC behavior in FishMMO. It combines four layers — a **behavior tree** (decision making), a **state machine** (movement/combat execution), an **ability selector** (combat logic), and an optional **boss script** (unique boss mechanics) — coordinated by a central `AIController`.

Each NPC has an `AIController` (a `CharacterBehaviour` / `NetworkBehaviour`) that manages a `NavMeshAgent`, a pluggable set of `BaseAIState` assets, an aggression (threat) table, AI LOD throttling, group awareness, and a virtual camera for aiming abilities at targets. All AI logic runs exclusively on the server — clients receive only the results via network synchronization.

States are **shared ScriptableObject assets** — every NPC referencing the same asset shares the same instance. This means **no mutable per-NPC data may be stored on the state itself**. Per-NPC runtime data (timers, targets, aggression, boss phase) lives on the `AIController` or its owned helper classes (`AggressionState`, `AggressionController`, `BossScriptState`).

### Architecture Layers

```
NPC Brain (AIController)
 ├─ Behavior Tree       ← high-level decisions ("what should I do?")
 │
 ├─ State Machine       ← movement/combat modes ("how do I do it?")
 │
 ├─ Ability Selector    ← rotation + scoring ("which ability?")
 │
 ├─ Boss Script         ← phased mechanics ("special encounter logic")
 │
 ├─ AI LOD              ← distance-based throttling ("how often to think?")
 │
 └─ Group AI            ← pack coordination ("what is my team doing?")
```

## Directory Structure

```
AI/
├── AIAbilityRotation.cs        # Condition/sequence-based ability rotation asset
├── AIController.cs             # Core AI controller (NavMeshAgent, state machine, virtual camera)
├── AILodSettings.cs            # AI LOD distance-based update throttling settings
├── AgentAvoidancePriority.cs   # Enum for NavMesh agent avoidance levels
├── AggressionController.cs     # Per-NPC threat table (plain C# class)
├── AggressionEntry.cs          # Single-character threat data
├── AggressionState.cs          # Owns AggressionController + global event subscriptions
├── BaseAIState.cs              # Abstract ScriptableObject base for all AI states
├── BehaviorTree/
│   ├── AIBehaviorNode.cs       # Abstract base for all BT nodes
│   ├── AIBehaviorTree.cs       # Root container ScriptableObject
│   ├── AICompositeNode.cs      # Base for composite nodes (Selector, Sequence)
│   ├── AISelector.cs           # OR node — tries children until one succeeds
│   ├── AISequence.cs           # AND node — runs children in order, fails on first failure
│   ├── AIInverter.cs           # Decorator — inverts child result
│   ├── AIRepeater.cs           # Decorator — repeats child N times
│   ├── AIConditionNode.cs      # Leaf — evaluates an AIAbilityCondition
│   ├── AIStateTransitionNode.cs# Leaf — transitions to a BaseAIState
│   ├── AIHasTargetNode.cs      # Leaf — checks if NPC has a target
│   ├── AIIsDeadNode.cs         # Leaf — checks if NPC is dead
│   ├── AIGroupInCombatNode.cs  # Leaf — checks if group is in combat
│   └── AIAdoptGroupTargetNode.cs # Leaf — sets NPC target to group target
├── Boss/
│   ├── BossPhase.cs            # Single boss phase definition
│   ├── BossScript.cs           # ScriptableObject defining phases + timed mechanics
│   ├── BossTimedMechanic.cs    # Interval-based ability/spawn mechanic
│   └── BossScriptState.cs      # Per-NPC runtime boss state (plain C# class)
├── Conditions/
│   ├── AIAbilityCondition.cs   # Abstract base for rotation/BT conditions
│   ├── AIBuffCondition.cs      # Check buff/debuff presence on self or target
│   ├── AIDistanceCondition.cs  # Check distance to target
│   ├── AIHealthCondition.cs    # Check health % of self or target
│   └── AIRandomCondition.cs    # Random chance condition
├── Group/
│   ├── NPCGroupRole.cs         # Enum: Tank, Healer, DPS, Support
│   ├── NPCGroupMember.cs       # Associates an AIController with a role
│   └── NPCGroup.cs             # Group coordinator MonoBehaviour
└── States/
    ├── BaseAttackingState.cs       # Base combat state
    ├── CasterAttackingState.cs     # Caster NPC combat
    ├── GetBehindState.cs           # Flanking movement
    ├── HealerAttackingState.cs     # Healer NPC combat
    ├── IdleState.cs                # Idle with randomized update rate
    ├── MeleeAttackingState.cs      # Melee NPC combat
    ├── OrbitState.cs               # Circle-strafe
    ├── PatrolState.cs              # Waypoint patrol
    ├── PetIdleState.cs             # Pet follow-owner
    ├── RangedAttackingState.cs     # Ranged NPC combat
    ├── RetreatState.cs             # Flee from target
    ├── ReturnHomeState.cs          # Return home with healing
    └── WanderState.cs              # Random wandering
```

## Inheritance Hierarchies

### AI States (ScriptableObjects)

```
CachedScriptableObject<BaseAIState>
└── BaseAIState (abstract)
    ├── BaseAttackingState
    │   ├── MeleeAttackingState
    │   ├── RangedAttackingState
    │   ├── CasterAttackingState
    │   └── HealerAttackingState
    ├── GetBehindState
    ├── IdleState
    ├── OrbitState
    ├── PatrolState
    ├── PetIdleState
    ├── RetreatState
    ├── ReturnHomeState
    └── WanderState
```

### Controller

```
CharacterBehaviour (NetworkBehaviour)
└── AIController : IAIController
        ├── IAINavigation      (Agent, Stop, Resume, speeds)
        ├── IAIStateMachine    (CurrentState, ChangeState, transitions)
        └── IAIWaypoints       (Waypoints, CurrentWaypointIndex)
```

### Aggression

```
AggressionState          (plain C# — owns controller + event subscriptions)
└── AggressionController (plain C# — threat table, decay, target picking)
    └── AggressionEntry  (plain C# — per-character threat data)
```

### Behavior Tree (ScriptableObjects)

```
ScriptableObject
├── AIBehaviorTree       (root container — references root node, tick rate)
└── AIBehaviorNode       (abstract base)
    ├── AICompositeNode  (abstract — has Children[])
    │   ├── AISelector   (OR — first child to succeed wins)
    │   └── AISequence   (AND — all children must succeed)
    ├── AIInverter       (decorator — inverts child result)
    ├── AIRepeater       (decorator — repeats child N times)
    ├── AIConditionNode  (leaf — evaluates an AIAbilityCondition)
    ├── AIStateTransitionNode (leaf — transitions to a BaseAIState)
    ├── AIHasTargetNode  (leaf — Success if target exists)
    ├── AIIsDeadNode     (leaf — Success if NPC is dead)
    ├── AIGroupInCombatNode   (leaf — Success if group is fighting)
    └── AIAdoptGroupTargetNode (leaf — adopts group's shared target)
```

### Boss Script

```
ScriptableObject
└── BossScript           (phases + timed mechanics definition)
    ├── BossPhase        ([Serializable] — HP threshold, overrides, spawns)
    └── BossTimedMechanic ([Serializable] — interval abilities/spawns)

BossScriptState          (plain C# — per-NPC runtime state, phase index, timers)
```

### Group AI

```
MonoBehaviour
└── NPCGroup             (group coordinator — shared target, roles, combat status)
    └── NPCGroupMember   ([Serializable] — AIController + NPCGroupRole)

enum NPCGroupRole        (None, Tank, Healer, DPS, Support)
```

### AI LOD

```
ScriptableObject
└── AILodSettings        (distance thresholds, stagger moduli, re-evaluate interval)

enum AILodTier           (Active, Nearby, Far, Dormant)
```

### Ability Rotation / Conditions

```
ScriptableObject
├── AIAbilityRotation    (ordered list of entries, Priority or Sequence mode)
└── AIAbilityCondition   (abstract)
    ├── AIHealthCondition
    ├── AIBuffCondition
    ├── AIDistanceCondition
    └── AIRandomCondition

[Serializable] class
└── AIAbilityRotationEntry  (template ID + list of conditions)
```

## AIController

`AIController` is the central brain attached to every NPC prefab. It is a `CharacterBehaviour` implementing `IAIController` (which composes `IAINavigation`, `IAIStateMachine`, and `IAIWaypoints`).

### Key Inspector Fields

| Field                       | Type                     | Default  | Description                                                  |
|-----------------------------|--------------------------|----------|--------------------------------------------------------------|
| `InitialState`              | `BaseAIState`            | —        | State entered on initialization                              |
| `IdleState`                 | `BaseAIState`            | —        | Passive idle state                                           |
| `WanderState`               | `BaseAIState`            | —        | Random wandering state                                       |
| `PatrolState`               | `BaseAIState`            | —        | Waypoint patrol state                                        |
| `ReturnHomeState`           | `BaseAIState`            | —        | Leash / return-home state                                    |
| `RetreatState`              | `BaseAIState`            | —        | Flee state                                                   |
| `AttackingState`            | `BaseAIState`            | —        | Combat state                                                 |
| `DeadState`                 | `BaseAIState`            | —        | Death state                                                  |
| `AbilityRotation`           | `AIAbilityRotation`      | —        | Optional condition/sequence ability rotation (see below)     |
| `BehaviorTree`              | `AIBehaviorTree`         | —        | Optional behavior tree for high-level decision making        |
| `LodSettings`               | `AILodSettings`          | —        | Optional LOD settings for distance-based AI throttling       |
| `BossScript`                | `BossScript`             | —        | Optional boss script for phased encounters                   |
| `AvoidancePriority`         | `AgentAvoidancePriority` | `Medium` | NavMeshAgent avoidance priority                              |
| `EnemySweepRate`            | `float`                  | `1.5`    | Seconds between enemy detection sweeps                       |
| `AggressionDamageWeight`    | `float`                  | `1.0`    | Threat points per 1 damage taken                             |
| `AggressionHealingWeight`   | `float`                  | `0.6`    | Threat points per 1 healing witnessed                        |
| `AggressionHitBonus`        | `float`                  | `5.0`    | Flat threat per hit                                          |
| `AggressionDecayRate`       | `float`                  | `3.0`    | Threat decay per second                                      |
| `AggressionStaleTimeout`    | `float`                  | `30.0`   | Seconds before stale entries are pruned                      |
| `AggressionVarietyChance`   | `float`                  | `0.15`   | Chance (0-1) to pick secondary threat target                 |
| `eyeTransform`              | `Transform`              | —        | Origin for vision checks and virtual camera                  |

### Runtime Properties

| Property                 | Type                  | Description                                              |
|--------------------------|-----------------------|----------------------------------------------------------|
| `Agent`                  | `NavMeshAgent`        | The navigation agent                                     |
| `CurrentState`           | `BaseAIState`         | Active state                                             |
| `Target`                 | `Transform`           | Current target (setting updates agent destination)       |
| `LookTarget`             | `Transform`           | Target for smooth facing rotation                        |
| `Home`                   | `Vector3`             | Home position for leash and wandering                    |
| `AggressionState`        | `AggressionState`     | Per-NPC threat system                                    |
| `Aggression`             | `AggressionController`| Convenience accessor for `AggressionState.Controller`    |
| `VirtualCameraPosition`  | `Vector3`             | Eye-level position for ability aiming                    |
| `VirtualCameraRotation`  | `Quaternion`          | Rotation from eye toward target center                   |
| `OrbitAngle`             | `float`               | Per-NPC orbit angle for OrbitState                       |
| `RotationIndex`          | `int`                 | Per-NPC index for Sequence-mode ability rotation         |
| `PhysicsScene`           | `PhysicsScene`        | Scene physics for overlap/raycast queries                |
| `Group`                  | `NPCGroup`            | NPC group this controller belongs to (set by NPCGroup)   |
| `GroupRole`              | `NPCGroupRole`        | This NPC's role within its group (Tank/Healer/DPS/etc.)  |
| `BossState`              | `BossScriptState`     | Runtime boss phase/mechanic state (null if no BossScript)|
| `CurrentLodTier`         | `AILodTier`           | Current LOD tier (Active/Nearby/Far/Dormant)             |
| `NpcRNG`                 | `System.Random`       | Seeded RNG from NPC for deterministic behavior           |

### Update Loop

The `AIController.Update()` method runs every frame (server-only) with multi-layered throttling:

**1. AI LOD Stagger** — When `LodSettings` is assigned, the NPC's LOD tier is re-evaluated periodically based on nearest observer distance. Each tier has a different frame stagger modulus. `Dormant` NPCs skip the entire update. Without `LodSettings`, a simple 1-in-3 frame stagger is used.

**2. Enemy Sweep** — `SweepForEnemies()` — Timer-based enemy detection. Skipped if already attacking or returning home.

**3. Leash Check** — `CheckLeash()` — Distance check to home. Warps + heals on max leash; transitions to `ReturnHomeState` on min leash. Resets boss phases if `BossScript.ResetOnLeash` is true.

**4. Behavior Tree** — If a `BehaviorTree` is assigned, it is evaluated on its own tick rate. If the tree returns `Success` (produced a state transition), the state machine's own `UpdateState` is **skipped** for that tick to avoid conflicting decisions.

**5. Boss Script** — If a `BossScript` is assigned, phase transitions are evaluated against current HP, and timed mechanics are ticked.

**6. State Machine** — `UpdateCurrentState()` — Calls `CurrentState.UpdateState()` on a per-state timer. Skipped if the behavior tree already handled this tick.

**7. Virtual Camera** — `UpdateVirtualCamera()` — Aims from eye transform toward target center.

**8. Aggression Decay** — `AggressionState.Tick()` — Throttled to 0.5s intervals.

**9. Face Target** — `FaceLookTarget()` — Smooth rotation toward `LookTarget`.

```
Update()
 │
 ├─ AI LOD stagger check ── Dormant? → return
 │
 ├─ SweepForEnemies()
 ├─ CheckLeash()
 │
 ├─ BehaviorTree.Evaluate()  ── Success? → skip state update
 ├─ BossScript (phases + mechanics)
 ├─ UpdateCurrentState()      ── only if BT didn't handle
 │
 ├─ UpdateVirtualCamera()
 ├─ AggressionState.Tick()    ── throttled 0.5s
 └─ FaceLookTarget()
```

### Ability Selection

`PickBestAbility(float preferredMaxRange)` is the central ability chooser. When an `AbilityRotation` asset is assigned, it is evaluated **first**:

1. **Rotation pass** — `AIAbilityRotation.Evaluate()` checks each entry's conditions against the current combat context. If an entry matches and its ability is usable (off cooldown, meets activation conditions), that ability is returned immediately.
2. **Fallback** — If no rotation entry matches and `FallbackToDefault` is true (or no rotation is assigned), the default scoring logic runs:
   - **In-range abilities** (range² ≥ distance²): score = `1000 + cooldown` (longer cooldown = typically stronger).
   - **Out-of-range abilities**: score = `range` (fallback).
   - A random jitter of 0-50 is added to prevent deterministic choices.
   - Abilities on cooldown or lacking resources are skipped.

This means designers can set up precise condition-based rotations while still getting sensible default behaviour for any gaps in the rotation.

### Virtual Camera

NPCs do not have a player camera. Instead, `AIController` maintains a virtual camera (`VirtualCameraPosition` / `VirtualCameraRotation`) computed from the `eyeTransform` aimed at the target's collider center. The ability system reads this to aim projectiles, identical to how `KCCController.VirtualCameraPosition` works for players.

## State Lifecycle

Every AI state implements three methods from `BaseAIState`:

| Method            | When Called                                         | Purpose                                |
|-------------------|-----------------------------------------------------|----------------------------------------|
| `Enter()`         | On transition into the state                        | Setup, destination setting, speed      |
| `UpdateState()`   | On a timer (`GetUpdateRate()`, default 1.0s)        | Core per-tick logic                    |
| `Exit()`          | On transition out of the state                      | Cleanup, target clearing, speed reset  |

## State Transition Diagram

```
                              enemies detected
┌──────────┐ ──────────────────────────────────> ┌───────────────────────┐
│   Idle   │                                     │   AttackingState      │
└──────────┘ <────────────────────────────────── └───────────────────────┘
      ▲         target lost / killed                   │  │  │
      │                                                │  │  │
      │     ┌──────────────────────────────────────────┘  │  │
      │     │  TransitionToRandomMovementState()          │  │
      │     ▼                                             │  │
      │  ┌────────────┐                                   │  │
      │  │   Wander   │                                   │  │
      │  └────────────┘                                   │  │
      │  ┌────────────┐                                   │  │
      │  │   Patrol   │                                   │  │
      │  └────────────┘                                   │  │
      │                                                   │  │
      │         leash exceeded (min)                      │  │
      │  ┌─────────────────┐ <────────────────────────────┘  │
      │  │  ReturnHome     │                                 │
      │  └─────────────────┘                                 │
      │         leash exceeded (max) → warp + heal + interrupt
      │                                                      │
      │         variety transitions (melee only)             │
      │  ┌─────────────────┐ <───────────────────────────────┘
      │  │  OrbitState     │
      │  │  GetBehindState │
      │  └─────────────────┘
      │                                     ┌──────────────────┐
      └─────────────────────────────────────│  RetreatState    │
                                            └──────────────────┘
                                              (ranged / caster flee)

Pet-specific:
  PetIdleState ── follow owner, warp if path invalid
```

## Aggression (Threat) System

The aggression system tracks per-character threat so NPCs make intelligent target decisions rather than always attacking the nearest enemy.

### Architecture

- **`AggressionState`** — Per-NPC wrapper created in `AIController.InitializeOnce()`. Owns the `AggressionController` and subscribes to global `ICharacterDamageController` events (`OnDamaged`, `OnHealed`, `OnKilled`). Destroyed in `AIController.OnDestroying()`.
- **`AggressionController`** — The threat table itself. Keyed by character ID (`long`). Pools `AggressionEntry` instances to avoid allocations.
- **`AggressionEntry`** — Per-character data: `Points`, `HitCount`, `TotalDamage`, `TotalHealing`, `LastEventTime`.

### Threat Sources

| Event                          | Points Added                                          |
|--------------------------------|-------------------------------------------------------|
| NPC takes damage               | `amount × DamageWeight + HitBonusPoints`              |
| Character heals a tracked ally | `amount × HealingWeight`                              |
| Custom (taunt, proximity)      | `AggressionController.AddPoints(characterID, points)` |

### Decay and Pruning

`AggressionController.Tick(deltaTime)` runs every frame:
1. All entries decay by `DecayRate × deltaTime`.
2. Entries with 0 points and `(now - LastEventTime) > StaleEntryTimeout` are removed and pooled.

### Target Selection

`AggressionController.PickTarget(candidates)`:
1. Finds the highest-threat and second-highest-threat candidates (alive, active).
2. With probability `TargetVarietyChance`, returns the second-highest instead of the top.
3. Keeps combat from being perfectly deterministic.

### Mid-Combat Re-evaluation

`BaseAttackingState.ReevaluateTarget()` runs on a timer (`TargetReevaluationRate`, default 3s). If any tracked character exceeds the current target's threat by `AggressionSwitchThreshold` (default 50 points), the NPC switches targets. The timer is stored on `AggressionState.TargetReevaluationTimer` (per-NPC, not on the shared ScriptableObject).

## Movement States

### IdleState

**Menu:** *FishMMO > Character > NPC > AI > Idle State*

The NPC stops moving and waits. Optionally randomizes its update interval for natural variation.

| Field              | Type    | Default | Description                                           |
|--------------------|---------|---------|-------------------------------------------------------|
| `RandomUpdateRate` | `float` | `0`     | If > base rate, randomizes the update interval        |

- **Enter:** Stops the agent.
- **UpdateState:** If target is null or beyond leash, transitions to random movement.
- **Exit:** Clears `Target`, calls `Agent.ResetPath()`.

### WanderState

**Menu:** *FishMMO > Character > NPC > AI > Wander State*

The NPC wanders randomly within a radius of its home position.

| Field              | Type    | Default | Description                                           |
|--------------------|---------|---------|-------------------------------------------------------|
| `AlwaysPickNew`    | `bool`  | —       | Always pick a new destination on update               |
| `WanderRadius`     | `float` | —       | Radius around home for random destinations            |
| `IdleChance`       | `float` | —       | Chance to idle on arrival instead of picking new point |

- **Enter / Exit:** No-op.
- **UpdateState:** On arrival (or always if `AlwaysPickNew`): 50% chance to transition idle, 50% chance to pick a new random home destination within `WanderRadius`. On invalid path: picks a new destination.

### PatrolState

**Menu:** *FishMMO > Character > NPC > AI > Patrol State*

The NPC follows a waypoint path set by the `ObjectSpawner`.

- **Enter:** Picks the nearest waypoint and sets it as destination.
- **UpdateState:** On arrival at a waypoint, transitions to the next one via `TransitionToNextWaypoint()`.
- **Exit:** No-op.

### ReturnHomeState

**Menu:** *FishMMO > Character > NPC > AI > Return Home State*

Triggered by the leash system. The NPC runs home, heals, and clears combat state.

| Field      | Type   | Default | Description                            |
|------------|--------|---------|----------------------------------------|
| `HealOnReturn` | `bool` | `true`  | Fully heal when entering this state |

- **Enter:** Clears `Target`/`LookTarget`, sets run speed, sets destination to home, optionally heals.
- **UpdateState:** On arrival, transitions to random movement.
- **Exit:** Resets to walk speed.

### RetreatState

**Menu:** *FishMMO > Character > NPC > AI > Retreat State*

The NPC flees away from its target to a safe distance.

| Field          | Type    | Default | Description                                 |
|----------------|---------|---------|---------------------------------------------|
| `SafeDistance`  | `float` | —       | Distance to flee from the target             |
| `RetreatSpeed`  | `float` | —       | Override speed while fleeing                 |

- **Enter:** Calculates a direction away from the target, NavMesh-samples a retreat position.
- **UpdateState:** If target lost → idle. On arrival: if far enough → idle; else recalculates a new retreat position.
- **Exit:** No-op.

### OrbitState

**Menu:** *FishMMO > Character > NPC > AI > Orbit State*

Circle-strafes around the current target at a configurable radius and speed.

| Field         | Type    | Default | Description                               |
|---------------|---------|---------|-------------------------------------------|
| `OrbitRadius` | `float` | —       | Distance from the target to orbit at       |
| `OrbitSpeed`  | `float` | —       | Angular speed of the orbit (degrees/tick)  |
| `MaxOrbits`   | `int`   | —       | Number of full circles before transitioning|

- **Enter:** Resets orbit angle. Transitions to idle if no target.
- **UpdateState:** Increments angle, computes position on a circle around the target, NavMesh-samples, sets destination. Slerps rotation toward target.
- **Exit:** No-op.

The orbit angle is stored per-NPC on `AIController.OrbitAngle` to avoid the shared ScriptableObject mutable-state problem.

### GetBehindState

**Menu:** *FishMMO > Character > NPC > AI > Get Behind State*

Moves the NPC behind the target for a flanking attack.

| Field             | Type    | Default | Description                               |
|-------------------|---------|---------|-------------------------------------------|
| `BehindDistance`  | `float` | —       | How far behind the target to position      |
| `ApproachSpeed`   | `float` | —       | Speed while flanking                       |

- **Enter:** Calculates a position opposite the target's forward direction, NavMesh-samples it.
- **UpdateState:** Slerps rotation toward target. On arrival → transitions to idle (or back to attacking).
- **Exit:** No-op.

### PetIdleState

**Menu:** *FishMMO > Character > NPC > AI > Pet Idle State*

Keeps a pet NPC following its owner at a comfortable distance.

- **Enter:** Sets run speed.
- **UpdateState:** Finds the owner via the `Pet` component. If path is invalid → warps to owner. If too far → moves to nearest position on a sphere around the owner. Otherwise stays idle.
- **Exit:** No-op.

## Combat States

All combat states extend `BaseAttackingState`, which provides shared logic for target validation, ability activation, positioning, aggression-based target selection, and mid-combat re-evaluation.

### BaseAttackingState

**Menu:** *FishMMO > Character > NPC > AI > Attacking State*

The foundational combat state. Can be used directly for generic NPCs or subclassed for specialized archetypes.

| Field                        | Type    | Default | Description                                              |
|------------------------------|---------|---------|----------------------------------------------------------|
| `PreferredDistance`          | `float` | `0`     | Preferred combat distance (0 = melee range)              |
| `MinComfortDistance`         | `float` | `0`     | Distance below which the NPC retreats (0 = never)        |
| `AttackCooldown`            | `float` | `1.5`   | Minimum seconds between ability activations              |
| `TargetReevaluationRate`    | `float` | `3.0`   | Seconds between mid-combat threat re-evaluation          |
| `AggressionSwitchThreshold` | `float` | `50`    | Threat lead required to switch targets mid-combat        |

**Behavior:**

- **Enter:** Sets agent to run speed.
- **Exit:** Resets to walk speed, clears `Target`/`LookTarget`, interrupts any active ability.
- **UpdateState:** Checks if NPC is dead → transitions out. Validates target is alive and active. Calls `TryAttack()`, then `ReevaluateTarget()`.
- **TryAttack:** If an ability is currently activating → stops the agent (prevents movement from canceling casts/channels). Auto-releases charged abilities when their activation time completes. Otherwise picks the best ability, checks range, and either attacks or moves closer.
- **PerformAttack:** Stops the agent. Queries `RequiresHeld()` on the ability to correctly pass `isHeld=true` for channeled and charged abilities. Calls `AbilityController.Activate()`.
- **ManagePositioning:** Refuses to reposition while an ability is activating. Retreats if too close, closes distance if too far, or holds position.
- **PickTarget:** Uses `AggressionController.PickTarget()` for threat-based selection, falling back to first-alive candidate.

### MeleeAttackingState

**Menu:** *FishMMO > Character > NPC > AI > Melee Attacking State*

Close-range fighters that rush into melee range and occasionally introduce movement variety.

| Field                    | Type          | Default | Description                                        |
|--------------------------|---------------|---------|----------------------------------------------------|
| `OrbitState`             | `BaseAIState` | —       | Optional orbit state for mid-combat variety         |
| `GetBehindState`         | `BaseAIState` | —       | Optional flanking state for mid-combat variety      |
| `MovementVarietyChance`  | `float`       | `0.15`  | Chance per attack cycle to orbit or flank           |
| `SprintDistanceMultiplier`| `float`      | `3.0`   | Distance multiplier beyond which the NPC sprints    |

**Recommended config:** `PreferredDistance = 0`, `MinComfortDistance = 0`.

**Behavior:**

- Calculates melee range from `Agent.radius × 2` (min 1.0).
- Stops and waits while casting. Auto-releases charged abilities.
- Occasionally transitions to `OrbitState` or `GetBehindState` for movement variety when close to the target.
- Picks abilities with max range = `meleeRange × 2` (short-range bias).
- Closes the gap aggressively when out of range.

### RangedAttackingState

**Menu:** *FishMMO > Character > NPC > AI > Ranged Attacking State*

Ranged fighters (archers, hunters) that maintain preferred distance, kite when threatened, and strafe for variety.

| Field                       | Type          | Default | Description                                          |
|-----------------------------|---------------|---------|------------------------------------------------------|
| `StrafeState`               | `BaseAIState` | —       | Optional orbit/strafe state while shooting            |
| `RetreatState`              | `BaseAIState` | —       | Optional retreat state for emergency flee              |
| `StrafeChance`              | `float`       | `0.2`   | Chance per attack cycle to strafe while shooting      |
| `EmergencyRetreatThreshold` | `float`       | `0.5`   | Fraction of MinComfortDistance triggering emergency    |

**Recommended config:** `PreferredDistance = 10-20`, `MinComfortDistance = 4-6`.

**Behavior:**

- **Emergency retreat:** If target is closer than `MinComfortDistance × EmergencyRetreatThreshold`, uses `RetreatState` or built-in retreat.
- **Kiting:** In the discomfort zone, fires an ability while backing away simultaneously.
- **Comfortable range:** Picks an ability, optionally strafes after firing.
- **ManagePositioning:** Retreats if too close, closes in if too far (never closer than preferred), else holds.

### CasterAttackingState

**Menu:** *FishMMO > Character > NPC > AI > Caster Attacking State*

Casters (mages, warlocks) that prioritize maximum distance, use longest-range abilities, and retreat aggressively.

| Field                       | Type          | Default | Description                                          |
|-----------------------------|---------------|---------|------------------------------------------------------|
| `RetreatState`              | `BaseAIState` | —       | Optional retreat state for emergency flee              |
| `WanderState`               | `BaseAIState` | —       | Optional wander state for cooldown repositioning      |
| `EmergencyRetreatThreshold` | `float`       | `0.4`   | Fraction of MinComfortDistance triggering emergency    |
| `CooldownRepositionChance`  | `float`       | `0.3`   | Chance to wander/reposition when all abilities cooling |

**Recommended config:** `PreferredDistance = 15-30`, `MinComfortDistance = 8-12`, `AttackCooldown = 2-3`.

**Behavior:**

- **Emergency retreat:** Interrupts any pending cast and flees if target is dangerously close.
- **Kiting:** Fires a quick spell while retreating when in the discomfort zone.
- **Cooldown repositioning:** When all abilities are cooling down, has a chance to wander to a new position, making the caster harder to pin down.
- **Comfortable range:** Stops completely and casts. Tries to stay at the edge of ability range.

### HealerAttackingState

**Menu:** *FishMMO > Character > NPC > AI > Healer Attacking State*

Healer NPCs that prioritize keeping allies alive before dealing damage. Scans for injured allies, selects the most wounded one, and uses heal abilities on them. Falls back to standard damage behavior when no ally needs healing.

| Field                       | Type          | Default | Description                                          |
|-----------------------------|---------------|---------|------------------------------------------------------|
| `AllyLayers`                | `LayerMask`   | —       | Physics layers to scan for allies                    |
| `AllyScanRadius`            | `float`       | `20`    | Radius within which to scan for injured allies       |
| `HealThreshold`             | `float`       | `0.75`  | Health fraction below which an ally is considered injured |
| `HealAbilityTemplateIDs`    | `List<int>`   | —       | Template IDs of abilities treated as heals           |
| `RetreatState`              | `BaseAIState` | —       | Optional retreat state for emergency flee              |
| `EmergencyRetreatThreshold` | `float`       | `0.5`   | Fraction of MinComfortDistance triggering emergency    |

**Recommended config:** `PreferredDistance = 15-25`, `MinComfortDistance = 6-10`.

**Behavior:**

1. **Emergency retreat:** Identical to caster — flees if enemy is dangerously close.
2. **Ally scan:** Uses `PhysicsScene.OverlapSphere()` with `AllyLayers` to find nearby characters. Filters by:
   - Not self.
   - `FactionAllianceLevel.Ally` (faction check).
   - Alive and health below `HealThreshold`.
3. **Heal priority:** If an injured ally is found and a heal ability is available:
   - If in range: faces the ally and activates the heal ability.
   - If out of range: moves toward the ally.
4. **Damage fallback:** When no ally needs healing, behaves like a caster — picks the best damage ability (non-heal), kites if uncomfortable, retreats if too close.
5. **Ability classification:** Abilities whose `Template.ID` appears in `HealAbilityTemplateIDs` are treated as heals. All others are damage. The list is converted to a `HashSet<int>` on first use for O(1) lookup.

## Ability Rotation System

The ability rotation system gives designers fine-grained control over which abilities an NPC uses and when. It replaces or supplements the default scoring-based picker with condition-driven, ordered ability selection.

### AIAbilityRotation

**Menu:** *FishMMO > Character > NPC > AI > Ability Rotation*

| Field              | Type                           | Default    | Description                                      |
|--------------------|--------------------------------|------------|--------------------------------------------------|
| `Mode`             | `AIRotationMode`               | `Priority` | Evaluation strategy (see below)                  |
| `Entries`          | `List<AIAbilityRotationEntry>` | —          | Ordered ability entries with conditions           |
| `FallbackToDefault`| `bool`                         | `true`     | Use default scorer when no entry matches          |

#### Evaluation Modes

| Mode       | Behavior                                                                                       |
|------------|------------------------------------------------------------------------------------------------|
| `Priority` | Entries are evaluated top-to-bottom. The **first** entry whose conditions all pass and whose ability is usable is selected. Ideal for conditional logic ("if health ≤ 40%, heal; else fireball"). |
| `Sequence` | Advances through entries in order after each successful use. If the next-in-sequence entry can't be used, wraps around and tries remaining entries. Ideal for structured rotations ("Fireball → Frost Bolt → Pyroblast → repeat"). The current position is stored per-NPC in `AIController.RotationIndex`. |

### AIAbilityRotationEntry

Each entry is a `[Serializable]` class pairing an ability template with conditions:

| Field              | Type                        | Description                                              |
|--------------------|-----------------------------|----------------------------------------------------------|
| `AbilityTemplateID`| `int`                       | Template ID of the ability to use                        |
| `Conditions`       | `List<AIAbilityCondition>`  | All must pass (AND logic). Empty = unconditional.        |

The system finds the matching `Ability` instance from `IAbilityController.KnownAbilities` by template ID, then verifies it is off cooldown and meets activation conditions before returning it.

### Conditions

Conditions are ScriptableObject assets that can be shared across multiple rotations.

#### AIHealthCondition

**Menu:** *FishMMO > Character > NPC > AI > Conditions > Health Condition*

Checks a character's health percentage against a threshold.

| Field       | Type                 | Default       | Description                        |
|-------------|----------------------|---------------|------------------------------------|
| `Subject`   | `ConditionSubject`   | `Self`        | Which character to check           |
| `Operator`  | `ComparisonOperator` | `LessOrEqual` | Comparison operation               |
| `Threshold` | `float` (0-1)       | `0.5`         | Health % threshold                 |

**Examples:** "Self health ≤ 40%" → use a heal. "Target health ≤ 20%" → use an execute.

#### AIBuffCondition

**Menu:** *FishMMO > Character > NPC > AI > Conditions > Buff Condition*

Checks whether a character has (or lacks) a specific buff or debuff.

| Field            | Type               | Default  | Description                              |
|------------------|--------------------|----------|------------------------------------------|
| `Subject`        | `ConditionSubject` | `Target` | Which character to check                 |
| `BuffTemplateID` | `int`              | —        | Template ID of the buff/debuff           |
| `RequirePresent` | `bool`             | `true`   | True = must have, False = must lack      |

**Examples:** "Target missing debuff 7" → apply a DoT. "Self has buff 42" → skip re-applying.

#### AIDistanceCondition

**Menu:** *FishMMO > Character > NPC > AI > Conditions > Distance Condition*

Checks the distance between the NPC and its target.

| Field      | Type                 | Default       | Description                 |
|------------|----------------------|---------------|-----------------------------|
| `Operator` | `ComparisonOperator` | `LessOrEqual` | Comparison operation        |
| `Distance` | `float`              | `5`           | Threshold in world units    |

**Examples:** "Distance ≤ 3" → use melee cleave. "Distance ≥ 15" → use snipe.

#### AIRandomCondition

**Menu:** *FishMMO > Character > NPC > AI > Conditions > Random Condition*

Passes with a configurable random probability.

| Field    | Type           | Default | Description                           |
|----------|----------------|---------|---------------------------------------|
| `Chance` | `float` (0-1)  | `0.5`   | Probability of passing each evaluation|

**Example:** Attach to a special attack entry with `Chance = 0.2` for 20% variety.

### Setup Example

To create a boss NPC that heals at low health, applies a debuff when the target doesn't have it, and otherwise uses a fireball rotation:

1. **Create conditions:**
   - `SelfHealthLow40.asset` — `AIHealthCondition` with `Subject = Self`, `Operator = LessOrEqual`, `Threshold = 0.4`
   - `TargetMissingPoison.asset` — `AIBuffCondition` with `Subject = Target`, `BuffTemplateID = 7`, `RequirePresent = false`

2. **Create rotation:** `BossRotation.asset` — `AIAbilityRotation` with `Mode = Priority`, `FallbackToDefault = true`:
   - Entry 0: `AbilityTemplateID = 10` (Heal), Conditions: `[SelfHealthLow40]`
   - Entry 1: `AbilityTemplateID = 7` (Poison), Conditions: `[TargetMissingPoison]`
   - Entry 2: `AbilityTemplateID = 3` (Fireball), Conditions: `[]` (unconditional fallback)

3. **Assign** the rotation to the NPC's `AIController.AbilityRotation` field.

Result: The boss heals when below 40%, applies poison when the target doesn't have it, and otherwise fireballs. If all three are on cooldown, the default scorer picks whatever is available.

## Ability Activation Pipeline (AI)

NPCs activate abilities through the same `AbilityController.Activate()` pipeline as players, using FishNet prediction/reconciliation. The AI integration works as follows:

### Activation Flow

1. **Ability selection:** The attacking state calls `AIController.PickBestAbility()` to choose an ability.
2. **Held detection:** `IAbilityController.RequiresHeld(abilityID)` returns `true` for channeled (`ChanneledTemplate`) or charged (`ChargedTemplate`) abilities.
3. **Activate:** The state calls `abilityController.Activate(ability.ID, held)` which queues the ability.
4. **Movement stop:** While `IsActivating || AbilityQueued`, all attacking states force `Agent.isStopped = true` to prevent NavMeshAgent movement from canceling casts.
5. **Channel maintenance:** Channeled abilities remain active because `isHeld=true` was passed to `Activate()`. The `Replicate()` method continues spawning channeled ability objects each tick.
6. **Charged release:** When `RemainingActivationTime` reaches 0, the attacking state calls `abilityController.Release()` to fire the charged ability.
7. **Completion:** `AbilityController` internally calls `Cancel()` to reset state, and the attacking state picks a new ability on the next update.

### Key Interface Methods

| Method                        | Purpose                                                  |
|-------------------------------|----------------------------------------------------------|
| `Activate(id, isHeld)`       | Queue an ability for activation                          |
| `Interrupt(attacker)`         | Queue an interrupt (processed next tick)                 |
| `Release()`                   | Release held state (fires charged, stops channel)        |
| `RequiresHeld(id)`            | Check if ability needs held input                        |
| `IsActivating`                | True if `currentAbilityID != NO_ABILITY`                 |
| `AbilityQueued`               | True if `queuedAbilityID != NO_ABILITY`                  |
| `RemainingActivationTime`     | Remaining cast/channel time for the active ability       |

## Leash System

`AIController.CheckLeash()` prevents NPCs from being kited indefinitely:

| Condition                        | Action                                                    |
|----------------------------------|-----------------------------------------------------------|
| Distance² > `MaxLeashRange²`    | Interrupt active ability → full heal → warp home → clear aggression |
| Distance² > `MinLeashRange²`    | Transition to `ReturnHomeState`                           |
| `LeashUpdateRate ≤ 0`           | Skip leash check                                         |
| Already in `ReturnHomeState`    | Skip leash check                                         |

Leash parameters (`MinLeashRange`, `MaxLeashRange`, `LeashUpdateRate`) are defined **per-state** on `BaseAIState`, allowing different leash behavior for different states (e.g., tighter leash while wandering, looser while attacking).

## Enemy Detection

`BaseAIState.SweepForEnemies()` uses `PhysicsScene.OverlapSphere()` with configurable `DetectionRadius` and `EnemyLayers`:

1. Ignores the NPC's own collider.
2. Faction alliance check — only `FactionAllianceLevel.Enemy` targets pass.
3. Line-of-sight raycast via `HasLineOfSight()` using `LineOfSightBlockingLayers`.

Sweeps occur on a timer (`EnemySweepRate`, default 1.5s) and are skipped when already in the attacking state or returning home.

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

- `height` and `radius` are auto-set from the NPC's collider via `TryGetDimensions()`.
- Falls back to `height = 2.0f`, `radius = 0.5f` if no collider is found.
- Speed toggles between `Constants.Character.WalkSpeed` (non-combat) and `Constants.Character.RunSpeed` (combat / return home).

## External Integration Points

| System                | Integration                                                                       |
|-----------------------|-----------------------------------------------------------------------------------|
| **ObjectSpawner**     | Spawns NPC prefabs, provides home position and waypoints                          |
| **AbilityController** | AI activates abilities via `Activate()` / `Interrupt()` / `Release()` pipeline    |
| **Faction System**    | `FactionAllianceLevel` determines enemy detection and healer ally scanning        |
| **Damage System**     | `ICharacterDamageController` events drive aggression; `CompleteHeal()` on leash   |
| **Cooldown System**   | `ICooldownController` checked during ability selection                            |
| **Target System**     | `ITargetController.UpdateTarget()` provides aim info for NPC ability spawning     |
| **Pet System**        | `PetIdleState` uses `Pet.PetOwner` for follow behavior                            |
| **Buff System**       | NPCs receive buffs that modify attributes, affecting combat                       |