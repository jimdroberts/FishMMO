# Ability System

## Overview

The Ability system is a server-authoritative, template-driven framework for abilities in FishMMO. It handles the full ability lifecycle — learning, queuing, activation, spawning, ticking, collision, and destruction — using FishNet's Replicate/Reconcile prediction model with deterministic RNG seeds for mismatch detection. Abilities are composed from ScriptableObject templates and modular Event-Condition-Action (ECA) events that can be combined at runtime through an ability crafting system. The system includes per-ability cooldown tracking, a snapshot mechanism for detached ability objects that outlive their caster, and speed/cast-time reduction via character attributes.

## Directory Structure

```
Ability/
├── Ability.cs                          # Runtime ability instance (event dictionaries, resource costs, stat aggregation)
├── AbilityActivationFlags.cs           # Enum: IsActualData, Interrupt
├── AbilityController.cs                # Per-entity controller (CharacterBehaviour, IAbilityController, Replicate/Reconcile)
├── AbilityObject.cs                    # Spawned ability object (MonoBehaviour, lifetime/collision/tick management)
├── AbilityObjectSnapshot.cs            # Immutable snapshot for detached ability objects
├── Activation/
│   ├── AbilityActivationReplicateData.cs   # IReplicateData: ActivationFlags, QueuedAbilityID, IsHeld
│   └── AbilityReconcileData.cs             # IReconcileData: AbilityID, RemainingTime, Seed, ResourceState
├── Cooldown/
│   ├── CooldownController.cs           # Per-entity cooldown manager (CharacterBehaviour, ICooldownController)
│   └── CooldownInstance.cs             # Cooldown timer: TotalTime, RemainingTime, IsOnCooldown
├── Snapshot/
│   ├── SnapshotCharacter.cs            # Lightweight phantom ICharacter for detached ability objects
│   └── SnapshotAttributeController.cs  # Read-only ICharacterAttributeController snapshot
└── Template/
    ├── BaseAbilityTemplate.cs          # Abstract ScriptableObject base (stats, ECA ActivationConditions, tooltip)
    ├── AbilityTemplate.cs              # Concrete template (prefab, SpawnTarget, HitCount, ECA trigger lists)
    ├── PetAbilityTemplate.cs           # Pet-specific template (PetPrefab, SpawnBoundingBox)
    ├── AbilitySpawnTarget.cs           # Enum: Self, PointBlank, Target, Forward, Camera, Spawner, SpawnerWithCameraRotation
    ├── AbilityType.cs                  # Enum: None, Physical, Magic, GroundedPhysical, GroundedMagic, AerialPhysical, AerialMagic
    ├── AbilityTypeOverrideEventType.cs # BaseAbilityTemplate subclass with OverrideAbilityType
    └── Events/
        ├── AbilityEvent.cs             # Abstract Trigger subclass (ActivationTime, LifeTime, Speed, Cooldown, Price)
        ├── AbilityOnDestroyEvent.cs    # Fired when the ability object is destroyed
        ├── AbilityOnHitEvent.cs        # Fired when the ability object collides with a character
        ├── AbilityOnPreSpawnEvent.cs   # Fired before the primary ability object is spawned
        ├── AbilityOnSpawnEvent.cs      # Fired when the primary ability object is spawned
        └── AbilityOnTickEvent.cs       # Fired each tick while the ability object is alive
```

## Inheritance Hierarchies

### Runtime Instances

```
Ability                                 # Plain C# class (no MonoBehaviour)
    ├── Constructed from AbilityTemplate + optional event list
    └── Holds event dictionaries, stat aggregation, resource cost cache
```

### Controllers (CharacterBehaviour)

```
CharacterBehaviour
├── AbilityController   : IAbilityController (extends IAbilityKnowledgeController)
└── CooldownController  : ICooldownController
```

### Ability Objects

