// FishMMO/Backdrop Ground — the ground past a generated scene's edge (SceneBackdrop), drawn with the scene's
// own terrain arrays so the textures run on across the edge, fading to the backdrop's colour bake with distance.
//
// Not a copy: it compiles FishMMO/Weather Terrain Array's passes with FISH_TERRAIN_BACKDROP defined, which
// (FishWeatherTerrainArrayInput.hlsl) takes the vertices from the mesh instead of a terrain heightmap, reads the
// control maps at the mesh's own 0..1 coordinate, and draws FishBackdropSurface: the arrays within
// _FishBackdropDetail of the camera, the colour bake past it and wherever the control maps do not cover the
// ground. Lighting, weather, fog and the depth passes are the terrain's, line for line, so the two meet without
// a change of shading at the edge.
//
// The control maps and the colour bake are this material's (SceneBackdropBuilder writes them); the arrays and
// their per-layer numbers are the scene's, pushed by TerrainArrayBinder exactly as it pushes them to the tiles.
// No instancing: the renderers carry a property block, which keeps them off the GPU Resident Drawer anyway, and
// there are a few dozen of them. No shadow caster: the backdrop casts none (SceneBackdropBuilder).
Shader "FishMMO/Backdrop Ground"
{
    Properties
    {
        _FishBackdropColour("Colour bake", 2D) = "grey" {}
        _FishBackdropDetail("Arrays fade from / to (m)", Vector) = (1500, 3000, 0, 0)

        _FishWeatherAmount("Weather on this ground", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _FishSnowDepth("Snow lifts the ground by (m, High only)", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _HeightTransition("Height Transition", Range(0, 1.0)) = 0.0

        // The backdrop's own control maps, baked over its 0..1 coordinate; channel c of map k is the scene's layer 4k + c.
        [HideInInspector] _FishControl0("Control 0", 2D) = "black" {}
        [HideInInspector] _FishControl1("Control 1", 2D) = "black" {}
        [HideInInspector] _FishControl2("Control 2", 2D) = "black" {}
        [HideInInspector] _FishControl3("Control 3", 2D) = "black" {}
        [HideInInspector] _FishControl4("Control 4", 2D) = "black" {}
        [HideInInspector] _FishControl5("Control 5", 2D) = "black" {}
        [HideInInspector] _FishControl6("Control 6", 2D) = "black" {}
        [HideInInspector] _FishControl7("Control 7", 2D) = "black" {}
        [HideInInspector] _FishAlbedoArray("Albedo array", 2DArray) = "" {}
        [HideInInspector] _FishNormalArray("Normal array", 2DArray) = "" {}
        [HideInInspector] _FishMaskArray("Mask array", 2DArray) = "" {}

        [HideInInspector] _MainTex("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor("Main Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        // Last of the opaques (the queue ends at 2500): everything in the scene stands in front of the backdrop, so
        // drawn after it all, early depth rejects every pixel the scene covers. In the terrain's own queue
        // (Geometry-100) it drew first and paid the arrays for pixels trees and props then painted over: +0.96 ms
        // opaque at the meadow view, where the backdrop is a sliver of horizon (ScenePerfProbe, 2026-10-08).
        Tags { "Queue" = "Geometry+490" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            // Texture arrays.
            #pragma target 3.5

            #pragma vertex SplatmapVert
            #pragma fragment SplatmapFragment

            #define FISH_TERRAIN_BACKDROP

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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #if USE_DYNAMIC_BRANCH_FOG_KEYWORD && SHADER_API_VULKAN && SHADER_API_MOBILE
            #define SKIP_SHADOWS_LIGHT_INDEX_CHECK 1
            #endif

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags{"LightMode" = "DepthOnly"}

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #define FISH_TERRAIN_BACKDROP
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
            #pragma target 3.5
            #pragma vertex DepthNormalOnlyVertex
            #pragma fragment DepthNormalOnlyFragment
            #define FISH_TERRAIN_NORMALS_ONLY
            #define FISH_TERRAIN_BACKDROP

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "FishWeatherTerrainArrayInput.hlsl"
            #include "FishWeatherTerrainArrayPasses.hlsl"
            ENDHLSL
        }
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
