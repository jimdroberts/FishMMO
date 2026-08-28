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
| Bake + migration | `WorldMapBaker` | Shared (Editor) |
| Shared runtime state | `ClientMapSystem` | Client |
| Overhead camera and frame cap | `MinimapCameraRenderer` | Client |
| Visibility rules and throttling | `MapMarkerFilter`, `MapRelationshipTracker` | Client |
| Explored territory | `FogOfWarMap`, `FogOfWarStore` | Client |
| Player annotations | `MapNote`, `MapNoteStore` | Client |
| Skill scaling seam | `Cartography`, `ICartographyProvider` | Client |
| Drawing | `UITKMapView`, `MapViewTransform` | Client |

## Authoring a scene's map

1. Drop `MapRegionLabel` and `MapPointOfInterest` components into the scene and place them on the
   terrain. Both draw gizmos, and neither exists at runtime — they are harvested into the
   definition.
2. Run **FishMMO/World Map/Bake Maps**. For every world scene it creates a `WorldMapDefinition`
   under `Assets/Prefabs/Shared/WorldMaps/` if there is not one already, assigns it to that scene's
   `WorldSceneSettings`, migrates the loading image off the component, derives the map bounds from
   the scene's boundaries and terrain, harvests the labels and landmarks, photographs the scene from
   overhead, and registers the image as an addressable.
3. Rebuild the world scene details cache so `WorldSceneDetails.MapDefinition` points at it.

The bake needs a graphics device. Under `-nographics` everything except the photograph is still
written, and the world map falls back to markers over a plain background.

Nothing above is required for a scene to work: with no definition at all, bounds come from the
scene's `SceneBoundary`, the minimap renders normally, and the world map draws markers and fog
over the background colour.

## Putting an object on the map

Add a `MapMarker`. Set `Type` for what it is and `Visibility` for who may see it:

- `Always` — world fixtures whose positions are public and fixed.
- `PartyOrGuild` — the group only.
- `Detection` — the default for anything that can be another player. Drawn only inside the filter's
  detection radius, at ~1 Hz, snapped to a 4 m grid, and never labelled.
- `Discovered` — appears once the fog has revealed its cell.

Party and guild members are promoted to full fidelity at runtime regardless of the authored rule,
so authoring the strict rule costs nothing.

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

## Fog of war and Cartography

`FogOfWarStore` writes one signed, gzipped file per character per scene under
`<install>/Cartography/<characterID>/`. It never crosses the network. The signature makes tampering
detectable, not impossible — the key ships in the client.

**Cartography experience must therefore be awarded by the server**, from the positions it already
receives, and never from anything read out of that file. Implement `ICartographyProvider` and
register it with `Cartography.SetProvider` when the profession lands; every map feature that scales
with skill — reveal radius, world-map zoom range, label detail tier, note capacity, minimap
resolution, coordinates, grid — already reads it from there. Until then the provider is absent and
the seam answers with the maximum tier, so players get the full map rather than a crippled one.
