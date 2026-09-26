// The breakers: waves rising out of the still water at the break line, curling and landing, on a
// sheet WaterBreakers lays along the contour where the ocean (FishMMO/Water/Ocean) has faded out.
//
// Its material is a hidden copy of the ocean's, refreshed every frame (WaterBreakers.SyncMaterials),
// and it includes the ocean's own UnityPerMaterial block: one material authors the look of the whole
// sea, breakers included, and the two can never be shaded differently where they meet.
//
// The Properties block below is the ocean's, copied, and it is NOT optional: a UnityPerMaterial member
// that is not declared as a property is never filled from the material, and reads zero. The ocean lost
// _FoamColor that way and drew its foam black for two days. FishWaterMaterialTests checks both lists
// against the cbuffer.
Shader "FishMMO/Water/Breaker"
{
    Properties
    {
        [Header(Colour and absorption)]
        _ShallowColor ("Shallow", Color) = (0.45, 0.78, 0.72, 1)
        _DeepColor ("Deep", Color) = (0.06, 0.24, 0.35, 1)
        _ShoreColor ("Shore sediment", Color) = (0.55, 0.66, 0.52, 1)
        _ScatterColor ("Sub-surface scatter", Color) = (0.12, 0.45, 0.35, 1)
        _FoamColor ("Foam", Color) = (0.95, 0.98, 1, 1)
        _WaterDensity ("Absorption per metre (RGB)", Vector) = (0.36, 0.12, 0.07, 0)
        _ShoreDepth ("Sediment reaches (m)", Range(0, 30)) = 2.0
        _ScatterStrength ("Scatter strength", Range(0, 3)) = 1.8

        [Header(Surface)]
        _Smoothness ("Smoothness", Range(0.5, 1)) = 0.96
        _SpecularStrength ("Sun glint", Range(0, 8)) = 1.6
        _ReflectionStrength ("Reflection", Range(0, 2)) = 0.85
        _MaxAlpha ("Maximum opacity", Range(0, 1)) = 1.0

        [Header(Normal maps)]
        [NoScaleOffset] [Normal] _NormalMap ("Wave normal map", 2D) = "bump" {}
        _NormalScaleA ("Tiling A (m)", Range(0.5, 120)) = 26
        _NormalScaleB ("Tiling B (m)", Range(0.5, 120)) = 7
        _NormalStrength ("Ripple strength", Range(0, 2)) = 0.75
        _WaveSpeed ("Ripple speed", Range(0, 4)) = 1.0
        _NormalFadeDistance ("Ripples fade by (m)", Range(20, 4000)) = 500

        [Header(Refraction)]
        _RefractionStrength ("Refraction (m)", Range(0, 3)) = 0.5

        [Header(Foam)]
        [NoScaleOffset] _FoamTexture ("Foam mask", 2D) = "white" {}
        _FoamScale ("Foam size (m)", Range(0.5, 64)) = 9
        _FoamSharpness ("Foam sharpness", Range(0.01, 1)) = 0.28
        _SurfStrength ("Whitewater behind the breakers", Range(0, 3)) = 1.4
        _SurfFoamOpacity ("Whitewater opacity at its thickest", Range(0, 1)) = 0.8
        _SurfFoamVeil ("Thin foam between the whitewater's froth", Range(0, 1)) = 0.22
        _WhitecapThreshold ("White cap threshold", Range(0, 1)) = 0.70
        _ShallowWhitecaps ("White caps where the sea fades out", Range(0, 1)) = 0.6

        [Header(Shore)]
        _EdgeFade ("Shore softness (m)", Range(0.01, 10)) = 0.5

        [Header(Breakers)]
        _BreakerHeight ("Height, times the sea's", Range(0, 2)) = 1
        _BreakerCurl ("Barrel: how far a plunging lip wraps", Range(0, 1.5)) = 1
        _BreakerFoam ("Whitewater on the breakers", Range(0, 2)) = 1
        _BreakerEdgeFade ("Breaker edges fade over (m)", Range(0.1, 10)) = 1.5

        [Header(Waves)]
        _WaveFadeStart ("Waves fade from (m)", Range(10, 8000)) = 900
        _WaveFadeEnd ("Waves flat past (m)", Range(20, 20000)) = 4000

        [Header(Variation)]
        _Clarity ("Clarity varies by", Range(0, 0.9)) = 0.40
        _ClarityScale ("Clarity patch size (m)", Range(20, 4000)) = 500

        [Header(Development)]
        // The names are a C# enum's: the inline form takes at most seven pairs, and these are thirteen.
        [Enum(FishMMO.Water.WaterDebugView)]
        _DebugView ("Show one term", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-99"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Transparent-99: straight after the ocean (-100), over the still water it rises from, and
        // before the spray (-95) and everything else afloat. A sheet, so both sides are drawn.
        Cull Off

        // First its depth alone, so the barrel sorts itself: the lip hides the face behind it and
        // the far side of the tube, which a blended sheet in one pass would draw in mesh order. URP
        // draws SRPDefaultUnlit before UniversalForward for the same object.
        Pass
        {
            Name "BreakerDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex BreakerVertex
            #pragma fragment BreakerDepthFragment
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWaterBreakerPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex BreakerVertex
            #pragma fragment BreakerFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // The sea's own quality switches (WaterSurface.ApplyQuality): global, never material.
            #pragma multi_compile_fragment _ _WATER_DEPTH
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWaterBreakerPass.hlsl"
            ENDHLSL
        }
    }

    // No fallback, for the ocean's reason: a grey plastic sheet where this fails to compile would
    // read as a content bug.
}
