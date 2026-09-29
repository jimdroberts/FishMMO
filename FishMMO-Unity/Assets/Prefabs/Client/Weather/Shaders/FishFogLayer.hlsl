#ifndef FISHMMO_FOG_LAYER_INCLUDED
#define FISHMMO_FOG_LAYER_INCLUDED

// The weather's fog as the thing it is: cloud whose base is the ground. A layer of chilled, saturated
// air lying on the ground with a top (FishMMO.Shared.Weather.FogLayer works out how deep, and whether
// the wind or the morning has lifted it off the ground into a sheet of stratus).
//
// ONE FOG, MARCHED WITH THE CLOUDS. The cloud march (FishCloudVolume.hlsl) walks this layer along
// every ray exactly as it walks a cloud, its banks read out of the sky's shape volume and drifting on the
// wind at the layer's own height, so a fog can be walked into and out of and seen from a hill lying in
// the low ground with a soft top. It is lit without a light march, because the whole layer is known in
// closed form: the beam and the sky come down through the fog above each point (FishFogLight). Where
// the march does not run (a renderer without the cloud feature, or before the sky has its volumes), the
// same layer is drawn instead by the froxel volume near the camera and the analytic pass beyond it —
// the fallback, from these same functions, so the three never disagree about where the fog is. Never
// both: the fallback passes stand down whenever the march has drawn the fog (FogLayerView.DrawnByClouds).
//
// THE LAYER. Its extinction at the ground is the fog's own, from its water (AirPhysics.FogExtinction):
// four kilometres of visibility in a mist, three hundred metres in a fog, sixty in a dense one. It is
// that from the ground up into a top that fades out smoothly over a share of the layer's own depth
// (FogLayerView.TopShare: all of it in a thin still mist, which is densest at the ground and thins the
// whole way up; the upper sixth or so of a deep, mixed one, whose top lies under its inversion), and —
// once the wind or the morning has lifted it — from a base above the ground. Each fade is a smoothstep,
// centred on the physics' top so the column holds exactly `depth` metres of fog however soft its top,
// and a polynomial, so the profile's integral along any straight ray has a closed form: the light that
// reaches a point in it, and the fallback's whole integral, need no loop.
//
// THE TOP LIES LEVEL. Cold air drains downhill and pools, so a fog fills a valley to one height and a
// hill stands clear of it: the top is the depth above the ground as the pooled air feels it — the
// terrain smoothed over some seven hundred metres (CloudTerrainMap, r) — and never less than a thin
// skin over ground that stands above the pool. A valley floor under the smoothed surface is deeper in
// it; a ridge above it is out of it.
//
// ITS TOP IS A FADE, NOT A SURFACE. A radiation fog's top is its inversion: the water in it peaks
// under the top and falls away through the entrainment zone above — over the upper tenth or sixth of
// a developed fog (Costabloz et al. 2025, "Vertical profiles of liquid water content in fog layers
// during the SOFOG3D experiment", ACP 25: 140 tethered-balloon profiles; one rose to 215 m and was
// gone by 255 m), and all the way from the ground in a thin, stable mist, whose water is greatest at
// the ground. Seen from above that is a soft, even whiteness, not a textured carpet. It was drawn as a
// surface — heaved on an 800 m tile, billowed in three dimensions by the banks and by eight-metre
// eddies read from the detail volume, twice as patchy near the top as in the body — which is how a
// cumulus edge is carved and nothing like a fog's top: from 400 m up it read as a mottled, speckled
// carpet with a hard edge. The games that draw fog well do what the physics says: a density that falls
// off smoothly with height, times one low-frequency noise drifting on the wind (Wronski 2014, "Volumetric
// fog", SIGGRAPH Advances: "just one octave of Perlin noise that is animated by wind … multiple octaves
// … difference was rather subtle", over an exponential height attenuation; Bauer 2019, "Creating the
// atmospheric world of Red Dead Redemption 2", SIGGRAPH Advances: a fog map of start height, falloff
// distance and density, and "tiling noise lookups, animated with wind"; Unreal's exponential height fog).
// The top is now that fade, and nothing else: the volumetric march through it makes the soft top.
//
// ITS BANKS (FishFogDensity). A fog is not even. It forms first in patches and in the hollows, and
// takes the best part of an hour to cover the ground (Mazoyer et al. 2017, "Large-eddy simulation of
// radiation fog: Part 1", ACP 17); in shear its top rolls in Kelvin–Helmholtz waves about 500 m long
// (Bergot 2013, QJRMS 139; Mazoyer 2017), more water in the crests than the troughs. That is structure
// hundreds of metres across, and it is the only structure drawn: one read of the shape volume's
// Perlin–Worley channel on a 1600 m tile (its correlation falls to a quarter over 200 m and to nothing
// by 300 m, measured off the baked volume), carried along by the wind and slowly turning over, applied
// to the whole column as a log-normal factor with a mean of one — it moves the fog about without
// changing how much of it there is, so the visibility the physics set is the visibility on average. A
// thinner patch is a lower top seen from above, a thicker one a higher: the top undulates by itself,
// softly, because the march finds its whiteness at a different depth, not because a surface was moved.

