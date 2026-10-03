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

#include "FishSurface.hlsl"
#include "FishAmbient.hlsl"
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
CBUFFER_END

// Not per material: one value for every plant, from the terrains' draw distances.
float4 _FishVegetationFade;

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

// ── The plant as it stands today ──────────────────────────────────────

// Where a vertex is once the snow has buried it, the ground has taken its base and the wind has
// bent it, and its normal there; fade is how much of it the distance leaves (x) and its dither shift (y).
void VegDeform(VegAttributes input, out float3 positionWS, out half3 normalWS, out half4 tint, out half2 fade, out half4 ground)
{
    float3 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float3 positionOS = input.positionOS.xyz;
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
        positionWS = VegFaceCamera(positionOS, input.normalOS, originWS, normalWS);
    }
    else
    {
        positionWS = TransformObjectToWorld(positionOS);
        normalWS = TransformObjectToWorldNormal(input.normalOS);
    }

    // Set into the ground: the whole instance moves down in world space by a depth between
    // _GroundSink.x and .y drawn from its own hash (the scatter rule's sink range; Unity's detail
    // layers have no per-instance height), so its base is buried rather than standing on the height
    // surface, and neighbours do not all start at one plane. A translation, never a squash: the
    // blades keep their proportions and the wind's weights still pin the base.
    positionWS.y -= lerp(_GroundSink.x, _GroundSink.y, FishSurfaceHash(originWS.xz * 0.61 + 7.3));

    // Wind: the whole plant leans with the weather's wind and its gusts, and leaves tremble on
    // their own. Calm when there is no weather — every global here is zero until it is set.
    float time = _FishWeatherMisc.w;
    float2 dir = _FishWeatherWind.xy;
    float speed = _FishWeatherWind.z;
    float gust = FishWindGust(originWS.xz, time);
    float phase = FishSurfaceHash(originWS.xz * 0.23) * 6.2831853;
    float push = speed * (0.7 + 0.3 * sin(time * _WindFrequency + phase)) + gust;
    float3 offset = float3(dir.x, 0.0, dir.y) * (push * _WindSway * input.wind.x);
    float tremble = sin(time * _WindFrequency * 4.0 + dot(positionWS, float3(1.7, 2.3, 1.1)) + phase);
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
    output.color = input.color;
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
    VegClip(tex.a, input.tint.a, input.color.a);
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
    color.rgb += albedo * mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation * back * _Translucency * (half)(1.0 - distanceFar));

    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = 1.0;
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
