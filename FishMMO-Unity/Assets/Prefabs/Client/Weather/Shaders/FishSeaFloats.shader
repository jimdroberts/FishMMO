// What drifts on the open sea (SeaFloatSystem, FishMMO.Client/World/SeaFloats, 2026-10-25, phase I2p): sargassum, by-the-wind
// sailors, barnacled logs, propagule rafts, corklines, growlers, pumice, pancake and brash ice. Pass code and the instance
// contract are in FishSeaFloats.hlsl. Drawn only with Graphics.RenderMeshIndirect by SeaFloatSystem, which binds the instance and
// command-base buffers on its own material instance; nothing else should use it.
//
// Opaque, no shadow casting (small, low on the water); the only clip is the dithered fade at each kind's draw distance.
// Read-only StructuredBuffers in the vertex and fragment stages only (WebGPU's floor); no geometry stage.
Shader "FishMMO/Sea Floats"
{
    Properties
    {
        _FloatSmoothness ("Smoothness", Range(0.0, 1.0)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FloatVertexMain
            #pragma fragment FloatForwardFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "FishShadowVariants.hlsl"
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define FISH_SEA_FLOATS_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishSeaFloats.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FloatDepthVertex
            #pragma fragment FloatDepthFragment

            #define FISH_SEA_FLOATS_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishSeaFloats.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FloatVertexMain
            #pragma fragment FloatDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_SEA_FLOATS_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishSeaFloats.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
