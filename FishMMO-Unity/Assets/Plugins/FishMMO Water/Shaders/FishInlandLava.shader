// Inland lava: a lava tube's rivers and pits, molten rock running down the tube's floor and standing in its chambers,
// drawn by InlandWaterRenderer on the same lake and river meshes as the inland water (SceneHydrology bodies flagged Lava).
// The lava sea's surface (FishLava.hlsl, FishLavaPasses.hlsl) with FISH_LAVA_INLAND: the mesh's own heights (a river
// falls along its run), the inland water's clock, the river's current under the crust, and only the lava's own glow to
// light it where no sky reaches. Opaque, as the sea's lava. Its properties are the lava's own, one for one: the two
// share one UnityPerMaterial (FishLava.hlsl); the defaults here are a tube's: ACTIVE lava, fed by the tube's rivers, so
// small plates (scaled again to each body, InlandWaterRenderer.Lava), wide seams, a crust that stays hot for tens of
// seconds (rinds and plates glow orange-red, only the oldest dull to dark red), frequent founderings and bursts, and
// a brighter melt with the dark-adapted eye's harder compression, so the lava is the brightest thing in a dark cave.
// A fall is a pour of its own (LavaPourFragment): no plates, streaks of melt drawn down the run.
Shader "FishMMO/Water/Inland Lava"
{
    Properties
    {
        [Header(Heat)]
        // Basaltic lava erupts at about 1150 to 1200 C. Io's are hotter, ultramafic, 1600 K and up.
        _MeltKelvin ("Melt temperature (K)", Range(900, 2000)) = 1450
        _CrustFloorKelvin ("Old crust surface (K)", Range(250, 900)) = 760
        // Open melt skins over within seconds: about 1170 K at 3 s, 840 K at half a minute.
        _CoolingSeconds ("Crust cooling time (s)", Range(0.5, 120)) = 18
        _Exposure ("Brightness of the melt", Range(0, 4)) = 3
        _Response ("Glow response at night: 1 a camera, 0.4 an eye", Range(0.1, 1)) = 0.32
        // In daylight the eye adapts to the sunlit crust and only the cracks and open melt glow.
        _DayResponse ("Glow response in daylight", Range(0.1, 1)) = 0.8
        _DaylightIlluminance ("Light on the lake that is full daylight", Range(0.05, 4)) = 1

        [Header(Crust)]
        _CrustColor ("Old crust albedo (ash, oxidation)", Color) = (0.065, 0.058, 0.052, 1)
        _CrustSmoothness ("Fresh glass smoothness", Range(0, 1)) = 0.78
        _AgedSmoothness ("Old crust smoothness", Range(0, 1)) = 0.42
        _DullingSeconds ("Time the glassy sheen lasts (s)", Range(10, 3600)) = 300
        // Baked by FishMMO/Water/Bake Lava Textures (LavaTextureBaker).
        [NoScaleOffset] [Normal] _CrustNormal ("Crust detail normal (baked)", 2D) = "bump" {}
        [NoScaleOffset] _CrustMask ("Crust tear, roughness, tear, cavity (baked)", 2D) = "white" {}
        _CrustTile ("Crust detail tile (m)", Range(0.5, 10)) = 1
        _CrustDetail ("Crust detail strength", Range(0, 2)) = 1

        [Header(Plates)]
        _PlateSize ("Plate size (m)", Range(1, 60)) = 1.6
        _CrackWidth ("Seam half-width (m)", Range(0.02, 2)) = 0.14
        _CrustRelief ("Plate relief (m)", Range(0, 1)) = 0.06
        _PlateJag ("Torn edge jag (m)", Range(0, 1)) = 0.1
        _FounderChance ("Plates that founder each flow cycle", Range(0, 0.3)) = 0.18

        [Header(Surface)]
        _Spatter ("Bubble bursts (share of 6 m cells per 10 s)", Range(0, 1)) = 0.6
        _SwellHeight ("Swell height (m)", Range(0, 1)) = 0.04
        _SwellLength ("Swell length (m)", Range(5, 200)) = 8

        [Header(Flow)]
        _FlowSpeed ("Crust drift (m per s)", Range(0, 1)) = 0.08
        _CellSize ("Convection cell size (m)", Range(10, 400)) = 10
        // Both should divide 10 000 (the inland clock's wrap), or the surface jumps once every 2.8 hours. Shorter
        // than the sea's: a river's current stretches the crust across a cycle, and a tube's lava runs fast.
        _FlowPeriod ("Flow map cycle (s)", Range(5, 200)) = 8
        _OverturnSeconds ("Convection overturn (s)", Range(60, 5000)) = 200

        [Header(Glow on the surroundings and fumes)]
        // There is no albedo buffer in a forward renderer, so the glow is added as the light a
        // surface of this albedo would send back. Basalt and most rock run 0.1 to 0.2.
        _GroundAlbedo ("Albedo the glow assumes", Range(0, 1)) = 0.15
        _FumeDensity ("Fume extinction at the surface (per m)", Range(0, 0.05)) = 0.006
        _FumeHeight ("Fume scale height (m)", Range(2, 200)) = 25
        _FumeScale ("Fume billow size (m)", Range(2, 200)) = 22
        _FumeRise ("Fume rise (m per s)", Range(0, 10)) = 1.5

        [Header(Development)]
        [Enum(Off, 0, Temperature, 1, Age, 2, Flow, 3, Seams, 4, Freshness, 5, Normal, 6, Events, 7)]
        _DebugView ("Show one term", Float) = 0
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

        // As the lava sea's (FishLava.shader), on InlandWaterRenderer's meshes; nothing of the inland water's draw-once
        // stencil: opaque, the depth test settles where a river meets its lake (the lake is laid a little under its level).
        // OPAQUE, in the Geometry queue, writing depth. Lava cannot be seen into, so there is nothing
        // for it to blend with, and as an opaque it is drawn before the weather's clouds and fog — which
        // then cover it through the depth it wrote, exactly as they cover the ground. (The sea is
        // transparent and has to re-apply both to itself.)
        //
        // NO ShadowCaster, deliberately: a flat horizontal sheet can only shadow what lies below it,
        // and everything below it is hidden by it. Casting would cost a draw of a disc kilometres
        // across into every cascade and could only ever self-shadow. It RECEIVES shadows.
        //
        // Cull Off: seen from under it (a camera dipped into a channel), the lava is still there.
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
            #pragma target 3.5

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "Assets/Prefabs/Client/Weather/Shaders/FishShadowVariants.hlsl"
            #include_with_pragmas "Assets/Prefabs/Client/Weather/Shaders/FishLightCookieVariants.hlsl"

            #define FISH_LAVA_INLAND 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishLavaPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex LavaDepthVertex
            #pragma fragment LavaDepthFragment
            #pragma target 3.5
            #pragma multi_compile_instancing

            #define FISH_LAVA_INLAND 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishLavaPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex LavaDepthVertex
            #pragma fragment LavaDepthNormalsFragment
            #pragma target 3.5
            #pragma multi_compile_instancing

            #define FISH_LAVA_INLAND 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishLavaPasses.hlsl"
            ENDHLSL
        }
    }

    // No fallback: a fallback to Lit would quietly draw a grey plane wherever this fails to compile.
}
