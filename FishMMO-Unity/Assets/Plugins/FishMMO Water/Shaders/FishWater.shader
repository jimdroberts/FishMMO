// An ocean surface for URP: an FFT sea in world metres that fades out over a real sea bed where
// the breakers (FishMMO/Water/Breaker) take over, shaded as water rather than as a tinted plane,
// and honest about what the render pipeline will and will not give it.
Shader "FishMMO/Water/Ocean"
{
    Properties
    {
        [Header(Colour and absorption)]
        _ShallowColor ("Shallow", Color) = (0.45, 0.78, 0.72, 1)
        _DeepColor ("Deep", Color) = (0.06, 0.24, 0.35, 1)
        _ShoreColor ("Shore sediment", Color) = (0.55, 0.66, 0.52, 1)
        _ScatterColor ("Sub-surface scatter", Color) = (0.12, 0.45, 0.35, 1)
        // In the cbuffer since the rebuild and missing from here since 2026-09-24, so every material
        // read it as black: the white caps and the surf drew as dark lace. A UnityPerMaterial member
        // that is not declared here is never filled from the material. FishWaterMaterialTests guards it.
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
            "Queue" = "Transparent-100"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Transparent-100: before everything that floats in or on the water, so spray, rain
        // splashes and the weather's own transparent effects sort in front of the surface.
        //
        // No ZWrite, and deliberately NO ShadowCaster pass: water that casts a shadow darkens the
        // sea bed it exists to show through.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        // Not inside a floating hull (FishHullLid.shader sets bit 128 across its open top): the water plane lies over
        // the hull's floor there, and the boats took on the sea.
        Stencil
        {
            Ref 128
            ReadMask 128
            Comp NotEqual
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            /* Set from C# against what the running URP asset actually provides, so a pipeline with
             * no depth or no opaque copy never samples a texture that is not there. Sampling one
             * URP has not bound is not an error and does not come back black — it returns whatever
             * was last in that slot, so the water refracts the previous frame's buffers and foams
             * in mid-air, on whichever quality tier nobody develops on.
             *
             * GLOBAL keywords, not shader_feature_local: the local form writes the answer into the
             * material asset and leaves a modified .mat in the tree on whichever machine opened it. */
            #pragma multi_compile_fragment _ _WATER_DEPTH
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWaterForwardPass.hlsl"
            ENDHLSL
        }
    }

    // No fallback. A fallback to Lit would quietly draw a grey plastic plane wherever this fails
    // to compile, which is the failure that looks like a content bug for a week.
}