// x the fog's extinction at the ground (1/m), y how deep it lies (m), z how far it has lifted off the
// ground (0..1), w how soft its top is (m: half the thickness it fades over, FogLayerView.TopSoftness).
float4 _FishFogLayer;
// x unused (it was how far the top heaved: the top is no longer displaced), y how patchy it is (the
// spread of its density, in e-foldings), z how far the banks have turned over (0..1), w 1 while anything
// draws the layer before the transparent queue (the march or a fallback pass: FogLayerView.Drawn), so
// what is drawn in front of it afterwards — the falling snow and rain — lays the layer's own fog between
// itself and the camera (FishPrecipitation.shader). Set on the globals only; the froxel compute is handed
// its own copy with w 0 and never reads it.
float4 _FishFogLayerShape;
// xy how far the wind has carried it (m, world x and z, wrapped to the bank tile), zw unused.
float4 _FishFogLayerDrift;
// xyz toward the light that leads the sky (the sun, or the moon at night), w the drops' asymmetry g.
float4 _FishFogLight;
// rgb what that light brings: its colour times its intensity, times the share of it the clouds let
// through ON AVERAGE over the view (FogLayerView.CurrentLighting) — what a pass that cannot read the
// cloud shadow is lit by. w: 0 when the light carries no cloud shadow; otherwise what the rgb is
// multiplied by, with the cloud shadow read at a point, to be the light a surface there gets
// (FishFogSunShare): the reciprocal of that average.
float4 _FishFogLightColor;
// rgb the sky's own light, from all round.
float4 _FishFogAmbient;
// The cloud shadow the leading light carries (CloudShadowPresenter's cookie), published beside the light
// by FogLayerView.PublishLighting with the light's own mapping from the world onto it: u = dot(U.xyz, p)
// + U.w, v likewise — URP's (light-space xy − offset) / size + ½, so it is the texel URP lights a surface
// at p with. White, and never read, while _FishFogLightColor.w is 0.
TEXTURE2D(_FishFogSunCookie);
SAMPLER(sampler_FishFogSunCookie);
float4 _FishFogSunCookieU;
float4 _FishFogSunCookieV;
// x the lowest the fog can lie anywhere in view (m: at or under the lowest ground), y the highest its
// top's fade reaches anywhere in view (m), z 1 while the cloud march draws the fog, w unused
// (FogLayerView.Shell). What the march skips the empty air above the fog by.
float4 _FishFogShell;

// The ground under the sky (CloudTerrainMap): r the terrain smoothed over some 700 m, gb which way that
// rises, a the ground itself; rect xy corner, z size (m), w 1 when there is a map. Outside it the
// ground is sea level. Declared here for both the fog and the clouds, which include this file.
TEXTURE2D(_FishCloudTerrain);
SAMPLER(sampler_FishCloudTerrain);
float4 _FishCloudTerrainRect;

// The sky's own noise volumes (CloudNoiseBaker): the shape volume (128 cubed: r Perlin–Worley, gba
// Worley octaves), which the fog's banks are read out of, and the detail volume (32 cubed: rgb Worley
// octaves), which only the clouds read. Declared here for the fog and the clouds alike.
TEXTURE3D(_FishCloudShape);
SAMPLER(sampler_FishCloudShape);
TEXTURE3D(_FishCloudDetail);
SAMPLER(sampler_FishCloudDetail);

