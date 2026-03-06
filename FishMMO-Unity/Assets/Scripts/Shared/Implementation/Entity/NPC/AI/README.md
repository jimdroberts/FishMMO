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
├── AICombatPersonality.cs      # Data-driven combat personality (per-category ability weights)
├── AIController.cs             # Core AI controller (NavMeshAgent, state machine, virtual camera)
├── AILodSettings.cs            # AI LOD distance-based update throttling settings
├── AIUtility.cs                # Shared helper methods for AI systems
├── AgentAvoidancePriority.cs   # Enum for NavMesh agent avoidance levels
├── AggressionController.cs     # Per-NPC threat table (plain C# class)
├── AggressionEntry.cs          # Single-character threat data
├── AggressionState.cs          # Owns AggressionController + global event subscriptions + OnCombatInitiated
├── BaseAIState.cs              # Abstract ScriptableObject base for all AI states
├── PackTactic.cs               # Enum for group spatial positioning tactics
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
│   ├── NPCGroup.cs             # Group coordinator MonoBehaviour (pack tactics)
│   └── PackTactic.cs           # Enum: Surround, Flank, FocusFire, Kite
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
└── NPCGroup             (group coordinator — shared target, roles, pack tactics, combat status)
    └── NPCGroupMember   ([Serializable] — AIController + NPCGroupRole)

enum NPCGroupRole        (None, Tank, Healer, DPS, Support)
enum PackTactic          (None, Surround, Flank, FocusFire, Kite)
```

### Combat Personality

```
ScriptableObject
└── AICombatPersonality  (per-category ability weights, retreat threshold, combat style)

enum NPCCombatStyle      (Balanced, Aggressive, Defensive, Cautious, Berserker)
enum AbilityCategory     (Unknown, Melee, Ranged, AOE, Support)
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
| `Personality`               | `AICombatPersonality`    | —        | Optional combat personality for ability score biasing        |
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
| `RepathInterval`            | `float`                  | `0.5`    | Min seconds between `NavMeshAgent.SetDestination` calls      |
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

### Update Loop — Tier-Dispatched Architecture

`AIController.Update()` runs every frame (server-only) with a **tier-dispatched** architecture. Instead of one monolithic pipeline, each LOD tier runs only the subsystems it needs:

**1. Dormant Quick Bail** — Dormant NPCs execute a single `DormantCheckModulus` gate (~2s at 60fps). Only a LOD re-evaluation runs; all other logic is skipped entirely.

**2. LOD Re-evaluation** — Periodically recalculates the NPC's LOD tier from the nearest observer's distance. On tier transitions, `OnLodTierChanged()` fires to clean up state (e.g., disengage combat when going to Far/Dormant).

**3. Frame Stagger** — `(npcID + Time.frameCount) % staggerModulus` spreads NPC updates evenly across frames. Active ≈ 50ms, Nearby ≈ 200ms, Far ≈ 1s.

**4. Tier Dispatch** — After the stagger gate, `Update()` dispatches to a tier-specific method:

| Tier | Method | Systems Running |
|------|--------|-----------------|
| **Active** | `UpdateActive(dt)` | SweepForEnemies, CheckLeash, BehaviorTree, BossScript, StateMachine, VirtualCamera, Aggression, FaceTarget |
| **Nearby** | `UpdateNearby(dt)` | CheckLeash, StateMachine, VirtualCamera, Aggression, FaceTarget |
| **Far** | `UpdateFar(dt)` | CheckLeash, StateMachine (forces idle if in combat, clears aggression) |
| **Dormant** | *(skipped)* | LOD re-evaluation only |

```
Update()
 │
 ├─ Dormant? → DormantCheckModulus gate → LOD re-eval only → return
 │
 ├─ LOD re-evaluation (periodic)
 │   └─ OnLodTierChanged(prev, new) → cleanup on downgrade
 │
 ├─ Frame stagger gate → skip if not this NPC's frame
 │
 └─ Tier dispatch:
     ├─ Active:  SweepForEnemies → CheckLeash → BT → Boss → StateMachine → Camera → Aggression → Face
     ├─ Nearby:  CheckLeash → StateMachine → Camera → Aggression → Face
     └─ Far:     Force idle if attacking → CheckLeash → StateMachine
```

