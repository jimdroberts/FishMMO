# Map system

> The client half below is the [Map module](../README.md) (`Assets/FishMMO/Modules/Map/Client`); the
> data, the bake and true north stay in the core.

Two panels, one subsystem. `UITKMinimap` (HUD) shows a live overhead render centred on the
player; `UITKMap` (window) shows the whole scene from a baked image. Both draw through the same
`UITKMapView` element and read the same shared state from `ClientMapSystem`, so anything pinned,
revealed or filtered on one appears identically on the other.

## Where things live

| Concern | Type | Assembly |
| --- | --- | --- |
| Per-scene map data, bounds, labels, landmarks | `WorldMapDefinition` | Core (`FishMMO.Shared`) |
| Scene authoring components | `MapRegionLabel`, `MapPointOfInterest` | Core (`FishMMO.Shared`) |
| "Put this object on the map" | `MapMarker`, `MapMarkerRegistry` | Core (`FishMMO.Shared`) |
| Bounds fallback when nothing is authored | `MapBoundsResolver` | Core (`FishMMO.Shared`) |
| Bake | `WorldMapBaker` | Core (`FishMMO.Shared.Map.Editor`) |
| Shared runtime state | `ClientMapSystem` | Map module (`FishMMO.Map.Client`) |
| Overhead camera and frame cap | `MinimapCameraRenderer` | Map module (`FishMMO.Map.Client`) |
| Visibility rules and throttling | `MapMarkerFilter`, `MapRelationshipTracker` | Map module (`FishMMO.Map.Client`) |
| Landmarks, notes and waypoints as markers | `MapContent` | Map module (`FishMMO.Map.Client`) |
| Which categories the player wants drawn | `MapFilters`, `MapFilterCategory` | Map module (`FishMMO.Map.Client`) |
| Explored territory | `FogOfWarMap` (client grid), `FogChunkSet`/`FogCodec` (shared), `MapFogSystem` (server) | Map module (`FishMMO.Map.Client`, `FishMMO.Map`, `FishMMO.Map.Server`) |
| Player annotations | `MapNote`, `MapNoteStore` | Map module (`FishMMO.Map.Client`) |
| Skill scaling seam | `Cartography`, `ICartographyProvider` | Map module (`FishMMO.Map.Client`) |
| Drawing | `UITKMapView`, `MapViewTransform` | Map module (`FishMMO.Map.Client`) |

## World maps are a per-machine cache, not source

Nothing under `Assets/Generated/WorldMaps/` — `WorldMapDefinition.BakedDirectory` — is
committed, and neither is the `ClientWorldMaps` addressable group. Both are gitignored. The maps stay
between builds, and the group exists only while a client build runs.

A **client** build runs, around the addressables step:

1. `WorldMapBaker.BakeOutOfDate()` — for every world scene whose map is missing or older than its scene
   file (none opens if none is), load it, find its `WorldSceneSettings`, take
   the definition assigned there or create one at `WorldMapDefinition.BakedAssetPath`,
   copy the scene's `SceneTransitionImage` into it, harvest the `MapRegionLabel` and
   `MapPointOfInterest` components, derive bounds through `MapBoundsResolver.FromOpenScene` when
   none are authored, photograph the scene from 2000 m up over the `Default`, `Ground` and `Water`
   layers into an image whose longest edge is 2048 px. Every map, kept or new, is then added to the
   `ClientWorldMaps` addressable group.
2. `WorldSceneDetailsCacheBuilder.Rebuild()`, with `WorldMapDefinition.ReferenceBakedMaps` set — so
   `WorldSceneDetails.MapDefinition` points at the current definitions, and their labels, landmarks,
   bounds and loading image are refreshed even when the photograph was kept.
3. The build.
4. `WorldMapBaker.RemoveBuildGroup()` and a second cache rebuild without the references, leaving the
   committed cache as it was found. The maps stay for the next build.

**Server builds skip both**, which is what keeps map images out of server bundles; the group name
carries "Client" so that a group which somehow lingered would still be excluded by substring. A
bake that throws is logged and does not fail the build — the world map falls back to a plain
background. `WorldMapBuildStep` / `WorldMapCleanupStep` (`Shared/Implementation/Tools/Extensions/Unity/Editor/WorldMapBuildSteps.cs`, Order 300) are the two hooks.

The bake writes no scene. It reads the loading image off `WorldSceneSettings` rather than moving
it, and it never assigns the definition back onto the component — a scene that must share one map
with another (an instanced twin) is the one case where `WorldSceneSettings.MapDefinition` is
hand-assigned, and the bake then fills that asset in place instead of creating one.

## Authoring a scene's map

1. Drop `MapRegionLabel` and `MapPointOfInterest` components into the scene and place them on the
   terrain. Both draw gizmos, and neither exists at runtime — they are harvested into the
   definition.