// The tile the fog's banks are read at, m: the wind's drift is wrapped to it (FogLayerView.BankTile).
// The shape volume's Perlin–Worley channel on it has banks some 200–300 m across (its correlation is
// 0.6 over 100 m, a quarter over 200 m, nothing by 300 m: measured off the baked volume, mip 0, along
// each axis) — the scale fogs form in patches at and their tops roll at (Mazoyer 2017: ~500 m waves).
// There used to be wisps on a 400 m tile and eddies on an 80 m one as well, fifty and eight metres
// across: removed, with the top's heave on an 800 m tile — a fog has no structure that fine.
#define FISH_FOG_BANK_TILE 1600.0
// How tall one tile of the banks stands, m: a quarter of its width, so a bank some 250 m across stands
// some 60 m tall. Stratified, as a fog is, but no longer ten to one — at a tenth of the tile the banks
// changed every ten metres of height, and a ray grazing the top crossed them as stripes; a mixed fog
// is nearly even with height (Costabloz 2025), and the profile, not the banks, gives it its top.
#define FISH_FOG_BANK_RISE 400.0
// The shape volume's Perlin–Worley channel as it comes, measured off the baked asset (mip 0, every
// texel): its mean and spread. Its coarser mips hold the same spread to within 4 % down to mip 2 — it
// has next to nothing finer than fifty metres on this tile.
#define FISH_FOG_BANK_MEAN 0.6675
#define FISH_FOG_BANK_SPREAD 0.0459
// Texels across one tile of the shape volume.
#define FISH_FOG_SHAPE_TEXELS 128.0
// Where a fully lifted fog's base sits, as a share of its top. FogLayer.LiftedBase says the same.
#define FISH_FOG_LIFTED_BASE 0.6
// The skin of fog left over ground that stands above the pool, as a share of the layer's depth.
#define FISH_FOG_RIDGE_SKIN 0.1
// How far under the terrain map's ground a fog lying on the ground reaches: deeper than any valley
// the map can hide. FogLayerView.UnderMap says the same.
#define FISH_FOG_UNDER_MAP 1000.0

/// <summary>The layer over one place, in altitudes.</summary>
struct FishFogColumn
{
    float top;       // the middle of the top's fade (m)
    float soft;      // half the thickness the top fades over (m)
    float base;      // the middle of the base's fade (m): under the ground while the fog lies on it
    float baseSoft;  // half the thickness the base fades over (m)
    float hollow;    // how far the ground here lies under the pooled air's level (m; negative on a rise)
};

/// <summary>x the ground under a place, y the ground as the pooled air feels it (m): the sea's surface over the sea.</summary>
float2 FishFogGround(float2 xz)
{
    float2 ground = float2(0.0, 0.0);
    if (_FishCloudTerrainRect.w > 0.5)
    {
        float2 uv = (xz - _FishCloudTerrainRect.xy) / max(1.0, _FishCloudTerrainRect.z);
        float2 toEdge = min(uv, 1.0 - uv);
        // Eased to sea level at the map's edge, as the clouds ease it, rather than cut off.
        float inside = saturate(min(toEdge.x, toEdge.y) / 0.08);
        float4 terrain = SAMPLE_TEXTURE2D_LOD(_FishCloudTerrain, sampler_FishCloudTerrain, saturate(uv), 0);
        ground = float2(terrain.a, terrain.r) * inside;
    }
    // Over the sea the ground a fog lies on is the water, not the sea bed under it: the terrain map
    // holds the bed, and read as it stood a fog over the sea lay as far under its surface as the bed is.
    return max(ground, 0.0);
}

/// <summary>The layer over a place: level over the pooled ground, a skin over what stands above it.</summary>
FishFogColumn FishFogColumnAt(float2 xz)
{
    float2 ground = FishFogGround(xz);
    float depth = max(0.0, _FishFogLayer.y);
    FishFogColumn c;
    // As soft as the layer is deep at most: a fade that half-wide starts at the pooled ground, which is
    // a thin still mist's profile — densest at the ground and thinning the whole way up.
    c.soft = clamp(_FishFogLayer.w, 0.25, max(0.25, depth));
    c.top = max(ground.y + depth, ground.x + depth * FISH_FOG_RIDGE_SKIN);
    // Lifted: the base rises off the lowest ground under it. Lying on the ground, a fog has no base
    // at all — it fills down to whatever ground a ray meets, and the depth buffer ends the ray
    // there. It used to stop 2 m under the terrain map's ground, but that map is a hundred metres
    // to the texel and blurred, and in a valley narrower than that — a gorge under a viaduct — its
    // ground stands well above the valley floor: the fog stopped in the air above the floor.
    float lifted = saturate(_FishFogLayer.z) * FISH_FOG_LIFTED_BASE * depth;
    float lying = 1.0 - smoothstep(0.0, 0.15, saturate(_FishFogLayer.z));
    c.baseSoft = max(0.25, lifted * 0.35);
    c.base = min(min(ground.x, ground.y) - 2.0 + lifted - lying * FISH_FOG_UNDER_MAP, c.top - c.soft - c.baseSoft);
    // And never under the sea. A fog is air: it lies on the water, and stops at its surface. Rays over
    // the sea end on the sea bed, under the water, which is drawn later and lays the cloud march's
    // buffer over itself as what stands between it and the camera (FishWaterFog.hlsl) — fog marched on
    // down to the bed would be laid over the sea as fog in front of it.
    c.base = max(c.base, c.baseSoft);
    c.hollow = ground.y - ground.x;
    return c;
}

