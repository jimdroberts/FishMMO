#ifndef FISHMMO_VEGETATION_PASSES_INCLUDED
#define FISHMMO_VEGETATION_PASSES_INCLUDED

// Grass, leaves, bark and billboards for the generated biome art (FishVegetation.shader).
//
// Everything that changes how they look at runtime comes from shader globals the client already
// sets — nothing on the wire, and nothing per instance from C# except _FishLodFade (FishLodFade.hlsl),
// which only the instanced tree channels write (or, in FishMMO/Vegetation Indirect's GPU-driven draws,
// their culling compute: FishIndirectInstancing.hlsl, which also supplies the object matrix):
//   _FishWeatherWind / _FishWeatherMisc.w  wind and the weather clock (WeatherShaderGlobals.Apply)
//   _FishCoverTex / _FishWeatherCover      snow, wet, ash, sand where it lies (the cover map; FishCoverAt)
//   _FishOcclusion*                        whether the sky reaches here (FishSurfaceExposure)
//   _FishSeason                            the season and the drought (WeatherShaderGlobals.ApplySeason)
//   _FishAmbient*                          the sky's ambient, where probes give none (FishTrilight)
//   _FishVegetationFade                    the distance fades: xy detail start/end, zw tree start/end
//                                          (metres; VegetationDistanceFade, from the terrains' draw
//                                          distances; zero = no fade)
//   _FishGroundColour / _FishGroundColourRect / _FishGroundColourParams
//                                          the colour of the ground, from the terrains' splat weights
//                                          and their layers' average colours (GroundColourMap); the
//                                          tintable parts take its hue (Params.w) and, at the root,
//                                          the colour itself (Params.z, fading by Shape.x metres up),
//                                          so a plant grows out of the ground it stands on.
//                                          Params.x = 0 (not set: edit mode, no terrain) = off.
// The ground reads the same cover through FishWeatherSurface, and foliage through FishWeatherFoliage,
// both built from FishCoverLook / FishCoverAmount / FishDampen, so a meadow and its grass agree.
//
// Vertex data the generators write (ProceduralArt/VegetationMeshes.cs, TreeMeshes.cs):
//   COLOR.rgb   the part's own colour (species leaf colour, petal colour, base darkening)
//   COLOR.a     1 where the healthy/dry/autumn tint applies (leaves, blades), 0 where it does not (bark)
//   TEXCOORD1.x sway: how far this vertex moves with the whole plant (0 at the root)
//   TEXCOORD1.y flutter: how much it trembles on its own (leaves 1, trunks 0)
//   TEXCOORD2   the part this vertex belongs to (MeshBuilder.SetPart): xyz where it attaches, w its hash
//   TEXCOORD3   x the card's hash, y the part's keep rank, z what it is (PlantPart: 0 fixed, 1 limb,
//               2 twig, 3 leaf, 4 frond), w the card's keep rank. Meshes without them read zero there:
//               fixed, never dropped or turned (VegVary).

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"
// The sea's surface and surge, for plants with _Aquatic set (kelp, corals, sea fans).
#include "FishSea.hlsl"
// The instanced tree channels' per-instance LOD cross-fade (_FishLodFade; contract in the file).
#include "FishLodFade.hlsl"

#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    half _BumpScale;
    half _Smoothness;
    half _Translucency;
    half _BackfaceFlip;
    half _WindSway;
    half _WindFlutter;
    half _WindFrequency;
    half4 _HealthyColor;
    half4 _DryColor;
    half _TintSpread;
    half _SnowBury;
    half _Deciduous;
    half4 _AutumnColor;
    half _FishWeatherAmount;
    half _DistanceFade;
    half _FacingCamera;
    half4 _GroundSink;
    float _TintPatchMetres;
    half _VaryLean;
    half _VaryTwist;
    half _VaryCrown;
    half _VaryHeight;
    half _VaryGirth;
    half _VaryPartSwing;
    half _VaryPartDroop;
    half _VaryPartLength;
    half4 _VaryFullness;
    half4 _VaryColour;
    half _Aquatic;
CBUFFER_END

// Not per material: one value for every plant, from the terrains' draw distances.
float4 _FishVegetationFade;
// 1 while the camera's target is multisampled (VegetationDistanceFade, from the pipeline's MSAA): leaf and
// needle edges are then drawn by alpha-to-coverage — a soft, antialiased fringe — instead of cut at the
// cutoff, which drew every card as a hard paper cut-out up close. 0: the plain cut.
float _FishVegetationAlphaToCoverage;

