// An ocean surface for URP: Gerstner waves in world metres, shoaling and breaking against a real
// sea bed, shaded as water rather than as a tinted plane, and honest about what the render
// pipeline will and will not give it.
Shader "FishMMO/Water/Ocean"
{
    Properties
    {
        [Header(Colour and absorption)]
        _ShallowColor ("Shallow", Color) = (0.45, 0.78, 0.72, 1)
        _DeepColor ("Deep", Color) = (0.06, 0.24, 0.35, 1)
        _ShoreColor ("Shore sediment", Color) = (0.55, 0.66, 0.52, 1)
        _ScatterColor ("Sub-surface scatter", Color) = (0.12, 0.45, 0.35, 1)
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
        _FoamDistance ("Shore foam depth (m)", Range(0, 12)) = 1.1
        _FoamSharpness ("Foam sharpness", Range(0.01, 1)) = 0.28
        _SurfStrength ("Surf foam", Range(0, 3)) = 1.4
        _WhitecapThreshold ("White cap threshold", Range(0, 1)) = 0.70

        [Header(Shore)]
        _EdgeFade ("Shore softness (m)", Range(0.01, 10)) = 0.5
        _ShoreBreak ("Breaks at depth fraction", Range(0.3, 1.2)) = 0.62
        _SwashMetres ("Swash run-up (m)", Range(0, 3)) = 0.35
        _ShoreRefraction ("Waves turn to face shore", Range(0, 1)) = 0.85

        [Header(Shore waves)]
        _ShoreWaveLength ("Surf wavelength (m)", Range(4, 160)) = 40
        _ShoreWaveHeight ("Surf height (m)", Range(0, 8)) = 1.1
        _ShoreWavePitch ("Barrel (1 = vertical face)", Range(0, 4)) = 1.6
        _ShoreWaveFoam ("Surf foam", Range(0, 3)) = 1.5

        [Header(Waves)]
        _GroupLength ("Wave group length", Range(2, 20)) = 7
        _WaveFadeStart ("Waves fade from (m)", Range(10, 8000)) = 900
        _WaveFadeEnd ("Waves flat past (m)", Range(20, 20000)) = 4000

        [Header(Variation)]
        _Clarity ("Clarity varies by", Range(0, 0.9)) = 0.40
        _ClarityScale ("Clarity patch size (m)", Range(20, 4000)) = 500

        [Header(Development)]
        [Enum(Off,0, Sediment,1, Clarity field,2, Transmittance,3, Fresnel,4, Opacity,5, Light on the water,6, Shadow,7, Depth to 10m,8, Body colour,9, Behind the surface,10, Reflection,11, Foam,12)]
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

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

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
