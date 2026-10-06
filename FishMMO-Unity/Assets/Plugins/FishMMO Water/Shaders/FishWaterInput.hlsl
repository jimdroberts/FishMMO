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
	half _FoamSharpness;
	half _SurfStrength;
	half _SurfFoamOpacity;
	half _SurfFoamVeil;
	half _WhitecapThreshold;
	half _ShallowWhitecaps;

	// ── Shore ──
	half _EdgeFade;

	// ── Breakers ── (drawn by FishMMO/Water/Breaker, whose material copies this one's)
	half _BreakerHeight;
	half _BreakerCurl;
	half _BreakerFoam;
	half _BreakerEdgeFade;

	// ── Waves ──
	half _WaveFadeStart;
	half _WaveFadeEnd;

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
// it must agree about. A second shader drawing part of the sea (the breakers) includes this whole
// file, cbuffer and all, so its material is a copy of the ocean's and the two cannot drift apart. Globals do not disturb SRP batching — only material constants have to live
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
// How far apart the ocean mesh's vertices are: x the gap as a share of a ring's radius, y the inner
// radius (WaterSurface). Zero unset: the vertex stage then keeps every cascade.
float4 _FishWaterMeshSpacing;

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
/// What the ripples scroll by (WaterSurface.PublishRippleWindow): xy the wind's direction held still for a
/// window of the shared clock, z seconds into that window, w the window's length in seconds.
float4 _FishWaterRippleWindow;

/// <summary>
/// How far a texture scrolling at <paramref name="uvPerSecond"/> (tiles a second) has moved this window,
/// snapped per axis to whole tiles a window: at the window's end it has come round to where the next
/// window starts it, so the scroll is seamless and every player's is the same.
/// </summary>
/// <remarks>
/// For a scroll whose rate follows the wind. Rate × clock moves by the change in rate times the whole
/// clock whenever the rate changes — the eased wind scrubbed the ripples across the sea — and jumps at
/// the clock's wrap. A layer that moves less than half a tile a window does not move.
/// </remarks>
float2 FishWaterHeldScroll(float2 uvPerSecond)
{
	float window = max(1.0, _FishWaterRippleWindow.w);
	return round(uvPerSecond * window) * (_FishWaterRippleWindow.z / window);
}

// ── The shore ──────────────────────────────────────────────────────────
//
// R holds the metres of water over the ground: positive in the sea, negative on dry land; G the
// signed metres to the water's edge. Built at
// load from the scene's terrains by WaterShoreField. A zero-width rect means no shore in this
// scene — open ocean.
TEXTURE2D(_FishWaterShore);
SAMPLER(sampler_FishWaterShore);
float4 _FishWaterShoreRect;   // xy world minimum, zw size
float _FishWaterShoreTexel;    // metres per texel of the shore field

/// The foam the shore keeps (WaterShore): R left on the sand by the swash, G left on the water by the
/// breakers' whitewater. Over the shore field's rectangle. Read only while a shore keeps time.
TEXTURE2D(_FishWaterFoamMemory);
SAMPLER(sampler_FishWaterFoamMemory);

#endif
