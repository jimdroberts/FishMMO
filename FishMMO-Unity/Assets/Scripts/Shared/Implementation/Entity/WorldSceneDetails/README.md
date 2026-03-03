# WorldSceneDetails System

## Overview

The WorldSceneDetails system is a data-driven framework for defining, caching, and managing scene configuration in FishMMO. It handles spawn positions, respawn positions, teleporters (both trigger-based and interactable), scene boundaries, day/night cycling, and per-scene settings such as client limits and transition images. At editor time a cache reader scans all world scenes, extracts these objects, and serializes them into a `WorldSceneDetailsCache` ScriptableObject that the server and client can access at runtime without loading scenes.

## Directory Structure

```
WorldSceneDetails/
├── WorldSceneDetails.cs                    # Serializable per-scene data container
├── WorldSceneDetailsCache.cs               # ScriptableObject cache of all scene details
├── WorldSceneDetailsCacheEditor.cs         # Custom inspector with Rebuild button
├── WorldSceneDetailsCacheReader.cs         # Editor-time scene scanner and cache builder
├── WorldSceneDetailsDictionary.cs          # SerializableDictionary<string, WorldSceneDetails>
├── WorldSceneSettings.cs                   # MonoBehaviour for per-scene settings and day/night cycle
├── WorldDayNightCycle.cs                   # Standalone MonoBehaviour for day/night cycle management
├── Editor/
│   ├── BaseHeightAdjustEditor.cs           # Abstract editor for mouse-driven height adjustment
│   ├── CharacterInitialSpawnPositionEditor.cs  # Custom editor for initial spawn positions
│   ├── CharacterRespawnPositionEditor.cs       # Custom editor for respawn positions
│   └── TeleporterDestinationEditor.cs          # Custom editor for teleporter destinations
├── SpawnPosition/
│   ├── CharacterInitialSpawnPosition.cs            # MonoBehaviour marker for initial spawn points
│   ├── CharacterInitialSpawnPositionDetails.cs     # Serializable spawn position data
│   └── CharacterInitialSpawnPositionDictionary.cs  # SerializableDictionary for spawn positions
├── RespawnPosition/
│   ├── CharacterRespawnPosition.cs             # MonoBehaviour marker for respawn points
│   ├── CharacterRespawnPositionDetails.cs      # Serializable respawn position data
│   └── CharacterRespawnPositionDictionary.cs   # SerializableDictionary for respawn positions
├── SceneBoundary/
│   ├── IBoundary.cs                    # Abstract MonoBehaviour base for boundaries
│   ├── SceneBoundary.cs                # Box-based boundary implementation
│   ├── TerrainBoundary.cs              # Terrain-data-driven boundary implementation
│   ├── SceneBoundaryDetails.cs         # Serializable boundary data with containment check
│   └── SceneBoundaryDictionary.cs      # SerializableDictionary with aggregate containment query
└── Teleporter/
    ├── SceneTeleporter.cs              # Trigger-based teleporter MonoBehaviour (server-only)
    ├── SceneTeleporterDetails.cs       # Serializable teleporter route data
    ├── SceneTeleporterDictionary.cs    # SerializableDictionary for teleporters
    ├── TeleporterDestination.cs        # MonoBehaviour marker for teleporter endpoints
    └── TeleporterDestinationDetails.cs # Serializable destination data
```

## Inheritance Hierarchies

### Scene Markers (MonoBehaviours)

```
MonoBehaviour
├── CharacterInitialSpawnPosition   # Initial spawn point marker
├── CharacterRespawnPosition        # Respawn point marker
├── SceneTeleporter                 # Trigger-based zone teleporter (server-only)
├── TeleporterDestination           # Teleporter arrival point marker
├── WorldSceneSettings              # Per-scene settings + day/night cycle
└── WorldDayNightCycle              # Standalone day/night cycle

IBoundary (abstract MonoBehaviour)
├── SceneBoundary                   # Manual box boundary
└── TerrainBoundary                 # Terrain-data-driven boundary
```

### ScriptableObjects

```
ScriptableObject
├── WorldSceneDetailsCache          # Central cache of all scene data
└── WorldSceneDetailsCacheReader    # Editor-time scene scanner
```

### Custom Editors

