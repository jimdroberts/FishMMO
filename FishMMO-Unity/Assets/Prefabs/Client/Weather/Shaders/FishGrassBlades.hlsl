#ifndef FISHMMO_GRASS_BLADES_INCLUDED
#define FISHMMO_GRASS_BLADES_INCLUDED

// FishMMO/Grass Blades: the procedural blade grass (GrassBladeRenderer, FishMMO.Client/World/Terrain/Grass).
// Each draw is a Graphics.RenderMeshIndirect of one blade strip mesh (LOD0 15 vertices, LOD1 7, LOD2 3)
// whose instances are the blades FishGrassBlades.compute wrote into this draw's own buffer,
// _GrassBlades, bound by the draw's MaterialPropertyBlock. startInstance is 0 in every args entry, so
// SV_InstanceID is the blade's index on every API.
//
// The strip's vertices carry only where they sit on a blade: POSITION.x the side (-1 left, +1 right,
// 0 the tip point), POSITION.y how far along it (0 root .. 1 tip). The vertex shader builds the blade:
// a quadratic Bezier from the root (height, lean toward its facing, bend, the wind's push, scaled back
// to its length: GrassMath.Curve), its width tapering to the tip and widened with distance as the field
// thins (GrassMath.Widen), its colour root to tip from its type, clump and season, the ground's colour
// near the root (GroundColourMap's globals), the biome's look of its patch (FishFlora.hlsl), snow burying
// it, and the dithered fade at the grass distance.
//
// Lighting (forward): normals rounded across the width (a blade reads as a cylinder, not a card),
// ambient from the sky's trilight (FishTrilight, never SampleSH: nothing binds probes to these draws),
// darkening toward the root (dense grass shades itself), a low narrow specular, light through the
// blade only when it is backlit, the weather's cover and damp through FishWeatherFoliage.

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"

#define GRASS_RINGS 6

struct GrassBlade
{
    float3 root;
    uint packed;
    uint colour;   // the terrain's albedo under the root (FishGrassBlades.compute GrassPackColour), a byte = valid
};

StructuredBuffer<GrassBlade> _GrassBlades;

/* Per type (GrassBladeRenderer.Publish): the table the compute reads too, a texel per type (x) per row (y). It was seven
 * constant arrays on the material (16 types, the head colours four times that); at 32 types a table read with Load
 * keeps the material's constants small and is the same floats. Rows 0..2 are the compute's (row 2's sprinkle level is read here too). */
Texture2D<float4> _GrassTypeTable;
#define GRASS_ROW_MORE 2          // y a sprinkled type's lattice level (GrassMath.SprinkleLevel): its stems thin at 4^level times the share
#define GRASS_ROW_ROOT 3          // rgb colour at the root, a how much of the terrain texture's colour the type takes (its biome's BiomeGrassType.GroundTint)
#define GRASS_ROW_TIP 4           // rgb colour at the tip, a base width (m)
#define GRASS_ROW_HEALTHY 5       // rgb healthy tint, a tint spread between neighbours
#define GRASS_ROW_DRY 6           // rgb dry tint, a healthy/dry patch size (m)
#define GRASS_ROW_PARAMS 7        // x stiffness, y sinks into snow, z bend, w translucency
#define GRASS_ROW_HEAD 8          // x head (0 none, 1 seed head, 2 flower, 3 plume), y head size (m), z head colours (1..4), w 1 = blunt strap tip
#define GRASS_ROW_HEAD_COLOUR 9   // ..12: the type's head colours, linear rgb (GrassType.HeadColours, else the tuning's palette or colour)

float4 GrassTypeRow(int type, int row)
{
    return _GrassTypeTable.Load(int3(type, row, 0));
}

float4 _GrassRingDistance[GRASS_RINGS];
float4 _GrassRingDensity[GRASS_RINGS];
float _GrassRingCount;

float4 _GrassParams0;   // x widen exponent, y max widen, z grass distance (m), w distance fade band (share of the distance)
float4 _GrassParams1;   // x normal rounding (radians), y root occlusion (0..1, light kept at the root), z translucency, w smoothness
float4 _GrassParams2;   // x wind strength, y gust wave length (m), z flutter, w idle breeze (0..1 of full wind)
float4 _GrassParams3;   // x root ground blend, y root ground height (share of the blade), z ground hue tint, w specular strength
float4 _GrassParams4;   // x max wind bend (radians from vertical), y gust crest sharpness, z gust travel speed scale, w gust sheen
float _GrassHighlight;   // debug: 1 draws every blade magenta (GrassBladeRenderer.Highlight)
// This camera's pixel size (GrassBladeRenderer.Execute, a global so the shadow pass widens as the camera does): x metres per
// pixel per metre of distance (perspective), y metres per pixel (orthographic), z the most pixels a widened blade spans
// divided by the base blade width.
float4 _GrassScreen;
// This camera's position (GrassBladeRenderer.Execute; the shadow pass's own camera is the light's): xyz.
float4 _GrassViewer;
float4 _GrassParams5;   // far-field blend: x start (m), y end (share of the grass distance), z ground colour pull, w normal flatten
float4 _GrassParams6;   // terrain texture colour: x unused (the strength is per type: GRASS_ROW_ROOT a), y brightness, z root strength; w edge-on thickening (0..1)
float4 _GrassParams7;   // wind calm with distance: x start (m), y end (m), z bend kept at the end (0..1); w top-down lay (sine of its angle)

// The ground colour map (GroundColourMap; same globals FishVegetationPasses.hlsl reads).
#include "FishGroundColour.hlsl"

/* The biome's looks (BiomeLooks, published by FloraPaletteMap; FishFlora.hlsl): each patch of ground takes one of its
 * biome's twelve looks, and the grass standing in it wears that look's colours, width and curl. The compute grew the
 * blade to the look's height and lean from the same patch of the same root (FishGrassBlades.compute: GRASS_FLORA_PATCH
 * and GRASS_FLORA_SALT must match), so nothing new rides the record. Flowers take theirs from larger drifts with their own
 * salt, so a drift of bloom does not end where a patch of grass does. _FishFloraInfo.x = 0 (nothing published, the A/B
 * off): every look is invalid and every blade is drawn as it was. */