/// <summary>
/// A unit step that falls smoothly from 1 to 0 across [edge − width, edge + width]: a smoothstep, whose
/// slope is zero at both ends, so the fog neither starts nor stops on a crease.
/// </summary>
/// <remarks>
/// It was a straight ramp. Its creases are where the density's slope jumps, and a ray grazing the top of
/// the fog sees the upper one as a line: the edge of a sheet, not the top of a fog.
/// </remarks>
float FishFogFall(float y, float edge, float width)
{
    return 1.0 - smoothstep(edge - width, edge + width, y);
}

/// <summary>
/// The column above an altitude of <see cref="FishFogFall"/>: ∫ from y up of the step, m.
/// </summary>
/// <remarks>
/// With x = (y − (edge − width)) / 2·width, the step is 1 − 3x² + 2x³, and what lies above x is
/// 2·width·(½ − x + x³ − x⁴/2): half the fade's width at its foot, as the straight ramp's was, so a
/// fade of any softness centred on the same edge holds the same fog.
/// </remarks>
float FishFogRampAbove(float y, float edge, float width)
{
    float x = saturate((y - (edge - width)) / (2.0 * width));
    float x2 = x * x;
    float inFade = 2.0 * width * (0.5 - x + x2 * x - 0.5 * x2 * x2);
    // Below the fade the whole step up to its foot and the fade's own half.
    return y <= edge - width ? edge - y : inFade;
}

/// <summary>The layer's mean profile at an altitude, 0..1 of its extinction at the ground.</summary>
float FishFogProfile(float y, FishFogColumn c)
{
    return FishFogFall(y, c.top, c.soft) * (1.0 - FishFogFall(y, c.base, c.baseSoft));
}

/// <summary>
/// How much of the layer lies above an altitude, m of it at full strength: the integral of the
/// profile from there up. Exact, since the base's fade and the top's never overlap (FishFogColumnAt).
/// </summary>
float FishFogColumnAbove(float y, FishFogColumn c)
{
    return max(0.0, FishFogRampAbove(y, c.top, c.soft) - FishFogRampAbove(y, c.base, c.baseSoft));
}

/// <summary>
/// How many metres of the layer at full strength a straight ray crosses between two distances along it.
/// </summary>
/// <remarks>
/// The profile depends on altitude alone, so along a ray y = y0 + t·dy the integral is the difference
/// of the column above the two ends over dy. A level ray is the commonest ray there is — anyone looking
/// at the horizon — and that form is 0/0 there, so a ray that climbs less than half a metre across the
/// segment takes the profile at its middle times its length: exact where the profile is flat, which is
/// everywhere but in its two fades, and inside a fade at least a quarter of a metre wide the smoothstep
/// bends too little across half a metre to matter.
/// </remarks>
float FishFogPath(float y0, float dy, float t0, float t1, FishFogColumn c)
{
    float span = max(0.0, t1 - t0);
    float ya = y0 + dy * t0;
    float yb = y0 + dy * t1;
    float level = FishFogProfile(0.5 * (ya + yb), c) * span;
    float climbing = (FishFogColumnAbove(ya, c) - FishFogColumnAbove(yb, c)) / (abs(dy) > 1e-6 ? dy : 1e-6);
    return abs(yb - ya) < 0.5 ? level : max(0.0, climbing);
}