```
Editor
└── BaseHeightAdjustEditor (abstract)
    ├── CharacterInitialSpawnPositionInspector
    ├── CharacterRespawnPositionEditor
    └── TeleporterDestinationEditor

Editor
└── WorldSceneDetailsCacheEditor    # Rebuild button for the cache
```

### Serializable Data

```
WorldSceneDetails                   # Per-scene container
CharacterInitialSpawnPositionDetails
CharacterRespawnPositionDetails
SceneBoundaryDetails
SceneTeleporterDetails
TeleporterDestinationDetails

SerializableDictionary<string, T>
├── WorldSceneDetailsDictionary
├── CharacterInitialSpawnPositionDictionary
├── CharacterRespawnPositionDictionary
├── SceneTeleporterDictionary
└── SceneBoundaryDictionary
```

## WorldSceneDetails

The core serializable data container for a single scene. Stored as a value in `WorldSceneDetailsDictionary`, keyed by scene name.

| Field                  | Type                                        | Description                                                   |
|------------------------|---------------------------------------------|---------------------------------------------------------------|
| `MaxClients`           | `int`                                       | Maximum number of clients allowed in this scene               |
| `SceneTransitionImage` | `Sprite`                                    | Image displayed during scene transitions                      |
| `InitialSpawnPositions`| `CharacterInitialSpawnPositionDictionary`    | Spawn positions for first-time character entry                |
| `RespawnPositions`     | `CharacterRespawnPositionDictionary`         | Respawn positions for death or re-entry                       |
| `Teleporters`          | `SceneTeleporterDictionary`                  | Teleporters with destination scene, position, and rotation    |
| `Boundaries`           | `SceneBoundaryDictionary`                    | Playable area boundaries                                     |

## Cache System

### WorldSceneDetailsCache

A `ScriptableObject` stored at `Assets/Prefabs/Shared/WorldSceneDetails.asset`. Contains:
- A `List<WorldSceneDetailsCacheReader>` of readers that know how to scan scenes.
- A `WorldSceneDetailsDictionary` mapping scene names to `WorldSceneDetails`.

### WorldSceneDetailsCacheReader

A `ScriptableObject` whose virtual `Rebuild(ref WorldSceneDetailsDictionary)` method scans all world scenes at editor time. The default implementation:

1. Clears the existing dictionary.
2. Opens each world scene additively in the editor.
3. Validates every scene has at least one `IBoundary` (logs error and skips if missing).
4. Extracts `WorldSceneSettings`, spawn positions, respawn positions, boundaries, teleporters, interactable teleporters, and teleporter destinations.
5. Closes each scene after scanning.
6. Connects teleporters to destinations by naming convention (`"From" + teleporterName`).
7. Restores the original editor scene.

### WorldSceneDetailsCacheEditor

Custom inspector that adds a **Rebuild** button to trigger cache regeneration and mark the asset dirty.

### Rebuild Flow

```
WorldSceneDetailsCacheEditor (Rebuild button)
    │
    └── WorldSceneDetailsCache.Rebuild()
            │
            └── foreach WorldSceneDetailsCacheReader
                    │
                    └── Rebuild(ref WorldSceneDetailsDictionary)
                            │
                            ├── Clear dictionary
                            ├── foreach world scene:
                            │   ├── Open additively
                            │   ├── Validate IBoundary exists
                            │   ├── Read WorldSceneSettings
                            │   ├── Extract CharacterInitialSpawnPosition[]
                            │   ├── Extract CharacterRespawnPosition[]
                            │   ├── Extract IBoundary[]
                            │   ├── Extract SceneTeleporter[]
                            │   ├── Extract Teleporter[] (interactable)
                            │   ├── Extract TeleporterDestination[]
                            │   └── Close scene
                            │
                            └── Connect teleporters → destinations by name
```

## Spawn Positions

### CharacterInitialSpawnPosition

MonoBehaviour placed in scenes to mark where new characters can spawn. Supports race-based filtering via `AllowedRaces`.

| Field          | Type                  | Description                                     |
|----------------|-----------------------|-------------------------------------------------|
| `AllowedRaces` | `List<RaceTemplate>`  | Races permitted to use this spawn point         |
| `GizmoColor`   | `Color` (editor only) | Color of the editor gizmo                       |

