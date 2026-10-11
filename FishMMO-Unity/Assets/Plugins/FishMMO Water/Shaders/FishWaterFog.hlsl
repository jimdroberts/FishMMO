#ifndef FISHMMO_WATER_FOG_INCLUDED
#define FISHMMO_WATER_FOG_INCLUDED

// The weather's fog, applied by the water to itself.
//
// The fog passes (FishHeightFog, FishVolumetricFogApply) run just before the transparent queue and
// fog what is already in the frame. The sea, the shore and anything else transparent are drawn after
// that, over the fogged ground — so they came out perfectly clear through the thickest fog, a bright
// clean sea under a white-out. They apply the same two fogs themselves, in the same order, at their
// own depth, from the values the passes publish for exactly this (_FishAirFog*). When a pass does not
// run, it publishes nothing and the water is not fogged by it.

float4 _FishAirFogParams;       // x density (0: no height fog), y falloff height (m), z base height (m), w most it can hide
float4 _FishAirFogColor;        // rgb the fog's colour
float4 _FishAirFogSun;          // xyz toward the light, w forward-scatter strength (0: none)
float4 _FishAirFogSunColor;     // rgb the light's colour
float4 _FishAirFogRange;        // x start distance, y end distance
TEXTURE3D(_FishAirFogVolume);
SAMPLER(sampler_FishAirFogVolume);
float4 _FishAirFogVolumeRange;  // x near, y far, z depth-curve exponent, w 1 when the volume is live

/// <summary>
/// The height fog's integral of density · exp(-(y - base)/H) along a ray, as FishHeightFog.shader
/// has it, and the share of what lies at the end of the ray that it hides.
/// </summary>
float FishAirFogAmount(float3 origin, float3 direction, float distance, float density, float falloff, float base)
{
	float h = max(1.0, falloff);
	float atOrigin = exp(-(origin.y - base) / h);
	float dy = direction.y;
	// A level ray is the common case, and the closed form is 0/0 there: its limit, taken explicitly.
	float integral = abs(dy) < 1e-4
		? distance * atOrigin
		: (h / dy) * atOrigin * (1.0 - exp(-distance * dy / h));
	return 1.0 - exp(-max(0.0, density * integral));
}

/// <summary>The volumetric fog's slice for an eye depth, as FishVolumetricFogApply.shader has it.</summary>
float FishAirFogSlice(float eyeDepth)
{
	float near = max(1e-3, _FishAirFogVolumeRange.x);
	float far = max(near + 1e-3, _FishAirFogVolumeRange.y);
	float ratio = log(clamp(eyeDepth, near, far) / near) / log(far / near);
	return pow(saturate(ratio), 1.0 / max(1e-3, _FishAirFogVolumeRange.z));
}

/// <summary>
/// A colour at a world point, seen through the fog between it and the camera: the fog layer's analytic
/// share when <paramref name="layer"/>, then the froxel volume's.
/// </summary>
/// <param name="keep">How much of the point's own light still reaches the eye, 0 to 1.</param>
half3 FishWaterAirFogParts(half3 color, float3 positionWS, float2 screenUV, bool layer, out half keep)
{
	keep = 1.0;
	if (layer && _FishAirFogParams.x > 1e-5)
	{
		float3 camera = _WorldSpaceCameraPos;
		float3 toPoint = positionWS - camera;
		float distance = length(toPoint);
		float3 direction = distance > 1e-5 ? toPoint / distance : float3(0.0, 0.0, 1.0);
		distance = max(0.0, min(distance, _FishAirFogRange.y) - _FishAirFogRange.x);

		float fog = min(FishAirFogAmount(camera, direction, distance, _FishAirFogParams.x, _FishAirFogParams.y, _FishAirFogParams.z),
			_FishAirFogParams.w);
		half3 fogColor = _FishAirFogColor.rgb;
		if (_FishAirFogSun.w > 0.0)
		{
			// Brighter looking toward the sun: fog scatters light forward.
			float toward = saturate(dot(direction, normalize(_FishAirFogSun.xyz)));
			fogColor = lerp(fogColor, _FishAirFogSunColor.rgb, saturate(pow(toward, 8.0) * _FishAirFogSun.w));
		}
		color = color * (1.0 - fog) + fogColor * fog;
		keep *= 1.0 - fog;
	}
	if (_FishAirFogVolumeRange.w > 0.5)
	{
		float eyeDepth = -TransformWorldToView(positionWS).z;
		float4 volume = SAMPLE_TEXTURE3D_LOD(_FishAirFogVolume, sampler_FishAirFogVolume,
			float3(screenUV, FishAirFogSlice(eyeDepth)), 0);
		color = color * saturate(volume.a) + volume.rgb;
		keep *= saturate(volume.a);
	}
	return color;
}

