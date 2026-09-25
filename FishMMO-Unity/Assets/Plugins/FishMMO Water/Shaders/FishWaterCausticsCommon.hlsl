#ifndef FISHMMO_WATER_CAUSTICS_COMMON_INCLUDED
#define FISHMMO_WATER_CAUSTICS_COMMON_INCLUDED

// Caustics: sunlight focused onto whatever is under the water by the waves above it.
//
// Shared by the two places that have to draw them. The caustics pass lights what is under the
// water in the frame — seen from below, and through water that is BLENDED over the frame. But water
// that REFRACTS draws a copy of the frame taken before any transparent pass runs, the caustics pass
// included, so the sea lights the sea bed it refracts itself, with this same function. One source,
// so the light under the water does not change when the pipeline's opaque texture is switched on.
//
// NOT A PATTERN. A wave's surface is a lens: refracted into the water, a ray through the surface
// point x tilts toward the uphill side, so at depth d it lands at x + d·(1 - 1/n)·∇η(x), and by
// conservation of energy its brightness there is 1 / |det(I + d·(1 - 1/n)·H)|, where H is the
// surface's curvature, taken from the FFT's own slope maps. Under a crest H is negative, the
// determinant falls below one and the light converges — which is what makes crests, not troughs,
// the bright lines. A calm sea casts soft wide ripples, a choppy one sharp lines, and they move
// with the waves overhead.
//
// The includer declares the globals by these names — the sea already does in FishWaterInput.hlsl:
// _FishWaterDerivatives1, _FishWaterDerivatives2, _FishWaterPatch, _FishWaterLevel,
// _FishWaterCloudShadow — and URP's lighting.

SAMPLER(sampler_linear_repeat);
float4 _FishWaterCaustics;   // x strength (0 off), y clarity in metres, z gone by this far from the camera

static const float FishWaterRefractiveIndex = 1.333;

/// <summary>
/// The world position of whatever the depth buffer holds at a screen point.
/// </summary>
/// <remarks>
/// The depth convention depends on the API: a reversed buffer runs 1 near to 0 far, OpenGL 0 to 1
/// with a clip space of -1 to 1. Passed through unconverted on OpenGL — the Linux editor — every
/// point comes back about twice as far away as it is.
/// </remarks>
float3 FishWaterSceneWorldPosition(float2 screenUV, float rawDepth)
{
	#if UNITY_REVERSED_Z
		float ndcDepth = rawDepth;
	#else
		float ndcDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
	#endif
	return ComputeWorldSpacePosition(screenUV, ndcDepth, UNITY_MATRIX_I_VP);
}

/// <summary>One cascade's curvature at a point: (∂²η/∂x², ∂²η/∂z², ∂²η/∂x∂z).</summary>
/// <param name="reach">Metres either side the slopes are compared across; a texel at the least.</param>
/// <remarks>
/// The slope map's own differences. A difference taken across 2r is exactly the curvature averaged
/// over those 2r, so a wider reach is a blur that costs nothing: a texel for the caustics, which want
/// every ripple, and more for the shafts, whose march cannot resolve a ripple finer than its step.
/// </remarks>
float3 FishWaterCausticCurvature(TEXTURE2D_PARAM(derivatives, derivativeSampler), float2 xz, float patch, float reach)
{
	float step = max(patch / 256.0, reach);
	float2 uv = xz / patch;
	float2 across = float2(step / patch, 0.0);
	float2 along = float2(0.0, step / patch);
	float2 east = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv + across, 0).xy;
	float2 west = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv - across, 0).xy;
	float2 north = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv + along, 0).xy;
	float2 south = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv - along, 0).xy;
	return float3(east.x - west.x, north.y - south.y, north.x - south.x) / (2.0 * step);
}

/// <summary>
/// Where the sunlight reaching a point this deep came through the surface: back up the refracted
/// sun ray. A low sun's light enters well to the sun's side of what it lights.
/// </summary>
float2 FishWaterCausticEntry(float3 positionWS, float depth, float3 toSun)
{
	float sinIncident = length(toSun.xz);
	float sinRefracted = sinIncident / FishWaterRefractiveIndex;
	float tanRefracted = sinRefracted * rsqrt(max(1e-4, 1.0 - sinRefracted * sinRefracted));
	float2 sunward = sinIncident > 1e-4 ? toSun.xz / sinIncident : float2(0.0, 0.0);
	return positionWS.xz + sunward * max(0.0, depth) * tanRefracted;
}

