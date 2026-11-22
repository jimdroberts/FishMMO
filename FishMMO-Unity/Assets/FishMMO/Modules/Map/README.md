# Map module

**Short description:** The player's maps: the world map window and the minimap, fog of war, map notes, marker filters
and visibility rules, the compass, and the client's shared map state. [Client/README.md](Client/README.md) documents the
map system in detail.

| | |
|---|---|
| **Module id** | `map` (`MapModule`) |
| **Assemblies** | `FishMMO.Map`, `.Client`, `.Server`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (map markers and their registry, world map definitions, points of interest, region labels, the baked world maps, true north, the waypoint controller contract) |
| **Depended on by** | World Design (its point-of-interest map preview and map tests draw with the module) |
| **Remove together with** | World Design |
| **Moved** | Out of the core, 2026-11-20 (Phase 5: the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Map/
├── Runtime/              FishMMO.Map              MapModule, MapRevealBroadcast
├── Server/               FishMMO.Map.Server       MapOperatorCommands (/gm reveal map|waypoints|sites|all)
├── Client/               FishMMO.Map.Client       UITKMap (+ UIMap.prefab/uxml/uss), UITKMinimap (+ UIMinimap.prefab/uxml/uss),
│                                                  UITKMapView, MapViewTransform, ClientMapSystem, MinimapCameraRenderer,
│                                                  MapMarkerFilter, MapRelationshipTracker, MapContent, MapFilters,
│                                                  FogOfWarMap/Store, MapNote/Store, MapCompass, Cartography; UIMapShared.uss,
│                                                  UIMapPOI.uss
├── Editor/RenderScratch/ FishMMO.Map.RenderScratch   The minimap in the HUD renders and overlap probe; the leak probe's
│                                                  minimap and marker modes
├── Tests/                FishMMO.Map.Tests        Map system, marker culling and clicks, dungeon entrances, explored
│                                                  readout, side bar layout, waypoints on the map, wiring
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `MapModule` ("map"). |
| `ClientWorldGUI_Panels` label | `UIMap.prefab` and `UIMinimap.prefab` are placed into the world GUI. The core opens them by name (`UIManager`, the Minimap and World Map keys); a Show that comes before the panel registered is applied when it does. |
| `MapMarkerRegistry`, `WorldSceneDetails` (shared core) | The markers objects register and the scene's baked map data are what the maps draw. |
| `ExploreMapAction.OnExploreMap` (shared core) | The minimap reveals fog when the action runs on the owning client. |
| `OperatorCommandProvider` (scene server) | `/gm reveal` (2026-11-22): the caller only, the scene they stand in. `map` sends `MapRevealBroadcast` (the minimap explores the scene); `waypoints` unlocks every registered waypoint through `IWaypointController`; `sites` sets every `PointOfInterestDiscovery` bit. Each new waypoint or site is credited with the `AchievementIncrementAction` that follows the find step in its own triggers. |
| `IWaypointController` events (shared core) | The world map draws unlocked waypoints and starts fast travel through the contract; the Travel module implements it. |

## What stays in the core

The data a scene and a build carry: `MapMarker` and `MapMarkerRegistry`, `WorldMapDefinition`, `MapPointOfInterest`,
`MapRegionLabel`, `MapBoundsResolver` (`Shared/Implementation/Entity/Map`), the world map bake (`WorldMapBaker`, the
build steps and the dashboard's bake buttons; the portal views and the world modules use it too), `ExploreMapAction`,
`TrueNorth` (a world contract), the map settings in `ClientSettings` and the Minimap and World Map key bindings.

## Removing it

Delete `Assets/FishMMO/Modules/Map` and `Assets/FishMMO/Modules/WorldDesign` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). Players then have no minimap and no world map, and the Minimap
and World Map keys do nothing. Map markers, map definitions and points of interest are still authored and the world maps
are still baked; nothing draws them. Fog of war and map notes are local files on the player's machine and stay there,
unread.

## License

This project is subject to the FishMMO project license.
