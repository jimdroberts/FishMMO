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
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Map/
├── Runtime/              FishMMO.Map              MapModule, MapRevealBroadcast; Fog/ = FogChunkSet, FogCodec, FogOfWarBroadcasts
├── Server/               FishMMO.Map.Server       MapOperatorCommands (/gm reveal map|waypoints|sites|all), MapFogSystem
│                                                  (account-wide fog of war: reveal, save to account_map_fog, send)
├── Client/               FishMMO.Map.Client       UITKMap (+ UIMap.prefab/uxml/uss), UITKMinimap (+ UIMinimap.prefab/uxml/uss),
│                                                  UITKMapView, MapViewTransform, ClientMapSystem, MinimapCameraRenderer,
│                                                  MapMarkerFilter, MapRelationshipTracker, MapContent, MapFilters,
│                                                  FogOfWarMap, MapNote/Store, MapSceneChoices, MapCompass, Cartography; UIMapShared.uss,
│                                                  UIMapPOI.uss
├── Editor/RenderScratch/ FishMMO.Map.RenderScratch   The minimap in the HUD renders and overlap probe; the leak probe's
│                                                  minimap and marker modes
├── Tests/                FishMMO.Map.Tests        Map system, marker culling and clicks, dungeon entrances, explored
│                                                  readout, side bar layout, waypoints on the map, wiring, server fog
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `[assembly: GameModule]` | `MapModule` ("map"). |
| `ClientWorldGUI_Panels` label | `UIMap.prefab` and `UIMinimap.prefab` are placed into the world GUI. The core opens them by name (`UIManager`, the Minimap and World Map keys); a Show that comes before the panel registered is applied when it does. |
| `MapMarkerRegistry`, `WorldSceneDetails` (shared core) | The markers objects register and the scene's baked map data are what the maps draw. |
| `ExploreMapAction.OnExploreMap` / `OnServerExploreMap` | The minimap draws the reveal on the owning client; `MapFogSystem` records it on the server. |
| `[assembly: ModuleServerSystem]` (scene server) | `MapFogSystem` is created by the scene server from its assembly declaration; no asset or Addressables entry. |
| `account_map_fog` (database) | Account-wide explored maps, one encoded row per account per scene (`IAccountMapFogService`). |
| `OperatorCommandProvider` (scene server) | `/gm reveal`: the caller only, the scene they stand in. `map` sends `MapRevealBroadcast` (the minimap explores the scene); `waypoints` unlocks every registered waypoint through `IWaypointController`; `sites` sets every `PointOfInterestDiscovery` bit. Each new waypoint or site is credited with the `AchievementIncrementAction` that follows the find step in its own triggers. |
| `IWaypointController` events (shared core) | The world map draws unlocked waypoints and starts fast travel through the contract; the Travel module implements it. |
| `GateStoneChoice` (shared core) | While a Gate spell waits for its destination, the world map opens in gate mode and sends the chosen Gate Stone; the Travel module begins and ends the wait (issue #241). |

## What stays in the core

The data a scene and a build carry: `MapMarker` and `MapMarkerRegistry`, `WorldMapDefinition`, `MapPointOfInterest`,
`MapRegionLabel`, `MapBoundsResolver` (`Shared/Implementation/Entity/Map`), the world map bake (`WorldMapBaker`, the
build steps and the dashboard's bake buttons; the portal views and the world modules use it too), `ExploreMapAction`,
`TrueNorth` (a world contract), the map settings in `ClientSettings` and the Minimap and World Map key bindings.

## Removing it

Delete `Assets/FishMMO/Modules/Map` and `Assets/FishMMO/Modules/WorldDesign` (to check it, follow
[the gate](../../README.md#removing-a-module-the-gate)). Players then have no minimap and no world map, and the Minimap
and World Map keys do nothing. Map markers, map definitions and points of interest are still authored and the world maps
are still baked; nothing draws them. The `account_map_fog` rows stay, unread; map notes are local files on the
player's machine and stay there.

## License

This project is subject to the FishMMO project license.