/// <summary>
/// The lens: how bright the light is this deep under a surface of this curvature, 1 where the
/// surface is flat.
/// </summary>
/// <remarks>
/// Clamped away from zero: on a focal line the determinant passes through nothing and the brightness
/// through infinity, which a real caustic never reaches — the sun is a disc, not a point, and it
/// softens every focus.
/// </remarks>
float FishWaterCausticLens(float3 curvature, float depth)
{
	float lens = max(0.0, depth) * (1.0 - 1.0 / FishWaterRefractiveIndex);
	float determinant = (1.0 + lens * curvature.x) * (1.0 + lens * curvature.y) - lens * lens * curvature.z * curvature.z;
	return 1.0 / max(abs(determinant), 0.12);
}

/// <summary>
/// How strongly the waves focus sunlight onto a point this deep: 1 for the light a flat surface
/// would let through, more on a focal line, less between.
/// </summary>
/// <remarks>
/// The chop and the short swell do the focusing at the depths a caustic is seen through; the long
/// swell's curvature is too gentle to converge anything within tens of metres, so its cascade is
/// left out.
/// </remarks>
float FishWaterCausticFocus(float3 positionWS, float depth, float3 toSun)
{
	float2 entry = FishWaterCausticEntry(positionWS, depth, toSun);
	float3 curvature =
		FishWaterCausticCurvature(TEXTURE2D_ARGS(_FishWaterDerivatives1, sampler_linear_repeat), entry, _FishWaterPatch.y, 0.0)
		+ FishWaterCausticCurvature(TEXTURE2D_ARGS(_FishWaterDerivatives2, sampler_linear_repeat), entry, _FishWaterPatch.z, 0.0);
	return FishWaterCausticLens(curvature, depth);
}

/// <summary>
/// The same focus for a point in the open water rather than on the ground: what makes a shaft.
/// </summary>
/// <param name="reach">Metres to blur the surface's curvature over, about the march's step.</param>
/// <remarks>
/// A shaft is the light of a caustic seen in the water it passes through on its way down, so it is
/// the same lens along the same refracted ray. The chop's cascade alone: the swell's focuses a
/// hundred metres down and changes nothing a diver can see, and each step of the march is already
/// four taps.
/// </remarks>
float FishWaterShaftFocus(float3 positionWS, float depth, float3 toSun, float reach)
{
	float2 entry = FishWaterCausticEntry(positionWS, depth, toSun);
	float3 curvature = FishWaterCausticCurvature(
		TEXTURE2D_ARGS(_FishWaterDerivatives2, sampler_linear_repeat), entry, _FishWaterPatch.z, reach);
	return FishWaterCausticLens(curvature, depth);
}

/// <summary>
/// How much the light at a world point is scaled by the waves focusing it: 1 above the water or
/// with caustics off, more on the focal lines, less between them.
/// </summary>
/// <remarks>
/// One exit, not several: the compiler inlines this and warns about any early return (X4000).
/// </remarks>
half FishWaterCausticLight(float3 positionWS)
{
	half light = 1.0;
	float depth = _FishWaterLevel - positionWS.y;
	Light sun = GetMainLight();
	float3 toSun = sun.direction;
	if (_FishWaterCaustics.x > 0.0 && depth > 0.02 && toSun.y > 0.02)
	{
		float focus = FishWaterCausticFocus(positionWS, depth, toSun);

		/* How much of that reaches here to be seen. The light scatters and is absorbed on its way
		 * down, so the contrast dies with depth; the pattern is finer than a pixel a little way off,
		 * where it would only shimmer, so it fades with distance; it starts from nothing at the
		 * surface, where the lens has not yet converged anything; and it needs the sun, unshadowed
		 * and out from under the clouds. */
		half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
		half open = saturate(toSun.y * 3.0) * shadow * saturate(_FishWaterCloudShadow);
		half fadeDistance = max(1.0, _FishWaterCaustics.z);
		half far = 1.0 - smoothstep(fadeDistance * 0.5, fadeDistance, distance(_WorldSpaceCameraPos, positionWS));
		half strength = _FishWaterCaustics.x * exp(-depth / max(0.5, _FishWaterCaustics.y))
			* smoothstep(0.02, 0.3, depth) * open * far;

		light = max(0.0, 1.0 + (focus - 1.0) * strength);
	}
	return light;
}

#endif
