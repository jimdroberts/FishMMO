# Map Data (Shared)

**Short description:** The authored, scene-side half of the map subsystem — what a scene's map *is*,
where its bounds are, what is labelled on it, and which objects ask to be drawn on it. Everything
here is data and scene authoring; nothing here draws.

> The rendering half — both panels, the fog grid, the marker visibility rules and the local stores —
> lives in the Client assembly and is documented in
> [`Client/GUI/World/Map/README.md`](../../../../Client/GUI/World/Map/README.md). Read that one for
> how a map is drawn and why the minimap is not a radar. This file covers what a scene author and
> the server-side assemblies touch.

## Table of Contents

- [Why this is split](#why-this-is-split)
- [What lives here](#what-lives-here)
- [Authoring a scene's map](#authoring-a-scenes-map)
- [Putting an object on the map](#putting-an-object-on-the-map)
- [Bounds resolution](#bounds-resolution)
- [Project Structure](#project-structure)

## Why this is split

A map definition is referenced by `WorldSceneSettings` and reached through
`WorldSceneDetails.MapDefinition`, both of which are Shared — the server resolves scene metadata and
must be able to load a scene whose map data is present without dragging in any UI. The client half
depends on this one; nothing here depends on the client.

That direction is also why the marker components are Shared: a `MapMarker` is a property of a world
object (a vendor, a teleporter, a resource node), not of a panel, and the same prefab is spawned on
a headless server where no map exists.

## What lives here

| Type | Kind | Purpose |
|---|---|---|
| `WorldMapDefinition` | `ScriptableObject` | Everything about one scene's map: bounds, baked image, tint/background, zoom range, north offset, fog settings, harvested labels and landmarks |
| `MapRegionLabel` | `MonoBehaviour` | Scene authoring — names an area. Draws a gizmo, harvested at bake, **does not exist at runtime** |
| `MapPointOfInterest` | `MonoBehaviour` | Scene authoring — a landmark. Same lifecycle |
| `MapRegionLabelDetails` / `MapPointOfInterestDetails` | `[Serializable]` | The harvested forms of the two above, stored in the definition |
| `MapMarker` | `MonoBehaviour` | "Draw this object on the map." Carries a `MapMarkerType` and a `MapMarkerVisibility` |
| `MapMarkerRegistry` | `static` | Runtime index of live markers, with register/unregister events so a panel does not poll |
| `MapMarkerType` | `enum` | What the thing is — 16 values, from `PartyMember` through `Vendor`, `Resource`, `Teleporter`, `Landmark`, `Note` |
| `MapMarkerVisibility` | `enum` | Who may see it — `Always`, `SelfOnly`, `PartyOrGuild`, `Detection`, `Discovered` |
| `MapBoundsResolver` | `static` | Derives a usable map rectangle when a scene has no definition |
| `Editor/WorldMapBaker` | Editor | The bake, behind **FishMMO → World Map → Bake Maps** |

## Authoring a scene's map

1. Drop `MapRegionLabel` and `MapPointOfInterest` components into the scene and place them on the
   terrain.
2. Run **FishMMO → World Map → Bake Maps**. For every world scene it creates a
   `WorldMapDefinition` under `Assets/Prefabs/Shared/WorldMaps/` if there is not one already,
   assigns it to that scene's `WorldSceneSettings`, migrates the loading image off the component,
   derives the map bounds from the scene's boundaries and terrain, harvests the labels and
   landmarks, photographs the scene from overhead, and registers the image as an addressable.
3. Rebuild the world scene details cache so `WorldSceneDetails.MapDefinition` points at it.

The bake needs a graphics device. Under `-nographics` everything except the photograph is still
written, and the world map falls back to markers over a plain background.

**None of this is required for a scene to work.** With no definition at all, bounds come from the
scene's `SceneBoundary`, the minimap renders normally, and the world map draws markers and fog over
the background colour. A scene author who never opens the baker gets a working map, just not a
pretty one.

## Putting an object on the map

Add a `MapMarker` and set `Visibility` for who may see it:

| Visibility | Meaning |
|---|---|
| `Always` | World fixtures whose positions are public and fixed |
| `SelfOnly` | Only the owning character |
| `PartyOrGuild` | The group only |
| `Detection` | **The default for anything that can be another player.** Drawn only inside the client filter's detection radius, refreshed at ~1 Hz, snapped to a 4 m grid, and never labelled |
| `Discovered` | Appears once the fog has revealed its cell |

Party and guild members are promoted to full fidelity at runtime regardless of the authored rule, so
**authoring the strict rule costs nothing** — author `Detection` and let the client widen it, rather
than authoring a permissive rule the filter then has to narrow.

## Bounds resolution

`MapBoundsResolver` answers "what rectangle is this scene's map?" in order of preference: the
definition's authored or derived bounds, then the scene's `SceneBoundary`, then the terrain. The
fallback chain exists so an unbaked scene is still mappable — a map that silently fails to open is
much worse than one drawn over a flat colour.

`WorldMapDefinition.BoundsAreDerived` records whether the current bounds came from the baker or were
set by hand, so a re-bake does not overwrite an author's deliberate override.

## Project Structure

```
Map/
├── WorldMapDefinition.cs        # Per-scene map asset (bounds, image, zoom, fog, labels, landmarks)
├── MapRegionLabel.cs            # Scene authoring: named area. Gizmo only; harvested at bake
├── MapRegionLabelDetails.cs     # Harvested form stored in the definition
├── MapPointOfInterest.cs        # Scene authoring: landmark. Gizmo only; harvested at bake
├── MapPointOfInterestDetails.cs # Harvested form stored in the definition
├── MapMarker.cs                 # "Draw this object on the map"
├── MapMarkerRegistry.cs         # Runtime index + register/unregister events
├── MapMarkerType.cs             # 16 marker kinds
├── MapMarkerVisibility.cs       # 5 visibility rules
├── MapBoundsResolver.cs         # Definition → SceneBoundary → terrain fallback chain
└── Editor/
    ├── WorldMapBaker.cs         # FishMMO → World Map → Bake Maps
    └── FishMMO.Shared.Map.Editor.asmdef
```

### Related

```
Client/GUI/World/Map/            # The rendering half — panels, fog, filter, local stores
Shared/.../WorldSceneDetails/    # WorldSceneDetails.MapDefinition, the server-visible reference
```

## License

See the repository root.
