// The background creatures of the sea (SeaLifeSystem, FishMMO.Client/World/SeaLife): schools of fish,
// reef fish, sharks, rays, turtles, whales, jellyfish and crabs. Pass code and the vertex contract are in
// FishSeaLife.hlsl. Drawn only with Graphics.DrawMeshInstanced, one runtime material per kind.
//
// Opaque kinds draw in the opaque queue, so the sea refracts them from above and the underwater pass
// fogs them with everything else; jellies draw blended (premultiplied), after the sea's surface and
// before the underwater pass (Transparent-95), with no depth and no shadow.
Shader "FishMMO/Sea Life"
{
    Properties
    {
        [Enum(Fish, 0, Whale, 1, Ray, 2, Turtle, 3, Jelly, 4, Crab, 5)] _Mode("Motion", Float) = 0
        _Amplitude("Stroke (share of length)", Range(0.0, 0.5)) = 0.08
        _WaveLength("Body wave (body lengths)", Range(0.2, 3.0)) = 0.9
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        [Toggle] _Translucent("Translucent (jellies)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 0
        [Toggle] _ZWrite("Write depth", Float) = 1
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
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SeaLifeVertexMain
            #pragma fragment SeaLifeForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define FISH_SEA_LIFE_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishSeaLife.hlsl"
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
            #pragma target 3.5
            #pragma vertex SeaLifeDepthVertex
            #pragma fragment SeaLifeDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #define FISH_SEA_LIFE_PASS_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishSeaLife.hlsl"
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
            #pragma target 3.5
            #pragma vertex SeaLifeDepthVertex
            #pragma fragment SeaLifeDepthFragment
            #pragma multi_compile_instancing

            #define FISH_SEA_LIFE_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishSeaLife.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SeaLifeVertexMain
            #pragma fragment SeaLifeDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_SEA_LIFE_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishSeaLife.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