**Key differences from a flat pipeline:**
- **Nearby** tier skips `SweepForEnemies`, `BehaviorTree`, and `BossScript`. Combat entry relies entirely on the event-driven `OnThreatReceived` callback (see Event-Driven AI section below).
- **Far** tier runs no combat at all — if the NPC is in an attacking state, it immediately clears aggression, transitions to idle, and only processes leash + basic state machine (wander/idle/return home).
- **Dormant** tier runs zero game logic — only a low-frequency LOD re-evaluation to detect approaching players.

### Ability Selection

`PickBestAbility(float preferredMaxRange)` is the central ability chooser. When an `AbilityRotation` asset is assigned, it is evaluated **first**:

1. **Rotation pass** — `AIAbilityRotation.Evaluate()` checks each entry's conditions against the current combat context. If an entry matches and its ability is usable (off cooldown, meets activation conditions), that ability is returned immediately.
2. **Fallback** — If no rotation entry matches and `FallbackToDefault` is true (or no rotation is assigned), the default scoring logic runs:
   - **In-range abilities** (range² ≥ distance²): score = `1000 + cooldown` (longer cooldown = typically stronger).
   - **Out-of-range abilities**: score = `range` (fallback).
   - A random jitter of 0-50 is added to prevent deterministic choices.
   - Abilities on cooldown or lacking resources are skipped.

**Ability Caching:** Both `PickBestAbility` and `HasAbilityInRange` iterate a **flat cached `List<Ability>`** instead of the dictionary's enumerator. `RebuildAbilityCacheIfDirty()` rebuilds this list only when `KnownAbilities.Count` changes (learning/unlearning an ability), avoiding per-tick dictionary enumeration and enumerator allocation for hundreds of NPCs.

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

`AIController.CheckLeash()` prevents NPCs from being kited indefinitely. **Aggression is always cleared on leash** to prevent pingpong — without this the threat table could immediately pull the NPC back into combat after arriving home.

| Condition                        | Action                                                    |
|----------------------------------|-----------------------------------------------------------|
| Distance² > `MaxLeashRange²`    | Interrupt ability → full heal → warp home → **clear aggression** → reset boss |
| Distance² > `MinLeashRange²`    | **Clear aggression** → transition to `ReturnHomeState`    |
| `LeashUpdateRate ≤ 0`           | Skip leash check                                         |
| Already in `ReturnHomeState`    | Skip leash check                                         |

Additional leash-adjacent paths that also clear aggression:

| Path                             | Action                                                    |
|----------------------------------|-----------------------------------------------------------|
| `OnLodTierChanged` → Far/Dormant | Interrupt ability → full heal → **clear aggression** → reset boss → idle |
| `UpdateFar` (combat disengage)   | **Clear aggression** → transition to idle                 |

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
| **NPCGroup**          | Group coordinator provides shared target, role assignments, combat status          |
| **BossScript**        | Phased encounter logic with timed mechanics, spawned adds, and overrides          |
| **AI LOD**            | `AILodSettings` throttles update frequency by distance from nearest player        |
| **Behavior Tree**     | `AIBehaviorTree` provides high-level decision layer above the state machine       |

---

## Behavior Tree System

The behavior tree provides a high-level decision-making layer that sits **above** the state machine. While the state machine handles execution (how to move, how to attack), the behavior tree decides **what to do** — selecting states, evaluating world conditions, and coordinating complex behaviors that span multiple states.

### AIBehaviorTree

**Menu:** *FishMMO > Character > NPC > AI > Behavior Tree*

| Field      | Type             | Default | Description                                    |
|------------|------------------|---------|------------------------------------------------|
| `Root`     | `AIBehaviorNode` | —       | Root node of the tree (typically a Selector)   |
| `TickRate` | `float`          | `0.5`   | Seconds between tree evaluations               |

The tree is a **shared ScriptableObject** — all NPCs referencing the same asset share the instance. No mutable state is stored on the tree or its nodes; all per-NPC state lives on `AIController`.

### Node Types

#### Composite Nodes

| Node          | Behavior                                                                      |
|---------------|-------------------------------------------------------------------------------|
| `AISelector`  | OR logic — evaluates children left-to-right, returns first `Success`. If all fail, returns `Failure`. |
| `AISequence`  | AND logic — evaluates children in order. Returns `Failure` on first failure. Returns `Success` only if all succeed. |

#### Decorator Nodes

| Node          | Behavior                                                                      |
|---------------|-------------------------------------------------------------------------------|
| `AIInverter`  | Inverts child result: `Success` ↔ `Failure`. `Running` passes through.       |
| `AIRepeater`  | Repeats child `RepeatCount` times. `RepeatCount = 0` means infinite (always returns `Running`). |

