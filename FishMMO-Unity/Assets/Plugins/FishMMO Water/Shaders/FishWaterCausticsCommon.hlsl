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
/// <remarks>
/// The slope map's own differences, a texel apart. The chop and the short swell do the focusing at
/// the depths a caustic is seen through; the long swell's curvature is too gentle to converge
/// anything within tens of metres, so its cascade is left out.
/// </remarks>
float3 FishWaterCausticCurvature(TEXTURE2D_PARAM(derivatives, derivativeSampler), float2 xz, float patch)
{
	float texel = patch / 256.0;
	float2 uv = xz / patch;
	float2 across = float2(1.0 / 256.0, 0.0);
	float2 along = float2(0.0, 1.0 / 256.0);
	float2 east = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv + across, 0).xy;
	float2 west = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv - across, 0).xy;
	float2 north = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv + along, 0).xy;
	float2 south = SAMPLE_TEXTURE2D_LOD(derivatives, derivativeSampler, uv - along, 0).xy;
	return float3(east.x - west.x, north.y - south.y, north.x - south.x) / (2.0 * texel);
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
		/* Where the light that lands here came through the surface: back up the refracted sun ray.
		 * A low sun's light enters well to the sun's side of what it lights. */
		float sinIncident = length(toSun.xz);
		float sinRefracted = sinIncident / FishWaterRefractiveIndex;
		float tanRefracted = sinRefracted * rsqrt(max(1e-4, 1.0 - sinRefracted * sinRefracted));
		float2 sunward = sinIncident > 1e-4 ? toSun.xz / sinIncident : float2(0.0, 0.0);
		float2 entry = positionWS.xz + sunward * depth * tanRefracted;

		float3 curvature =
			FishWaterCausticCurvature(TEXTURE2D_ARGS(_FishWaterDerivatives1, sampler_linear_repeat), entry, _FishWaterPatch.y)
			+ FishWaterCausticCurvature(TEXTURE2D_ARGS(_FishWaterDerivatives2, sampler_linear_repeat), entry, _FishWaterPatch.z);

		/* The lens. Clamped away from zero: on a focal line the determinant passes through nothing
		 * and the brightness through infinity, which a real caustic never reaches — the sun is a
		 * disc, not a point, and it softens every focus. */
		float lens = depth * (1.0 - 1.0 / FishWaterRefractiveIndex);
		float determinant = (1.0 + lens * curvature.x) * (1.0 + lens * curvature.y) - lens * lens * curvature.z * curvature.z;
		float focus = 1.0 / max(abs(determinant), 0.12);

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
