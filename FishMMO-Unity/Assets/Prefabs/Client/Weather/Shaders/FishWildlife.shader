// The world's networked wild animals (WildlifeBody, FishMMO.Shared/Entity/Wildlife): one procedural low-poly mesh per species
// (WildlifeBodyMesh), posed entirely here from four numbers a renderer's property block sets each frame: the gait (phase, leg
// swing, gallop, bob) and the pose (attack lunge, fall onto the side, tail wag, the side's offset). Colours ride the mesh. Pass
// code and the vertex contract are in FishWildlife.hlsl; the lighting follows FishAmbientLife's.
Shader "FishMMO/Wildlife"
{
    Properties
    {
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.15
        [HideInInspector] _WildGait("Gait (phase, swing, gallop, bob)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _WildPose("Pose (attack, death, wag, side)", Vector) = (0, 0, 0, 0.2)
        [HideInInspector] _WildFlight("Flight (wing angle, height, -, -)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex WildlifeVertex
            #pragma fragment WildlifeForwardFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "Assets/FishMMO/Shaders/FishShadowVariants.hlsl"
            #include_with_pragmas "Assets/FishMMO/Shaders/FishLightCookieVariants.hlsl"
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define FISH_WILDLIFE_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWildlife.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex WildlifeDepthVertex
            #pragma fragment WildlifeDepthFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #define FISH_WILDLIFE_PASS_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishWildlife.hlsl"
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
            #pragma target 3.5
            #pragma vertex WildlifeDepthVertex
            #pragma fragment WildlifeDepthFragment

            #define FISH_WILDLIFE_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWildlife.hlsl"
            ENDHLSL
        }

        // The target outline's mask, drawn in the pose (TargetOutlineFeature uses a material's own "FishOutlineMask" pass
        // when it has one). With the generic mask the outline was the unposed mesh: a dead animal's outline stood up.
        Pass
        {
            Name "FishOutlineMask"
            Tags { "LightMode" = "FishOutlineMask" }
            ZTest Always ZWrite Off Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex WildlifeOutlineVertex
            #pragma fragment WildlifeOutlineFragment

            #define FISH_WILDLIFE_PASS_OUTLINE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWildlife.hlsl"
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
            #pragma target 3.5
            #pragma vertex WildlifeVertex
            #pragma fragment WildlifeDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_WILDLIFE_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWildlife.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