```
MonoBehaviour
└── AbilityObject       # Spawned projectile/effect in the world
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<BaseAbilityTemplate>
└── BaseAbilityTemplate (abstract)
    ├── AbilityTemplate
    │   └── PetAbilityTemplate
    └── AbilityTypeOverrideEventType

Trigger (CachedScriptableObject<Trigger>)
└── AbilityEvent (abstract)
    ├── AbilityOnTickEvent
    ├── AbilityOnHitEvent
    ├── AbilityOnPreSpawnEvent
    ├── AbilityOnSpawnEvent
    └── AbilityOnDestroyEvent
```

### Snapshot Types

```
ICharacter
└── SnapshotCharacter               # Phantom implementation for detached ability objects

ICharacterAttributeController
└── SnapshotAttributeController     # Read-only attribute snapshot
```

### Enums

```
AbilityActivationFlags : int
├── IsActualData = 1    # Marks the replicate data as real (not default)
└── Interrupt    = 2    # Queues an interrupt of the current ability

AbilitySpawnTarget : byte
├── Self = 0                        # Apply directly to caster
├── PointBlank = 1                  # Spawn at caster's feet
├── Target = 2                      # Spawn at target's position
├── Forward = 3                     # Spawn in caster's forward direction
├── Camera = 4                      # Spawn from camera direction
├── Spawner = 5                     # Spawn from AbilitySpawner transform
└── SpawnerWithCameraRotation = 6   # Spawn from AbilitySpawner with camera rotation

AbilityType : byte
├── None = 0
├── Physical = 1
├── Magic = 2
├── GroundedPhysical = 3
├── GroundedMagic = 4
├── AerialPhysical = 5
└── AerialMagic = 6
```

## Ability Lifecycle

```
Learn Ability (server grants template + events)
        │
        ▼
Queue Activation (player presses hotbar key)
        │
        ▼
  ┌─────────────────────────────────────┐
  │  AbilityController.Replicate()      │
  │  (IReplicateData: QueuedAbilityID,  │
  │   IsHeld, ActivationFlags)          │
  └─────────────────┬───────────────────┘
                    │
                    ▼
        Validate CanManipulate()
        Check Cooldown (CooldownController)
        Validate Resource Costs (ECA ActivationConditions)
                    │
                    ▼
        Apply Speed Reduction (AttackSpeed / CastSpeed attributes)
        Count down ActivationTime
                    │
                    ▼
        Spawn AbilityObject(s)
        ├── OnPreSpawn events fire
        ├── Object instantiated at SpawnTarget position
        ├── OnSpawn events fire
        │
        ▼
  ┌─────────────────────────────────────┐
  │  AbilityObject lifetime loop        │
  │  ├── OnTick events fire each frame  │
  │  ├── OnHit events fire on collision │
  │  └── HitCount decrements on hit     │
  └─────────────────┬───────────────────┘
                    │
                    ▼
        RemainingLifeTime expires OR HitCount reaches 0
                    │
                    ▼
        OnDestroy events fire
        AbilityObject destroyed
        CooldownController.AddCooldown()
```

## Network Prediction

The `AbilityController` uses FishNet's **Replicate/Reconcile** prediction model for responsive ability activation.

### Replicate Data (`AbilityActivationReplicateData`)

| Field              | Type   | Description                                        |
|--------------------|--------|----------------------------------------------------|
| `ActivationFlags`  | `int`  | Bit flags: `IsActualData`, `Interrupt`             |
| `QueuedAbilityID`  | `long` | The ability ID the player wants to activate        |
| `IsHeld`           | `bool` | Whether the activation key is held (for charged/channeled abilities) |

### Reconcile Data (`AbilityReconcileData`)

| Field           | Type                              | Description                                     |
|-----------------|-----------------------------------|-------------------------------------------------|
| `AbilityID`     | `long`                            | The currently active ability (or `NO_ABILITY`)  |
| `RemainingTime` | `float`                           | Remaining activation/cooldown time              |
| `Seed`          | `int`                             | Deterministic RNG seed for mismatch detection   |
| `ResourceState` | `CharacterAttributeResourceState` | Current resource values for reconciliation      |

### Deterministic RNG Seeds

The server generates a seed via `playerSeedGenerator` and sends it in reconcile data. Both client and server use the same seed to drive ability object spawning, ensuring identical outcomes. On reconcile, if the client's `currentSeed` differs from the server's `Seed`, the client knows its prediction was wrong and rolls back any erroneously spawned ability objects (identified by `SpawnTick`).

