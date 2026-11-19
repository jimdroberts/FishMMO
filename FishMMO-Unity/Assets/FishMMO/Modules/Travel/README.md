# Travel module

**Short description:** Discovering waypoints and fast travel between them.

| | |
|---|---|
| **Module id** | `travel` (`TravelModule`) |
| **Assemblies** | `FishMMO.Travel`, `.Server`, `.Tests` |
| **Depends on** | The core (the `Waypoint` scene objects and their baked details) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Phase 2 of the [core and modules plan](https://claude.ai/artifact/FwoyRum2ScqjbE1gm8sm9k), 2026-11-16 |

## Folders

```
Assets/FishMMO/Modules/Travel/
├── Runtime/   FishMMO.Travel          WaypointController (a CharacterPart), WaypointTravel (the rule), WaypointBroadcasts,
│                                      WaypointEventData, UnlockWaypointAction, TeleportToWaypointAction, TravelModule
├── Server/    FishMMO.Travel.Server   WaypointTravelSystem (+ asset), WaypointPersistence and its save participant
├── Templates/                         Waypoint Interact (the discovery trigger)
├── Tests/     FishMMO.Travel.Tests    TravelWaypointTests, TravelModuleTests
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: CharacterPart]` | `WaypointController` is added to every player at wake; its spawn state rides the character's payload. No prefab carries it. |
| `IWaypointController` (core contract) | The controller implements it, including `RequestTravel`; the map reads it. |
| `InteractableTriggers` | `WaypointTravelSystem` attaches `Waypoint Interact` to every `Waypoint`; the core waypoint prefab names no trigger. |
| `SceneServer_Systems` label | `WaypointTravelSystem.asset` is adopted by the scene server. |
| `[assembly: CharacterPersistence]` | `WaypointPersistenceParticipant` over `character_waypoints`. |
| `[assembly: GameModule]` | `TravelModule` ("travel"). |

## What stays in the core

The world uses waypoints without fast travel, so the core keeps the `Waypoint` scene object and its baked details
(rescue points, cloud landings, the map's landmarks), `WaypointUnlockMask` (portals), `WaypointRegistry`,
`WaypointTravelPolicy`, `WaypointSceneAudit` and the "Waypoint Travel" arrival trigger.

## Removing it

Delete `Assets/FishMMO/Modules/Travel` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
The map then finds no controller: nothing is unlocked and there is no fast travel. The `character_waypoints` table and
its rows stay as they are.

## License

This project is subject to the FishMMO project license.
