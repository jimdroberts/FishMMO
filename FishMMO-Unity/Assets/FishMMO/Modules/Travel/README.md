# Travel module

Discovering waypoints and fast travel between them (Phase 2 module). Everything the feature is lives in this folder;
deleting it removes the feature while the game still builds and runs, and the `character_waypoints` table and its rows
stay as they are.

```
Assets/FishMMO/Modules/Travel/
├── Runtime/   FishMMO.Travel          WaypointController (a CharacterPart), WaypointTravel (the rule), broadcasts,
│                                      UnlockWaypointAction, TeleportToWaypointAction, TravelModule
├── Server/    FishMMO.Travel.Server   WaypointTravelSystem (+ asset), WaypointPersistence + its save participant
├── Templates/                         Waypoint Interact (the discovery trigger)
├── Tests/     FishMMO.Travel.Tests    TravelWaypointTests, TravelModuleTests
└── README.md
```

How it plugs in, with nothing outside the folder naming it:

| Seam | Where (core) | What the module does |
|---|---|---|
| Per-character record | `CharacterPart` / `[assembly: CharacterPart]` | `WaypointController` is added to every player at wake; its spawn state rides the character's payload. No prefab carries it. |
| Interface the map reads | `IWaypointController` (Shared/Core) | The controller implements it, including `RequestTravel`. Without the module the map finds no controller: nothing unlocked, no travel. |
| Discovery on use | `InteractableTriggers` | `WaypointTravelSystem` attaches `Waypoint Interact` to every `Waypoint`; the core waypoint prefab names no trigger. |
| Server system | `Server.AdoptModuleSystems` | `WaypointTravelSystem.asset` carries the `SceneServer_Systems` label. |
| Save and load | `[assembly: CharacterPersistence]` | `WaypointPersistenceParticipant` over `character_waypoints`. |

Stays in core, because the world uses it without fast travel: the `Waypoint` scene object and its baked details
(rescue points, cloud landings, the map's landmarks), `WaypointUnlockMask` (portals), `WaypointRegistry`,
`WaypointTravelPolicy`, `WaypointSceneAudit` and the "Waypoint Travel" arrival trigger.
