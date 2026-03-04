# Spawner System

## Overview

The Spawner system is a server-authoritative, condition-driven framework for spawning and respawning networked objects in FishMMO. It provides configurable spawn selection (linear, random, weighted), conditional respawn gating (OR and AND condition lists), bounding-box placement with physics-based ground detection, object pooling via FishNet, and per-object respawn timers. Spawners are placed in scenes as `NetworkBehaviour` components and manage the full lifecycle of spawned entities.

## Directory Structure

```
Spawner/
├── ISpawnable.cs              # Interface for entities managed by an ObjectSpawner
├── ObjectSpawnType.cs          # Enum for spawn selection strategy (Linear, Random, Weighted)
├── ObjectSpawner.cs            # Core spawner component (NetworkBehaviour)
├── Settings/
│   ├── SpawnableSettings.cs        # Per-object spawn configuration (respawn times, chance, offset)
│   ├── ItemSpawnableSettings.cs    # Item-specific spawnable settings
│   └── NPCSpawnableSettings.cs     # NPC-specific spawnable settings
└── Condition/
    ├── BaseRespawnCondition.cs          # Abstract base for respawn conditions
    └── Types/
        └── DeadNPCRespawnCondition.cs   # Condition: all specified NPCs must be dead
```

## Inheritance Hierarchies

### Spawner Components

```
NetworkBehaviour
└── ObjectSpawner
```

### Spawnable Interface

```
ISpawnable
├── NPC : BaseCharacter, ISceneObject, ISpawnable
│   └── Pet
└── (any entity implementing ISpawnable)
```

### Respawn Conditions

```
MonoBehaviour
└── BaseRespawnCondition (abstract)
    └── DeadNPCRespawnCondition
```

### Enums

```
ObjectSpawnType : byte
├── Linear   = 0   # Sequential cycling through the list
├── Random   = 1   # Uniform random selection
└── Weighted = 2   # Weighted random using SpawnChance values
```

## Core Components

### ISpawnable

Interface implemented by any entity that can be spawned and managed by an `ObjectSpawner`.

| Member             | Type                | Description                                         |
|--------------------|---------------------|-----------------------------------------------------|
| `ObjectSpawner`    | `ObjectSpawner`     | The spawner that created and manages this entity     |
| `SpawnableSettings`| `SpawnableSettings` | The settings used when spawning this entity          |
| `NetworkObject`    | `NetworkObject`     | FishNet network object for synchronization           |
| `ID`               | `long`              | Unique identifier for the spawned entity             |
| `Despawn()`        | `void`              | Despawns the entity via its owning ObjectSpawner     |

### SpawnableSettings

Serializable configuration for each spawnable object in the spawner's list.

| Field               | Type             | Default | Description                                          |
|---------------------|------------------|---------|------------------------------------------------------|
| `NetworkObject`     | `NetworkObject`  | —       | The prefab to spawn                                  |
| `MinimumRespawnTime`| `float`          | 0       | Minimum respawn delay (seconds)                      |
| `MaximumRespawnTime`| `float`          | 0       | Maximum respawn delay (seconds)                      |
| `SpawnChance`       | `float [0–1]`    | 0.5     | Selection weight for weighted spawn mode             |
| `YOffset`           | `float`          | auto    | Vertical offset from ground, calculated from collider|

`OnValidate()` ensures the `NetworkObject` is marked spawnable and auto-calculates `YOffset` from the prefab's collider dimensions.

### ObjectSpawner

The core spawner component, attached as a `NetworkBehaviour` in the scene.

#### Configuration

| Field                | Type                          | Default        | Description                                          |
|----------------------|-------------------------------|----------------|------------------------------------------------------|
| `InitialSpawnCount`  | `int`                         | 0              | Objects spawned immediately on start                 |
| `MaxSpawnCount`      | `int`                         | 1              | Maximum concurrent spawned objects                   |
| `SpawnType`          | `ObjectSpawnType`             | Linear         | Selection strategy for choosing from the list        |
| `RandomRespawnTime`  | `bool`                        | true           | If true, randomizes between min/max respawn times    |
| `InitialRespawnTime` | `float`                       | 0              | Fixed respawn time when RandomRespawnTime is false   |
| `RandomSpawnPosition`| `bool`                        | true           | If true, picks random position within bounding box   |
| `BoundingBoxSize`    | `Vector3`                     | (1, 1, 1)      | Size of the spawn area                               |
| `SphereRadius`       | `float`                       | 0.5            | SphereCast radius for ground detection               |
| `Spawnables`         | `List<SpawnableSettings>`     | —              | List of spawnable object configurations              |
| `OrConditions`       | `List<BaseRespawnCondition>`  | —              | Any condition true → respawn allowed (logical OR)    |
| `TrueConditions`     | `List<BaseRespawnCondition>`  | —              | All conditions must be true (logical AND)            |

#### Runtime State

| Field                    | Type                             | Description                                    |
|--------------------------|----------------------------------|------------------------------------------------|
| `Spawned`                | `Dictionary<long, ISpawnable>`   | Currently active spawned objects by ID          |
| `SpawnableRespawnTimers` | `List<DateTime>`                 | Pending respawn timestamps                     |

