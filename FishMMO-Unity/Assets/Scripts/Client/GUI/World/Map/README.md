# Map system

Two panels, one subsystem. `UITKMinimap` (HUD) shows a live overhead render centred on the
player; `UITKMap` (window) shows the whole scene from a baked image. Both draw through the same
`UITKMapView` element and read the same shared state from `ClientMapSystem`, so anything pinned,
revealed or filtered on one appears identically on the other.

## Where things live

| Concern | Type | Assembly |
| --- | --- | --- |
| Per-scene map data, bounds, labels, landmarks | `WorldMapDefinition` | Shared |
| Scene authoring components | `MapRegionLabel`, `MapPointOfInterest` | Shared |
| "Put this object on the map" | `MapMarker`, `MapMarkerRegistry` | Shared |
| Bounds fallback when nothing is authored | `MapBoundsResolver` | Shared |
| Bake | `WorldMapBaker` | Shared (Editor) |
| Shared runtime state | `ClientMapSystem` | Client |
| Overhead camera and frame cap | `MinimapCameraRenderer` | Client |
| Visibility rules and throttling | `MapMarkerFilter`, `MapRelationshipTracker` | Client |
| Landmarks, notes and waypoints as markers | `MapContent` | Client |
| Which categories the player wants drawn | `MapFilters`, `MapFilterCategory` | Client |
| Explored territory | `FogOfWarMap`, `FogOfWarStore` | Client |
| Player annotations | `MapNote`, `MapNoteStore` | Client |
| Skill scaling seam | `Cartography`, `ICartographyProvider` | Client |
| Drawing | `UITKMapView`, `MapViewTransform` | Client |

## World maps are build output, not source

Nothing under `Assets/Prefabs/Shared/WorldMaps/` — `WorldMapDefinition.BakedDirectory` — is
committed, and neither is the `ClientWorldMaps` addressable group. Both are gitignored, produced by
a client build and removed again afterwards.

A **client** build runs, around the addressables step:

1. `WorldMapBaker.BakeAll()` — for every world scene, load it, find its `WorldSceneSettings`, take
   the definition assigned there or create a transient one at `WorldMapDefinition.BakedAssetPath`,
   copy the scene's `SceneTransitionImage` into it, harvest the `MapRegionLabel` and
   `MapPointOfInterest` components, derive bounds through `MapBoundsResolver.FromOpenScene` when
   none are authored, photograph the scene from 2000 m up over the `Default`, `Ground` and `Water`
   layers into an image whose longest edge is 2048 px, and add that image to the `ClientWorldMaps`
   addressable group.
2. `WorldSceneDetailsCacheBuilder.Rebuild()` — so `WorldSceneDetails.MapDefinition` points at the
   fresh definitions.
3. The build.
4. `WorldMapBaker.CleanBakedMaps()` and a second cache rebuild, leaving the project as it was found.

**Server builds skip both**, which is what keeps map images out of server bundles; the group name
carries "Client" so that a group which somehow lingered would still be excluded by substring. A
bake that throws is logged and does not fail the build — the world map falls back to a plain
background. `BakeWorldMaps` / `CleanWorldMaps` in `CustomBuildTool` are the two hooks.

The bake writes no scene. It reads the loading image off `WorldSceneSettings` rather than moving
it, and it never assigns the definition back onto the component — a scene that must share one map
with another (an instanced twin) is the one case where `WorldSceneSettings.MapDefinition` is
hand-assigned, and the bake then fills that asset in place instead of creating one.

## Authoring a scene's map

1. Drop `MapRegionLabel` and `MapPointOfInterest` components into the scene and place them on the
   terrain. Both draw gizmos, and neither exists at runtime — they are harvested into the
   definition.
2. To see the result in the editor without a build, run **FishMMO/World Map/Bake Maps** and then
   rebuild the world scene details cache. **FishMMO/World Map/Remove Baked Maps** undoes it.

The bake needs a graphics device. Under `-nographics` everything except the photograph is still
written, and the world map falls back to markers over a plain background; `xvfb-run` is the way to
get the photograph headlessly.

