#ifndef FISHMMO_LIGHT_COOKIE_VARIANTS_INCLUDED
#define FISHMMO_LIGHT_COOKIE_VARIANTS_INCLUDED
#pragma dynamic_branch _ _LIGHT_COOKIES
// Share one program for cookie ON/OFF while honoring URP's live global keyword.
// The self-referential macro keeps URP's #if defined checks enabled without replacing
// the dynamic keyword's HLSL uniform. Include URP before wrapping its sample calls.
#ifndef _LIGHT_COOKIES
#define _LIGHT_COOKIES _LIGHT_COOKIES
#endif
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LightCookie/LightCookie.hlsl"
real3 FishSampleMainLightCookie(float3 positionWS)
{
    UNITY_BRANCH
    if (_LIGHT_COOKIES) return SampleMainLightCookie(positionWS);
    return real3(1, 1, 1);
}
real3 FishSampleAdditionalLightCookie(int lightIndex, float3 positionWS)
{
    UNITY_BRANCH
    if (_LIGHT_COOKIES) return SampleAdditionalLightCookie(lightIndex, positionWS);
    return real3(1, 1, 1);
}
#define SampleMainLightCookie FishSampleMainLightCookie
#define SampleAdditionalLightCookie FishSampleAdditionalLightCookie
#endif