2. To see the result in the editor without a build, run **FishMMO Dashboard → World → World Map → Bake Maps** and enter
   play mode: the editor reads the baked map directly. **FishMMO Dashboard → World → World Map → Remove Baked Maps** undoes it.

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
awake. See the [shared map README](../../../Shared/Implementation/Entity/Map/README.md#objects-that-register-themselves).
When an entrance has no artwork yet, or its dungeon has none, the marker falls back to the type's
**USS shape** — there is no type-to-icon table anywhere, so a marker type with no `.map-marker--*`
rule draws in the fallback grey, which is an NPC's. `DungeonEntranceMapMarkerTests.EveryMarkerTypeHasAStyleRule`
is the guard that keeps a new type from arriving that way.

## What a marker is labelled

- **A fixture** (a dungeon entrance, a portal) draws its authored `MapMarker.Label`. `MapMarker.HoverText`
  is text shown only on hover — a plot for sale has no label and says "Plot for sale (2,800 gold)" when
  hovered.
- **An NPC** is labelled from itself, never from the authored field: its live name (`MapMarker.NpcName`,
  the GameObject's, which the naming system sets) and, when it has an interactable with a title, that
  title (`MapMarker.NpcTitle`), as "Bramwell <Banker>". The authored label is ignored on characters
  because a prefab cloned from another keeps it — every townsperson cloned from a merchant was drawn as
  "Goods" and every simulated player cloned from the banker as "Bank". `NPCPrefabFactory` now clears it
  and sets the marker type from the clone's own role.
- **A player** is never labelled (see the anti-radar story below).

The Options UI tab's MAP rows turn the name and the title off separately for the world map and the
minimap (`ClientSettings.WorldMapNpcNames`, `WorldMapNpcTitles`, `MinimapNpcNames`, `MinimapNpcTitles`;
all on by default). The tooltip names an NPC in full whatever the labels show.
`MapMarkerFilter.ResolveLabel` / `ResolveTooltip` are the single place this is decided.

## Guiding the player to a marker

Clicking any marker on the world map but the player's own chooses it for the guide (`MapGuide`,
client-side only: nothing is sent). Notes and waypoints keep their own selection too. The side bar's
GUIDE section names the target, gives the distance and has STOP GUIDING; the chosen marker wears
`.map-marker--guided` on both maps. The minimap draws the guide overlay (`UITKMapView.GuideTarget`,
`--map-guide`): a ring on the target while it is in view, an arrow on the rim pointing at it while it is
not, with the distance across the ground beside either.

- **Fixed targets** (notes, places, waypoints, plots) are positions, cleared on arrival
  (`MapGuide.ArrivalDistance`, 6 m).
- **Live targets** (an NPC, anything with a marker component) are followed through
  `MapMarkerFilter.TryResolve`, the map's own rules and throttle record, so the guide never knows more
  than the map draws: a detection-tier target is followed at its coarse position, and one the map may no
  longer show is kept at the last place it was seen ("~120 m"). The marker is let go of when it leaves the
  registry, because a pooled object returns as a different creature.
- Only the scene the character stands in: a marker on another scene's map cannot be chosen, and changing
  scene clears the guide.

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
`UITKMap.OnFastTravelClicked` for anything else that wants the click. The dropdown shows another
scene's discovered waypoints, but fast travel works only within the scene the character stands in.

When the server's `WaypointTravelPolicy` requires it (the default, issue #253), the button is also
disabled unless the character stands within the policy's range of a waypoint it has discovered, and
the hint says so. The map still opens anywhere for looking. The policy reaches the client in the
`WaypointController` owner payload (`IWaypointController.TravelPolicy`); the check runs against the
baked `WorldSceneDetails.Waypoints` positions on the refresh tick, so walking up to a waypoint with
the map open enables the button. It is a hint only — the server refuses with `NotNearWaypoint`.

### Gate Stones and gate mode (issue #241)

A Gate Stone is a waypoint of kind `GateStone` (`SceneWaypointDetails.IsGateStone`): drawn like a waypoint in its own
tint (`MapContent.GateStoneTint`), discovered the same way, and never a fast-travel point (the button reads GATE STONE
and stays off). When the Gate spell's cast ends, the Travel module calls `GateStoneChoice.Begin`; the map opens on the
current scene in **gate mode**: selecting a discovered Gate Stone, in this scene or any other the dropdown lists, enables
OPEN GATE, which sends the choice (`GateStoneChoice.Choose`). The hint counts the seconds left. The server answers by
ending the wait (the map closes) or with a toast (the wait goes on). Closing the map gives the Gate up
(`GateStoneChoice.Cancel`).

## Points of interest

Generated points of interest (one `ScenePointOfInterest` per site, written by the scene generator) and
hand-placed `MapPointOfInterest` landmarks are harvested by the world scene details cache rebuild into
`WorldSceneDetails.PointsOfInterest` — not only into the map definition, which exists only while a map
is baked. `MapContent.AppendPointsOfInterest(results, details, definition, fog, forWorldMap, labelTier)`
reads the details first and the definition's list for back-compat, drawing a landmark that is in both
(same name, same place to the metre) once.

- **Discovery.** Every generated point requires discovery: it appears once the fog has
  explored its chunk, never before.
- **Tiers.** A kind's detail tier is 0 to 3 (`PointOfInterestKinds`). It is gated twice: against
  `Cartography.VisibleContentTier` (0 to `Cartography.MaximumDetailTier`, 4; with no provider every tier
  shows), which decides whether the point is known at all; and on the world map by zoom, through
  `MapContent.LabelTierForZoom`, which decides only whether it is *named* — tiers 0 and 1 at any zoom,
  tier 2 from half the scene, tier 3 from a quarter. The icon is always drawn.
- **Styles.** One `MapMarkerType` per kind, same name as the `POIType`. `UIMapPOI.uss` is generated from
  the kinds table and styles every kind by its `PointOfInterestGroup`: one shape and one
  `--map-poi-<group>` token (FishMMO-Theme.uss) per group. Both map UXMLs load it after
  `UIMapShared.uss`. Regenerate it when a kind is added; `PointOfInterestMapTests` fails until you do.
- **Filters.** The legend lists one PLACES row per group (`MapFilters.PlaceCategories`, appended to
  `MapFilterCategory` after Notes), two to a row with the group's colour swatch. Landmark and
  DungeonEntrance keep the Landmarks row.

## North offset

`WorldMapDefinition.NorthOffsetDegrees` turns the bake camera, so the baked image's up is not world +Z.
The world map samples the image through the same frame (`UITKMapView.MapTextureNorthDegrees`,
`WorldToTextureUV`), so terrain and markers agree. The view itself stays north-up: panning, clamping
and the cursor readout all assume an axis-aligned view, so turning the whole map by the offset is a
separate change that has not been made.

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

Exploration works in **chunks** on a **world-anchored grid**: chunk (x, z) covers
`[x·size, (x+1)·size)` in world metres (`FogChunkSet`), and the client's grid starts on the chunk line at or before
the scene's corner, so its chunk *i* is world chunk `OriginChunkX + i`. A scene's bounds are divided into squares of `FogChunkSize` metres
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

All of them apply to the scene the character is in and explore nothing twice. They change only what this client
draws: reading the chunk grid directly — `Fog.Chunks`, `ChunksX`, `TryGetChunk` — is available for content that needs
to reason about it.

For asset-authored content there is `ExploreMapAction`, an ECA action that goes on an item's use
event, a region's enter event or a quest completion. On the server it raises `OnServerExploreMap`, which the server
fog system records; on the owning client (suppressed during reconcile) it raises `OnExploreMap`, which `UITKMinimap`
draws.

### Kept by the server, account-wide (issue #241)

Explored maps are **account-wide** and **kept by the server** (`MapFogSystem`, Map.Server, adopted by its assembly
declaration): every character on an account shares one explored map per scene, stored in `account_map_fog`
(account, scene, encoded chunks, explored count). Nothing is written on the player's machine any more; the old
per-character `.fow` files are ignored.

- **Derived, never uploaded.** The server reveals the chunk under each character every 0.25 s — the line between two
  samples, so a mount or a flight skips nothing; a sample over 120 m away is a jump and explores only the arrival —
  plus what `ExploreMapAction` and `/gm reveal map` grant. The client reveals with the same rule so the map moves at
  once, but nothing it says is stored, so neither the fog nor the scene list can be forged.
- **Encoding.** `FogCodec`: the tight bounding box of the explored chunks as alternating run lengths (varints), plain
  managed code so a browser build reads it like every other platform. A solid patch is a few bytes.
- **Saving.** Every 30 s while new chunks wait, and as the character leaves the server. A save merges into the stored
  row under a row lock (`IAccountMapFogService.MergeAsync`; the merge is `MapFogSystem.Merge`, a union), and an account's
  reads and merges share one async lane, so two writers converge on the union.
- **Sending.** At spawn the owner gets `FogOfWarSceneBroadcast` for its scene and `FogOfWarScenesBroadcast` — every
  scene the account has a row for, which is what "entered at least once" means. `ClientMapSystem.ApplyServerFog` folds
  a scene's fog into whichever grid shows it. A map showing another scene asks with `FogOfWarRequestBroadcast`
  (rate-limited); the minimap owns the connection and installs `ClientMapSystem.RequestFog`.

### The scene dropdown

The world map's header dropdown lists every scene the account has entered (the character's own first, marked
"(you are here)"; scenes this build no longer has are left out; labels are made unique — `MapSceneChoices`). Picking
one calls `ClientMapSystem.ViewScene`: the map draws `ClientMapSystem.Viewed` — that scene's baked map, the
account's fog for it and this character's notes for it — read-only. Live markers (the character, the party, NPCs),
pinning notes and fast travel belong to the scene the character stands in and are off while another is shown. The map
opens on the character's scene every time.

**Cartography experience must be awarded by the server**, from the positions it already
receives. Implement `ICartographyProvider` and
register it with `Cartography.SetProvider` when the profession lands; every map feature that scales
with skill — world-map zoom range, label detail tier, note capacity, minimap resolution,
coordinates, grid — already reads it from there. Chunk size is deliberately not one of them: it
belongs to the scene, and making it vary per player would mean two characters disagreeing about
what a chunk is and neither one's saved file surviving a change in skill. Until then the provider is absent and
the seam answers with the maximum tier, so players get the full map rather than a crippled one.
