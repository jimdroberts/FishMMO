#ifndef FISHMMO_WATER_INPUT_INCLUDED
#define FISHMMO_WATER_INPUT_INCLUDED

// Every material constant the water shader has, declared ONCE.
//
// The SRP Batcher requires that all passes of a shader declare an identical UnityPerMaterial
// layout, and it verifies that by comparing the blocks — so two passes that list the same
// properties in a different order silently drop the whole shader out of batching, with no error
// anywhere. Keeping the block in a header is the only arrangement that cannot drift.
//
// A second shader that needs the same globals (the underwater pass) must NOT include this file:
// a shader can only have one UnityPerMaterial, and Unity fills a constant buffer from the material
// only when it carries exactly that name.

CBUFFER_START(UnityPerMaterial)
	// ── Colour and absorption ──
	half4 _ShallowColor;
	half4 _DeepColor;
	half4 _ShoreColor;
	half4 _ScatterColor;
	half4 _FoamColor;
	half4 _WaterDensity;
	half _ShoreDepth;
	half _ScatterStrength;

	// ── Surface ──
	half _Smoothness;
	half _SpecularStrength;
	half _ReflectionStrength;
	half _MaxAlpha;

	// ── Normal maps ──
	half _NormalScaleA;
	half _NormalScaleB;
	half _NormalStrength;
	half _WaveSpeed;
	half _NormalFadeDistance;

	// ── Refraction ──
	half _RefractionStrength;

	// ── Foam ──
	half _FoamScale;
	half _FoamDistance;
	half _FoamSharpness;
	half _SurfStrength;
	half _WhitecapThreshold;

	// ── Shore ──
	half _EdgeFade;
	half _ShoreBreak;
	half _SwashMetres;
	half _ShoreRefraction;
	half _ShoreWaveLength;
	half _ShoreWaveHeight;
	half _ShoreWavePitch;
	half _ShoreWaveFoam;

	// ── Waves ──
	half _WaveFadeStart;
	half _WaveFadeEnd;
	half _GroupLength;

	// ── Variation ──
	half _Clarity;
	half _ClarityScale;

	// ── Development ──
	half _DebugView;
CBUFFER_END

TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
TEXTURE2D(_FoamTexture);
SAMPLER(sampler_FoamTexture);

// ── Globals, set by WaterSurface ───────────────────────────────────────
//
// Arrays cannot be material properties, and there is one sea per scene whose crests every piece of
// it must agree about. Globals do not disturb SRP batching — only material constants have to live
// in the block above.

/* The ocean, as three FFT cascades.
 *
 * Each holds the inverse transform of one band of the wave spectrum: displacement in xyz, and
 * slope plus folding in the derivative map. Summing three tiles of incommensurable size gives a
 * combined repeat period far beyond anything the eye can find, and lets each scale carry the
 * wavelengths it can actually resolve — swell in the big tile, chop in the small one. */
TEXTURE2D(_FishWaterDisplacement0);
TEXTURE2D(_FishWaterDisplacement1);
TEXTURE2D(_FishWaterDisplacement2);
TEXTURE2D(_FishWaterDerivatives0);
TEXTURE2D(_FishWaterDerivatives1);
TEXTURE2D(_FishWaterDerivatives2);
SAMPLER(sampler_FishWaterDisplacement0);

/// The three patch sizes in metres, and w the vertical scale.
float4 _FishWaterPatch;

float _FishWaterLevel;
/// The level with no tide on it: what the shore field was built against.
float _FishWaterMeanLevel;
/// Seconds, wrapped by the CPU. NOT _Time.y, which is a float counting from level load: after a
/// few hours of a session its resolution is coarser than a frame and the sea visibly judders.
float _FishWaterTime;
/// 1 under open sky, 0 fully under cloud. Driven by the weather system.
float _FishWaterCloudShadow;
/// The wind as a unit vector in world XZ, and z the speed in metres per second.
float4 _FishWaterWind;
/// 0 below a stiff breeze, 1 in a gale. Lifts the white-cap threshold.
float _FishWaterWhitecap;

// ── The shore ──────────────────────────────────────────────────────────
//
// R holds the metres of water over the ground: positive in the sea, negative on dry land. Built at
// load from the scene's terrains by WaterShoreField. A zero-width rect means no shore in this
// scene — open ocean.
TEXTURE2D(_FishWaterShore);
SAMPLER(sampler_FishWaterShore);
float4 _FishWaterShoreRect;   // xy world minimum, zw size
float _FishWaterShoreTexel;    // metres per texel of the shore field

#endif