/// <summary>
/// Henyey–Greenstein, as the share of a beam one scattering throws toward the eye, in the pipeline's
/// own units: the phase per steradian times π, which averages a QUARTER over the sphere.
/// </summary>
/// <remarks>
/// A light's colour is what a white card facing it shows — its irradiance over π — so a scattering of
/// β per metre puts β · p(θ) · π of that colour into the eye for every metre the beam crosses, p the
/// phase per steradian. Normalised to average one instead, as if a thin haze were a white card, the
/// forward peak of drops this size came out four times too bright: the sun's first scattering through
/// a mist a few hundred metres thick blazed round it and washed out the sky and the cloud behind.
/// A white card's brightness is what a fog reaches only once it has turned the beam round many times,
/// which is the diffuse term's in FishFogLight, not the beam's.
/// </remarks>
float FishFogPhase(float cosAngle, float g)
{
    float gg = g * g;
    float d = max(1e-4, 1.0 + gg - 2.0 * g * cosAngle);
    return (1.0 - gg) / (4.0 * d * sqrt(d));
}

/// <summary>
/// The light a point in the fog scatters toward the eye, per unit of its scattering: the light's own
/// beam, what the fog above has already scattered of it, and the sky's.
/// </summary>
/// <remarks>
/// <para>
/// The beam reaches the point through the fog between it and the top along the light's slant, and is
/// thrown forward — the glow round the sun seen through a mist, g about 0.8 for drops of a fog's size —
/// by one scattering (FishFogPhase: a quarter of the light's colour on average, eleven times it straight
/// down the beam). What the fog above has scattered out of the beam is not lost: a conservative
/// scatterer only turns light, and the two-stream answer for how much still gets through a layer that thick is
/// 1 / (1 + ¾(1 − g)τ) — the direct beam's share of that is the beam, the rest arrives diffuse, from
/// all round. That is what keeps the inside of a fog two hundred metres deep bright at noon and makes
/// its sunlit top the brightest thing in it. The sky's own light comes down through the fog the same way.
/// </para>
/// <para>
/// A light under the horizon lights nothing. The shadow, where there is one, falls on the beam alone:
/// the diffuse light comes from everywhere and a tree does not stop it.
/// </para>
/// <para>
/// The light arriving at the fog's top is the light through the cloud over THIS point (`sunShare`,
/// FishFogSunShare): the same light a surface here is lit by, beam and diffuse alike, since what the
/// cloud turned is part of what reaches the fog's top from that side of the sky, and the fog above then
/// turns it again. It was the average over the whole view — under a storm's core, where the cloud lets
/// down a few per cent, the fog was lit by the half the open sky around lets down, several times what the
/// ground and a white flake standing in it got (the ground under a thunderstorm, measured off the renders:
/// about 0.03 in linear light, against 0.2–0.3 for the fog round it). Snow drawn over it was a scatter of
/// dark specks: a white flake darker than the air it hung in.
/// </para>
/// </remarks>
/// <param name="ray">Which way the eye is looking through this point.</param>
/// <param name="extinction">The fog's extinction at the ground, 1/m.</param>
/// <param name="shadow">How much of the beam reaches this point past what stands in its way, 0..1.</param>
/// <param name="sunShare">The cloud's share of the light at this point against the average the light was published with (FishFogSunShare); 1 where it cannot be read.</param>
float3 FishFogLight(float y, float3 ray, FishFogColumn c, float extinction, float shadow, float sunShare)
{
    float g = _FishFogLight.w;
    float carry = 0.75 * (1.0 - g);
    float above = extinction * FishFogColumnAbove(y, c);
    float3 toLight = _FishFogLight.xyz;
    float risen = saturate(toLight.y * 20.0 + 1.0);
    float slant = above / max(0.05, toLight.y);
    float beam = exp(-slant);
    float diffuse = max(0.0, 1.0 / (1.0 + carry * slant) - beam);
    float3 sun = _FishFogLightColor.rgb * sunShare * risen * (beam * FishFogPhase(dot(ray, toLight), g) * shadow + diffuse);
    float3 sky = _FishFogAmbient.rgb / (1.0 + carry * above);
    return sun + sky;
}

