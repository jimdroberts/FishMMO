Shader "FishMMO/Water/Inland Water"
{
    // Lakes and rivers: one shader for both, so where they meet there is nothing to differ.
    //
    // Their colour is the water column behind each pixel (the depth texture), not the mesh, so a river
    // running into a lake changes colour only as its depth does. Their ripples ride the scene's one flow
    // field, read per vertex at the vertex's own position (SceneWaterBodies.FlowAt), so two meshes meeting
    // at a confluence or a mouth carry the same current where they meet. And each pixel of inland water
    // is drawn once (the stencil below), so where two meshes overlap the water is not laid on twice.
    Properties
    {
        [Header(Colour)]
        _ShallowColor("Shallow colour", Color) = (0.20, 0.42, 0.40, 1)
        _DeepColor("Deep colour", Color) = (0.03, 0.12, 0.16, 1)
        _WaterDensity("Absorption per metre (rgb)", Vector) = (0.45, 0.12, 0.08, 0)
        _Turbidity("Silt in moving water", Range(0, 1)) = 0.35
        _SiltColor("Silt colour", Color) = (0.36, 0.33, 0.24, 1)

        [Header(Surface)]
        [NoScaleOffset] _NormalMap("Ripples", 2D) = "bump" {}
        _NormalScale("Ripple size (m)", Float) = 3
        _NormalStrength("Ripple strength", Range(0, 2)) = 0.6
        _NormalFadeDistance("Ripples fade by (m)", Float) = 250
        _FlowCycle("Flow-map cycle (s)", Float) = 2
        _Smoothness("Smoothness", Range(0, 1)) = 0.94
        _ReflectionStrength("Reflection", Range(0, 2)) = 1
        _SpecularStrength("Sun glint", Range(0, 4)) = 1.2
        _RefractionStrength("Refraction", Range(0, 1)) = 0.25
        _EdgeFade("Shore softness (m)", Float) = 0.4
        _MaxAlpha("Opacity without refraction", Range(0, 1)) = 0.85

        [Header(Foam)]
        [NoScaleOffset] _FoamTexture("Foam", 2D) = "black" {}
        _FoamScale("Foam size (m)", Float) = 2.5
        _FoamColor("Foam colour", Color) = (0.92, 0.94, 0.95, 1)
        _FoamSpeed("Speed foam starts (m/s)", Float) = 1.2
        _ShoreFoam("Shore foam depth (m)", Float) = 0.25
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

        // After the sea (Transparent-100), so a river fading into the sea is drawn over it; before
        // everything that floats in it. No ZWrite and no shadow caster, as the sea.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        // Each pixel of inland water once: the first mesh drawn there (the lake, then the larger river)
        // claims it, and a second covering it is not composited over the first.
        Stencil
        {
            Ref 64
            ReadMask 64
            WriteMask 64
            Comp NotEqual
            Pass Replace
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex InlandVertex
            #pragma fragment InlandFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // The sea's global quality keywords, set against what the running URP asset provides.
            #pragma multi_compile_fragment _ _WATER_DEPTH
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishInlandWater.hlsl"
            ENDHLSL
        }
    }
}