Nothing above is required for a scene to work: with no definition at all, bounds come from the
scene's `SceneBoundary`, the minimap renders normally, and the world map draws markers and fog
over the background colour.

## Which scene is being mapped

`ClientMapSystem` keys everything — the definition lookup, the fog file, the note file, the
waypoint list — on `IPlayerCharacter.CurrentSceneName()`, not on `SceneName`. Inside an instance
those differ: `CurrentSceneName()` is the instance scene, which is also what the server keys
waypoint unlocks by and validates travel requests against. Drawing `SceneName` from inside a
dungeon showed the open world and had every fast-travel request refused as `NotInScene`.

## Putting an object on the map

Add a `MapMarker`. Set `Type` for what it is and `Visibility` for who may see it:

- `Always` — world fixtures whose positions are public and fixed.
- `PartyOrGuild` — the group only.
- `Detection` — the default for anything that can be another player. Drawn only inside the filter's
  detection radius, at ~1 Hz, snapped to a 4 m grid, and never labelled.
- `Discovered` — appears once the chunk it stands in has been explored.

Party and guild members are promoted to full fidelity at runtime regardless of the authored rule,
so authoring the strict rule costs nothing.

Some objects put themselves on the map instead, because they are always mappable and an author
should not have to remember a component. A `DungeonEntrance` requires a `MapMarker`, fills it in on
awake as `DungeonEntrance` / `Discovered`, and draws its `DungeonTemplate`'s artwork through
`IMapMarkerIconSource` — read live, because that artwork arrives from Addressables some time after
awake. See the [shared map README](../../../../Shared/Implementation/Entity/Map/README.md#objects-that-register-themselves).
When an entrance has no artwork yet, or its dungeon has none, the marker falls back to the type's
**USS shape** — there is no type-to-icon table anywhere, so a marker type with no `.map-marker--*`
rule draws in the fallback grey, which is an NPC's. `DungeonEntranceMapMarkerTests.EveryMarkerTypeHasAStyleRule`
is the guard that keeps a new type from arriving that way.

## What stays on the frame

A marker is drawn until the whole of it has left the frame, and it is the rectangle it actually
draws that decides that — its icon plus the name hanging off the icon's right — not the position it
marks. A marker whose position has crossed the border is usually still half on screen, and hiding it
there cuts a half-drawn icon out of the picture; a name is worse, because it extends to the right of
its icon, so off the frame's left edge a marker can have its icon entirely gone while the whole name
is still legible, and culling by the icon takes the name away while it is being read. The same
rectangle answers clicks (`FindNearestSnapshot`), so what is drawn and what is clickable cannot come
apart.

The rectangle is measured from the laid-out element and kept as offsets from the marker's position,
for two reasons: the map moves under its markers every frame, and a hidden element has no geometry
left to measure. A marker that has never been laid out is drawn for a frame to be measured; one taken
from the pool throws the previous marker's measurement away rather than being culled by another
marker's name. A marker wearing `.map-marker--clamped` is never measured, because it is drawn shrunk,
turned and unlabelled.

`ClampToEdge` markers are the exception to all of it. One whose position is off the view is pinned
inside the border instead of being culled, since a `Detection` marker has to be readable from off the
edge or the minimap would be a picture of nothing. A `ClampToEdge` marker still on the frame is drawn
where it is: pinning it inwards would move it away from the object it marks, to a point it already
sits on top of.

## Waypoints and fast travel

A discovered waypoint is drawn as an orange diamond from the character's own unlock record
(`IWaypointController`, filled by the spawn payload and `WaypointUnlockedBroadcast`) against the
scene's authored waypoints in `WorldSceneDetails.Waypoints`. `MapContent.AppendWaypoints` is the
only producer; fog plays no part, and an undiscovered waypoint is absent however close the player
has walked. Clicking one selects it into the side bar's WAYPOINT section; the button sends
`WaypointTravelRequestBroadcast` and disables until the server answers (or five seconds pass). A
refusal re-enables it with the reason; an arrival closes the map. The panel has four clip slots
(click, arrive, refused, discovered) played through `ClientUIAudio` on the Interface channel, and
`UITKMap.OnFastTravelClicked` for anything else that wants the click. Only the current scene's
waypoints are shown or travelable; other scenes wait for the world-map system.

## The anti-radar story, briefly

The observer system decides which entities exist on a client at all; nothing without a GameObject
can have a marker at any setting. On top of that, `MapMarkerFilter` never *produces* an exact
position for a `Detection` marker — the value it hands to the UI is already stale and coarse, and
the detection radius is smaller than `ObserverStreamingPolicy.MinimumRange`, so the honest client's
map is strictly less informative than the network stream it is drawn from. `MinimapCameraRenderer`
re-applies the camera's whole configuration on every render, so a widened field of view survives at
most one frame — and widening it only ever reveals terrain, which is public.

A client that edits its own memory defeats all of this. The point is that doing so gains nothing the
map subsystem was protecting, because the map never held anything better.

## Exploration and Cartography

Exploration works in **chunks**. A scene's bounds are divided into squares of `FogChunkSize` metres
(the map definition's field, defaulting to `FogOfWarDefaults.ChunkSize`, 40 m — twenty-eight chunks
across a shipped thousand-metre scene), and walking into a chunk explores all of it. A chunk is
explored or it is not; there is no coverage value and no radius. `ExploredFraction` is therefore
chunks visited over chunks in the scene, and the world map's readout prints it to a tenth of a
percent — one chunk of a shipped scene is 0.13% — so the number climbs as the player walks instead
of stepping a block at a time.

This replaced a per-cell radial reveal that stored a coverage byte per four metres of ground:
seventy-seven thousand bytes for a scene, gzipped on every save, uploaded through a dirty-rectangle
tracker, and producing a percentage that climbed about one point per sixty metres walked — which
read, correctly, as a readout that never changed. The chunk model first answered that with a grain
coarse enough that every chunk entered was a visible step, and that turned out to be its own
failure: over a nine-chunk grid a whole-number readout can only move in ones, so a step of a bit over
one percent is indistinguishable from a fixed increment. The grain and the readout are one decision —
see `FogOfWarDefaults.ChunkSize` — and neither half works without the other.

### Granting exploration that was not walked

`ClientMapSystem` exposes the granting API for map consumables, discovery triggers and quest
rewards:

| Call | Grants |
| --- | --- |
| `ExploreAround(worldCenter, radius)` | Every chunk the circle reaches. The shape a map item wants. |
| `ExploreArea(worldRect)` | Every chunk the rectangle touches. |
| `ExploreChunk(x, z)` | One chunk by grid coordinates. Off-grid coordinates are inert. |
| `ExploreEverything()` | The whole current scene. |

All of them apply to the scene the character is in, explore nothing twice, and schedule the same
debounced save that walking does. Reading the chunk grid directly — `Fog.Chunks`, `ChunksX`,
`TryGetChunk` — is available for content that needs to reason about it.

For asset-authored content there is `ExploreMapAction`, an ECA action that goes on an item's use
event, a region's enter event or a quest completion. It follows `ChangeFogAction`: shared assembly,
owner-client only, suppressed during reconcile, raising an event that `UITKMinimap` applies. Note
what that means — **exploration is client-side data**, so the action is the whole delivery mechanism
and the server neither stores nor validates it. Fine for revealing a map, wrong for anything a
player could gain by lying about.

`FogOfWarStore` writes one signed file per character per scene under
`<install>/Cartography/<characterID>/`. It never crosses the network. One byte per chunk means a
whole scene is smaller than the header describing it, so the payload is written raw. The signature
makes tampering detectable, not impossible — the key ships in the client.

**Cartography experience must therefore be awarded by the server**, from the positions it already
receives, and never from anything read out of that file. Implement `ICartographyProvider` and
register it with `Cartography.SetProvider` when the profession lands; every map feature that scales
with skill — world-map zoom range, label detail tier, note capacity, minimap resolution,
coordinates, grid — already reads it from there. Chunk size is deliberately not one of them: it
belongs to the scene, and making it vary per player would mean two characters disagreeing about
what a chunk is and neither one's saved file surviving a change in skill. Until then the provider is absent and
the seam answers with the maximum tier, so players get the full map rather than a crippled one.
