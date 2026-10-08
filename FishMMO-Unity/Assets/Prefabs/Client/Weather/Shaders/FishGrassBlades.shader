// The procedural blade grass (GrassBladeRenderer, FishMMO.Client/World/Terrain/Grass): every blade is a
// strip shaped in the vertex shader from a 16-byte record FishGrassBlades.compute wrote this frame.
// The pass code and the buffer contract are in FishGrassBlades.hlsl. Drawn only with
// Graphics.RenderMeshIndirect by the renderer, which binds _GrassBlades per draw and the per-type
// arrays on its own material instance; nothing else should use this shader.
//
// Alpha-free geometry: no cutout; the only clip is the dithered fade at the grass distance.
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

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex GrassVertexMain
            #pragma fragment GrassForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
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
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
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