## AbilityController

The `AbilityController` (`CharacterBehaviour`, `IAbilityController`) manages all ability state for a character.

### Key Fields

| Field                       | Type                          | Description                                      |
|-----------------------------|-------------------------------|--------------------------------------------------|
| `AbilitySpawner`            | `Transform`                   | Spawn point for ability objects (e.g., hand)     |
| `AttackSpeedReductionTemplate` | `CharacterAttributeTemplate` | Attribute for physical speed reduction        |
| `CastSpeedReductionTemplate`   | `CharacterAttributeTemplate` | Attribute for magical speed reduction         |
| `CooldownReductionTemplate`    | `CharacterAttributeTemplate` | Attribute for cooldown reduction              |
| `BloodResourceConversionTemplate` | `AbilityEvent`            | Event template for health-to-mana conversion  |
| `ChargedTemplate`           | `AbilityEvent`                | Event template for charged abilities             |
| `ChanneledTemplate`         | `AbilityEvent`                | Event template for channeled abilities           |

### Knowledge System (`IAbilityKnowledgeController`)

The controller tracks what the character has learned:

| Property                     | Type               | Description                              |
|------------------------------|--------------------|------------------------------------------|
| `KnownAbilities`             | `Dictionary<long, Ability>` | All crafted/learned ability instances |
| `KnownBaseAbilities`         | `HashSet<int>`     | Known base template IDs                  |
| `KnownAbilityEvents`         | `HashSet<int>`     | Known event template IDs                 |
| `KnownAbilityOnTickEvents`   | `HashSet<int>`     | Known OnTick event IDs                   |
| `KnownAbilityOnHitEvents`    | `HashSet<int>`     | Known OnHit event IDs                    |
| `KnownAbilityOnPreSpawnEvents` | `HashSet<int>`   | Known OnPreSpawn event IDs               |
| `KnownAbilityOnSpawnEvents`  | `HashSet<int>`     | Known OnSpawn event IDs                  |
| `KnownAbilityOnDestroyEvents`| `HashSet<int>`     | Known OnDestroy event IDs                |

### Events

| Event                | Signature                          | Description                        |
|----------------------|------------------------------------|------------------------------------|
| `OnCanManipulate`    | `Func<bool>`                       | Checked before activation (e.g., not stunned) |
| `OnUpdate`           | `Action<string, float, float>`     | UI cast bar updates                |
| `OnInterrupt`        | `Action`                           | Current ability interrupted        |
| `OnCancel`           | `Action`                           | Current ability cancelled          |
| `OnReset`            | `Action`                           | Ability UI reset                   |
| `OnAddAbility`       | `Action<Ability>`                  | New crafted ability learned        |
| `OnAddKnownAbility`  | `Action<BaseAbilityTemplate>`      | New base template learned          |
| `OnAddKnownAbilityEvent` | `Action<AbilityEvent>`         | New event template learned         |

## Ability (Runtime Instance)

The `Ability` class is a plain C# object constructed from an `AbilityTemplate` and an optional list of event IDs.

### Stat Aggregation

Each `Ability` instance aggregates stats from the base template and all attached events:

| Property        | Type    | Description                                      |
|-----------------|---------|--------------------------------------------------|
| `ActivationTime`| `float` | Total activation time (template + all events)   |
| `LifeTime`      | `float` | Total lifetime (template + all events)           |
| `Speed`         | `float` | Total movement speed (template + all events)     |
| `Cooldown`      | `float` | Total cooldown (template + all events)           |
| `Range`         | `float` | Computed: `Speed * LifeTime`                     |

### Event Dictionaries

Events are stored by type for efficient lifecycle dispatch:

| Dictionary          | Key    | Value                  | Fired When                    |
|---------------------|--------|------------------------|-------------------------------|
| `AbilityEvents`     | `int`  | `AbilityEvent`         | Master lookup (all events)    |
| `OnTickEvents`      | `int`  | `AbilityOnTickEvent`   | Each frame while alive        |
| `OnHitEvents`       | `int`  | `AbilityOnHitEvent`    | Collision with a character    |
| `OnPreSpawnEvents`  | `int`  | `AbilityOnPreSpawnEvent` | Before object instantiation |
| `OnSpawnEvents`     | `int`  | `AbilityOnSpawnEvent`  | After object instantiation    |
| `OnDestroyEvents`   | `int`  | `AbilityOnDestroyEvent`| Object lifetime expires       |

### Resource Cost Calculation

Resource costs are determined via ECA conditions implementing `IResourceCost` on the template's `ActivationConditions` and each event's `Conditions`. Costs are cached and lazily recalculated when events are added or removed (`resourceCostsDirty` flag).

## AbilityObject

A `MonoBehaviour` attached to the spawned ability prefab. Manages lifetime countdown, collision detection, tick dispatch, and hit count tracking.

### Key Fields

| Field              | Type                     | Description                                         |
|--------------------|--------------------------|-----------------------------------------------------|
| `Ability`          | `Ability`                | Live ability reference (null after caster disconnects) |
| `Caster`           | `ICharacter`             | Caster reference (live or phantom `SnapshotCharacter`) |
| `Snapshot`         | `AbilityObjectSnapshot`  | Immutable fallback data when `Ability` is null      |
| `HitCount`         | `int`                    | Remaining collision hits before destruction          |
| `RemainingLifeTime`| `float`                  | Countdown timer in seconds                           |
| `SpawnTick`        | `uint`                   | Network tick at spawn (for prediction rollback)      |
| `RNG`              | `System.Random`          | Deterministic RNG seeded from the controller         |

### Snapshot Fallback

When the live `Ability` reference becomes null (caster disconnected), the `AbilityObject` falls back to `AbilityObjectSnapshot` for:
- `Speed` — movement calculations
- `LifeTime` — lifetime countdown
- `OnTickEvents` / `OnHitEvents` / `OnDestroyEvents` — event dispatch

## Snapshot System

When a caster disconnects while ability objects are still alive in the world, the system creates lightweight phantom replacements:

### SnapshotCharacter

A `sealed class` implementing `ICharacter` that preserves:
- Identity data (`ID`, `Name`, `Flags`)
- The `AbilityObject.Transform` as its `Transform` (so positional queries resolve to the projectile, not a stale character position)
- A `SnapshotAttributeController` for stat-scaled calculations

Only `TryGet<ICharacterAttributeController>()` is supported. All other behaviour lookups return `false`, causing downstream systems to gracefully degrade.

`IsSpawned` always returns `true` so that `AbilityObject.Update()` and collision dispatch continue to function.

### SnapshotAttributeController

A read-only `ICharacterAttributeController` that clones all `CharacterAttribute` instances from the live controller. Stat-scaled abilities continue to resolve damage/healing values correctly even after the caster is gone.

## Cooldown System

The `CooldownController` (`CharacterBehaviour`, `ICooldownController`) manages per-ability cooldowns.

### CooldownInstance

| Field           | Type    | Description                        |
|-----------------|---------|------------------------------------|
| `TotalTime`     | `float` | Original cooldown duration         |
| `RemainingTime` | `float` | Time remaining before expiry       |
| `IsOnCooldown`  | `bool`  | `true` while `RemainingTime > 0`   |

### CooldownController API

| Method                                | Description                                            |
|---------------------------------------|--------------------------------------------------------|
| `IsOnCooldown(long id)`               | Returns true if the ability is on cooldown             |
| `TryGetCooldown(long id, out float)`  | Gets remaining cooldown time                           |
| `AddCooldown(long id, CooldownInstance)` | Starts a cooldown for an ability                    |
| `RemoveCooldown(long id)`             | Removes a cooldown (called when it expires)            |
| `OnTick(float deltaTime)`             | Ticks all active cooldowns, removes expired ones       |
| `Read(Reader)` / `Write(Writer)`      | Network serialization for cooldown state               |

### Static Events on `ICooldownController`