#### Leaf Nodes

| Node                      | Behavior                                                               |
|---------------------------|------------------------------------------------------------------------|
| `AIConditionNode`         | Evaluates an `AIAbilityCondition` asset. Reuses the same conditions as the ability rotation system. |
| `AIStateTransitionNode`   | Calls `controller.ChangeState(TargetState)`. Returns `Success`.        |
| `AIHasTargetNode`         | Returns `Success` if `controller.Target != null`.                      |
| `AIIsDeadNode`            | Returns `Success` if the NPC is dead (`ICharacterDamageController.IsAlive == false`). |
| `AIGroupInCombatNode`     | Returns `Success` if `controller.Group != null && Group.IsInCombat`.   |
| `AIAdoptGroupTargetNode`  | Copies `Group.GroupTarget` to `controller.Target`. Returns `Success` if target adopted. |

### AINodeResult

| Value     | Meaning                                                        |
|-----------|----------------------------------------------------------------|
| `Success` | Node completed successfully                                    |
| `Failure` | Node failed its condition or action                            |
| `Running` | Node is still in progress (e.g., repeater mid-iteration)       |

### Integration with State Machine

When `AIController` has a `BehaviorTree` assigned:

1. The tree is evaluated on its own `TickRate` timer (separate from state update timers).
2. If `Root.Evaluate()` returns `Success`, the tree produced a meaningful decision (e.g., a state transition). The state machine's `UpdateCurrentState()` is **skipped** for that tick.
3. If the tree returns `Failure` or `Running`, the state machine runs normally.

This means the behavior tree acts as an **override layer** — it can choose to intervene or defer to the existing state logic.

### Setup Example

A basic combat behavior tree for a melee NPC:

```
Selector (root)
├── Sequence: "Dead Check"
│   ├── AIIsDeadNode
│   └── AIStateTransitionNode → IdleState
├── Sequence: "Combat"
│   ├── AIHasTargetNode
│   └── AIStateTransitionNode → MeleeAttackingState
└── Sequence: "Idle"
    └── AIStateTransitionNode → WanderState
```

1. Create node assets via their respective Create Asset menus.
2. Wire children into composite nodes via the Inspector.
3. Create an `AIBehaviorTree` asset and assign the root `Selector`.
4. Assign the tree to `AIController.BehaviorTree`.

---

## AI LOD System

AI LOD (Level of Detail) is a **three-pronged performance system** that combines tick scheduling, behavior simplification, and event-driven combat entry. Together these enable servers to support **10,000+ NPCs** by dramatically reducing per-frame work on distant NPCs.

### AILodSettings

**Menu:** *FishMMO > Character > NPC > AI > LOD Settings*

| Field                  | Type    | Default  | Description                                        |
|------------------------|---------|----------|----------------------------------------------------|
| `ActiveDistanceSqr`    | `float` | `1600`   | ≤ 40m — full update rate                           |
| `NearbyDistanceSqr`    | `float` | `10000`  | ≤ 100m — moderate throttle                         |
| `FarDistanceSqr`       | `float` | `90000`  | ≤ 300m — heavy throttle                            |
| `ActiveStaggerModulus`  | `int`   | `3`      | Frame modulus for Active tier (~50ms at 60fps)     |
| `NearbyStaggerModulus`  | `int`   | `12`     | Frame modulus for Nearby tier (~200ms at 60fps)    |
| `FarStaggerModulus`     | `int`   | `60`     | Frame modulus for Far tier (~1s at 60fps)          |
| `DormantCheckModulus`   | `int`   | `120`    | Frame modulus for Dormant wake-up (~2s at 60fps)   |
| `ReevaluateInterval`   | `float` | `2.0`    | Seconds between LOD tier re-evaluation             |

### AILodTier

| Tier       | Distance               | Stagger (default) | Approx. Interval | Pipeline |
|------------|------------------------|--------------------|-------------------|----------|
| `Active`   | ≤ 40m                  | Every 3rd frame    | ~50ms             | **Full:** BT, StateMachine, Abilities, Threat, BossScripts, EnemySweep, Camera, Facing |
| `Nearby`   | ≤ 100m                 | Every 12th frame   | ~200ms            | **Simplified:** StateMachine, Abilities, Threat, Camera, Facing (no BT, no boss, no sweep) |
| `Far`      | ≤ 300m                 | Every 60th frame   | ~1s               | **Minimal:** StateMachine only (wander/idle/return home — no combat, no threat) |
| `Dormant`  | > 300m (or no observers) | Every 120th frame | ~2s               | **Disabled:** LOD re-evaluation only |

