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
/// A colour at a world point, seen through the fog between it and the camera.
/// </summary>
/// <param name="keep">How much of the point's own light still reaches the eye, 0 to 1.</param>
half3 FishWaterAirFog(half3 color, float3 positionWS, float2 screenUV, out half keep)
{
	keep = 1.0;
	if (_FishAirFogParams.x > 1e-5)
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

#endif
