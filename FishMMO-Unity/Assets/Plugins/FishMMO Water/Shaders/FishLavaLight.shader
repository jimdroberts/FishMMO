// The lava's glow on its surroundings and the fumes over it — FishLavaLight.hlsl. A projected,
// full-screen pass that WaterSurface draws while its liquid is Lava, on a child of its own (as it draws
// the caustics), with a material it copies from the lava's every frame.
//
// The Properties are the lava's own, EXACTLY (FishWaterMaterialTests pins it): this pass includes the
// lava's UnityPerMaterial whole, and a member of it not declared here would read zero.
Shader "FishMMO/Water/Lava Light"
{
    Properties
    {
        [Header(Heat)]
        // Basaltic lava erupts at about 1150 to 1200 C. Io's are hotter, ultramafic, 1600 K and up.
        _MeltKelvin ("Melt temperature (K)", Range(900, 2000)) = 1420
        _CrustFloorKelvin ("Old crust surface (K)", Range(250, 900)) = 550
        // Open melt skins over within seconds: about 1170 K at 3 s, 840 K at half a minute.
        _CoolingSeconds ("Crust cooling time (s)", Range(0.5, 120)) = 3
        _Exposure ("Brightness of the melt", Range(0, 4)) = 2
        _Response ("Glow response at night: 1 a camera, 0.4 an eye", Range(0.1, 1)) = 0.45
        // In daylight the eye adapts to the sunlit crust and only the cracks and open melt glow.
        _DayResponse ("Glow response in daylight", Range(0.1, 1)) = 1
        _DaylightIlluminance ("Light on the lake that is full daylight", Range(0.05, 4)) = 1

        [Header(Crust)]
        _CrustColor ("Old crust albedo (ash, oxidation)", Color) = (0.065, 0.058, 0.052, 1)
        _CrustSmoothness ("Fresh glass smoothness", Range(0, 1)) = 0.78
        _AgedSmoothness ("Old crust smoothness", Range(0, 1)) = 0.42
        _DullingSeconds ("Time the glassy sheen lasts (s)", Range(10, 3600)) = 300
        // Baked by FishMMO/Water/Bake Lava Textures (LavaTextureBaker).
        [NoScaleOffset] [Normal] _CrustNormal ("Crust detail normal (baked)", 2D) = "bump" {}
        [NoScaleOffset] _CrustMask ("Crust tear, roughness, tear, cavity (baked)", 2D) = "white" {}
        _CrustTile ("Crust detail tile (m)", Range(0.5, 10)) = 2.5
        _CrustDetail ("Crust detail strength", Range(0, 2)) = 1

        [Header(Plates)]
        _PlateSize ("Plate size (m)", Range(1, 60)) = 12
        _CrackWidth ("Seam half-width (m)", Range(0.02, 2)) = 0.16
        _CrustRelief ("Plate relief (m)", Range(0, 1)) = 0.25
        _PlateJag ("Torn edge jag (m)", Range(0, 1)) = 0.3
        _FounderChance ("Plates that founder each flow cycle", Range(0, 0.3)) = 0.05

        [Header(Surface)]
        _Spatter ("Bubble bursts (share of 6 m cells per 10 s)", Range(0, 1)) = 0.35
        _SwellHeight ("Swell height (m)", Range(0, 1)) = 0.15
        _SwellLength ("Swell length (m)", Range(5, 200)) = 35

        [Header(Flow)]
        _FlowSpeed ("Crust drift (m per s)", Range(0, 1)) = 0.12
        _CellSize ("Convection cell size (m)", Range(10, 400)) = 80
        // Both should divide 10 000 (WaterSurface's clock wrap), or the surface jumps once every 2.8 hours.
        _FlowPeriod ("Flow map cycle (s)", Range(5, 200)) = 40
        _OverturnSeconds ("Convection overturn (s)", Range(60, 5000)) = 500

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
        // Transparent, so after the weather's clouds and fog are in the frame — which is why it lays
        // both over what it adds (FishWaterFog.hlsl). Early in the queue: anything that floats in front
        // of the lava sorts over its fumes rather than under them.
        Tags { "Queue" = "Transparent-170" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "LavaLight"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied: frame' = frame · a + rgb, with rgb = glow · T + in-scatter and a = T.
            Blend One SrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex LavaLightVertex
            #pragma fragment LavaLightFragment
            #pragma target 3.5
            // Set by WaterSurface when the pipeline makes a depth texture; without one there is
            // nothing to project onto and the pass adds nothing.
            #pragma multi_compile_fragment _ _WATER_DEPTH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishLavaLight.hlsl"
            ENDHLSL
        }
    }
}