// The ground colour map (GroundColourMap): xy the map's world xz origin, zw one over its xz size.
#include "FishGroundColour.hlsl"

/* Globals, not material properties: materials generated before these existed read 0 for them, not the
 * shader's defaults, so a per-material strength silently switched the whole effect off. One scene-wide
 * setting (GroundColourMap.RootBlend / PlantBlend / BlendHeight / CrownStart / CrownEnd); the vertex colour's alpha still keeps it
 * off petals and bark. */

// The ground's colour under a plant (rgb), and how much of it this vertex takes (a): Params.z at the
// root, easing to Params.w by Shape.x metres up and above — the whole plant shares the ground's colour,
// the root matches it outright. (A hue-only shift kept the blades' brightness, and pale blades over
// dark red earth still read as pale grass: the colour has to come with its value.) It fades out between
// Shape.y and Shape.z metres up, so grass and shrubs take the ground's colour while a tree's crown, high
// above the soil, keeps its own; bark and petals are excluded by the tint mask.
half4 VegGroundColour(float3 originWS, float heightAboveRoot, out half coverage)
{
    coverage = 0.0;
    half amount = (half)_FishGroundColourParams.z;
    if ((half)_FishGroundColourParams.x <= 0.0)
    {
        return half4(0.0, 0.0, 0.0, 0.0);
    }
    float2 uv = (originWS.xz - _FishGroundColourRect.xy) * _FishGroundColourRect.zw;
    // Alpha is coverage: 0 where the map knows nothing (no terrain read there).
    half4 ground = SAMPLE_TEXTURE2D_LOD(_FishGroundColour, sampler_FishGroundColour, uv, 0.0);
    half up = saturate(heightAboveRoot / max(0.05, _FishGroundColourShape.x));
    half root = (1.0 - up) * (1.0 - up);
    // Outside the map (beyond every terrain) nothing is known: no blend.
    half inside = all(uv >= 0.0) && all(uv <= 1.0) ? 1.0 : 0.0;
    coverage = inside * ground.a;
    half low = 1.0 - saturate((heightAboveRoot - _FishGroundColourShape.y) / max(0.05, _FishGroundColourShape.z - _FishGroundColourShape.y));
    return half4(ground.rgb, lerp((half)_FishGroundColourParams.w, amount, root) * low * coverage);
}

struct VegAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    half4 color : COLOR;
    float2 uv : TEXCOORD0;
    float2 wind : TEXCOORD1;
    float4 partPivot : TEXCOORD2;
    float4 partData : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// ── The season ─────────────────────────────────────────────────────────

// The colour multiplier a plant takes from the season and the drought (rgb), and how bare a
// deciduous plant's branches are (a, 0 in leaf .. 1 bare). Read from where the plant stands, so a
// plant keeps its colour as it sways.
//
// Mostly in PATCHES, not plant by plant: real ground dries out and greens up by its moisture and
// soil depth, which change over metres, so a meadow browns in swathes with greener hollows between
// them — the look Unity's own detail "noise spread" gave and a pure per-plant hash loses (it reads
// as salt and pepper). World-space value noise at _TintPatchMetres sets the patch; a quarter of
// each plant's own hash keeps neighbours inside a patch from matching exactly. 0 patch size is the
// old per-plant behaviour.
float VegTintVariation(float3 originWS)
{
    float own = FishSurfaceHash(originWS.xz * 0.37 + 0.5);
    if (_TintPatchMetres <= 0.0)
    {
        return own;
    }
    float2 p = originWS.xz / _TintPatchMetres;
    float patch = FishSurfaceNoise(p) * 0.65 + FishSurfaceNoise(p * 2.13 + 17.3) * 0.35;
    // Two octaves of value noise bunch toward the middle; stretched back out so the patches reach
    // both ends of the healthy-to-dry range rather than all reading half-dry.
    patch = saturate((patch - 0.5) * 1.7 + 0.5);
    return lerp(patch, own, 0.25);
}