### Tick Scheduling

Frame stagger uses `(npcID + Time.frameCount) % staggerModulus == 0` to distribute NPC updates evenly across frames. The NPC's `NetworkObject.ObjectId` provides a unique per-NPC hash so that 10,000 NPCs don't all tick on the same frame.

**Expected load at 10k NPCs (60fps):**

| Tier     | ~% of NPCs | Count  | Ticks/Frame | Per-NPC Cost      |
|----------|-----------|--------|-------------|-------------------|
| Active   | ~5%       | 500    | ~167        | Full pipeline     |
| Nearby   | ~15%      | 1,500  | ~125        | Simplified        |
| Far      | ~20%      | 2,000  | ~33         | Minimal           |
| Dormant  | ~60%      | 6,000  | ~50         | 1 integer compare |

### Behavior Simplification

Each tier runs a progressively simpler subset of the AI pipeline:

- **Active (`UpdateActive`)** — Full AI: sweep for enemies, check leash, evaluate behavior tree, tick boss scripts, run state machine, update virtual camera, tick aggression decay, face look target.
- **Nearby (`UpdateNearby`)** — No enemy sweep (relies on event-driven `OnThreatReceived`), no behavior tree, no boss scripts. Combat still functions via the state machine.
- **Far (`UpdateFar`)** — No combat at all. If the NPC is in an attacking state, aggression is cleared and it transitions to idle. Only leash check + basic state machine (wander/idle/return home).
- **Dormant** — Zero game logic. Only a `DormantCheckModulus`-gated LOD re-evaluation to detect approaching players.

### LOD Tier Transitions (`OnLodTierChanged`)

When an NPC transitions **down** to Far or Dormant while in combat:

1. Interrupts any active ability.
2. Heals to full (no players nearby to notice).
3. Clears the aggression/threat table.
4. Resets boss script phases (if `ResetOnLeash`).
5. Transitions to idle state.

This acts as a soft-leash reset — if no players are close enough to observe, the NPC shouldn't remain damaged or in combat.

### Distance Calculation

LOD distance is calculated using **FishNet `NetworkObject.Observers`**:

1. Every `ReevaluateInterval` seconds, `AIController.EvaluateLodTier()` iterates all current observers (connected clients who can see this NPC).
2. For each observer, it finds their first owned object (typically their player character) and calculates the squared distance.
3. The **nearest** observer's distance determines the LOD tier.
4. If there are **no observers**, the NPC is immediately `Dormant`.

This approach avoids expensive spatial queries — FishNet already tracks which clients observe which objects.

### Setup

1. Create an `AILodSettings` asset.
2. Assign it to `AIController.LodSettings`.
3. Tune distance thresholds based on your game's world scale.

Without `LodSettings`, a simple 1-in-3 frame stagger is applied as a default fallback.

---

## Event-Driven AI

The event-driven AI system replaces expensive per-frame polling with zero-cost event callbacks. This is the single biggest performance win for non-Active NPCs.

### Problem: Polling Is Expensive

Previously, **every** NPC ran `SweepForEnemies()` (a `PhysicsScene.OverlapSphere()`) every `EnemySweepRate` seconds, regardless of distance. For 10,000 NPCs this meant thousands of physics queries per second, most of which returned nothing.

### Solution: `OnCombatInitiated` Callback

`AggressionState` already subscribes to global `ICharacterDamageController.OnDamaged` events to record threat. The event-driven system piggybacks on this existing subscription:

1. **`AggressionState.OnCombatInitiated`** — A `System.Action<ICharacter>` callback that fires when the NPC's threat table transitions from **empty to non-empty** (i.e., the first damage event from any source).
2. **`AIController.OnThreatReceived(attacker)`** — Wired to `OnCombatInitiated` during `InitializeOnce()`. When called, immediately verifies the attacker is alive, sets `Target` and `LookTarget`, and transitions to the attacking state — all without waiting for the next physics sweep.

### Tier Integration

| Tier     | Enemy Detection Method |
|----------|----------------------|
| Active   | `SweepForEnemies()` (proactive) **+** `OnThreatReceived` (reactive) |
| Nearby   | `OnThreatReceived` only (no sweep) |
| Far      | `OnThreatReceived` fires but `UpdateFar` immediately disengages → no combat |
| Dormant  | No events processed (NPC is suspended) |