#include "FishFlora.hlsl"
// The grass's patches are FishFlora.hlsl's own: the terrain's living ground reads the same, so the sward under a patch is
// the patch's colour too.
#define GRASS_FLORA_PATCH FISH_FLORA_GRASS_PATCH
#define GRASS_FLORA_SALT FISH_FLORA_GRASS_SALT
#define GRASS_FLORA_BLOOM_PATCH 11.0
#define GRASS_FLORA_BLOOM_SALT 0xBB67AE85u
// GrassMath.FloraMinWidth / FloraMaxWidth, FloraMinBend / FloraMaxBend, FloraMinBloom / FloraMaxBloom.
// The smooth swells of the blade's shape (FishFloraSwell; the bend's salt and spread match FishGrassBlades.compute).
#define GRASS_FLORA_BEND_SWELL 0.25
#define GRASS_FLORA_BEND_SALT 0xA54FF53Au
#define GRASS_FLORA_WIDTH_SWELL 0.15
#define GRASS_FLORA_WIDTH_SALT 0x510E527Fu
#define GRASS_FLORA_HEADSIZE_SWELL 0.25
#define GRASS_FLORA_HEADSIZE_SALT 0x9B05688Cu
// Cured (dormant) grass: straw, as a multiple of the blade's own lightness (Rec. 709 luminance 1.1).
#define GRASS_CURED float3(1.30, 1.09, 0.67)
#define GRASS_FLORA_MIN_WIDTH 0.6
#define GRASS_FLORA_MAX_WIDTH 1.6
#define GRASS_FLORA_MIN_BEND 0.5
#define GRASS_FLORA_MAX_BEND 1.5
#define GRASS_FLORA_MIN_BLOOM 0.5
#define GRASS_FLORA_MAX_BLOOM 1.5

// ── Unpacking (GrassMath.Unpack) ──────────────────────────────────────

// GrassMath.UnpackColour: square-root 8:8:8, bit 31 set when the terrain's texture was read.
float4 GrassUnpackColour(uint c)
{
    float3 q = float3(c & 0xFFu, (c >> 8) & 0xFFu, (c >> 16) & 0xFFu) / 255.0;
    return float4(q * q, (c >> 31) != 0u ? 1.0 : 0.0);
}

// GrassMath.HeadPartOf: 0 the stem itself, 1 and 2 the two crossed cards of its head (bits 29..30).
uint GrassHeadPart(uint c)
{
    return (c >> 29) & 3u;
}

// The colour word carries the record's shape bits (GrassMath.RecordBits): bit 28 the type's fifth bit (types 16..31),
// bit 27 a tall blade, whose packed height is half its own.
void GrassUnpack(uint packed, uint colour, out int type, out float facing, out float height, out float lean, out float fade, out float clumpColour)
{
    type = (int)(packed & 15u) | (int)(((colour >> 28) & 1u) << 4);
    facing = (float)((packed >> 4) & 127u) / 128.0 * 6.2831853;
    float q = (float)((packed >> 11) & 127u) / 127.0;
    height = q * q * 2.5 * (((colour >> 27) & 1u) != 0u ? 2.0 : 1.0);
    lean = (float)((packed >> 18) & 15u) / 15.0;
    fade = (float)((packed >> 22) & 31u) / 31.0;
    clumpColour = (float)((packed >> 27) & 31u) / 31.0;
}