half4 VegSeasonTint(float3 originWS)
{
    float variation = VegTintVariation(originWS);
    float strength = _FishSeason.w;
    float known = step(1e-4, strength);
    float phase = _FishSeason.x;

    // Winter browns what grows; a drier year than usual browns it more, and does so in any season.
    float dormant = saturate(-_FishSeason.y) * 0.7;
    float drought = saturate(-_FishSeason.z * 3.0);
    // _TintSpread is how far apart neighbours start (Unity's "noise spread"): 0 all alike, 1 the
    // whole healthy-to-dry range across a patch.
    float dry = saturate(variation * _TintSpread + max(dormant, drought) * known);
    half3 tint = lerp(_HealthyColor.rgb, _DryColor.rgb, dry);

    // Autumn turns broadleaf crowns a little after the equinox; the deepest winter strips them.
    float autumn = saturate(1.0 - abs(phase - (0.8 + variation * 0.06)) / 0.1) * strength * known;
    float fromWinter = abs(phase - 0.02);
    fromWinter = min(fromWinter, 1.0 - fromWinter);
    float bare = saturate((0.16 - fromWinter + (variation - 0.5) * 0.04) / 0.06) * strength * known;
    tint = lerp(tint, tint * _AutumnColor.rgb, autumn * _Deciduous);
    return half4(tint, bare * _Deciduous);
}

// ── Distance: fading out before the terrain drops the plant ───────────

// How much of the plant is drawn, 1 .. 0, by its distance from the camera and the material's fade
// class (_DistanceFade: 0 none, 1 detail, 2 tree). The terrain culls a detail by whole patches at its
// detail distance and a tree at its tree distance; both bands end inside those (VegetationDistanceFade
// works out where), so by the time the terrain drops a patch or a tree there is nothing left to pop.
// Measured from the plant's origin, so a whole plant fades together.
half VegDistanceVisible(float3 originWS)
{
    float2 band = _DistanceFade > 1.5 ? _FishVegetationFade.zw : _FishVegetationFade.xy;
    if (_DistanceFade < 0.5 || band.y <= band.x)
    {
        return 1.0;
    }
    float distanceWS = distance(originWS, _WorldSpaceCameraPos);
    return (half)(1.0 - saturate((distanceWS - band.x) / (band.y - band.x)));
}

// The dissolve: the shared 4×4 screen-door (FishDitherClip, as the day/night fade uses), its
// pattern shifted per instance so a meadow fading together does not clip the same pixels in every
// plant. fade.x is how much is drawn, fade.y the instance's shift (0..15).
void VegFadeClip(float4 positionCS, half2 fade)
{
    float2 shift = float2(fmod(fade.y, 4.0), floor(fade.y * 0.25));
    FishDitherClip(positionCS.xy + shift, fade.x);
}

// A tree billboard turned to face the camera about the tree's up axis (cylindrical billboarding):
// object x goes to the camera's right, object -z toward the camera, y stays up. The quad is
// BillboardImpostor.FacingQuad; the scale is the instance's own (terrain trees vary width and height).
float3 VegFaceCamera(float3 positionOS, float3 normalOS, float3 originWS, out half3 normalWS)
{
    float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
    float2 toCamera = _WorldSpaceCameraPos.xz - originWS.xz;
    float len = length(toCamera);
    float2 f2 = len > 1e-4 ? toCamera / len : float2(0.0, -1.0);
    float3 toward = float3(f2.x, 0.0, f2.y);
    float3 up = float3(0.0, 1.0, 0.0);
    float3 right = cross(toward, up);
    normalWS = (half3)normalize(right * normalOS.x + up * normalOS.y - toward * normalOS.z);
    return originWS + right * (positionOS.x * scaleX) + up * (positionOS.y * scaleY) - toward * (positionOS.z * scaleX);
}

// ── Every plant its own ───────────────────────────────────────────────
//
// One mesh is drawn for every tree of a species and every clump of a grass, so all of them were the same
// plant, turned and resized. Each one now draws its own figures from a hash of where it is rooted — the
// same plant every frame and in every pass, so its shadow is its own — and is reshaped in object space
// before anything else happens to it:
//   - how full it is: the generators grow spare limbs and leaf cards and rank them (TreeMeshes,
//     VegetationMeshes); a plant keeps those its fullness reaches and collapses the rest to their pivots,
//     where they draw nothing — one tree sparse, the next lush;
//   - each kept part swung about its foot, drooped or raised, and lengthened or shortened, by a hash of the
//     part and the plant together: no two neighbours hold their limbs alike;
//   - the whole plant taller or shorter, its crown wider or narrower (more so further up, so the foot stays
//     put), twisted about its trunk and leant a little off true;
//   - and its colour (VegVaryColour): its own brightness and hue, each leaf card a little its own.
// All of it scaled by the material (_Vary*: the generator sets them by form); a material made before
// these existed reads 0 and its plants are as they were.