Active tier NPCs still run physics sweeps for **proactive** detection (hostile-faction NPCs that should aggro on proximity). Nearby tier NPCs rely **entirely** on damage events — they only enter combat when actually hit.

---

## Performance Optimizations

Beyond the LOD system, two micro-optimizations reduce per-tick work for large NPC populations:

### Ability Cache

**Problem:** `PickBestAbility()` and `HasAbilityInRange()` previously iterated `IAbilityController.KnownAbilities` — a `Dictionary<int, Ability>`. `foreach` over a dictionary allocates an enumerator struct each call, and dictionary iteration has poor cache locality. With 200+ active NPCs each picking abilities multiple times per second, this adds up.

**Solution:** `AIController` maintains a flat `List<Ability> cachedAbilities` that mirrors the dictionary values. A dirty check (`lastKnownAbilityCount`) triggers `RebuildAbilityCacheIfDirty()` only when abilities are learned or unlearned.

| Component                 | Details                                                    |
|---------------------------|------------------------------------------------------------|
| `cachedAbilities`         | `List<Ability>(8)` — pre-allocated flat ability list       |
| `lastKnownAbilityCount`   | `int` — tracks dictionary count for dirty detection        |
| `RebuildAbilityCacheIfDirty()` | Rebuilds list when count changes; skips null entries   |

**Benefits:**
- Zero enumerator allocation per tick (flat `for (int i = ...)` loop).
- Better CPU cache locality (contiguous array vs. hash bucket traversal).
- Rebuild cost amortized — only fires when abilities change (rare event).

### Pathfinding Throttle

**Problem:** States like `BaseAttackingState.MoveTowardTarget()`, `OrbitState`, and `HealerAttackingState.MoveTowardAlly()` call `NavMeshAgent.SetDestination()` every tick while chasing a moving target. Each call triggers a full A* pathfinding recalculation on the NavMesh, which is expensive. With hundreds of NPCs in combat, this creates significant NavMesh contention.

**Solution:** `AIController.SetThrottledDestination(Vector3)` wraps `Agent.SetDestination()` behind a cooldown timer. States that chase moving targets call this instead of `Agent.SetDestination()` directly.

| Component                | Details                                                       |
|--------------------------|---------------------------------------------------------------|
| `RepathInterval`         | `float` (default `0.5s`) — min seconds between repath calls   |
| `repathCooldown`         | `float` — timer decremented in `UpdateActive`/`UpdateNearby`  |
| `SetThrottledDestination()` | Returns `bool`, only calls `SetDestination` when cooldown ≤ 0 |

**States using throttled pathing:**
- `BaseAttackingState.MoveTowardTarget()` — chasing combat target
- `BaseAttackingState.RetreatFromTarget()` — kiting away from target
- `OrbitState.UpdateState()` — circle-strafing around target
- `HealerAttackingState.MoveTowardAlly()` — chasing ally to heal
- `RetreatState` — ongoing retreat recalculation
- `PetIdleState` — following owner

**States keeping direct `SetDestination`:**
- `GetBehindState.Enter()` — one-shot flanking destination
- `RetreatState.Enter()` — one-shot initial retreat position
- `AIController.Target` setter — one-shot on target change
- Waypoint navigation — one-shot per waypoint

**Benefits:**
- At 0.5s interval, a 200-NPC combat goes from ~12,000 → ~400 SetDestination calls/second.
- NavMeshAgent continues moving along its last-computed path between repathing — movement remains smooth.
- `[MethodImpl(AggressiveInlining)]` keeps the hot path (cooldown check) zero-cost when skipping.

---

## Combat Personality System

The combat personality system makes two NPCs with identical ability sets behave differently in combat by applying **data-driven score biases** to ability selection.

### AICombatPersonality

**Menu:** *FishMMO > Character > NPC > AI > Combat Personality*