uint GrassMixV(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float GrassUnitV(uint h)
{
    return (float)(h & 0xFFFFFFu) / 16777216.0;
}

// GrassMath.HashPosition: the blade's own randoms from its root's exact bits.
uint GrassHashPosition(float x, float z, uint salt)
{
    return GrassMixV(asuint(x) ^ GrassMixV(asuint(z) ^ salt));
}

// GrassMath.Share (the same rings the compute thinned with).
float GrassShareV(float d)
{
    int count = (int)_GrassRingCount;
    if (count <= 0 || _GrassRingDensity[0].x <= 0.0 || d <= _GrassRingDistance[0].x)
    {
        return 1.0;
    }
    float share = min(1.0, _GrassRingDensity[count - 1].x / _GrassRingDensity[0].x);
    bool done = false;
    [unroll] for (int r = 1; r < GRASS_RINGS; r++)
    {
        if (!done && r < count && d <= _GrassRingDistance[r].x)
        {
            float a = log(max(1e-3, _GrassRingDistance[r - 1].x));
            float b = log(max(1e-3, _GrassRingDistance[r].x));
            float t = b > a ? (log(d) - a) / (b - a) : 1.0;
            float da = log(max(1e-6, _GrassRingDensity[r - 1].x));
            float db = log(max(1e-6, _GrassRingDensity[r].x));
            share = min(1.0, exp(da + (db - da) * t) / _GrassRingDensity[0].x);
            done = true;
        }
    }
    return share;
}

// ── The blade ─────────────────────────────────────────────────────────

struct GrassVertex
{
    float3 positionWS;
    float3 normalWS;     // the blade's face normal at this row (not yet rounded, not flipped)
    float3 sideWS;       // across the blade, unit
    float side;          // -1 .. 1 across the width
    float along;         // 0 root .. 1 tip
    float3 albedo;
    float thin;          // 0 broad .. 1 thin (how much light gets through)
    float2 fade;         // x share drawn by distance, y dither shift
    float distance;
};

// The blade's quadratic Bezier, root at the origin, scaled back to the blade's length. The tip's
// direction is given (its own lean plus the wind, as an angle from vertical — a blade lies down in a
// gale rather than only pushing its tip sideways); the control point keeps the base rising before the
// blade arcs over, more so the further it is bent, as a stem does.
void GrassCurve(float height, float3 tipDirection, float bend, out float3 control, out float3 tip)
{
    tip = tipDirection * height;
    float3 above = float3(0.0, max(tip.y, height * 0.55), 0.0);
    control = lerp(tip * 0.5, above, bend);
    float arc = (2.0 * sqrt(dot(tip, tip)) + sqrt(dot(control, control)) + distance(tip, control)) / 3.0;
    float scale = height / max(1e-5, arc);
    control *= scale;
    tip *= scale;
}

// VegSeasonTint's arithmetic (FishVegetationPasses.hlsl) with the type's own colours: patches of
// healthy and dry ground over metres, a quarter of each blade's own, browned by drought. Winter is not
// in the tint but in `cure` (0 growing .. 1 dormant), which GrassBuild applies last (GRASS_CURED).
float3 GrassSeasonTint(float3 rootWS, float4 healthy, float4 dryTint, float own, out float cure)
{
    float patchMetres = dryTint.a;
    float variation = own;
    if (patchMetres > 0.0)
    {
        float2 p = rootWS.xz / patchMetres;
        float patch = FishSurfaceNoise(p) * 0.65 + FishSurfaceNoise(p * 2.13 + 17.3) * 0.35;
        patch = saturate((patch - 0.5) * 1.7 + 0.5);
        variation = lerp(patch, own, 0.25);
    }
    float known = step(1e-4, _FishSeason.w);
    float drought = saturate(-_FishSeason.z * 3.0);
    float dry = saturate(variation * healthy.a + drought * known);
    cure = saturate(-FishSeasonGrowing(rootWS.y)) * known;
    return lerp(healthy.rgb, dryTint.rgb, dry);
}

GrassVertex GrassBuild(uint instanceID, float2 vertex)
{
    GrassVertex o = (GrassVertex)0;
    GrassBlade blade = _GrassBlades[instanceID];
    int type;
    float facingAngle, height, lean, thinFade, clumpColour;
    GrassUnpack(blade.packed, blade.colour, type, facingAngle, height, lean, thinFade, clumpColour);
    // The type's rows, each read once.
    float4 typeParams = GrassTypeRow(type, GRASS_ROW_PARAMS);
    float4 typeTip = GrassTypeRow(type, GRASS_ROW_TIP);
    float4 typeRoot = GrassTypeRow(type, GRASS_ROW_ROOT);
    float4 typeHealthy = GrassTypeRow(type, GRASS_ROW_HEALTHY);
    float3 root = blade.root;
    float t = vertex.y;
    float u = vertex.x;

    uint h0 = GrassHashPosition(root.x, root.z, 0xA511E9B3u);
    float r0 = GrassUnitV(h0);
    float r1 = GrassUnitV(GrassMixV(h0 ^ 0x63D83595u));
    float r2 = GrassUnitV(GrassMixV(h0 ^ 0x2C1B3C6Du));
    // The look of the blade's patch, the one the compute grew it by (its height and lean are already in the record). Every
    // pass asks it, so the shadow casters' blades are as wide and curled as the camera's.
    FishFloraLook look = FishFloraAt(root.xz, GRASS_FLORA_PATCH, GRASS_FLORA_SALT);

    float d = distance(root, _WorldSpaceCameraPos);
    o.distance = d;

    // At the grass distance: the last band dissolves (dithered) and shrinks to stubble.
    float maxDistance = max(1.0, _GrassParams0.z);
    float band = max(0.01, _GrassParams0.w) * maxDistance;
    float visible = 1.0 - saturate((d - (maxDistance - band)) / band);
    o.fade = float2(visible, floor(r2 * 15.999));

    // Deep snow buries it.
    float snow = saturate(FishCoverAt(root).x) * FishSurfaceExposure(root);
    float bury = 1.0 - saturate(snow * 1.4) * typeParams.y;
    float bladeHeight = height * thinFade * lerp(0.35, 1.0, visible) * max(0.02, bury);

    // The wind: a world-space wave field carried downwind (gust bands rolling across the meadow), a
    // steady push by the wind's speed, and each blade's own flutter at its own phase.
    float time = _FishWeatherMisc.w;
    float2 dir = _FishWeatherWind.xy;
    if (dot(dir, dir) < 1e-6)
    {
        dir = float2(0.7071, 0.7071);
    }
    dir = normalize(dir);
    float speed = _FishWeatherWind.z;
    float gust = _FishWeatherWind.w;
    /* Rolling gust bands, as wind over a meadow looks from above: crests lying across the wind and
     * travelling downwind at the wind's pace, wobbled by noise so they are not ruled lines, swelling
     * and dying in wider gusty patches. Each blade follows a moment late (stiffer ones later), so
     * neighbours never move in lockstep. */
    float lambda = max(1.0, _GrassParams2.y);
    // Laid out on the scene's own way (FishWindWay), not the eased wind at the camera: the bands and the swell
    // stand where they do for every player. The blades still lean on the wind they feel (dir, below).
    float2 way = FishWindWay();
    float alongWind = dot(root.xz, way);
    float acrossWind = dot(root.xz, float2(-way.y, way.x));
    // At the wind's pace, held for each window of the shared clock (FishWindTravel): the bands lie in
    // the same place for every player, and never scrub as the wind eases.
    float travel = FishWindTravel((1.5 + _FishWindHold.z * 6.0) * max(0.0, _GrassParams4.z), lambda);
    float wobble = (FishSurfaceNoise(float2(acrossWind, alongWind) / (lambda * 2.5) + time * 0.05) - 0.5) * lambda * 1.2;
    float stiffness = max(0.1, typeParams.x);
    float phase = (alongWind - travel + wobble) / lambda - r1 * 0.06 * stiffness;
    float crest = pow(saturate(0.5 + 0.5 * sin(phase * 6.2831853)), max(1.0, _GrassParams4.y));
    float swell = FishSurfaceNoise(root.xz / (lambda * 4.0) - way * time * 0.04);
    float gustBand = crest * lerp(0.35, 1.0, swell);
    float strength = _GrassParams2.w + speed * 0.7 + gust * 0.8;
    // The wind's bend, an angle from vertical: a steady lean by the wind's strength, deepened in the bands.
    float fullAngle = max(0.0, _GrassParams4.x) * saturate(strength * (0.3 + 0.7 * gustBand) * _GrassParams2.x / stiffness);
    /* Far blades barely move. A blade thinner than a pixel swinging through a gust crest flips each pixel
     * between blade and ground: the far field shimmered, and from a low view the laid-over crests bared
     * the darker soil as long dark streaks. The waves still read there through the sheen (below), which
     * keeps the full crest. */
    float calm = lerp(1.0, saturate(_GrassParams7.z), smoothstep(_GrassParams7.x, max(_GrassParams7.x + 1.0, _GrassParams7.y), d));
    float windAngle = fullAngle * calm;
    /* Its own pace snapped to whole turns in the clock's wrap, so the wrap is seamless; tied to where
     * the band's crest lies, not to the band's travel, which restarts each window a whole band on
     * (phase × 2 radians would jump every blade then). */
    float flutterPhase = (alongWind + wobble) / lambda;
    float flutter = sin(time * FishWrapAngular(2.0 + 1.5 * r1) + r0 * 6.2831853 + flutterPhase * 2.0) * _GrassParams2.z * (0.3 + speed + gust) * calm;
    float2 facing = float2(cos(facingAngle), sin(facingAngle));
    float2 across = float2(facing.y, -facing.x);
    // The tip's direction: its own lean toward its facing plus the wind's angle downwind and the flutter.
    /* Seen from above, an upright blade is a dot and a lawn was pins in a cushion. The steeper the view looks down on
     * this blade, the further it lies over, along its own facing (its clump's): never toward or away from the camera,
     * so nothing swivels as the camera moves, and the shadow pass (which takes the camera from _GrassViewer) lays it
     * over alike. None at eye level; easing in from a view about 20 degrees down to all of it from about 70. */
    float3 toViewer = _GrassViewer.xyz - root;
    float viewSteep = toViewer.y / max(1e-3, length(toViewer));
    float viewLay = _GrassParams7.w * smoothstep(0.35, 0.95, viewSteep);
    /* Which way: a smooth field over a few metres, turned toward the wind as it rises, so neighbouring blades and
     * whole clumps lie the same way and a lawn seen from above is combed in patches. Along each blade's own facing
     * (outward from its clump's centre) every clump burst into a star round a dark middle: polka dots from above
     * (render 2026-10-07). */
    float combAngle = (FishSurfaceNoise(root.xz / 3.0) * 0.7 + FishSurfaceNoise(root.xz / 1.1 + 31.7) * 0.3) * 12.566371;
    float2 comb = float2(cos(combAngle), sin(combAngle)) + dir * (0.4 + 1.5 * speed);
    comb = dot(comb, comb) > 1e-6 ? normalize(comb) : facing;
    float2 lay = facing * lean + comb * viewLay + dir * sin(windAngle) + across * flutter;
    /* Trodden (GroundTrailMap): pushed over away from where a body stands or has just gone through, lying almost flat
     * along its track and rising again over a few seconds as the map lets it go. Away is down the slope of how flat the
     * grass lies, so blades part to either side of a path and outward round someone standing still. */
    float flattened = saturate(FishTrailAt(root.xz).y);
    if (flattened > 0.01)
    {
        float texel = _FishTrailParams.x;
        float2 gradient = float2(FishTrailAt(root.xz + float2(texel, 0.0)).y - FishTrailAt(root.xz - float2(texel, 0.0)).y,
            FishTrailAt(root.xz + float2(0.0, texel)).y - FishTrailAt(root.xz - float2(0.0, texel)).y);
        float2 away = dot(gradient, gradient) > 1e-6 ? -normalize(gradient) : facing;
        lay = lerp(lay, away * 0.95, smoothstep(0.0, 0.8, flattened));
    }
    float layLength = length(lay);
    if (layLength > 0.97)
    {
        lay *= 0.97 / layLength;
    }
    float3 tipDirection = float3(lay.x, sqrt(max(0.0, 1.0 - dot(lay, lay))), lay.y);

    // Bent further, the blade curves more: the base still rises before it arcs over.
    // A look's bend curls its patch's blades more or less (a wind-combed look arcs over, a stiff one stands straight).
    // Shape from the smooth swell, never the patch (FishFloraSwell): the same lean the compute gave the blade.
    float floraBend = clamp(FishFloraSwell(root.xz, GRASS_FLORA_BEND_SWELL, GRASS_FLORA_BEND_SALT), GRASS_FLORA_MIN_BEND, GRASS_FLORA_MAX_BEND);
    float bend = saturate(typeParams.z * floraBend * (0.5 + 0.5 * r1) + 0.6 * windAngle / max(0.1, _GrassParams4.x) + 0.5 * viewLay);
    float3 control, tip;
    GrassCurve(bladeHeight, tipDirection, bend, control, tip);
    float3 curvePoint = 2.0 * t * (1.0 - t) * control + t * t * tip;
    float3 tangent = 2.0 * (1.0 - t) * control + 2.0 * t * (tip - control);
    tangent = dot(tangent, tangent) > 1e-10 ? normalize(tangent) : float3(0.0, 1.0, 0.0);

    float3 sideWS = float3(across.x, 0.0, across.y);
    /* Thinned blades widen to stand for the ones thinned out (share^-exponent), but never past MaxBladePixels on
     * this camera's screen, nor MaxWiden. Widening in metres alone left blades one or two pixels wide past 8 m and
     * under one past 100 m: the field read as smaller and sparser the further it went. A sprinkled type on a coarser
     * lattice level is thinned only at 4^level times the share (GrassMath.SprinkleLevel): it widens by its own share. */
    float pixelMetres = d * _GrassScreen.x + _GrassScreen.y;
    float screenCap = max(1.0, pixelMetres * _GrassScreen.z);
    float share = min(1.0, GrassShareV(d) * exp2(2.0 * round(GrassTypeRow(type, GRASS_ROW_MORE).y)));
    float widen = min(min(_GrassParams0.y, screenCap), pow(max(1e-6, share), -_GrassParams0.x));
    float width = typeTip.a * widen * (0.75 + 0.5 * r0) * lerp(0.5, 1.0, thinFade) * clamp(FishFloraSwell(root.xz, GRASS_FLORA_WIDTH_SWELL, GRASS_FLORA_WIDTH_SALT) * (_FishFloraInfo.x < 0.5 ? 1.0 : _FishFloraWorld.y), GRASS_FLORA_MIN_WIDTH, GRASS_FLORA_MAX_WIDTH);
    float4 headInfo = GrassTypeRow(type, GRASS_ROW_HEAD);
    // A strap (a reed's leaf) keeps its width to a rounded end; a blade tapers to a point.
    float taper = headInfo.w > 0.5 ? 1.0 - 0.45 * pow(saturate(t), 3.0) : 1.0 - pow(saturate(t), 1.6);
    o.positionWS = root + curvePoint + sideWS * (u * 0.5 * width * taper);
    /* Edge-on, a blade is a sliver: its width lies along the line of sight. The share of the width it has lost is put
     * back ACROSS the view (perpendicular to both the view and the blade's own line), so an edge-on blade covers
     * EdgeOnThicken of what it would face-on. Face-on, or seen down its length from above (where the lay-over does the
     * work), nothing is added. */
    float3 toEye = normalize(_GrassViewer.xyz - root);
    float3 screenSide = cross(toEye, tangent);
    float screenSideLength = length(screenSide);
    if (_GrassParams6.w > 0.0 && screenSideLength > 1e-3)
    {
        screenSide /= screenSideLength;
        screenSide *= dot(screenSide, sideWS) < 0.0 ? -1.0 : 1.0;
        float lost = 1.0 - length(cross(sideWS, toEye));
        o.positionWS += screenSide * (u * 0.5 * width * taper * _GrassParams6.w * lost);
    }
    o.normalWS = normalize(cross(sideWS, tangent));

    /* A head record (GrassMath.WithHeadPart): the same stem, rebuilt above with the same wind, and one of its head's
     * two crossed cards drawn at its tip from the strip's own vertices: the row t runs across the card's length, the
     * side u across its width, shaped to an ellipse (flower), a spindle (seed head) or a nodding panicle (plume). The
     * strip's root row and its tip point close the outline, so the LOD meshes need nothing new. It widens with the
     * field as the blades do, so thinned far flowers keep their share of colour. */
    uint headPart = GrassHeadPart(blade.colour);
    bool isHead = headPart != 0u && headInfo.x > 0.5;
    bool isPlume = isHead && headInfo.x > 2.5;
    bool isFlower = isHead && headInfo.x >= 1.5 && headInfo.x <= 2.5;
    // A flower's drift: its own, larger patches (the look's bloom colour and size), read only for a flower's cards.
    FishFloraLook bloomLook = look;
    [branch] if (isFlower)
    {
        bloomLook = FishFloraAt(root.xz, GRASS_FLORA_BLOOM_PATCH, GRASS_FLORA_BLOOM_SALT);
    }
    if (isHead)
    {
        float3 tipWS = root + tip;
        float3 tipTangent = tip - control;
        tipTangent = dot(tipTangent, tipTangent) > 1e-10 ? normalize(tipTangent) : float3(0.0, 1.0, 0.0);
        // A head keeps its AREA as the field thins (share^-0.5), whatever the blades' widening: a disc grown by the
        // blades' width factor would grow by its square.
        float headWiden = min(_GrassParams0.y, rsqrt(max(1e-6, share)));
        float size = max(0.002, headInfo.y) * headWiden * thinFade * lerp(0.35, 1.0, visible) * (0.85 + 0.3 * r2);
        float second = headPart == 2u ? 1.0 : 0.0;
        float3 axisA, axisB, centre;
        float profile;
        if (headInfo.x < 1.5)
        {
            // A seed head: a spindle up the stem's last direction, a little below the tip to well above it.
            float3 cross0 = normalize(cross(tipTangent, abs(tipTangent.y) < 0.95 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
            float3 cross1 = cross(tipTangent, cross0);
            axisA = tipTangent * size;
            axisB = lerp(cross0, cross1, second) * (size * 0.09);
            centre = tipWS - tipTangent * (size * 0.15);
            profile = pow(saturate(sin(t * 3.14159265)), 0.6);
            o.positionWS = centre + axisA * t + axisB * u * profile;
        }
        else if (headInfo.x > 2.5)
        {
            /* A plume (feather grass's silky awns, a Phragmites reed's panicle): a soft feathery spray, much wider than a
             * seed head and longer, starting a little below the tip and nodding over toward the stem's facing (the
             * blade's lean, so a clump's plumes droop outward together) as it goes, broadest past its middle and
             * closing to a soft point. Its spine is a parabola: up the stem's last direction, bent down and out by the
             * nod; the two cards cross along it. Lit through like a thin leaf (o.thin below), so a backlit stand of
             * Stipa or a reed bed against a low sun glows, as it does. */
            float3 cross0 = normalize(cross(tipTangent, abs(tipTangent.y) < 0.95 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0)));
            float3 cross1 = cross(tipTangent, cross0);
            float3 nod = float3(cos(facingAngle), -0.55, sin(facingAngle)) * (0.45 + 0.25 * r1);
            float3 spine = tipTangent * t + nod * (t * t);
            axisA = (tipTangent + nod * (2.0 * t)) * size;   // the spine's direction at t, for the card's normal
            axisB = lerp(cross0, cross1, second) * (size * 0.24);
            centre = tipWS - tipTangent * (size * 0.12);
            profile = pow(saturate(sin(t * 3.14159265)), 0.7) * (0.45 + 0.55 * t);
            o.positionWS = centre + spine * size + axisB * u * profile;
        }
        else
        {
            // A flower: a disc tipped toward the sky, and one upright across it so it reads from the side too. Its drift's
            // look sizes it (the compute's sphere allows the most a look can: GRASS_FLORA_MAX_BLOOM there).
            size *= clamp(FishFloraSwell(root.xz, GRASS_FLORA_HEADSIZE_SWELL, GRASS_FLORA_HEADSIZE_SALT), GRASS_FLORA_MIN_BLOOM, GRASS_FLORA_MAX_BLOOM);
            // Only in its season (FishBloom): out of it the head closes to nothing and the stem stands bare. A clump's
            // flowers open close together, each a little on its own.
            size *= FishBloom(0.6 * clumpColour + 0.4 * GrassUnitV(GrassMixV(h0 ^ 0x1B873593u)), root.y);
            float3 along = float3(cos(facingAngle), 0.0, sin(facingAngle));
            float3 aside = float3(-along.z, 0.0, along.x);
            axisA = lerp(along, aside, second) * size;
            axisB = lerp(normalize(aside + float3(0.0, 0.45, 0.0)), float3(0.0, 1.0, 0.0), second) * size;
            centre = tipWS + float3(0.0, 0.15 * size, 0.0);
            float x = 2.0 * t - 1.0;
            profile = sqrt(saturate(1.0 - x * x));
            o.positionWS = centre + axisA * x + axisB * u * profile;
        }
        float3 faceNormal = cross(axisB, axisA);
        faceNormal = dot(faceNormal, faceNormal) > 1e-12 ? normalize(faceNormal) : float3(0.0, 1.0, 0.0);
        o.normalWS = faceNormal.y < 0.0 ? -faceNormal : faceNormal;
        sideWS = normalize(axisB);
    }
    // Far blades lean their shading toward the ground's: a far meadow is lit like a surface, not sparkle.
    o.normalWS = normalize(lerp(o.normalWS, float3(0.0, 1.0, 0.0), saturate(d / 120.0) * 0.6));
    o.sideWS = sideWS;
    o.side = isHead ? u * 0.3 : u;
    // 2 marks a head for the fragment (no root shading, no terrain colour, kept through the far field).
    o.along = isHead ? 2.0 : t;
    o.thin = isPlume ? 1.0 : isHead ? 0.6 : saturate(1.0 - width / 0.04);

    // Colour, root to tip, with the clump's and the blade's own variation and the season.
    float3 colour = lerp(typeRoot.rgb, typeTip.rgb, pow(saturate(t), 0.8));
    float cure;
    float3 season = GrassSeasonTint(root, typeHealthy, GrassTypeRow(type, GRASS_ROW_DRY), r1, cure);
    colour *= season;
    colour *= 0.85 + 0.3 * clumpColour;
    colour *= 0.9 + 0.2 * r2;
    // A clump's bias also leans its hue a little toward straw.
    colour = lerp(colour, colour * float3(1.12, 1.02, 0.78), (clumpColour - 0.5) * 0.4);
    /* The look of the blade's patch, as a SHIFT of the type's own colour (FishFloraShift), before the ground's colour is
     * blended in below: that colour (the terrain under the root, read by the compute through FishFloraGround, or the
     * ground colour map) carries only the biome's ground shift, so the blend below takes it the rest of the way to the
     * patch, and every part of the blade is shifted once; the very root stays the ground's own colour. The terrain's own colour, the type's shading and the season's browning stay, so a dry slope is still
     * straw and a wet hollow still green, and the planet and the patch move them (olive under a K star, plum under a red
     * dwarf; a golden or silvered patch a tint of its meadow). A look that REPLACED the colour (2026-10-15, the first
     * version) turned meadows into chunks of colour that ignored their climate. The common look of an Earth-like world
     * shifts nothing. */
    if (!isHead && look.valid)
    {
        colour = FishFloraShift(colour, look.shift, _FishFloraStrength.x);
    }

    /* The terrain's own texture under the root (read by the compute pass), as Ghost of Tsushima colours
     * its grass: the blade takes the ground's colour, and keeps from its type only its shading (root dark
     * to tip light, clump and blade variation, as a brightness ratio) and the season's browning (as a
     * hue ratio over the type's healthy tint). Grass then changes with the ground it grows from, and the
     * ground showing between blades is the same colour, so the gaps stop reading. */
    float4 terrain = GrassUnpackColour(blade.colour);
    float3 lumWeights = float3(0.2126, 0.7152, 0.0722);
    // The season's browning as a hue ratio over the type's healthy tint (1 when healthy), for whatever colour replaces
    // the type's own: the ground's, then the look's.
    float3 browning = season / max(1e-3, typeHealthy.rgb);
    browning /= max(1e-3, dot(browning, lumWeights));
    /* What the ground does to the stem, gathered from whichever ground it knows and applied after the look (below): the
     * ground's colour at the very root and how much of it, and the ground-colour diagnostic's override. */
    float3 rootGround = colour;
    float rootGroundAmount = 0.0;
    float3 groundView = colour;
    bool groundViewOn = false;
    if (isHead)
    {
        // The head's own colour, one of the type's (a clump's flowers share it), browned as the season goes dormant.
        int colours = clamp((int)round(headInfo.z), 1, 4);
        int pick = min(colours - 1, (int)floor(clumpColour * (float)colours));
        colour = GrassTypeRow(type, GRASS_ROW_HEAD_COLOUR + pick).rgb * (0.85 + 0.3 * r1);
        if (isFlower && bloomLook.valid)
        {
            /* A flower takes its drift's bloom instead of its type's palette: the same meadow flower opens gold in one
             * biome, violet or ember-red in another, and moon-pale in a rare fantastic drift. A clump's flowers a shade
             * lighter or darker than the next, so a drift is not one flat colour. */
            float3 bloom = bloomLook.bloom * (0.8 + 0.4 * clumpColour) * (0.9 + 0.2 * r1);
            colour = lerp(colour, bloom, saturate(_FishFloraStrength.y));
        }
        else if (look.valid)
        {
            /* A seed head or a plume keeps its type's colour (straw, silver, russet: what it is made of), leaning a little
             * toward its patch's accent, at the head's own lightness, so a steppe's plumes pick up the biome's cast
             * without turning into flowers. */
            colour = lerp(colour, FishFloraRecolour(colour, look.accent, 1.0), 0.35 * saturate(_FishFloraStrength.z));
        }
        float known = step(1e-4, _FishSeason.w);
        float dormant = saturate(-FishSeasonGrowing(root.y)) * 0.7 * known;
        colour = lerp(colour, dot(colour, lumWeights) * float3(0.75, 0.62, 0.45), dormant);
    }
    else if (terrain.a > 0.5)
    {
        float3 typeMid = lerp(typeRoot.rgb, typeTip.rgb, 0.57) * season;
        float shade = dot(colour, lumWeights) / max(1e-3, dot(typeMid, lumWeights));
        float3 target = terrain.rgb * shade * lerp(1.0, browning, 0.6) * _GrassParams6.y;
        // The ground carries its biome's ground shift; the blade takes its patch's (the ground stays as the terrain draws it).
        if (look.valid)
        {
            target = FishFloraShift(target, look.shift / max(look.biomeShift, 1e-3), _FishFloraStrength.x);
        }
        colour = lerp(colour, target, saturate(typeRoot.a));
        // The very root is the ground itself.
        float rootUp = saturate(t / max(0.02, _GrassParams3.y));
        rootGround = terrain.rgb;
        rootGroundAmount = _GrassParams6.z * (1.0 - rootUp) * (1.0 - rootUp);
        if (_FishGroundColourParams.y > 0.5 && _FishGroundColourParams.y < 1.5)
        {
            groundView = terrain.rgb;
            groundViewOn = true;
        }
    }
    // Otherwise (no terrain arrays bound) the ground colour map: the root grows out of the soil (its
    // colour outright at the very root, easing off up the blade), and the whole blade leans to its hue.
    else if (_FishGroundColourParams.x > 0.0)
    {
        float2 uv = (root.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
        if (all(uv >= 0.0) && all(uv <= 1.0))
        {
            float4 ground = SAMPLE_TEXTURE2D_LOD(_FishGroundColour, sampler_FishGroundColour, uv, 0.0);
            float lum = max(1e-3, dot(colour, lumWeights));
            float groundLum = max(1e-3, dot(ground.rgb, lumWeights));
            colour = lerp(colour, ground.rgb * (lum / groundLum), _GrassParams3.z * ground.a);
            float up = saturate(t / max(0.02, _GrassParams3.y));
            rootGround = ground.rgb;
            rootGroundAmount = _GrassParams3.x * (1.0 - up) * (1.0 - up) * ground.a;
            if (_FishGroundColourParams.y > 0.5 && _FishGroundColourParams.y < 1.5)
            {
                groundView = ground.a > 0.0 ? ground.rgb : float3(1.0, 0.0, 1.0);
                groundViewOn = true;
            }
        }
    }
    /* Winter cures the blade: dormant grass loses its green to straw at its own lightness (a little lighter, as cured grass
     * is), whatever colour the ground lent it. Last, so nothing dilutes it: it rode the dry tint once, and on terrain-
     * coloured grass the terrain blend left a few per cent of it, so the seasons did nothing visible to a meadow. Heads
     * brown on their own (above). */
    if (!isHead)
    {
        colour = lerp(colour, dot(colour, lumWeights) * GRASS_CURED, cure);
    }
    colour = lerp(colour, rootGround, rootGroundAmount);
    if (groundViewOn)
    {
        colour = groundView;
    }
    // Gust sheen: blades laid over in a crest turn their flat side to the sky and catch the light, which
    // is what makes the waves read across a meadow; strongest toward the tips.
    // A plume's silky awns catch it too (a gust running through a feather-grass steppe is a silver wave).
    float sheen = (isHead && !isPlume) ? 0.0 : gustBand * saturate(fullAngle / max(0.1, _GrassParams4.x)) * _GrassParams4.w * (isPlume ? 0.6 : t);
    colour = lerp(colour, colour * 1.55 + 0.03, saturate(sheen));
    o.albedo = colour;
    return o;
}

// The shaded normal: the face normal turned toward each edge by the rounding angle (a cylinder, not a
// card), flipped with the face seen. The edge term does not flip: seen from behind, the same edge
// still faces outward the same way.
float3 GrassShadingNormal(float3 normalWS, float3 sideWS, float side, float facing)
{
    float a = _GrassParams1.x;
    return normalize(normalWS * (facing * cos(a)) + sideWS * (side * sin(a)));
}

void GrassFadeClip(float4 positionCS, float2 fade)
{
    float2 shift = float2(fmod(fade.y, 4.0), floor(fade.y * 0.25));
    FishDitherClip(positionCS.xy + shift, fade.x);
}

#if defined(FISH_GRASS_PASS_FORWARD) || defined(FISH_GRASS_PASS_DEPTHNORMALS)

struct GrassAttributes
{
    float4 positionOS : POSITION;
    uint instanceID : SV_InstanceID;
};

struct GrassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 sideWS : TEXCOORD2;     // xyz across the blade, w side (-1 .. 1)
    float4 albedo : TEXCOORD3;     // rgb, a along (0 root .. 1 tip)
    float4 extra : TEXCOORD4;      // x thin, y fog, zw fade
};

GrassVaryings GrassVertexMain(GrassAttributes input)
{
    GrassVertex v = GrassBuild(input.instanceID, input.positionOS.xy);
    GrassVaryings o = (GrassVaryings)0;
    o.positionWS = v.positionWS;
    o.normalWS = v.normalWS;
    o.sideWS = float4(v.sideWS, v.side);
    o.albedo = float4(v.albedo, v.along);
    o.positionCS = TransformWorldToHClip(v.positionWS);
    o.extra = float4(v.thin, ComputeFogFactor(o.positionCS.z), v.fade);
    return o;
}

#endif

#if defined(FISH_GRASS_PASS_FORWARD)

float4 GrassShadowCoord(float3 positionWS)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    return ComputeScreenPos(TransformWorldToHClip(positionWS));
#else
    return TransformWorldToShadowCoord(positionWS);
#endif
}

// One light on a blade: wrapped diffuse, a low waxy specular, and light through the blade from behind. `colour` is
// the light's own, already through its shadow and falloff.
float3 GrassLit(float3 L, float3 colour, float3 albedo, float3 n, float3 faceN, float3 V, float smoothness, float ao, float far, float thin)
{
    // Thin leaves wrap a little light round the edge.
    float ndl = saturate((dot(n, L) + 0.2) / 1.2);
    float3 lit = albedo * colour * ndl * lerp(0.55, 1.0, ao);

    // Low, narrow, subtle specular (a waxy blade, never a mirror).
    float3 H = normalize(L + V);
    float power = exp2(10.0 * smoothness + 1.0);
    float spec = pow(saturate(dot(n, H)), power) * (power + 2.0) / 8.0 * 0.04 * _GrassParams3.w;
    lit += colour * spec * saturate(dot(n, L)) * ao * (1.0 - far);

    // Light through the blade only when the light is behind it, and more through a thin one.
    float back = saturate(dot(V, -L));
    back *= back;
    back *= back;
    float behind = saturate(-dot(faceN, L) * 2.0 + 0.3);
    lit += albedo * colour * back * behind * _GrassParams1.z * lerp(0.3, 1.0, thin) * ao * (1.0 - far);
    return lit;
}

half4 GrassForwardFragment(GrassVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    GrassFadeClip(input.positionCS, input.extra.zw);
    float facing = IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 n = GrassShadingNormal(normalize(input.normalWS), input.sideWS.xyz, input.sideWS.w, facing);
    float3 faceN = normalize(input.normalWS) * facing;
    // A head (GrassBuild's along = 2): lit as the stem's top, its colour kept further into the far field.
    bool head = input.albedo.a > 1.5;
    float t = head ? 1.0 : input.albedo.a;
    float3 albedo = input.albedo.rgb;
    half smoothness = (half)_GrassParams1.w;

    /* The far field converges to the ground: blades lit and coloured as the terrain they stand on, so a
     * distant meadow reads as textured ground rather than dark specks (sideways normals, shadowed sides
     * and dark roots made far blades darker than the flat-lit terrain at the same colour). Opaque all the
     * way: blending would need sorting millions of blades and would drop depth, shadows and fog. */
    float viewDistance = distance(_WorldSpaceCameraPos, input.positionWS);
    float farEnd = max(_GrassParams5.x + 1.0, _GrassParams0.z * _GrassParams5.y);
    float far = smoothstep(_GrassParams5.x, farEnd, viewDistance);
    // Toward the terrain's own normal (FishGroundColour.hlsl): far grass shades like the slope it covers.
    n = normalize(lerp(n, FishGroundNormalAt(input.positionWS), far * _GrassParams5.w));
    if (far > 0.0 && _FishGroundColourParams.x > 0.0)
    {
        float2 groundUV = (input.positionWS.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
        if (all(groundUV >= 0.0) && all(groundUV <= 1.0))
        {
            float4 ground = SAMPLE_TEXTURE2D_LOD(_FishGroundColour, sampler_FishGroundColour, groundUV, 0.0);
            albedo = lerp(albedo, ground.rgb, far * _GrassParams5.z * ground.a * (head ? 0.25 : 1.0));
        }
    }

    // The weather's cover and damp, as on every other plant (FishSurface.hlsl).
    half3 weatheredNormal = (half3)n;
    half3 weatheredAlbedo = (half3)albedo;
    float cover;
    FishWeatherFoliage(input.positionWS, weatheredAlbedo, weatheredNormal, smoothness, cover);
    albedo = weatheredAlbedo;

    float3 V = normalize(_WorldSpaceCameraPos - input.positionWS);
    // Dense grass shades its own base: the ambient and the sun both fall off toward the root.
    float ao = lerp(lerp(_GrassParams1.y, 1.0, saturate(pow(t, 0.75))), 1.0, far);

    /* With the cloud shadow (URP's cookie on the main light) and the shadow's fade at its distance, as the terrain
     * under the blades has them. Under cloud SkySystem.ApplyLights divides the light's intensity by the average the
     * cloud lets through and leaves the cookie to dim it patch by patch: the cookie-less GetMainLight(shadowCoord)
     * lit a meadow under a storm several times brighter than full sun, over ground that was dark. */
    Light mainLight = GetMainLight(GrassShadowCoord(input.positionWS), input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
    float3 colour = GrassLit(mainLight.direction, mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation),
        albedo, n, faceN, V, smoothness, ao, far, input.extra.x);

    /* Every other light, as the terrain and the trees under the grass have them: the moon when it is not the
     * main light (it is not, until it outshines what is left of the sun), a companion sun, a torch. Without
     * them a moonlit meadow went black where the blades stood, over ground that was lit. No shadows: the
     * main light's are the ones a blade needs. */
#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    half4 noMask = half4(1.0, 1.0, 1.0, 1.0);
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, input.positionWS, noMask);
        colour += GrassLit(light.direction, light.color * light.distanceAttenuation, albedo, n, faceN, V, smoothness, ao, far, input.extra.x);
    }
#endif
    uint additionalCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(additionalCount)
        Light light = GetAdditionalLight(lightIndex, input.positionWS, noMask);
        colour += GrassLit(light.direction, light.color * light.distanceAttenuation, albedo, n, faceN, V, smoothness, ao, far, input.extra.x);
    LIGHT_LOOP_END
#endif

    // The sky's ambient, with the base's occlusion.
    colour += albedo * FishTrilight((half3)n) * ao;

    colour = MixFog(colour, input.extra.y);
    if (_GrassHighlight > 0.5)
    {
        colour = float3(1.0, 0.0, 1.0);
    }
    return half4(colour, 1.0);
}

