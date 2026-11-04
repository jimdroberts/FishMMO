// Distant static opaque aggregates; source origins/looks ride UV3. No shadow casting.
Shader "FishMMO/World HLOD"
{
    Properties
    {
        // Specular vs Metallic workflow
        _WorkflowMode("WorkflowMode", Float) = 1.0

        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0

        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}

        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        _SpecGlossMap("Specular", 2D) = "white" {}

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0

        // Both are set from code — URP's Lit inspector, which this shader keeps so that assigning a
        // map still sets its keyword, draws a fixed set of properties and would not show them.
        // Weather Tools → "Toggle weather on selected materials" flips the first one.
        [HideInInspector] _FishWeatherAmount("Weather on this surface", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _FishVisible("Visible (day/night dissolve)", Range(0.0, 1.0)) = 1.0
        // FishMMO edit: 1 on the structure kit's materials (weathered walls, FishStructureWeathering.hlsl); 0 rocks and props.
        [Toggle] _FishStructure("Structure (weathered, no ground unfold)", Float) = 0.0
        // FishMMO edit: moss on this surface (FishMoss.hlsl), times the scene's dampness; 0 none. Set by the art generator.
        _FishMoss("Moss on this surface", Range(0.0, 1.0)) = 0.0
        // FishMMO edit: cave rock (keyword _FISH_CAVE_ROCK, FishCaveRock.hlsl): the habitat's floor grounds and wall looks.
        // Set by the cave shaper (CaveRockMaterials) on its clones of the shell rock's material; unused elsewhere.
        [HideInInspector] _FishCaveSiltMap("Cave silt", 2D) = "grey" {}
        [HideInInspector][Normal] _FishCaveSiltNormal("Cave silt normal", 2D) = "bump" {}
        [HideInInspector] _FishCaveGravelMap("Cave gravel", 2D) = "grey" {}
        [HideInInspector][Normal] _FishCaveGravelNormal("Cave gravel normal", 2D) = "bump" {}
        [HideInInspector] _FishCaveOrganicMap("Cave litter", 2D) = "grey" {}
        [HideInInspector] _FishCaveTroddenMap("Cave worn way", 2D) = "grey" {}
        [HideInInspector] _FishCaveTiles("Cave grounds per metre", Vector) = (0.4, 0.4, 0.4, 0.4)
        [HideInInspector] _FishCaveStain("Cave stain (a strength)", Color) = (0.62, 0.42, 0.24, 0.7)
        [HideInInspector] _FishCaveCrust("Cave flowstone crust", Color) = (0.9, 0.86, 0.77, 1)
        [HideInInspector][HDR] _FishCaveGlow("Cave glow crust", Color) = (0.7, 2.2, 1.6, 1)
        [HideInInspector] _FishCaveGuano("Cave guano", Color) = (0.52, 0.46, 0.36, 1)
        [HideInInspector] _FishCaveParams("Cave moss, wet gloss, grounds bound", Vector) = (1, 1, 0, 0)
        [HideInInspector] _FishCaveFlood("Cave tea ring: water level, rise, edge, on", Vector) = (0, 0, 0.05, 0)
        [HideInInspector] _FishCaveFloodStain("Cave tea ring colour (a strength)", Color) = (0.35, 0.23, 0.1, 0)

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        _Parallax("Scale", Range(0.005, 0.08)) = 0.005
        _ParallaxMap("Height Map", 2D) = "black" {}

        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}

        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}

        _DetailMask("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMapScale("Scale", Range(0.0, 2.0)) = 1.0
        _DetailAlbedoMap("Detail Albedo x2", 2D) = "linearGrey" {}
        _DetailNormalMapScale("Scale", Range(0.0, 2.0)) = 1.0
        [Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}

        // SRP batching compatibility for Clear Coat (Not used in Lit)
        [HideInInspector] _ClearCoatMask("_ClearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness("_ClearCoatSmoothness", Float) = 0.0

        // Blending state
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [HideInInspector] _XRMotionVectorsPass("_XRMotionVectorsPass", Float) = 1.0

        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        // Editmode props
        _QueueOffset("Queue offset", Float) = 0.0

        // ObsoleteProperties
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _GlossMapScale("Smoothness", Float) = 0.0
        [HideInInspector] _Glossiness("Smoothness", Float) = 0.0
        [HideInInspector] _GlossyReflections("EnvironmentReflections", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}

        // FishMMO edit (2026-10-28): look arrays (_FISH_LOOK_ARRAY, FishWeatherLitInput.hlsl). The art generator writes a surface
        // family's maps as Texture2DArray assets (ProceduralArtLookArrays) and its materials name their slices here instead of 2D
        // maps (−1: the slot is empty); a GPU-driven look family's material (_FISH_LOOK_TABLE, TerrainGpuRenderer.Looks.cs) reads
        // its members' slices and scalars from the table instead, at the row its vertex (or, DOTS-instanced, its instance) names.
        [HideInInspector][NoScaleOffset] _FishLookColour("Look albedo and emission", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] _FishLookNormal("Look normals", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] _FishLookMask("Look gloss and occlusion", 2DArray) = "" {}
        [HideInInspector] _FishLookSlices("Look slices (albedo, normal, gloss, occlusion)", Vector) = (-1, -1, -1, -1)
        [HideInInspector] _FishLookEmissionSlice("Look emission slice", Float) = -1
        [HideInInspector] _FishLook("Look row (family table)", Float) = -1
        [HideInInspector][NoScaleOffset] _FishLookTable("Look table", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit" "Queue"="Geometry" }
        Pass
        {
            // Lightmode matches the ShaderPassName set in UniversalRenderPipeline.cs. SRPDefaultUnlit and passes with
            // no LightMode tag are also rendered by Universal Render Pipeline
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForwardOnly"
            }

            // -------------------------------------
            // Render State Commands
            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 4.5

            // -------------------------------------
            // Shader Stages
            #pragma vertex HlodVertex
            #pragma fragment LitPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            // FishMMO edit: a cave's rock dressed by its habitat (FishCaveRock.hlsl; the plain shader's cave rock materials only).
            
            // FishMMO edit (2026-10-28): the generated surface families' maps from their look arrays, at the material's own slices
            // (_FISH_LOOK_ARRAY, ProceduralArtLookArrays); and a GPU-driven look family's draws (_FISH_LOOK_TABLE,
            // TerrainGpuRenderer.Looks.cs): slices and scalars from the family's table, the look per vertex (uv3.x) or per DOTS
            // instance (_FishLook). The table is ForwardLit's only: a family is opaque and unclipped, so its depth, shadow and
            // motion passes sample nothing. Both shaders declare both (IndirectShaderTests); only the twin's family clones enable the table.
            
            

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #include_with_pragmas "FishShadowVariants.hlsl"
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"


            // -------------------------------------
            // Unity defined keywords
            
            
            
            
            
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            
            
            
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:FishWorldHlodSetup
            #pragma instancing_options renderinglayer
            

            #define _FISH_LOOK_ARRAY 1
            #define _FISH_LOOK_TABLE 1
            #include "FishWeatherLitInput.hlsl"
            #include "FishWorldHlodForward.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:FishWorldHlodSetup
            #include "FishWeatherLitInput.hlsl"
            #include "FishWorldHlodSetup.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma shader_feature_local _NORMALMAP
            #define _FISH_LOOK_ARRAY 1
            #define _FISH_LOOK_TABLE 1
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:FishWorldHlodSetup
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "FishWeatherLitInput.hlsl"
            #include "FishWorldHlodSetup.hlsl"
            #include "FishWorldHlodDepthNormals.hlsl"
            ENDHLSL
        }

    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
