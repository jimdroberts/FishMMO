// The procedural blade grass (GrassBladeRenderer, FishMMO.Client/World/Terrain/Grass): every blade is a
// strip shaped in the vertex shader from a 24-byte record FishGrassBlades.compute wrote this frame.
// The pass code and the buffer contract are in FishGrassBlades.hlsl. Drawn only with
// Graphics.RenderMeshIndirect by the renderer, which binds _GrassBlades per draw and the per-type
// table (_GrassTypeTable) on its own material instances; nothing else should use this shader.
//
// Alpha-free geometry: no cutout; the only clips are the dithered fade at the grass distance and, under MSAA or a temporal
// resolve (STP/TAA), a sliver under a rendered pixel drawn a pixel wide with its true width as coverage.
// MotionVectors: the blades' own sway for STP/TAA (GrassMotionVertex), drawn for the slots the renderer gives per-object motion.
// Read-only StructuredBuffer in the vertex stage only (WebGPU's floor); no geometry stage.
Shader "FishMMO/Grass Blades"
{
    Properties
    {
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
            Cull Off
            ZWrite On
            // The fade and a thin blade's share of its pixel as MSAA coverage (GrassForwardFragment); full coverage at alpha 1.
            AlphaToMask On

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex GrassVertexMain
            #pragma fragment GrassForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "FishShadowVariants.hlsl"
            // The cloud shadow, a cookie on the main light (GrassForwardFragment).
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            // The moon (until it is the main light), companion suns and point lights (GrassForwardFragment).
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define FISH_GRASS_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishGrassBlades.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex GrassDepthVertex
            #pragma fragment GrassDepthFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #define FISH_GRASS_PASS_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishGrassBlades.hlsl"
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
            #pragma vertex GrassDepthVertex
            #pragma fragment GrassDepthFragment

            #define FISH_GRASS_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishGrassBlades.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            Cull Off
            // Pulled a hair toward the camera: the depth it tests against is the forward pass's, from another program, and a
            // last-bit difference in a long vertex shader must not drop a blade's motion.
            Offset -1, -1

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex GrassMotionVertex
            #pragma fragment GrassMotionFragment

            #define FISH_GRASS_PASS_MOTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishGrassBlades.hlsl"
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
            #pragma vertex GrassVertexMain
            #pragma fragment GrassDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_GRASS_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishGrassBlades.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