/// <summary>
/// How much of the leading light reaches a point through the cloud over it, against the average over
/// the view the light was published with: the cloud shadow's own texel there, times the reciprocal of
/// that average (_FishFogLightColor.w). _FishFogLightColor.rgb times this is exactly the light URP lights
/// a surface at the point with — the light's colour and intensity times its cookie — so the fog, the
/// ground and the snow in front of them are lit by one light. 1 when the light carries no cloud shadow,
/// and past the cookie's window the cookie holds its edge, which the presenter fades to that average.
/// </summary>
/// <remarks>
/// A pixel pass only: it reads a global texture. A compute pass (the froxel fallback) is handed its
/// inputs one by one and has no cookie, and passes 1 — the fallback only runs where the cloud march does
/// not, and without the march there is no cloud shadow to read (SkySystem draws both from the same volume).
/// </remarks>
float FishFogSunShare(float3 positionWS)
{
    if (_FishFogLightColor.w <= 0.0)
    {
        return 1.0;
    }
    float2 uv = float2(dot(_FishFogSunCookieU.xyz, positionWS) + _FishFogSunCookieU.w, dot(_FishFogSunCookieV.xyz, positionWS) + _FishFogSunCookieV.w);
    return SAMPLE_TEXTURE2D_LOD(_FishFogSunCookie, sampler_FishFogSunCookie, saturate(uv), 0).r * _FishFogLightColor.w;
}

// ── The fog's structure ─────────────────────────────────────────────

/// The mip a read of a tile takes for a sample that stands for `footprint` metres of the world. The
/// march asks for none (0): its samples are jittered and averaged over frames, and a fractional mip
/// that changes with distance draws rings. The froxel fallback has no averaging and asks for its
/// froxel's own size, or its far slices flicker.
float FishFogLod(float footprint, float tile)
{
    return footprint > 0.0 ? log2(max(1.0, footprint / (tile / FISH_FOG_SHAPE_TEXELS))) : 0.0;
}

/// Where the air at a point came from: carried by the wind, which is what makes the fog drift. The
/// drift is wrapped to the bank tile.
float3 FishFogCarried(float3 at)
{
    return at - float3(_FishFogLayerDrift.x, 0.0, _FishFogLayerDrift.y);
}

/// The fog's banks at a point, in spreads either side of their mean: one read of the shape volume's
/// Perlin–Worley channel, carried on the wind and turning over slowly — rising through its tile by a
/// whole tile per turn (FogLayerView.BankTurnSeconds), so the wrap of the turn is not a jump. `at` is
/// (x, altitude, z).
float FishFogStructure(float3 at, float footprint)
{
    float3 carried = FishFogCarried(at);
    float3 uv = float3(carried.x / FISH_FOG_BANK_TILE, carried.y / FISH_FOG_BANK_RISE + _FishFogLayerShape.z, carried.z / FISH_FOG_BANK_TILE);
    float bank = SAMPLE_TEXTURE3D_LOD(_FishCloudShape, sampler_FishCloudShape, uv, FishFogLod(footprint, FISH_FOG_BANK_TILE)).r;
    return clamp((bank - FISH_FOG_BANK_MEAN) / FISH_FOG_BANK_SPREAD, -3.0, 3.0);
}

/// The fog's extinction at a point, 1/m: the layer's smooth profile (FishFogProfile) times its banks.
/// `at` is (x, altitude, z); `column` the layer over it (FishFogColumnAt); `footprint` how much of the
/// world the sample stands for, m (0 for the march, which reads the banks whole).
///
/// The banks are a log-normal factor with a mean of one about the physics' extinction, exp(σs − σ²/2)
/// for s of unit spread (FogLayerView.StructureFactor is its twin): they move the fog into thicker and
/// thinner patches without changing how much of it there is. The same factor over the whole column — the
/// body and the top alike, and no patchier at the top than in the body (it was twice as patchy there,
/// which speckled the top): nothing moves the top but the fade.
float FishFogDensity(float3 at, FishFogColumn column, float footprint)
{
    float profile = FishFogProfile(at.y, column);
    // Above the top's fade, or under the lifted base's: nothing to read the banks for.
    if (profile <= 0.0)
    {
        return 0.0;
    }
    float spread = _FishFogLayerShape.y;
    // The chilled air drains downhill before it condenses, so a fog forms first and thickest in the
    // hollows and thinnest on the rises — the more so the younger and patchier it is.
    float s = FishFogStructure(at, footprint) + clamp(column.hollow / max(5.0, _FishFogLayer.y), -1.0, 1.0);
    return _FishFogLayer.x * profile * exp(spread * s - 0.5 * spread * spread);
}

#endif