## Spawn Lifecycle

### Initialization (`OnStartNetwork`)

1. Validates all `SpawnableSettings` via `OnValidate()`.
2. Clamps `InitialSpawnCount` to `[0, MaxSpawnCount]`.
3. Spawns `InitialSpawnCount` objects immediately.
4. Creates respawn timers for the remaining slots up to `MaxSpawnCount`.

### Spawn Selection (`GetSpawnIndex`)

| SpawnType  | Selection Logic                                                    |
|------------|--------------------------------------------------------------------|
| `Linear`   | Increments an index, wrapping at list end                          |
| `Random`   | Uniform random: `Random.Range(0, Count)`                          |
| `Weighted` | Cumulative weight: picks based on `SpawnChance` proportional odds  |

### Spawn Placement (`SpawnObject`)

1. Selects a `SpawnableSettings` via `GetSpawnIndex()`.
2. If `RandomSpawnPosition` is true:
   - Picks a random XZ point within `BoundingBoxExtents`.
   - Casts a sphere downward from the top of the bounding box.
   - Places the object at the hit point + `YOffset`.
3. Retrieves a pooled instance via `NetworkManager.GetPooledInstantiated()`.
4. Moves the object to the spawner's scene.
5. Initializes `IAIController` (if present) with the spawn position as home.
6. Sets `ISpawnable.ObjectSpawner` and `SpawnableSettings` references.
7. Adds to `Spawned` dictionary.
8. Calls `ServerManager.Spawn()` to network the object.
9. Clears respawn timers if `MaxSpawnCount` is reached.

### Despawn (`Despawn`)

1. Removes the entity from the `Spawned` dictionary.
2. Schedules a new respawn timer via `GetNextRespawnTime()`.
3. Clears the entity's `ObjectSpawner` and `SpawnableSettings` references.
4. Returns the object to the pool via `ServerManager.Despawn(DespawnType.Pool)`.

### Respawn Loop (`TryRespawn`)

Runs every frame in `Update()`:

1. Skips if no spawnables or no pending timers.
2. Clears all timers if `Spawned.Count >= MaxSpawnCount`.
3. For each timer that has elapsed (`DateTime.UtcNow >= respawnTime`):
   - **OR conditions**: If any condition returns true, respawn is allowed. If the list is empty, defaults to allowed.
   - **AND conditions**: All conditions must return true (checked only if OR conditions passed).
   - If all conditions pass, spawns the object and removes the timer.

## Respawn Conditions

### BaseRespawnCondition

Abstract `MonoBehaviour` base. Subclasses implement `OnCheckCondition(ObjectSpawner)` returning `bool`.

### DeadNPCRespawnCondition

Allows respawn only when **all** specified NPCs are dead:

- Iterates the `NPCs` list.
- Skips null entries or those without `ICharacterDamageController`.
- Returns `false` if any NPC's `IsAlive` is true.
- Returns `true` if all are dead or the list is empty.

Typical use: Boss encounters where all mobs must be defeated before the encounter resets.

## Condition Evaluation Flow

```
Timer Elapsed?
    │
    ├── No  → Skip
    │
    └── Yes → Evaluate OrConditions
                  │
                  ├── List empty → shouldRespawn = true
                  │
                  ├── Any condition true → shouldRespawn = true
                  │
                  └── All conditions false → shouldRespawn = false
                            │
                            └── (skip AND check, no respawn)
                  │
                  └── shouldRespawn = true → Evaluate TrueConditions
                            │
                            ├── List empty → shouldRespawn = true
                            │
                            ├── All conditions true → shouldRespawn = true → Spawn
                            │
                            └── Any condition false → shouldRespawn = false → No respawn
```

## Editor Support

In the Unity Editor (`UNITY_EDITOR`):
- `OnDrawGizmos()` visualizes the spawner's bounding box or collider in scene view.
- `GizmoColor` is configurable (default: red).
- Collider-based gizmo uses `DrawGizmo()` extension; fallback uses `DrawWireCube`.

## External Integration Points

The Spawner system is consumed by and integrates with many other systems:

- **NPC System** — NPCs implement `ISpawnable`; spawner sets their `ObjectSpawner` and `SpawnableSettings`, and calls `IAIController.Initialize()` with home position.
- **Pet System** — Pets (extending NPC) are spawnable entities managed by ObjectSpawner.
- **AI System** — `IAIController.Initialize(spawnPosition)` is called on spawn, setting the AI's home position.
- **CharacterAttribute System** — `ICharacterDamageController.IsAlive` is used by `DeadNPCRespawnCondition` for alive/dead checks.
- **FishNet Networking** — Object pooling via `GetPooledInstantiated()` / `Despawn(DespawnType.Pool)`, server spawning via `ServerManager.Spawn()`.
- **Scene System** — Spawned objects are moved to the spawner's scene via `SceneManager.MoveGameObjectToScene()`.
- **Physics System** — SphereCast for ground detection when placing objects with `RandomSpawnPosition`.