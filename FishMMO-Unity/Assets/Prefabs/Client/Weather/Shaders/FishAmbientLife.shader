// The land's background creatures (AmbientLifeSystem, FishMMO.Client/World/AmbientLife): birds alone and in
// flocks, raptors, bats, rats, mice, squirrels, rabbits, lizards, shore crabs and frogs, beetles, and insect swarms
// (midges, mosquitoes, flies, fireflies, bees, locusts), each group in its biome's look (AmbientLooks), and the caves'
// creatures (AmbientCaveLife), lit underground only by the cave's own lights (FishUnderground, additional lights) and, the
// light-makers among them (2026-10-20: millipedes, snails, click beetles, lanternfish, jellies, wisp moths, newts, ember mites,
// ice worms), by their own: a per-kind emission over the mesh's mask on the shared clock (AmbientGlow). Pass code and the vertex
// contract are in FishAmbientLife.hlsl. Drawn only with Graphics.DrawMeshInstanced, one runtime
// material per kind; opaque, dithered out at the draw distance and when an animal goes to ground.
Shader "FishMMO/Ambient Life"
{
    Properties
    {
        [Enum(Bird, 0, Walker, 1, Lizard, 2, Crab, 3, Swarm, 4, Jelly, 5)] _Mode("Motion", Float) = 0
        _Amplitude("Stroke (beat radians, or stride share)", Range(0.0, 1.5)) = 0.8
        _Dihedral("Glide dihedral (radians)", Range(-0.3, 0.5)) = 0.1
        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.25
        _Shoulder("Shoulder (xyz) and neck (w)", Vector) = (0.08, 0.25, 0.08, 0.15)
        _SwarmStyle("Swarm style (column, cloud, buzz, drift, hover, plague, spores)", Float) = 0
        _BugSize("Swarm insect length (m)", Float) = 0.006
        _GlowColour("Swarm glow (rgb linear HDR, a on)", Vector) = (0, 0, 0, 0)
        // Its own light (AmbientGlow): the colour at its brightest, and how it runs over the body.
        _EmitColour("Own light (rgb linear HDR, a on)", Vector) = (0, 0, 0, 0)
        _EmitPattern("Own light's pattern (x even/wave/twinkle, y crests, z sharpness, w rest)", Vector) = (0, 1, 1, 1)
        // The biome looks (AmbientLooks): how far the kind takes them, and its own colours the mesh's colour roles swap out.
        [HideInInspector] _LookMix("Look mix (coat, accents, markings, own brightness kept)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _OwnBack("Own back (linear)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _OwnBelly("Own belly (linear)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _OwnAccent("Own accent (linear)", Vector) = (0, 0, 0, 0)
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
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex AmbientLifeVertexMain
            #pragma fragment AmbientLifeForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            // The cave's torches and crystals and a carried light (additional lights; Forward+ clusters them).
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define FISH_AMBIENT_LIFE_PASS_FORWARD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishAmbientLife.hlsl"
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
            #pragma target 3.5
            #pragma vertex AmbientLifeDepthVertex
            #pragma fragment AmbientLifeDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #define FISH_AMBIENT_LIFE_PASS_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "FishAmbientLife.hlsl"
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
            #pragma target 3.5
            #pragma vertex AmbientLifeDepthVertex
            #pragma fragment AmbientLifeDepthFragment
            #pragma multi_compile_instancing

            #define FISH_AMBIENT_LIFE_PASS_DEPTH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishAmbientLife.hlsl"
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
            #pragma target 3.5
            #pragma vertex AmbientLifeVertexMain
            #pragma fragment AmbientLifeDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #define FISH_AMBIENT_LIFE_PASS_DEPTHNORMALS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishAmbientLife.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