uint VegMix(uint h)
{
    h ^= h >> 16;
    h *= 0x7feb352du;
    h ^= h >> 15;
    h *= 0x846ca68bu;
    h ^= h >> 16;
    return h;
}

/// The plant's own key, from where it is rooted (to a sixty-fourth of a metre).
uint VegPlantKey(float3 originWS)
{
    int2 q = int2(floor(originWS.xz * 64.0));
    return VegMix(asuint(q.x) * 73856093u ^ asuint(q.y) * 19349663u ^ 0x5bd1e995u);
}

/// A value in [0, 1) from a key and a salt; -1..1 from VegSigned.
float VegRandom(uint key, uint salt)
{
    return (VegMix(key ^ (salt * 0x9e3779b9u)) >> 8) * (1.0 / 16777216.0);
}

float VegSigned(uint key, uint salt)
{
    return VegRandom(key, salt) * 2.0 - 1.0;
}

/// A vector turned about a unit axis by an angle (radians): Rodrigues' formula.
float3 VegRotate(float3 v, float3 axis, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

/// Reshapes this plant's vertex in object space (root at the origin, y up). False when the part it belongs
/// to is one this plant does not grow: the vertex is then put on its part's pivot, and the part draws nothing.
bool VegVary(inout float3 positionOS, inout float3 normalOS, float4 partPivot, float4 partData, float3 originWS)
{
    uint plant = VegPlantKey(originWS);
    float kind = partData.z;
    if (kind > 0.5)
    {
        // How full this plant is: a share of the spares between the material's fewest and all of them.
        float limbs = lerp(saturate(_VaryFullness.x), 1.0, VegRandom(plant, 1u));
        float leaves = lerp(saturate(_VaryFullness.y), 1.0, VegRandom(plant, 2u));
        bool leafCard = kind > 2.5 && kind < 3.5;
        if (partData.y > limbs + 1e-4 || (leafCard && partData.w > leaves + 1e-4))
        {
            positionOS = partPivot.xyz;
            return false;
        }
        // This part, on this plant: swung about its foot, drooped or raised, lengthened or shortened.
        uint part = VegMix(plant ^ (uint)(partPivot.w * 16777215.0) * 0x85ebca6bu);
        float3 arm = positionOS - partPivot.xyz;
        float swing = radians(VegSigned(part, 3u) * _VaryPartSwing);
        arm = VegRotate(arm, float3(0.0, 1.0, 0.0), swing);
        normalOS = VegRotate(normalOS, float3(0.0, 1.0, 0.0), swing);
        float2 flat = arm.xz;
        float reach = length(flat);
        if (reach > 1e-3)
        {
            // About the horizontal line across the part where it leaves its pivot: down or up.
            float3 across = float3(-flat.y, 0.0, flat.x) / reach;
            float droop = radians(VegSigned(part, 4u) * _VaryPartDroop);
            arm = VegRotate(arm, across, droop);
            normalOS = VegRotate(normalOS, across, droop);
        }
        arm *= 1.0 + VegSigned(part, 5u) * _VaryPartLength;
        positionOS = partPivot.xyz + arm;
    }
    if (_FacingCamera > 0.5)
    {
        // A billboard is a picture of the tree already: only its colour is the plant's own.
        return true;
    }
    // Its trunk thickened or thinned about its own centre line (partPivot), the taller of a species the thicker;
    // the limbs start on that line, inside the trunk, so nothing comes away from it.
    if (kind < 0.5 && partData.w > 0.5)
    {
        float girth = 1.0 + VegSigned(plant, 11u) * _VaryGirth + 0.5 * VegSigned(plant, 6u) * _VaryHeight;
        positionOS = partPivot.xyz + (positionOS - partPivot.xyz) * max(0.5, girth);
    }
    // The whole plant: its height, its crown, its twist about the trunk and its lean off true.
    positionOS.y *= 1.0 + VegSigned(plant, 6u) * _VaryHeight;
    float up = max(0.0, positionOS.y);
    positionOS.xz *= 1.0 + VegSigned(plant, 7u) * _VaryCrown * saturate(up / 3.0);
    float twist = radians(VegSigned(plant, 8u) * _VaryTwist * saturate(up / 10.0));
    positionOS = VegRotate(positionOS, float3(0.0, 1.0, 0.0), twist);
    normalOS = VegRotate(normalOS, float3(0.0, 1.0, 0.0), twist);
    float leanToward = VegRandom(plant, 9u) * 6.2831853;
    float3 leanAxis = float3(cos(leanToward), 0.0, sin(leanToward));
    float lean = radians(VegRandom(plant, 10u) * _VaryLean);
    positionOS = VegRotate(positionOS, leanAxis, lean);
    normalOS = VegRotate(normalOS, leanAxis, lean);
    return true;
}

/// This plant's colour, as a multiplier on its vertex colour: its own brightness, hue and saturation on what
/// takes the season's tint (leaves, blades: COLOR.a 1), each leaf card a little its own, and a brightness
/// of its own on the bark.
half3 VegVaryColour(float3 originWS, half tintable, float4 partData)
{
    uint plant = VegPlantKey(originWS);
    half value = 1.0 + VegSigned(plant, 11u) * _VaryColour.x;
    // Hue and saturation as a shift of the three channels about their mean: toward yellow, toward blue-green,
    // richer or greyer.
    half3 shift = half3(VegSigned(plant, 12u), VegSigned(plant, 13u), VegSigned(plant, 14u)) * _VaryColour.y;
    shift -= (shift.x + shift.y + shift.z) / 3.0;
    half card = 1.0 + (partData.x > 0.0 ? (half)((partData.x * 2.0 - 1.0) * _VaryColour.z) : 0.0);
    half3 leaf = max(0.0, value * card * (1.0 + shift));
    half bark = 1.0 + VegSigned(plant, 15u) * _VaryColour.w;
    return lerp(bark.xxx, leaf, tintable);
}

// ── The plant as it stands today ──────────────────────────────────────

// Where a vertex is once the snow has buried it, the ground has taken its base and the wind has
// bent it, and its normal there; fade is how much of it the distance leaves (x) and its dither shift (y).
void VegDeform(VegAttributes input, out float3 positionWS, out half3 normalWS, out half4 tint, out half2 fade, out half4 ground)
{
    float3 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 positionOS = input.positionOS.xyz;
    float3 normalOS = input.normalOS;
    // This plant's own shape, before the snow, the distance and the wind (VegVary).
    VegVary(positionOS, normalOS, input.partPivot, input.partData, originWS);
    // Height above the root as authored (before snow, distance shrink and wind), in world metres.
    half groundCoverage;
    ground = VegGroundColour(originWS, TransformObjectToWorld(input.positionOS.xyz).y - originWS.y, groundCoverage);
    // Debug view: every plant wholly the colour its root reads, magenta where it reads nothing (map
    // off, outside it, or no coverage) — which link of the chain is broken, at a glance.
    if (_FishGroundColourParams.y > 0.5 && _FishGroundColourParams.y < 1.5)
    {
        ground = half4(groundCoverage > 0.0 ? ground.rgb : half3(1.0, 0.0, 1.0), 1.0);
    }
    half visible = VegDistanceVisible(originWS);

    // Deep snow buries low plants: they sink into it rather than stand on top of it. The same cover
    // the ground is whitened by, read where the plant is rooted.
    if (_SnowBury > 0.0 && _FishWeatherAmount > 0.0)
    {
        float snow = saturate(FishCoverAt(originWS).x) * FishSurfaceExposure(originWS);
        positionOS.y *= 1.0 - saturate(snow * 1.4) * _SnowBury * _FishWeatherAmount;
    }

    // A detail shrinks toward its root as it dissolves, so the last of a patch is low stubble, not
    // full-height plants with holes in them.
    if (_DistanceFade > 0.5 && _DistanceFade < 1.5)
    {
        positionOS *= lerp(0.35, 1.0, visible);
    }

    if (_FacingCamera > 0.5)
    {
        positionWS = VegFaceCamera(positionOS, normalOS, originWS, normalWS);
    }
    else
    {
        positionWS = TransformObjectToWorld(positionOS);
        normalWS = TransformObjectToWorldNormal(normalOS);
    }

    // Set into the ground: the whole instance moves down in world space by a depth between
    // _GroundSink.x and .y drawn from its own hash (the scatter rule's sink range; Unity's detail
    // layers have no per-instance height), so its base is buried rather than standing on the height
    // surface, and neighbours do not all start at one plane. A translation, never a squash: the
    // blades keep their proportions and the wind's weights still pin the base.
    positionWS.y -= lerp(_GroundSink.x, _GroundSink.y, FishSurfaceHash(originWS.xz * 0.61 + 7.3));

    /* Land plants do not grow under the sea, whatever a scene's scatter wrote there: one rooted below the lowest tide
     * collapses to its root and draws nothing (the sea floor's own plants are _Aquatic). */
    if (_Aquatic < 0.5 && FishSeaPresent() && originWS.y < FishSeaCeiling())
    {
        positionWS = originWS;
        tint = VegSeasonTint(originWS);
        fade = half2(0.0, 0.0);
        return;
    }

    if (_Aquatic > 0.5)
    {
        /* Under the sea there is no wind: the swell's surge rocks the plant back and forth (its sway
         * weight is how far each vertex goes with it, 0 at the holdfast), fronds shiver on their own,
         * and nothing breaks the surface — what would rise past the low-tide ceiling is eased under it
         * and the length it loses is laid along the surge instead, the way a kelp canopy spreads under
         * the water. */
        float lag = FishSurfaceHash(originWS.xz * 0.23) * 0.3 + input.wind.x * 0.04;
        float2 surge = FishSeaSurge(positionWS, lag);
        float3 drift = float3(surge.x, 0.0, surge.y) * (_WindSway * input.wind.x);
        drift += normalWS * (FishSeaFlutter(positionWS, lag) * _WindFlutter * input.wind.y);
        // A bent stem is no longer: what moves sideways comes down a little, as in the wind.
        drift.y -= dot(drift.xz, drift.xz) * 0.5 / max(0.5, input.wind.x * 4.0);
        positionWS += drift;
        float lowered = FishSeaKeepUnder(positionWS, 0.6);
        float2 lay = dot(surge, surge) > 1e-6 ? normalize(surge) : float2(0.7071, 0.7071);
        positionWS.xz += lay * (lowered * 0.8);
        tint = VegSeasonTint(originWS);
        fade = half2(visible, floor(FishSurfaceHash(originWS.xz * 1.37 + 3.1) * 15.999));
        return;
    }

    // Wind: the whole plant leans with the weather's wind and its gusts, and leaves tremble on
    // their own. Calm when there is no weather — every global here is zero until it is set.
    float time = _FishWeatherMisc.w;
    float2 dir = _FishWeatherWind.xy;
    float speed = _FishWeatherWind.z;
    float gust = FishWindGust(originWS.xz, time);
    float phase = FishSurfaceHash(originWS.xz * 0.23) * 6.2831853;
    // Snapped to whole turns in the clock's wrap (FishWrapAngular), so the wrap is seamless.
    float sway = FishWrapAngular(_WindFrequency);
    float push = speed * (0.7 + 0.3 * sin(time * sway + phase)) + gust;
    float3 offset = float3(dir.x, 0.0, dir.y) * (push * _WindSway * input.wind.x);
    float tremble = sin(time * sway * 4.0 + dot(positionWS, float3(1.7, 2.3, 1.1)) + phase);
    offset += normalWS * (tremble * _WindFlutter * input.wind.y * (0.15 + speed + gust));
    // A bent stem is no longer: what moves sideways comes down a little, so tips do not stretch.
    offset.y -= dot(offset.xz, offset.xz) * 0.5 / max(0.5, input.wind.x * 4.0);
    positionWS += offset;

    tint = VegSeasonTint(originWS);
    fade = half2(visible, floor(FishSurfaceHash(originWS.xz * 1.37 + 3.1) * 15.999));
}

// Alpha test, with the leaves of a bare deciduous plant clipped away. Only where the tint applies:
// the bark stays.
void VegClip(half alpha, half bare, half tintable)
{
#if defined(_ALPHATEST_ON)
    clip(alpha * _BaseColor.a - lerp(_Cutoff, 1.01, bare * tintable));
#endif
}

#if defined(FISH_VEG_PASS_FORWARD) || defined(FISH_VEG_PASS_DEPTHNORMALS)

struct VegVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half4 color : TEXCOORD4;
    half4 tint : TEXCOORD5;
    half fogFactor : TEXCOORD6;
    half2 fade : TEXCOORD7; // x drawn share by distance, y dither shift
    half4 ground : TEXCOORD8; // rgb the ground's colour under the plant, a how much this vertex takes
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

VegVaryings VegVertex(VegAttributes input)
{
    VegVaryings output = (VegVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS;
    half3 normalWS;
    half4 tint;
    half2 fade;
    half4 ground;
    VegDeform(input, positionWS, normalWS, tint, fade, ground);
    output.fade = fade;
    output.ground = ground;
    output.positionWS = positionWS;
    output.normalWS = normalWS;
    // A billboard's tangent is not turned with it; billboards carry no normal map, so nothing reads it.
    output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.color = half4(input.color.rgb * VegVaryColour(TransformObjectToWorld(float3(0.0, 0.0, 0.0)), input.color.a, input.partData), input.color.a);
    output.tint = tint;
    output.positionCS = TransformWorldToHClip(positionWS);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}

// The shaded normal. Cards and blades carry normals bent toward the crown or the sky for soft
// lighting, and those must not be flipped on the back face (the back of a card would then face
// into the crown and go dark); flat parts flip, by the material's choice.
half3 VegNormal(VegVaryings input, half facing)
{
    half3 n = normalize(input.normalWS);
#if defined(_NORMALMAP)
    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    half3 t = normalize(input.tangentWS.xyz);
    half3 b = input.tangentWS.w * cross(n, t);
    n = normalize(normalTS.x * t + normalTS.y * b + normalTS.z * n);
#endif
    return n * lerp(1.0, facing, _BackfaceFlip);
}

#endif

#if defined(FISH_VEG_PASS_FORWARD)

half4 VegForwardFragment(VegVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    half coverage = 1.0;
#if defined(_ALPHATEST_ON)
    if (_FishVegetationAlphaToCoverage > 0.5)
    {
        // The cut drawn as a ramp one pixel wide about the cutoff (the alpha's own screen gradient), turned into
        // MSAA coverage by the hardware (AlphaToMask in the pass): the edge is antialiased like geometry. Only what
        // is plainly outside is clipped; a bare deciduous plant's leaves still drop out whole.
        half a = tex.a * _BaseColor.a;
        half cutoff = lerp(_Cutoff, 1.01, input.tint.a * input.color.a);
        coverage = saturate((a - cutoff) / max(fwidth(a), 1e-4) + 0.5);
        clip(coverage - 0.01);
    }
    else
    {
        VegClip(tex.a, input.tint.a, input.color.a);
    }
#endif
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif
    FishLodFadeClip(input.positionCS);
    VegFadeClip(input.positionCS, input.fade);

    half3 albedo = tex.rgb * _BaseColor.rgb * input.color.rgb;
    albedo *= lerp(half3(1.0, 1.0, 1.0), input.tint.rgb, input.color.a);
    // The ground's colour, on the tintable parts only (blades and leaves; not petals or bark). The blade
    // keeps its own light and dark (texture detail, the darker root of its vertex colour) as a ±25%
    // brightness around the ground's, so it shares the soil's colour without turning into a flat sticker.
    half debugView = (half)_FishGroundColourParams.y;
    half groundMix = input.ground.a * (debugView > 0.5 && debugView < 1.5 ? 1.0 : input.color.a);
    // The blade's own light and dark, kept as darkening only (never lighter than the ground): lit
    // grass already reads lighter than the soil it is the colour of.
    half bladeValue = saturate(dot(tex.rgb * input.color.rgb, half3(0.2126, 0.7152, 0.0722)) * 1.6);
    half3 groundAlbedo = debugView > 0.5 && debugView < 1.5 ? input.ground.rgb
        : input.ground.rgb * (1.0 - (half)_FishGroundColourShape.w * (1.0 - bladeValue));
    albedo = lerp(albedo, groundAlbedo, groundMix);
    if (debugView > 1.5)
    {
        albedo = groundMix.xxx;   // debug 2: how much ground colour the normal blend gives (white all, black none)
    }
    half3 normalWS = VegNormal(input, IS_FRONT_VFACE(face, 1.0, -1.0));
    half smoothness = _Smoothness;

    if (_FishWeatherAmount > 0.0)
    {
        half3 weatheredAlbedo = albedo;
        half3 weatheredNormal = normalWS;
        half weatheredSmoothness = smoothness;
        float cover;
        FishWeatherFoliage(input.positionWS, weatheredAlbedo, weatheredNormal, weatheredSmoothness, cover);
        albedo = lerp(albedo, weatheredAlbedo, _FishWeatherAmount);
        smoothness = lerp(smoothness, weatheredSmoothness, _FishWeatherAmount);
    }
    // Distant plants and trees settle into the landscape (FishGroundColour.hlsl).
    float distanceFar, flatten;
    float3 flattenTo;
    half4 pull;
    FishDistanceBlend(input.positionWS, distanceFar, flatten, flattenTo, pull);
    normalWS = normalize(lerp(normalWS, (half3)flattenTo, (half)flatten));
    albedo = lerp(albedo, pull.rgb, pull.a);
    // Far away the leaves' gloss and glow-through go too: on a billboard seen toward the sun they read as pale cut-outs.
    smoothness *= (half)(1.0 - distanceFar);

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    // No gloss where the shaded normal faces away from the eye. A two-sided card keeps its front's bent normal
    // on its back face (so the back is not lit as the inside of the crown), and seen from behind that normal
    // points away: URP takes the angle as grazing, the Fresnel term goes to one, and the back of every leaf
    // reflected the sky brighter than its own lit colour — the glow on crowns and meadows seen with the sun
    // behind them. The gloss is faded out as the normal turns away instead.
    smoothness *= saturate(dot(normalWS, inputData.viewDirectionWS) * 4.0 + 0.5);
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
    inputData.shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#else
    inputData.shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
#endif
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);
    // Light probes where there are any; the sky's own ambient where there are none. Terrain trees and
    // details are drawn instanced by the terrain, and an instanced draw is not guaranteed probe
    // coefficients — SampleSH reads zero there, as it did for the rain (FishAmbient.hlsl).
    inputData.bakedGI = max(SampleSH(normalWS), FishTrilight(normalWS));

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = 0.0;
    surface.specular = half3(0.0, 0.0, 0.0);
    surface.smoothness = smoothness;
    surface.occlusion = 1.0;
    surface.emission = half3(0.0, 0.0, 0.0);
    surface.alpha = 1.0;
    surface.normalTS = half3(0.0, 0.0, 1.0);

    half4 color = UniversalFragmentPBR(inputData, surface);

    // Light through a leaf: thin foliage glows when the sun is behind it.
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    half back = saturate(dot(inputData.viewDirectionWS, -mainLight.direction));
    back *= back;
    back *= back;
    // Only through the side away from the sun: light comes through a leaf from behind it. Added to a face the
    // sun already lights fully, it doubled the light on every leaf seen toward the sun (the grass blades gate it
    // the same way).
    back *= saturate(-dot(normalWS * IS_FRONT_VFACE(face, 1.0, -1.0), mainLight.direction) * 2.0 + 0.3);
    color.rgb += albedo * mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation * back * _Translucency * (half)(1.0 - distanceFar));

    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = coverage;
    return color;
}

