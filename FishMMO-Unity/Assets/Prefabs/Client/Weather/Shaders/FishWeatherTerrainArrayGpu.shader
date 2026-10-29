// FishMMO/Weather Terrain Array GPU — the client's own draw of FishMMO/Weather Terrain Array's tiles (TerrainHeightmapSystem,
// 2026-10-28): per-tile indirect draws of geomorphed patches, sampling the tiles' existing textures
// (FishTerrainHeightmap.compute / .hlsl), with the same passes and the same pixel shader as the tiles' own material
// (FishWeatherTerrainArrayInput.hlsl / Passes.hlsl, compiled with FISH_TERRAIN_GPU). Unity's terrain keeps the tiles'
// collision, the editor, the cut and the server; the client sets drawHeightmap off on the tiles it draws, in play only.
//
// Never on a material asset: TerrainHeightmapField makes two runtime materials from the tiles' own (its numbers and keywords
// copied, the binder's per-tile bindings in arrays), one with _ALPHATEST_ON for the patches a hole touches. Hidden from the
// shader menu. Referenced from the weather render profile so a client build carries it.
//
// Keywords: _ALPHATEST_ON is multi_compile (a runtime material's shader_feature would be stripped from the build); the tiles'
// _TERRAIN_BLEND_HEIGHT is a branch on _FishGpuHeightBlend; the normals are always read per pixel (the instanced tiles' look).
Shader "Hidden/FishMMO/Weather Terrain Array GPU"
{
    Properties
    {
        _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0
        _FishWeatherAmount("Weather on this ground", Range(0.0, 1.0)) = 1.0
        _FishSnowDepth("Snow lifts the ground by (m, High only)", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _FishGpuArrayInfo("Array info (x layers, z normals, w masks)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _FishGpuPathLayers("Path layers", Vector) = (-1, -1, -1, -1)
        [HideInInspector] _FishGpuHeightBlend("Height blend", Float) = 0.0
        [HideInInspector] _FishGpuShadowCull("Shadow cull (UnityEngine.Rendering.CullMode)", Float) = 0.0
        [HideInInspector] _FishLayerTable("Layer table", 2D) = "black" {}
        [HideInInspector] _FishAlbedoArray("Albedo array", 2DArray) = "" {}
        [HideInInspector] _FishNormalArray("Normal array", 2DArray) = "" {}
        [HideInInspector] _FishMaskArray("Mask array", 2DArray) = "" {}
        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)
    }

    HLSLINCLUDE

    #pragma multi_compile_local_fragment _ _ALPHATEST_ON

    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma use_dxc vulkan
            // Texture arrays, and the patches' buffers in the vertex stage.
            #pragma target 4.5

            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #include_with_pragmas "FishShadowVariants.hlsl"
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #define FISH_TERRAIN_GPU 1


            #if USE_DYNAMIC_BRANCH_FOG_KEYWORD && SHADER_API_VULKAN && SHADER_API_MOBILE
            #define SKIP_SHADOWS_LIGHT_INDEX_CHECK 1
            #endif

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ColorMask 0
            // The tiles' own shadow casting (TwoSided on the generated scenes: Off); a shadows-only draw cannot ask for it.
            Cull [_FishGpuShadowCull]

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #define FISH_TERRAIN_GPU 1

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags{"LightMode" = "UniversalGBuffer"}

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #include_with_pragmas "FishShadowVariants.hlsl"
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_TERRAIN_GPU 1

            #define TERRAIN_GBUFFER 1

            #if USE_DYNAMIC_BRANCH_FOG_KEYWORD && SHADER_API_VULKAN && SHADER_API_MOBILE
            #define SKIP_SHADOWS_LIGHT_INDEX_CHECK 1
            #endif

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferOutputFormat.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #define FISH_TERRAIN_GPU 1

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            ENDHLSL
        }

        // This pass is used when drawing to a _CameraNormalsTexture texture
        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode" = "DepthNormals"}

            ZWrite On

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex DepthNormalOnlyVertex
            #pragma fragment DepthNormalOnlyFragment
            #define FISH_TERRAIN_NORMALS_ONLY

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #define FISH_TERRAIN_GPU 1

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            ENDHLSL
        }

    }

    Fallback Off
}
