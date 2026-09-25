# FishMMO Water

An ocean surface for URP 17: Gerstner waves in world metres, shaded as water rather than as a
tinted plane, and honest about what the render pipeline will and will not give it.

Everything here works in **metres** — one Unity unit is one metre in FishMMO, and that is not a
convention this plugin can ignore. Deep-water waves travel at `√(g/k)`, so a 64 m swell moves at
10 m/s and a 4 m chop at 2.5 m/s, and the long waves visibly overtake the short ones. At any other
scale the dispersion is wrong and the sea moves like a bath.

## What is here

| File | What it does |
| --- | --- |
| `Scripts/WaterWaves.cs` | The sea state: wind in, Gerstner waves out. Evaluated identically on the CPU and in the shader. |
| `Scripts/WaterMesh.cs` | The camera-centred disc the sea is drawn on, with rings spaced so each is about as wide on screen wherever it is. |
| `Scripts/WaterSurface.cs` | The component. Holds the sea level, follows the camera, publishes the waves, and turns off what the pipeline cannot support. |
| `Shaders/FishWater.hlsl` | The wave sum, the ripple slopes and the one shared material constant buffer. |
| `Shaders/FishWater.shader` | `FishMMO/Water/Ocean`. |
| `Materials/OceanWater.mat` | The default open-sea material. |

## Using it

Add a **FishMMO → Water → Water Surface** component to an empty object. **Its Y is the still-water
level, in metres.** Assign `OceanWater.mat`, set the wind, and that is the whole setup — the mesh
is built for you and follows whichever camera is rendering.

Scenes cut from a planet by the World Atlas get one automatically. In those scenes world Y is
altitude above the water line: the sea sits at `y = 0` and the terrain stands at its real height
above and below it, so the coastline underfoot is the coastline on the globe. A scene whose lowest
ground is above the water line gets no sea at all, and neither does a world with no liquid water on
it.

## Floating things on it

```csharp
float surface = water.HeightAt(transform.position);
bool submerged = water.IsSubmerged(transform.position);
```

`HeightAt` evaluates the same sea the shader draws — the FFT spectrum's strongest components,
summed on the CPU, with the same shoaling, tide and surf on top — so a boat sits in the trough a
player can see. It is an inverse, not a lookup: waves move water sideways as well as up, so the
piece of water that ends up above a given point started somewhere else. Three fixed-point
iterations bring it within a centimetre.

## URP settings this actually depends on

**This is the part that bites.** Sampling a texture URP has not bound is not an error and does not
come back black — it returns whatever was last written to that slot. Left unchecked, the water
refracts the previous frame's buffers and foams in mid-air.

| Feature | Needs | Without it |
| --- | --- | --- |
| Refraction | **Opaque Texture** on the URP asset | Falls back to alpha blending. On in `URP-HighFidelity`, off in the other two. |
| Shore foam, soft edges, depth absorption | **Depth Texture** on the URP asset | Foam and soft shorelines are dropped; the sea is treated as uniformly deep. Off in `URP-Performant`. |

`WaterSurface` reads the running pipeline asset and disables each one that cannot work, through
**global** shader keywords — not material keywords, which would write the answer into the `.mat`
and leave it modified in the working tree on whichever machine opened it last. It warns once in the
log so a missing effect is never silent.

## Under the water

While the camera is below the surface, a full-screen pass (`FishMMO/Water/Underwater`, driven by
the **Underwater** fields on `WaterSurface`) fogs everything in front of the camera by the water
between — the sea bed, anything swimming, and the surface overhead, which it draws after. What it
adds to the view is the light the water scatters toward the eye:

- **Daylight that fades with depth**, per colour, integrated along each ray: looking down falls away
  into dark blue, looking up brightens toward the surface.
- **Shafts of sun** (`UnderwaterShafts`): the caustics' own lens, marched in sixteen steps through
  the nearest 40 m and cut by the main light's shadow map, so a cliff or a hull shadows the water
  as well as the floor. The dearest part of the pass; 0 skips the march.
- **Drifting matter** (`UnderwaterMotes`): specks on a world grid, drifting with the current,
  sinking, and swaying with the swell near the surface.

Looking up, Snell's window shows the actual scene above, bent by the waves when refraction is on.

## Cost

Per frame: one transparent draw of a `Rings × Segments` disc (12k vertices at the defaults), three
sines per vertex per wave, and a fragment stage of three sines for the ripples, one reflection
probe sample, and two depth samples when refraction is on.

Three things keep it in budget, and all three are also correctness fixes:

- **Waves fade out with distance.** Past `_WaveFadeStart` the crests are closer together on screen
  than a pixel, so the geometry is under-sampled and aliases into a crawling moiré no
  anti-aliasing touches. The far sea is flat and the reflection carries it, which is why the
  horizon of a good ocean is calm.
- **Ripples fade too**, and the lost detail is put back as roughness — which is what ripples do to
  light anyway. Without this the specular highlight becomes a field of crawling sparkles.
- **One material constant buffer, declared once in the header.** Every pass of a shader must
  declare an identical `UnityPerMaterial` layout or the SRP Batcher silently drops the shader.

There is deliberately **no shadow caster pass**: water that casts a shadow darkens the sea bed it
exists to show through.

## Known limits

- **No motion vectors.** Under TAA or motion blur the surface does not report its movement, so fast
  wave motion can ghost slightly. The mesh is set to `ForceNoMotion`, which is the better of the
  two wrong answers — the alternative reports the mesh sliding with the camera.
- **No screen-space reflections.** Reflection comes from the reflection probe or the sky, so
  objects standing in the water are not reflected in it.
- **One sea per scene.** The wave state is global, which is correct for an ocean and wrong for a
  lake at a different height beside it.
- **Cloud shadow is an input, not a lookup.** `WaterSurface.CloudShadow` is there for the weather
  system to drive; nothing drives it yet.
