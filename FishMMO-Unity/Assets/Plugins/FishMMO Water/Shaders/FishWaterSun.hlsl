#ifndef FISHMMO_WATER_SUN_INCLUDED
#define FISHMMO_WATER_SUN_INCLUDED

// What of the main light the cloud lets through at a point.
//
// When the weather lays a cloud shadow on the sun it does it with a light cookie, and hands the light
// over with the cloud's average divided back OUT of it, for the cookie to put in again patch by patch.
// The ground samples that cookie; the water read the light bare, so under a rain deck — where the cloud
// lets through a tenth — it was lit by ten suns: a bright shore line and an orange-sparkling swash on a
// dark, wet beach. Every water shader that takes the main light's colour multiplies it by this.
//
// With no cookie the light already carries the cloud's average and the fallback is used: the sea's
// own cloud shadow (_FishWaterCloudShadow) where it has one, 1 where it does not. Read at the top
// mip, so the vertex stage may use it; the cookie is a shadow kilometres across and has no fine detail.
//
// Needs Lighting.hlsl included first, and `#pragma multi_compile _ _LIGHT_COOKIES` in the pass.

half3 FishWaterCloudLight(float3 positionWS, half fallback)
{
	#if defined(_LIGHT_COOKIES)
		if (IsMainLightCookieEnabled())
		{
			float2 uv = ComputeLightCookieUVDirectional(_MainLightWorldToLight, positionWS, float4(1.0, 1.0, 0.0, 0.0), URP_TEXTURE_WRAP_MODE_NONE);
			half4 c = SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv, 0);
			return IsMainLightCookieTextureRGBFormat() ? c.rgb : (IsMainLightCookieTextureAlphaFormat() ? c.aaa : c.rrr);
		}
	#endif
	return half3(fallback, fallback, fallback);
}

#endif