### CharacterInitialSpawnPositionDetails

Serialized data extracted during cache rebuild.

| Field         | Type              | Description                                |
|---------------|-------------------|--------------------------------------------|
| `SpawnerName` | `string`          | Name of the spawn position object          |
| `SceneName`   | `string`          | Scene containing this spawn position       |
| `Position`    | `Vector3`         | World position                             |
| `Rotation`    | `Quaternion`      | Spawn rotation                             |
| `AllowedRaces`| `List<RaceTemplate>` | Race filter copied from the MonoBehaviour |

### CharacterRespawnPosition

MonoBehaviour placed in scenes to mark respawn points after death. No race filtering — any character can respawn at any respawn point in the scene.

### CharacterRespawnPositionDetails

| Field      | Type         | Description                     |
|------------|--------------|---------------------------------|
| `Position` | `Vector3`    | World respawn position          |
| `Rotation` | `Quaternion` | Respawn rotation                |

## Scene Boundaries

### IBoundary

Abstract MonoBehaviour base class requiring implementations to provide:

| Method               | Returns   | Description                           |
|----------------------|-----------|---------------------------------------|
| `GetBoundaryOffset()`| `Vector3` | Center/origin of the boundary         |
| `GetBoundarySize()`  | `Vector3` | Extents of the boundary               |

### SceneBoundary

Manual box-based boundary. Uses `transform.position` as the center and a configurable `BoundarySize` vector. Draws wireframe (grey) and solid (green, when selected) gizmos.

### TerrainBoundary

Automatically derives boundary size and center from the attached `Terrain` component's `terrainData.bounds`. Supports an additional `BoundaryOffset` for fine-tuning. Requires `[RequireComponent(typeof(Terrain))]`.

### SceneBoundaryDetails

Serialized boundary data with an axis-aligned containment check:

```csharp
ContainsPoint(Vector3 point)
    → checks X, Z, Y half-extents from BoundaryOrigin
```

### SceneBoundaryDictionary

Extends `SerializableDictionary<string, SceneBoundaryDetails>` with an aggregate query:

```csharp
PointContainedInBoundaries(Vector3 point)
    → returns true if point is inside ANY boundary
    → returns true if no boundaries are defined (permissive default)
```

## Teleporters

### SceneTeleporter

Trigger-based teleporter. On the server (`UNITY_SERVER`), when a collider enters the trigger:
1. Validates the collider has an `IPlayerCharacter`.
2. Checks the character is not already teleporting.
3. Calls `character.Teleport(gameObject.name)` using the teleporter's GameObject name as a lookup key.

### Teleporter (Interactable)

An interactable-based teleporter (defined in `Entity/Interactable/Teleporter.cs`). Also scanned by the cache reader and connected to destinations using the same naming convention.

### TeleporterDestination

MonoBehaviour marker placed at the arrival point of a teleporter. The naming convention links teleporters to destinations: a teleporter named `"MyPortal"` connects to a destination named `"FromMyPortal"`.

### SceneTeleporterDetails

| Field        | Type         | Description                                       |
|--------------|--------------|---------------------------------------------------|
| `From`       | `string`     | Source teleporter name (internal use)              |
| `ToScene`    | `string`     | Target scene name                                 |
| `ToPosition` | `Vector3`    | Arrival position in the target scene              |
| `ToRotation` | `Quaternion` | Arrival rotation in the target scene              |

### TeleporterDestinationDetails

| Field      | Type         | Description                                |
|------------|--------------|--------------------------------------------|
| `Scene`    | `string`     | Scene containing the destination           |
| `Position` | `Vector3`    | World position of the destination          |
| `Rotation` | `Quaternion` | Rotation at the destination                |

### Teleporter Connection Flow

```
Cache Reader scans scenes:
    SceneTeleporter "Portal_A" → SceneTeleporterDetails { From = "Portal_A" }
    TeleporterDestination "FromPortal_A" → TeleporterDestinationDetails { Scene, Position, Rotation }

Post-scan connection:
    destinationName = "From" + "Portal_A" = "FromPortal_A"
    → Looks up TeleporterDestinationDetails
    → Assigns ToScene, ToPosition, ToRotation to SceneTeleporterDetails
    → Adds to scene's Teleporters dictionary
```