| Field                     | Type              | Default     | Description                                         |
|---------------------------|-------------------|-------------|-----------------------------------------------------|
| `Style`                   | `NPCCombatStyle`  | `Balanced`  | Broad combat archetype (affects retreat behavior)    |
| `MeleeWeight`             | `float` (0-5)     | `1.0`       | Score multiplier for melee-classified abilities      |
| `RangedWeight`            | `float` (0-5)     | `1.0`       | Score multiplier for ranged-classified abilities     |
| `AOEWeight`               | `float` (0-5)     | `1.0`       | Score multiplier for AOE-classified abilities        |
| `SupportWeight`           | `float` (0-5)     | `1.0`       | Score multiplier for support/self-buff abilities     |
| `MeleeRangeThreshold`     | `float`            | `4.0`       | Abilities with range ≤ this are considered melee     |
| `AOEHitCountThreshold`    | `int`              | `1`         | Abilities with HitCount > this are considered AOE    |
| `RetreatHealthThreshold`  | `float` (0-1)      | `0`         | Health % below which NPC considers retreating        |
| `HealthyAggressionBonus`  | `float`            | `0`         | Bonus score on offensive abilities when healthy      |
| `LowHealthSupportBonus`   | `float`            | `100`       | Bonus score on support abilities when hurt           |

### NPCCombatStyle

| Style        | Behavior                                              |
|--------------|-------------------------------------------------------|
| `Balanced`   | No strong bias — default approach                     |
| `Aggressive` | Prefers closing to melee, high-pressure attacks       |
| `Defensive`  | Prefers distance, kiting, control abilities            |
| `Cautious`   | Avoids risk, retreats earlier, favours safe abilities |
| `Berserker`  | All-out damage, ignores self-preservation, never retreats |

### AbilityCategory

Abilities are classified at runtime from template data:

| Category   | Classification Rule                                                     |
|------------|-------------------------------------------------------------------------|
| `Melee`    | `SpawnTarget == PointBlank` OR `range ≤ MeleeRangeThreshold`           |
| `Ranged`   | `range > MeleeRangeThreshold` (and not AOE/Support)                    |
| `AOE`      | `HitCount > AOEHitCountThreshold` OR `GroundedPhysical`/`GroundedMagic` |
| `Support`  | `SpawnTarget == Self`                                                   |

### Integration with PickBestAbility

When `AIController.Personality` is assigned, `PickBestAbility()` applies:
1. **Weight multiplier** — Each ability's base score is multiplied by the personality's weight for its category.
2. **Bonus score** — `GetBonusScore()` adds `HealthyAggressionBonus` to offensive abilities when healthy, or `LowHealthSupportBonus` to support abilities when hurt.
3. **Retreat check** — `ShouldRetreat()` triggers retreat state transitions when health drops below threshold (Berserker style ignores this).

### Setup Example

An aggressive melee warrior personality:
- `Style = Aggressive`, `MeleeWeight = 2.5`, `RangedWeight = 0.5`, `AOEWeight = 1.5`
- `RetreatHealthThreshold = 0` (never retreats), `HealthyAggressionBonus = 50`
- Result: Strongly prefers melee and AOE abilities, ignores ranged options, pushes damage hard.

A cautious healer personality:
- `Style = Cautious`, `SupportWeight = 3.0`, `MeleeWeight = 0.2`
- `RetreatHealthThreshold = 0.5`, `LowHealthSupportBonus = 200`
- Result: Heavily favors heals/buffs, avoids melee, retreats at 50% health.

---

## Pack Tactics

The Pack Tactic system extends `NPCGroup` with coordinated spatial positioning during combat. Each group can be assigned a `PackTactic` that controls how members position themselves around the target.

### PackTactic Enum

| Tactic       | Behavior                                                                    |
|--------------|-----------------------------------------------------------------------------|
| `None`       | No coordinated positioning — members act independently                     |
| `Surround`   | Members spread evenly in a ring (360° / alive-member-count)                |
| `Flank`      | Tank holds front, DPS/support position behind the target (rear 180° arc)   |
| `FocusFire`  | All members converge from the same direction (tightly clustered angles)    |
| `Kite`       | Members maintain max distance, orbit angles rotate slowly (swirling pattern)|

### How It Works

1. `NPCGroup` assigns each member's `AIController.OrbitAngle` based on the active tactic and the member's index among alive members.
2. Combat states (especially `OrbitState`) read `OrbitAngle` to determine their position around the target.
3. Pack tactic assignment updates when group state is re-evaluated (`EvaluateGroupState()` every 0.5s).

### Tactic × Role Interaction

| Tactic     | Tank Behavior          | DPS Behavior             | Healer Behavior        |
|------------|------------------------|--------------------------|------------------------|
| Surround   | Part of the ring       | Part of the ring         | Part of the ring       |
| Flank      | Faces target head-on   | Rear 180° arc positions  | Rear 180° arc          |
| FocusFire  | Same angle as group    | Same angle as group      | Same angle as group    |
| Kite       | Orbits at range        | Orbits at range          | Orbits at range        |

