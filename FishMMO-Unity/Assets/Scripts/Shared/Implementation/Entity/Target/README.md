# Target System

**Short description:** Raycast-based targeting system for FishMMO characters, handling target selection, self-hit avoidance, and event-driven notifications on both client and server.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation / Build](#installation--build)
- [Quick Start Guide](#quick-start-guide)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

The Target system provides raycast-based targeting for FishMMO characters. It handles target selection via physics raycasts, self-hit avoidance, target change/update/clear event notifications, and supports both client-side (screen-space mouse ray) and server-side (physics scene ray) targeting. The system operates on a configurable tick rate and exposes target state through a lightweight `TargetInfo` struct consumed by the ability system and UI.

## Supported Platforms

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | Yes       | Full client and server support |
| Linux    | Yes       | Full client and server support |
| WebGL    | Yes       | Client-side targeting only |

- **Unity Version:** Unity 6.3 LTS
- **Scripting Backend:** IL2CPP

## Features

- Raycast-based target selection from mouse position (client) or caller-provided ray (server)
- Self-hit avoidance with automatic re-raycast past the player's own collider
- Configurable physics layer mask for target filtering
- Event-driven notifications: `OnChangeTarget`, `OnUpdateTarget`, `OnClearTarget`
- Lightweight `TargetInfo` value-type struct for zero-allocation target state
- Configurable tick rate (`TARGET_UPDATE_RATE`) to control update frequency
- Maximum target distance clamping (`MAX_TARGET_DISTANCE`)
- Platform-aware raycasting (client `Physics.Raycast` vs. server `PhysicsScene.Raycast`)
- Automatic cleanup of event delegates and target state on destroy

## Prerequisites

- **Unity 6.3 LTS**
- **FishNetworking** — Network transport and `CharacterBehaviour` base class
- **FishMMO Shared Core** — `PlayerCharacter`, `CharacterBehaviour`, scene/physics infrastructure

## Installation / Build

This is an integrated module within the FishMMO project. No separate installation or build steps are required. The target system is included automatically when the FishMMO Shared assembly is referenced.

## Quick Start Guide

1. Ensure `PlayerCharacter` has a `TargetController` component (added automatically via `[RequireComponent]`).
2. Configure the `LayerMask` field in the Inspector to include targetable physics layers.
3. Subscribe to targeting events on `ITargetController`:
   - `OnChangeTarget` — fired when a new target is selected.
   - `OnUpdateTarget` — fired when the same target is re-validated.
   - `OnClearTarget` — fired when the previous target is deselected.
4. On the server, call `ITargetController.UpdateTarget(origin, direction, maxDistance)` directly from the ability system.

## Configuration

### Constants

| Constant               | Value   | Description                                      |
|------------------------|---------|--------------------------------------------------|
| `MAX_TARGET_DISTANCE`  | `50.0f` | Maximum allowed raycast distance                 |
| `TARGET_UPDATE_RATE`   | `0.05f` | Seconds between target update ticks (client-side)|

### Inspector Fields

| Field       | Type         | Description                                     |
|-------------|--------------|-------------------------------------------------|
| `LayerMask` | `LayerMask`  | Physics layer mask for target raycasts          |

### Runtime State

| Field       | Type         | Description                                     |
|-------------|--------------|-------------------------------------------------|
| `Last`      | `TargetInfo` | The previous frame's target information         |
| `Current`   | `TargetInfo` | The current frame's target information          |

### TargetInfo Struct

| Field         | Type        | Description                                           |
|---------------|-------------|-------------------------------------------------------|
| `Target`      | `Transform` | The transform of the targeted object, or `null` if nothing was hit |
| `HitPosition` | `Vector3`   | The world-space position where the ray hit, or the ray endpoint if nothing was hit |

### Platform Differences

| Feature              | Client (`!UNITY_SERVER`)                          | Server (`UNITY_SERVER`)                                |
|----------------------|---------------------------------------------------|--------------------------------------------------------|
| **Raycast API**      | `Physics.Raycast(ray, ...)`                       | `PlayerCharacter.Motor.PhysicsScene.Raycast(...)`      |
| **Update loop**      | `Update()` with tick rate                         | No automatic updates; called by ability system         |
| **Ray source**       | `Camera.main.ScreenPointToRay(Input.mousePosition)` | Provided by caller (e.g., ability controller)       |

## Usage Examples

### ITargetController Interface

| Member           | Type / Signature                                          | Description                                         |
|------------------|-----------------------------------------------------------|-----------------------------------------------------|
| `Current`        | `TargetInfo`                                              | The current target information (read-only)          |
| `OnChangeTarget` | `event Action<Transform>`                                 | Fired when the target changes to a different object |
| `OnUpdateTarget` | `event Action<Transform>`                                 | Fired when the same target is re-validated          |
| `OnClearTarget`  | `event Action<Transform>`                                 | Fired when the previous target is deselected        |
| `UpdateTarget`   | `TargetInfo UpdateTarget(Vector3, Vector3, float)`        | Performs a raycast and returns updated target info   |

### Client-Side Update Loop

On the client (`!UNITY_SERVER`), `Update()` runs at `TARGET_UPDATE_RATE`:

1. Constructs a ray from `Camera.main.ScreenPointToRay(Input.mousePosition)`.
2. Calls `UpdateTarget()` with the ray's origin, direction, and `MAX_TARGET_DISTANCE`.
3. Compares `Current.Target` with `Last.Target`:
   - **Target changed**: Invokes `OnClearTarget` for the old target, then `OnChangeTarget` for the new.
   - **Target unchanged**: Invokes `OnUpdateTarget` for the current target.

### Self-Hit Avoidance

When the raycast hits the casting character itself, the controller fires a second raycast:
1. Moves the ray origin slightly past the hit point (`hit.point + direction.normalized * 0.1f`).
2. Reduces the max distance by the already-traveled distance.
3. Uses the new hit (if any) as the actual target.

This prevents the player from always targeting themselves when the camera is behind the character.

### External Integration Points

- **Ability System** — `AbilityController` calls `ITargetController.UpdateTarget()` to resolve the target before spawning ability objects. `AbilityObject.Spawn()` and `SetAbilitySpawnPosition()` use `TargetInfo` to determine projectile aim direction and ground-targeted ability placement.
- **UI System** — Subscribes to `OnChangeTarget`, `OnUpdateTarget`, and `OnClearTarget` to display target frames, health bars, name labels, and outline highlights.
- **PlayerCharacter** — `TargetController` is a `[RequireComponent]` on `PlayerCharacter`, ensuring every player entity has targeting capability.

### Cleanup

`OnDestroying()` nulls all three event delegates (`OnChangeTarget`, `OnUpdateTarget`, `OnClearTarget`) and resets `Last` and `Current` to `default` to prevent dangling references.

## Operational Checks

| Check | How to Verify | Expected Result |
|-------|---------------|-----------------|
| Target selection | Hover mouse over a targetable entity, observe target frame UI | `OnChangeTarget` fires, UI displays target info |
| Target clear | Move mouse away from all entities | `OnClearTarget` fires, UI hides target info |
| Self-hit avoidance | Position camera behind character, hover over distant entity | Target resolves to distant entity, not self |
| Layer mask filtering | Set `LayerMask` to exclude a layer, hover over entity on that layer | Entity is not targeted |
| Server-side targeting | Use ability on server, check `UpdateTarget` return | `TargetInfo` contains correct target and hit position |
| Event consistency | Subscribe to all three events, cycle through targets | Events fire in correct order: Clear → Change or Update |

## Flow Diagram

### UpdateTarget Flow

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

### Event Flow

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

## Project Structure

### Directory Structure

```
Target/
├── ITargetController.cs    # Target controller interface
├── TargetController.cs      # Per-entity controller (CharacterBehaviour)
└── TargetInfo.cs            # Lightweight target data struct
```

### Inheritance Hierarchies

#### Controllers (CharacterBehaviour)

```
CharacterBehaviour
└── TargetController : ITargetController
```

#### Data Structures

```
TargetInfo (struct)
    ├── Target      : Transform
    └── HitPosition : Vector3
```

## License

This project is subject to the FishMMO project license.