| Event                | Signature                              | Description                  |
|----------------------|----------------------------------------|------------------------------|
| `OnAddCooldown`      | `Action<long, CooldownInstance>`       | Cooldown started (owner)     |
| `OnUpdateCooldown`   | `Action<long, CooldownInstance>`       | Cooldown ticked (owner)      |
| `OnRemoveCooldown`   | `Action<long>`                         | Cooldown expired (owner)     |

## Template System

### BaseAbilityTemplate (Abstract)

The abstract base for all ability templates. Defines shared fields and ECA-based activation requirements.

| Field                 | Type                    | Description                                    |
|-----------------------|-------------------------|------------------------------------------------|
| `icon`                | `Sprite`                | Ability icon for UI display                    |
| `Description`         | `string`                | Ability description text                       |
| `ActivationTime`      | `float`                 | Base activation time                           |
| `LifeTime`            | `float`                 | Base effect lifetime                           |
| `Speed`               | `float`                 | Base effect speed                              |
| `Cooldown`            | `float`                 | Base cooldown                                  |
| `Price`               | `int`                   | Crafting price in game currency                |
| `ActivationConditions`| `List<BaseCondition>`   | ECA conditions for activation (resource costs, attribute requirements, faction, archetype) |

### AbilityTemplate (Concrete)

| Field                  | Type                          | Description                                |
|------------------------|-------------------------------|--------------------------------------------|
| `AbilityObjectPrefab`  | `GameObject`                  | Prefab to instantiate as the ability object |
| `AbilitySpawnTarget`   | `AbilitySpawnTarget`          | Where the object spawns relative to caster  |
| `RequiresTarget`       | `bool`                        | Whether a target is needed                  |
| `AdditionalEventSlots` | `byte`                        | Extra crafting slots for events             |
| `HitCount`             | `int`                         | Max collision hits before destruction       |
| `Type`                 | `AbilityType`                 | Physical/Magic/Grounded/Aerial type         |
| `TargetTrigger`        | `AbilityEvent`                | Primary ECA trigger on activation or hit    |
| `OnTickEvents`         | `List<AbilityOnTickEvent>`    | Tick-phase event list                       |
| `OnHitEvents`          | `List<AbilityOnHitEvent>`     | Hit-phase event list                        |
| `OnPreSpawnEvents`     | `List<AbilityOnPreSpawnEvent>`| Pre-spawn-phase event list                  |
| `OnSpawnEvents`        | `List<AbilityOnSpawnEvent>`   | Spawn-phase event list                      |
| `OnDestroyEvents`      | `List<AbilityOnDestroyEvent>` | Destroy-phase event list                    |

### PetAbilityTemplate

Extends `AbilityTemplate` with pet-specific fields:

| Field               | Type         | Description                       |
|---------------------|--------------|-----------------------------------|
| `PetPrefab`         | `GameObject` | The pet NPC prefab to summon      |
| `SpawnBoundingBox`  | `Vector3`    | Random spawn offset bounding box  |

### AbilityEvent (Abstract)

All events extend `Trigger` (which is `CachedScriptableObject<Trigger>`) and implement `ITooltip`. Each event contributes additive stat modifiers to the runtime `Ability`.

| Field            | Type    | Description                              |
|------------------|---------|------------------------------------------|
| `ActivationTime` | `float` | Additional activation time               |
| `LifeTime`       | `float` | Additional lifetime                      |
| `Speed`          | `float` | Additional speed                         |
| `Cooldown`       | `float` | Additional cooldown                      |
| `Price`          | `int`   | Crafting price to add this event         |

Concrete subclasses: `AbilityOnTickEvent`, `AbilityOnHitEvent`, `AbilityOnPreSpawnEvent`, `AbilityOnSpawnEvent`, `AbilityOnDestroyEvent`.

## Related Files

```
Shared/Core/Entity/Ability/                            # Core interfaces (IAbilityController, IAbilityKnowledgeController, ICooldownController)
Shared/Implementation/Entity/CharacterAttribute/       # Attribute templates for speed/cooldown reduction
Shared/Implementation/Entity/Target/                   # TargetController used for targeted abilities
Server/Implementation/World/SceneServer/Ability/       # Server-side ability systems and DB persistence
Client/UI/Controls/World/Ability/                      # Client-side ability bar and crafting UI
```
