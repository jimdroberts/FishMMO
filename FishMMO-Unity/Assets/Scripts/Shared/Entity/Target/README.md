# Target System

## Overview

The Target system provides raycast-based targeting for FishMMO characters. It handles target selection via physics raycasts, self-hit avoidance, target change/update/clear event notifications, and supports both client-side (screen-space mouse ray) and server-side (physics scene ray) targeting. The system operates on a configurable tick rate and exposes target state through a lightweight `TargetInfo` struct consumed by the ability system and UI.

## Directory Structure

```
Target/
├── ITargetController.cs    # Target controller interface
├── TargetController.cs      # Per-entity controller (CharacterBehaviour)
└── TargetInfo.cs            # Lightweight target data struct
```

## Inheritance Hierarchies

### Controllers (CharacterBehaviour)

```
CharacterBehaviour
└── TargetController : ITargetController
```

### Data Structures

```
TargetInfo (struct)
    ├── Target      : Transform
    └── HitPosition : Vector3
```

## TargetInfo

A lightweight value-type struct that holds the result of a targeting raycast.

| Field         | Type        | Description                                           |
|---------------|-------------|-------------------------------------------------------|
| `Target`      | `Transform` | The transform of the targeted object, or `null` if nothing was hit |
| `HitPosition` | `Vector3`   | The world-space position where the ray hit, or the ray endpoint if nothing was hit |

## ITargetController

Interface exposing targeting capabilities and events.

| Member           | Type / Signature                                          | Description                                         |
|------------------|-----------------------------------------------------------|-----------------------------------------------------|
| `Current`        | `TargetInfo`                                              | The current target information (read-only)          |
| `OnChangeTarget` | `event Action<Transform>`                                 | Fired when the target changes to a different object |
| `OnUpdateTarget` | `event Action<Transform>`                                 | Fired when the same target is re-validated          |
| `OnClearTarget`  | `event Action<Transform>`                                 | Fired when the previous target is deselected        |
| `UpdateTarget`   | `TargetInfo UpdateTarget(Vector3, Vector3, float)`        | Performs a raycast and returns updated target info   |

## TargetController

The `TargetController` is a `CharacterBehaviour` that manages target selection through raycasting. It is a required component on `PlayerCharacter` via `[RequireComponent(typeof(TargetController))]`.

### Constants

| Constant               | Value   | Description                                      |
|------------------------|---------|--------------------------------------------------|
| `MAX_TARGET_DISTANCE`  | `50.0f` | Maximum allowed raycast distance                 |
| `TARGET_UPDATE_RATE`   | `0.05f` | Seconds between target update ticks (client-side)|

### Fields

| Field       | Type         | Description                                     |
|-------------|--------------|-------------------------------------------------|
| `LayerMask` | `LayerMask`  | Physics layer mask for target raycasts          |
| `Last`      | `TargetInfo` | The previous frame's target information         |
| `Current`   | `TargetInfo` | The current frame's target information          |

### Client-Side Update Loop

On the client (`!UNITY_SERVER`), `Update()` runs at `TARGET_UPDATE_RATE`:

1. Constructs a ray from `Camera.main.ScreenPointToRay(Input.mousePosition)`.
2. Calls `UpdateTarget()` with the ray's origin, direction, and `MAX_TARGET_DISTANCE`.
3. Compares `Current.Target` with `Last.Target`:
   - **Target changed**: Invokes `OnClearTarget` for the old target, then `OnChangeTarget` for the new.
   - **Target unchanged**: Invokes `OnUpdateTarget` for the current target.

### UpdateTarget Flow

The core targeting method, used by both client and server:

```
UpdateTarget(origin, direction, maxDistance)
    │
    ├── Clamp distance to [0, MAX_TARGET_DISTANCE]
    │
    ├── Raycast (client: Physics.Raycast, server: PhysicsScene.Raycast)
    │   │
    │   ├── Hit detected:
    │   │   │
    │   │   ├── Self-hit check (hitPlayerCharacter.ID == Character.ID)
    │   │   │   │
    │   │   │   └── Re-raycast from hit point + 0.1 * direction
    │   │   │       (uses remaining distance to avoid overshooting)
    │   │   │
    │   │   └── Return TargetInfo(hit.transform, hit.point)
    │   │
    │   └── No hit:
    │       └── Return TargetInfo(null, ray.GetPoint(distance))
    │
    └── Store result in Current, move old Current to Last
```

### Self-Hit Avoidance

When the raycast hits the casting character itself, the controller fires a second raycast:
1. Moves the ray origin slightly past the hit point (`hit.point + direction.normalized * 0.1f`).
2. Reduces the max distance by the already-traveled distance.
3. Uses the new hit (if any) as the actual target.

This prevents the player from always targeting themselves when the camera is behind the character.

### Platform Differences

| Feature              | Client (`!UNITY_SERVER`)                          | Server (`UNITY_SERVER`)                                |
|----------------------|---------------------------------------------------|--------------------------------------------------------|
| **Raycast API**      | `Physics.Raycast(ray, ...)`                       | `PlayerCharacter.Motor.PhysicsScene.Raycast(...)`      |
| **Update loop**      | `Update()` with tick rate                         | No automatic updates; called by ability system         |
| **Ray source**       | `Camera.main.ScreenPointToRay(Input.mousePosition)` | Provided by caller (e.g., ability controller)       |

### Cleanup

`OnDestroying()` nulls all three event delegates (`OnChangeTarget`, `OnUpdateTarget`, `OnClearTarget`) and resets `Last` and `Current` to `default` to prevent dangling references.

## Event Flow

```
Frame N:
    UpdateTarget() → Current = { TargetA, hitPos }

Frame N+1:
    UpdateTarget() → Current = { TargetB, hitPos }
    Current.Target != Last.Target
        → OnClearTarget(TargetA)      // old target deselected
        → OnChangeTarget(TargetB)     // new target selected

Frame N+2:
    UpdateTarget() → Current = { TargetB, hitPos }
    Current.Target == Last.Target
        → OnUpdateTarget(TargetB)     // same target, position refreshed

Frame N+3:
    UpdateTarget() → Current = { null, endpoint }
    Current.Target != Last.Target
        → OnClearTarget(TargetB)      // old target deselected
        → OnChangeTarget(null)        // no new target
```

## External Integration Points

The target system is consumed by several other systems:

- **Ability System** — `AbilityController` calls `ITargetController.UpdateTarget()` to resolve the target before spawning ability objects. `AbilityObject.Spawn()` and `SetAbilitySpawnPosition()` use `TargetInfo` to determine projectile aim direction and ground-targeted ability placement.
- **UI System** — Subscribes to `OnChangeTarget`, `OnUpdateTarget`, and `OnClearTarget` to display target frames, health bars, name labels, and outline highlights.
- **PlayerCharacter** — `TargetController` is a `[RequireComponent]` on `PlayerCharacter`, ensuring every player entity has targeting capability.