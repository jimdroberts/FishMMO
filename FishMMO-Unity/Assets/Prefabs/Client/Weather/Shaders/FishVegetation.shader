// Grass, leaves, bark and tree billboards for the generated biome art. Alpha-clipped, optionally
// two-sided, GPU instanced (terrain detail prototypes need that), and weather-aware through the
// client's shader globals only: wind bends it, snow buries and whitens it, rain darkens it, and the
// season and drought tint it. See FishVegetationPasses.hlsl for what each global is and who sets it.
// Every pass dithers the same way — LOD cross-fades (LOD_FADE_CROSSFADE) and the distance fade —
// so a plant's shadow and depth disappear with it.
//
// Per-instance LOD fade (FishLodFade.hlsl): `_FishLodFade`, an instanced float in its own instancing
// buffer (FishLodFadeProps, INSTANCING_ON variants only — UnityPerMaterial and the SRP Batcher are
// untouched). Set by TerrainTreeInstancing (FishMMO.Client/World/Terrain) as a MaterialPropertyBlock
// float array, one entry per RenderMeshInstanced instance. 0 = fully drawn (default; every other
// draw); +f = fading in, draws share f; -f = fading out, draws exactly the complement of +f. The two
// levels of a transition get +f and -f. Read in ForwardLit, ShadowCaster, DepthOnly and DepthNormals,
// independent of LOD_FADE_CROSSFADE / unity_LODFade, which LODGroup renderers keep using.
//
// FishMMO/Vegetation Indirect (FishVegetationIndirect.shader) is this shader for GPU-driven draws:
// same Properties, same passes, same FishVegetationPasses.hlsl, plus procedural instancing. Keep the
// two in step — IndirectShaderTests compares them.
Shader "FishMMO/Vegetation"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (A cutout)", 2D) = "white" {}
        [MainColor] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha cutoff", Range(0.0, 1.0)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clip", Float) = 1.0
        [Toggle(_NORMALMAP)] _UseNormalMap("Normal map", Float) = 0.0
        [Normal] _BumpMap("Normal map", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1.0
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.2
        _Translucency("Translucency (light through leaves)", Range(0.0, 2.0)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0.0
        [Toggle] _BackfaceFlip("Flip normal on back faces (flat parts; off for bent-normal cards)", Float) = 0.0

        [Header(Wind)]
        _WindSway("Sway (m at sway weight 1, full wind)", Range(0.0, 2.0)) = 0.4
        _WindFlutter("Flutter (m)", Range(0.0, 0.2)) = 0.03
        _WindFrequency("Frequency", Range(0.1, 4.0)) = 1.2

        [Header(Season)]
        _HealthyColor("Healthy tint", Color) = (0.9, 0.95, 0.9, 1)
        _DryColor("Dry tint", Color) = (0.75, 0.7, 0.55, 1)
        _TintSpread("Tint spread between neighbours", Range(0.0, 1.0)) = 0.35
        [Toggle] _Deciduous("Deciduous (turns in autumn, bare in winter)", Float) = 0.0
        [HDR] _AutumnColor("Autumn multiplier", Color) = (1.6, 0.8, 0.3, 1)

        [Header(Weather)]
        _SnowBury("Sinks into deep snow", Range(0.0, 1.0)) = 0.0
        _FishWeatherAmount("Weather on this surface", Range(0.0, 1.0)) = 1.0

        [Header(Distance and ground)]
        [Enum(None, 0, Detail, 1, Tree, 2)] _DistanceFade("Distance fade (detail: before its patch is culled; tree: before the tree distance)", Float) = 0.0
        [Toggle] _FacingCamera("Turn to face the camera (tree billboards)", Float) = 0.0
        _GroundSink("Ground sink: x min, y max metres (each instance draws between them)", Vector) = (0, 0, 0, 0)
        _TintPatchMetres("Healthy/dry patch size (m, 0 = plant by plant)", Float) = 12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VegVertex
            #pragma fragment VegForwardFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #define FISH_VEG_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishVegetationPasses.hlsl"
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
            #pragma target 3.0
            #pragma vertex VegDepthVertex
            #pragma fragment VegDepthFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #define FISH_VEG_PASS_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishVegetationPasses.hlsl"
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
            #pragma target 3.0
            #pragma vertex VegDepthVertex
            #pragma fragment VegDepthFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing

            #define FISH_VEG_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishVegetationPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex VegVertex
            #pragma fragment VegDepthNormalsFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #define FISH_VEG_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishVegetationPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
