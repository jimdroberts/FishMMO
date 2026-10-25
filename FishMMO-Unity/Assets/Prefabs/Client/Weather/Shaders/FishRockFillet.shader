// The rock seam fillets (RockFillets, FishMMO.Client/World/Terrain; built by FishRockFillets.compute): the smooth surfaces
// that fill the seams where rocks meet, drawn with Graphics.RenderPrimitivesIndirect, one call per rock look. Each look's
// material is a runtime copy of that rock's own material (Material.CopyPropertiesFromMaterial), so the property names are
// FishMMO/Weather Lit's and the material buffer is its (FishWeatherLitInput.hlsl). The pass code is in
// FishRockFilletPass.hlsl. Opaque; casts and receives shadows; a depth and a depth-normals pass for URP's prepasses.
Shader "FishMMO/Rock Fillet"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _Metallic("Metallic", Range(0.0, 1.0)) = 0.0
        _MetallicGlossMap("Metallic", 2D) = "white" {}
        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}
        _OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
        _OcclusionMap("Occlusion", 2D) = "white" {}
        [HideInInspector] _FishWeatherAmount("Weather on this surface", Range(0.0, 1.0)) = 1.0
        [HideInInspector] _FishVisible("Visible", Range(0.0, 1.0)) = 1.0
        _FishMoss("Moss on this surface", Range(0.0, 1.0)) = 0.0
        [HideInInspector] _FishFilletMaps("Maps in use (metallic-gloss, occlusion, normal)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _FishContactOn("Contact blend", Float) = 1.0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FishFilletForwardVertex
            #pragma fragment FishFilletForwardFragment

            // As FishMMO/Weather Lit's forward pass, less the lightmaps, probe volumes and debug views a GPU-built surface
            // never has.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #include_with_pragmas "FishShadowVariants.hlsl"
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "FishWeatherLitInput.hlsl"
            #include "FishSurface.hlsl"
            #include "FishGroundColour.hlsl"
            #include "FishRockContact.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishGroundLighting.hlsl"
            // The fine ground map through the core's inline sampler, as the rock's forward pass reads it.
            #define FISH_GROUND_MAP_SAMPLER sampler_LinearClamp
            #include "FishGroundMap.hlsl"
            #include "FishMoss.hlsl"
            #include "FishGroundContactLit.hlsl"
            #include "FishIndoor.hlsl"
            // FishWeatherLitForwardPass.hlsl's share of the sky's ambient a room's inside loses.
            #define FISH_INDOOR_DIM 0.65h
            #define FISH_FILLET_PASS_FORWARD
            #include "FishRockFilletPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FishFilletShadowVertex
            #pragma fragment FishFilletShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "FishWeatherLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishSurface.hlsl"
            #define FISH_FILLET_PASS_SHADOW
            #include "FishRockFilletPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FishFilletDepthVertex
            #pragma fragment FishFilletDepthFragment

            #include "FishWeatherLitInput.hlsl"
            #include "FishSurface.hlsl"
            #define FISH_FILLET_PASS_DEPTH
            #include "FishRockFilletPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 4.5
            #pragma vertex FishFilletDepthVertex
            #pragma fragment FishFilletDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "FishWeatherLitInput.hlsl"
            #include "FishSurface.hlsl"
            #define FISH_FILLET_PASS_DEPTHNORMALS
            #include "FishRockFilletPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