## Day/Night Cycle

Two MonoBehaviours provide day/night cycle management: `WorldSceneSettings` (combined with scene configuration) and `WorldDayNightCycle` (standalone). Both share the same core logic.

### Time Calculation

The game time of day is derived from `DateTime.UtcNow`:

```
secondsPerGameDay = DayCycleDuration + NightCycleDuration
gameTimeOfDay = UtcNow.TimeOfDay.TotalSeconds % secondsPerGameDay
```

Default cycle duration is 3 hours day + 3 hours night (6-hour full cycle).

### Cycle Components

| Feature            | Description                                                                |
|--------------------|----------------------------------------------------------------------------|
| **State Toggle**   | Switches `isDaytime` flag, enables/disables `DayObjects` and `NightObjects`|
| **Rotation**       | Rotates `RotateObjects` 0°–180° during day, 180°–360° during night         |
| **Skybox Lerp**    | Lerps between `DaySkyboxMaterial` and `NightSkyBoxMaterial` (client only)  |
| **Object Fading**  | Fades `DayFadeObjects` and `NightFadeObjects` alpha over `FadeThreshold`   |
| **Fog**            | Triggers `RegionChangeFogAction.Invoke()` on Awake for initial fog setup   |

### WorldSceneSettings Fields

| Field                  | Type                      | Description                                          |
|------------------------|---------------------------|------------------------------------------------------|
| `MaxClients`           | `int`                     | Maximum clients for this scene                       |
| `SceneTransitionImage` | `Sprite`                  | Transition loading screen image                      |
| `DefaultSceneFog`      | `RegionChangeFogAction`   | Fog settings triggered on scene load                 |
| `DayNightCycle`        | `bool`                    | Enable/disable the cycle                             |
| `DayCycleDuration`     | `int`                     | Day duration in seconds                              |
| `NightCycleDuration`   | `int`                     | Night duration in seconds                            |
| `DaySkyboxMaterial`    | `Material`                | Skybox material for daytime                          |
| `NightSkyBoxMaterial`  | `Material`                | Skybox material for nighttime                        |
| `RotateObjects`        | `List<GameObject>`        | Objects rotated with the cycle                       |
| `DayObjects`           | `List<GameObject>`        | Objects enabled during day                           |
| `NightObjects`         | `List<GameObject>`        | Objects enabled during night                         |
| `FadeThreshold`        | `float`                   | Fade transition duration in seconds                  |
| `DayFadeObjects`       | `List<GameObject>`        | Objects that fade out during day                     |
| `NightFadeObjects`     | `List<GameObject>`        | Objects that fade out during night                   |

## Editor Tools

### BaseHeightAdjustEditor

Abstract `Editor` class that provides mouse-driven height adjustment in the Scene view:
1. On mouse down, captures the selected `GameObject`.
2. On mouse up, performs a `Physics.SphereCast` downward from above the object.
3. Snaps the object to the hit point (+ 0.1 Y offset) for ground alignment.

Three concrete editors inherit this:
- `CharacterInitialSpawnPositionInspector` — for initial spawn positions
- `CharacterRespawnPositionEditor` — for respawn positions
- `TeleporterDestinationEditor` — for teleporter destinations

## External Integration Points

- **Server Scene Management** — Reads `MaxClients`, `InitialSpawnPositions`, `RespawnPositions`, `Teleporters`, and `Boundaries` to manage player placement and scene capacity.
- **Client Scene Transitions** — Uses `SceneTransitionImage` for loading screens during scene changes.
- **Player Teleportation** — `IPlayerCharacter.Teleport(name)` looks up `SceneTeleporterDetails` by name to determine the target scene and position.
- **Boundary Enforcement** — Server validates player positions against `SceneBoundaryDictionary.PointContainedInBoundaries()`.
- **Race System** — `CharacterInitialSpawnPositionDetails.AllowedRaces` filters spawn points by the player's race template.
- **Region System** — `RegionChangeFogAction` is used by both `WorldSceneSettings` and `WorldDayNightCycle` to trigger fog on scene load.
- **Interactable System** — `Teleporter` (interactable) is scanned alongside `SceneTeleporter` during cache rebuild.