#endif

#if defined(FISH_GRASS_PASS_DEPTHNORMALS)

half4 GrassDepthNormalsFragment(GrassVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    GrassFadeClip(input.positionCS, input.extra.zw);
    float facing = IS_FRONT_VFACE(face, 1.0, -1.0);
    float3 n = GrassShadingNormal(normalize(input.normalWS), input.sideWS.xyz, input.sideWS.w, facing);
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(n);
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
    return half4(n, 0.0);
#endif
}

#endif

#if defined(FISH_GRASS_PASS_SHADOW) || defined(FISH_GRASS_PASS_DEPTH)

struct GrassDepthAttributes
{
    float4 positionOS : POSITION;
    uint instanceID : SV_InstanceID;
};

struct GrassDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 fade : TEXCOORD0;
};

#if defined(FISH_GRASS_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

GrassDepthVaryings GrassDepthVertex(GrassDepthAttributes input)
{
    GrassVertex v = GrassBuild(input.instanceID, input.positionOS.xy);
    GrassDepthVaryings o = (GrassDepthVaryings)0;
#if defined(FISH_GRASS_PASS_SHADOW)
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDirectionWS = normalize(_LightPosition - v.positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    // A blade is edge-on to the light as often as not: bias along the face normal turned to the light.
    float3 n = dot(v.normalWS, lightDirectionWS) < 0.0 ? -v.normalWS : v.normalWS;
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(v.positionWS, n, lightDirectionWS)));
#else
    o.positionCS = TransformWorldToHClip(v.positionWS);
#endif
    o.fade = v.fade;
    return o;
}

half4 GrassDepthFragment(GrassDepthVaryings input) : SV_Target
{
    GrassFadeClip(input.positionCS, input.fade);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
