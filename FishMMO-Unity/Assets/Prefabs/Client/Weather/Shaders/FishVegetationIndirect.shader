// FishMMO/Vegetation for GPU-driven draws: the terrain's trees and details drawn by
// TerrainTreeInstancing (FishMMO.Client/World/Terrain) with Graphics.RenderMeshIndirect, culled on the
// GPU. Each instance's transform and LOD fade come from compute-written buffers through a procedural
// setup (FishIndirectSetup, FishIndirectInstancing.hlsl — the buffer contract is in that file).
//
// Why a second shader rather than a keyword on the first: `instancing_options procedural:` changes
// what the instanced variant IS — its matrices come from the setup, not from Unity's instancing
// arrays — so the original would stop working for everything Unity draws instanced itself (the
// terrain's own detail and tree painting, LODGroups, DrawMeshInstanced). Kept apart,
// FishMMO/Vegetation is byte-for-byte what it was, and the renderer clones a material and swaps in
// this shader.
//
// So the clone keeps every value, the Properties block is IDENTICAL to FishVegetation.shader's, and
// every pass is the same pass: the same pragmas, the same render state, the same
// FishVegetationPasses.hlsl. The only differences: each pass adds
// `#pragma instancing_options procedural:FishIndirectSetup` and includes FishIndirectInstancing.hlsl
// before the pass code (so FishLodFade reads the indirect fade). Everything the pass code reads from
// the object matrix — VegFaceCamera's billboard scale, the distance fade's origin, the ground sink
// and the tint hashes — reads the instance, because the setup writes unity_ObjectToWorld, which is
// what UNITY_MATRIX_M is under procedural instancing. UnityPerMaterial is untouched;
// _FishVisibleBase is a per-draw $Globals uniform. IndirectShaderTests compares the two shaders pass
// by pass, so edit both together.
Shader "FishMMO/Vegetation Indirect"
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
            #pragma instancing_options procedural:FishIndirectSetup
            #pragma instancing_options renderinglayer

            #define FISH_VEG_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishIndirectInstancing.hlsl"
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
            #pragma instancing_options procedural:FishIndirectSetup

            #define FISH_VEG_PASS_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishIndirectInstancing.hlsl"
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
            #pragma instancing_options procedural:FishIndirectSetup

            #define FISH_VEG_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishIndirectInstancing.hlsl"
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
            #pragma instancing_options procedural:FishIndirectSetup

            #define FISH_VEG_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishIndirectInstancing.hlsl"
            #include "FishVegetationPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