---

## Group AI System

The Group AI system enables **pack behavior** — groups of NPCs that coordinate their actions, share targets, fill tactical roles (tank, healer, DPS, support), and execute pack tactics (surround, flank, focus fire, kite).

### NPCGroupRole

| Role      | Typical Behavior                                               |
|-----------|----------------------------------------------------------------|
| `None`    | Default, no special role behavior                              |
| `Tank`    | Engages enemies first, holds aggro                             |
| `Healer`  | Prioritizes healing group members                              |
| `DPS`     | Focuses damage on the group's shared target                    |
| `Support` | Applies buffs, crowd control, utility                          |

### NPCGroup

A `MonoBehaviour` placed on an empty GameObject near the NPC spawn area. It acts as a **coordinator** for a group of NPCs.

| Field            | Type                    | Default | Description                                  |
|------------------|-------------------------|---------|----------------------------------------------|
| `Members`        | `List<NPCGroupMember>`  | —       | NPCs and their roles                         |
| `FocusTargeting` | `bool`                  | `true`  | DPS share the tank's target                  |
| `Tactic`         | `PackTactic`            | `None`  | Coordinated spatial positioning tactic       |

#### Runtime Properties

| Property              | Type              | Description                                         |
|-----------------------|-------------------|-----------------------------------------------------|
| `GroupTarget`         | `Transform`       | Shared enemy target for the whole group              |
| `IsInCombat`          | `bool`            | True if any member is alive with a target            |
| `AliveMemberCount`    | `int`             | Number of alive group members                        |
| `LowestHealthMember`  | `AIController`    | Group member with the lowest HP%                     |
| `LowestHealthPercent` | `float`           | HP% of the lowest-health member                      |

#### Key Methods

| Method                    | Description                                                        |
|---------------------------|--------------------------------------------------------------------|
| `AlertGroup(enemy)`       | Called when any member detects an enemy. Sets group target and alerts all members. Tank acquires the target; DPS adopt it if `FocusTargeting` is on. |
| `GetMemberByRole(role)`   | Returns the first alive member with the given role.                |
| `EvaluateGroupState()`    | Runs every 0.5s. Updates `IsInCombat`, `LowestHealthMember`, `AliveMemberCount`. |

### NPCGroupMember

A `[Serializable]` class pairing an `AIController` reference with an `NPCGroupRole`.

### Behavior Tree Integration

Two BT leaf nodes enable group-aware decisions:

- **`AIGroupInCombatNode`** — Returns `Success` if `controller.Group.IsInCombat`. Use this in a behavior tree to trigger combat states when any group member is fighting.
- **`AIAdoptGroupTargetNode`** — Sets `controller.Target` and `controller.LookTarget` to the group's shared target. Use this so DPS/healers automatically engage the same enemy.

### Setup Example

1. Create an empty GameObject in the scene where the NPC pack spawns.
2. Add an `NPCGroup` component.
3. Drag each NPC's `AIController` into the `Members` list and assign roles.
4. The group auto-registers members on `Start()` and sets their `Group`/`GroupRole`.
5. When any member detects an enemy, `AlertGroup()` notifies the entire pack.

---

## Boss Script System

The Boss Script system enables **phased boss encounters** with timed mechanics, add spawning, ability/rotation/BT overrides per phase, and automatic reset on leash.

### BossScript

**Menu:** *FishMMO > Character > NPC > AI > Boss Script*

A ScriptableObject defining the entire boss encounter.

| Field            | Type                       | Default | Description                            |
|------------------|----------------------------|---------|----------------------------------------|
| `Phases`         | `List<BossPhase>`          | —       | Ordered phases (highest HP threshold first) |
| `TimedMechanics` | `List<BossTimedMechanic>`  | —       | Interval-based mechanics active across phases |
| `ResetOnLeash`   | `bool`                     | `true`  | Reset to phase 0 when NPC leashes home |

### BossPhase

A `[Serializable]` class defining a single phase of the encounter.