// ── The clouds, likewise ────────────────────────────────────────────────
//
// The weather's clouds (FishCloudsFeature) are composited over the frame just before the transparent
// queue as well, so the sea drawn after them painted over every cloud between the camera and the
// water: from any height, the water plane cut the clouds off. The cloud pass leaves what it laid down
// — the light the clouds scatter toward the eye, and how much they let through — in _FishCloudBuffer,
// published for exactly this (the sky bodies put themselves back behind the clouds the same way), and
// the water lays it back over itself.
//
// EXACT, not an estimate: each cloud ray was marched to the opaque world, which under the water is the
// sea bed, and there are no clouds below sea level — so everything a ray gathered lies between the
// camera and the water's surface, which is precisely what the water should be behind. In the frame the
// fog passes come after the clouds, so the water takes the clouds first and the fog after them.

/// <summary>A colour at a world point, seen through all the fog between it and the camera.</summary>
half3 FishWaterAirFog(half3 color, float3 positionWS, float2 screenUV, out half keep)
{
	return FishWaterAirFogParts(color, positionWS, screenUV, true, keep);
}

/// <summary>
/// The fog for a surface the cloud buffer has already been laid over (FishWaterBehindClouds,
/// FishWaterCloudsInFront). Once the cloud march draws the fog layer (_FishAirFogRange.z = 1) that buffer
/// holds it, so only the froxel volume is left to apply — and while the march draws the layer the volume
/// carries the falls' mist alone, which the buffer does not. The sea, the rivers and the falls skipped both
/// (mist rising off a fall stood in front of the water behind it unseen); the swash, the caustics and the
/// lava skipped neither, so on a foggy shore the swash took the fog twice and glowed through it as a pale
/// band (audit 2026-10-09).
/// </summary>
half3 FishWaterAirFogOver(half3 color, float3 positionWS, float2 screenUV, out half keep)
{
	return FishWaterAirFogParts(color, positionWS, screenUV, _FishAirFogRange.z < 0.5, keep);
}

TEXTURE2D(_FishCloudBuffer);
SAMPLER(sampler_FishCloudBuffer);
float4 _FishCloudBufferTexel;   // xy one over its size, zw its size
float4 _FishCloudScreen;        // x 1 while the buffer holds this frame's clouds, else 0

/// <summary>
/// The clouds in front of a pixel: rgb the light they scatter toward the eye, a what they let through.
/// Clear air — (0, 0, 0, 1) — with no clouds this frame.
/// </summary>
float4 FishWaterCloudsInFront(float2 screenUV)
{
	if (_FishCloudScreen.x < 0.5)
	{
		return float4(0.0, 0.0, 0.0, 1.0);
	}
	#if defined(_WATER_DEPTH)
		/* The composite's own four taps, each weighed by whether it looks at the same thing
		 * (FishClouds.shader). The clouds are marched at a fraction of the screen, and a texel that
		 * straddles a headland's edge holds the open sea's long march: taken blindly, its light rims
		 * the land. */
		if (_FishCloudBufferTexel.z > 0.5)
		{
			float2 texel = _FishCloudBufferTexel.xy * 0.5;
			float here = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
			float4 sum = 0.0;
			float total = 0.0;
			[unroll]
			for (int t = 0; t < 4; t++)
			{
				float2 at = screenUV + float2(t == 0 || t == 2 ? -texel.x : texel.x, t < 2 ? -texel.y : texel.y);
				float there = LinearEyeDepth(SampleSceneDepth(at), _ZBufferParams);
				float weight = saturate(1.0 - abs(there - here) / max(12.0, here * 0.2));
				weight = max(weight, saturate(min(there, here) / 4000.0));
				sum += SAMPLE_TEXTURE2D_LOD(_FishCloudBuffer, sampler_FishCloudBuffer, at, 0) * weight;
				total += weight;
			}
			if (total > 1e-3)
			{
				return sum / total;
			}
		}
	#endif
	return SAMPLE_TEXTURE2D_LOD(_FishCloudBuffer, sampler_FishCloudBuffer, screenUV, 0);
}

/// <summary>A surface seen through the clouds in front of it: its own light through them, theirs added.</summary>
half3 FishWaterBehindClouds(half3 color, float2 screenUV)
{
	float4 clouds = FishWaterCloudsInFront(screenUV);
	return color * clouds.a + clouds.rgb;
}

#endif
