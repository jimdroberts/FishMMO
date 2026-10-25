// The run-time cave cover (CaveCoverRenderer, FishMMO.Client/World/Caves/Cover, 2026-10-20): small dense cover scattered on a
// carved cave's rock every frame by FishCaveCover.compute and drawn here off the cover art's one mesh (CaveCover_Set). The
// pass code and the instance contract are in FishCaveCover.hlsl. Drawn only with Graphics.RenderMeshIndirect by the renderer,
// which binds the instance, command-base, palette and look buffers on its own material instance; nothing else should use it.
//
// Opaque, no shadow casting (the cover is tiny and the cave dark); the only clip is the dithered thinning with distance.
// Read-only StructuredBuffers in the vertex and fragment stages only (WebGPU's floor); no geometry stage.
Shader "FishMMO/Cave Cover"
{
    Properties
    {
        _CoverGlow ("Glow (strength, shimmer, shimmer speed, foot shade)", Vector) = (2, 0.35, 0.15, 0.55)
        _CoverDarkness ("Darkness (underground, highlight)", Vector) = (0.9, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex CoverVertexMain
            #pragma fragment CoverForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "FishShadowVariants.hlsl"
            // The cave's lanterns, torches and crystal lights (CoverForwardFragment).
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define FISH_COVER_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishCaveCover.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex CoverDepthVertex
            #pragma fragment CoverDepthFragment

            #define FISH_COVER_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishCaveCover.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex CoverVertexMain
            #pragma fragment CoverDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_COVER_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishCaveCover.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