| Field                  | Type                | Default | Description                                          |
|------------------------|---------------------|---------|------------------------------------------------------|
| `HealthThreshold`      | `float` (0-1)       | —       | Phase activates when HP ≤ this threshold             |
| `OverrideBehaviorTree` | `AIBehaviorTree`    | —       | Optional BT override for this phase                  |
| `OverrideState`        | `BaseAIState`       | —       | Optional state to force-transition to on phase entry |
| `OverrideRotation`     | `AIAbilityRotation` | —       | Optional ability rotation override for this phase    |
| `SpawnOnEnter`         | `GameObject[]`      | —       | Prefabs to spawn (adds) when entering this phase     |
| `SpawnOffsets`         | `Vector3[]`         | —       | World-space offsets for spawned adds                  |
| `PhaseAnnouncement`    | `string`            | —       | Chat message broadcast on phase transition           |

### BossTimedMechanic

A `[Serializable]` class defining an interval-based mechanic.

| Field              | Type           | Default | Description                                             |
|--------------------|----------------|---------|---------------------------------------------------------|
| `Interval`         | `float`        | —       | Seconds between activations                             |
| `AbilityTemplateID`| `int`          | `-1`    | Ability to force-activate (if ≥ 0)                      |
| `SpawnPrefabs`     | `GameObject[]` | —       | Prefabs to spawn each interval                          |
| `SpawnOffsets`     | `Vector3[]`    | —       | World-space offsets for spawned prefabs                  |
| `ActivePhases`     | `int[]`        | —       | Phase indices where this mechanic is active (empty = all)|

### BossScriptState

A **plain C# class** (not a ScriptableObject) that holds per-NPC mutable runtime state for boss encounters. Created by `AIController.InitializeOnce()` when a `BossScript` is assigned.

| Property            | Type      | Description                                        |
|---------------------|-----------|----------------------------------------------------|
| `CurrentPhaseIndex` | `int`     | Index into `BossScript.Phases`                     |
| Mechanic timers     | `float[]` | Countdown timers for each `BossTimedMechanic`      |

#### Key Methods

| Method                        | Description                                                              |
|-------------------------------|--------------------------------------------------------------------------|
| `EvaluatePhases(controller)`  | Checks NPC HP% against phase thresholds. Triggers `TransitionToPhase()` on change. |
| `TickMechanics(controller, dt)`| Decrements mechanic timers. Fires `ExecuteMechanic()` when timer ≤ 0.   |
| `Reset()`                     | Resets to phase 0 and reinitializes all mechanic timers.                 |

#### Phase Transition

When a phase changes (`TransitionToPhase()`):

1. Overrides `controller.BehaviorTree` if `OverrideBehaviorTree` is set.
2. Overrides `controller.AbilityRotation` if `OverrideRotation` is set.
3. Calls `controller.ChangeState(OverrideState)` if `OverrideState` is set.
4. Spawns all `SpawnOnEnter` prefabs at the NPC's position + offsets using `NetworkManager.ServerManager.Spawn()`.
5. Broadcasts `PhaseAnnouncement` to all observers (TODO: integrate with chat system).

#### Timed Mechanic Execution

When a mechanic timer fires (`ExecuteMechanic()`):

1. Force-activates the ability with `AbilityTemplateID` via `IAbilityController.Activate()`.
2. Spawns any `SpawnPrefabs` at the NPC's position + offsets.

### Setup Example

A two-phase boss with an enrage at 30% health:

1. **Create BossScript:** `DragonBoss.asset`
   - Phase 0: `HealthThreshold = 1.0` (full HP — normal phase)
   - Phase 1: `HealthThreshold = 0.3` (30% — enrage phase)
     - `OverrideRotation` → `EnrageRotation.asset` (faster, harder-hitting abilities)
     - `SpawnOnEnter` → `[DragonAdd.prefab, DragonAdd.prefab]` (two adds spawn)
     - `SpawnOffsets` → `[(-3,0,0), (3,0,0)]`
     - `PhaseAnnouncement` → `"The Dragon roars with fury!"`
   - Timed Mechanic: `Interval = 15`, `AbilityTemplateID = 42` (fire breath), `ActivePhases = [0, 1]`

2. **Assign** `DragonBoss.asset` to `AIController.BossScript`.

3. At runtime:
   - `AIController.InitializeOnce()` creates a `BossScriptState`.
   - Every frame, `BossScriptState.EvaluatePhases()` checks HP.
   - When HP drops to 30%, phase transitions: rotation swaps, two adds spawn, announcement broadcasts.
   - Every 15 seconds, the fire breath ability force-activates.
   - If the boss leashes, `BossScriptState.Reset()` restores phase 0.