#endif

#if defined(FISH_VEG_PASS_DEPTHNORMALS)

half4 VegDepthNormalsFragment(VegVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    VegClip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a, input.tint.a, input.color.a);
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif
    FishLodFadeClip(input.positionCS);
    VegFadeClip(input.positionCS, input.fade);
    float3 normalWS = VegNormal(input, IS_FRONT_VFACE(face, 1.0, -1.0));
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(normalize(normalWS));
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
#endif
}

#endif

#if defined(FISH_VEG_PASS_SHADOW) || defined(FISH_VEG_PASS_DEPTH)

struct VegDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half2 drop : TEXCOORD1; // x bare, y tintable
    half2 fade : TEXCOORD2; // x drawn share by distance, y dither shift
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

#if defined(FISH_VEG_PASS_SHADOW)
// Set by URP's ShadowUtils for the caster being rendered.
float3 _LightDirection;
float3 _LightPosition;
#endif

VegDepthVaryings VegDepthVertex(VegAttributes input)
{
    VegDepthVaryings output = (VegDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS;
    half3 normalWS;
    half4 tint;
    half2 fade;
    half4 ground;
    VegDeform(input, positionWS, normalWS, tint, fade, ground);
    output.fade = fade;
#if defined(FISH_VEG_PASS_SHADOW)
    // The shadow moves with the plant: the same bend, the same burial.
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
#else
    output.positionCS = TransformWorldToHClip(positionWS);
#endif
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.drop = half2(tint.a, input.color.a);
    return output;
}

half4 VegDepthFragment(VegDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    VegClip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a, input.drop.x, input.drop.y);
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif
    FishLodFadeClip(input.positionCS);
    VegFadeClip(input.positionCS, input.fade);
    return half4(input.positionCS.z, 0.0, 0.0, 0.0);
}

#endif

#endif
