Shader "FishMMO/Water/Inland Water"
{
    // Lakes and rivers: one shader for both, so where they meet there is nothing to differ.
    //
    // Their colour is the water column behind each pixel (the depth texture), not the mesh, so a river
    // running into a lake changes colour only as its depth does. Their ripples ride the scene's one flow
    // field, read per vertex at the vertex's own position (SceneWaterBodies.FlowAt), so two meshes meeting
    // at a confluence or a mouth carry the same current where they meet. And each pixel of inland water
    // is drawn once (the stencil below), so where two meshes overlap the water is not laid on twice.
    Properties
    {
        [Header(Colour)]
        _ShallowColor("Shallow colour", Color) = (0.20, 0.42, 0.40, 1)
        _DeepColor("Deep colour", Color) = (0.03, 0.12, 0.16, 1)
        _WaterDensity("Absorption per metre (rgb)", Vector) = (0.45, 0.12, 0.08, 0)
        _Turbidity("Silt in moving water", Range(0, 1)) = 0.35
        _SiltColor("Silt colour", Color) = (0.36, 0.33, 0.24, 1)

        [Header(Surface)]
        [NoScaleOffset] _NormalMap("Ripples", 2D) = "bump" {}
        _NormalScale("Ripple size (m)", Float) = 3
        _NormalStrength("Ripple strength", Range(0, 2)) = 0.6
        _NormalFadeDistance("Ripples fade by (m)", Float) = 250
        _FlowCycle("Flow-map cycle (s)", Float) = 2
        _Smoothness("Smoothness", Range(0, 1)) = 0.94
        _ReflectionStrength("Reflection", Range(0, 2)) = 1
        _SpecularStrength("Sun glint", Range(0, 4)) = 1.2
        _RefractionStrength("Refraction", Range(0, 1)) = 0.25
        _EdgeFade("Shore softness (m)", Float) = 0.4
        _MaxAlpha("Opacity without refraction", Range(0, 1)) = 0.85

        [Header(Foam)]
        [NoScaleOffset] _FoamTexture("Foam", 2D) = "black" {}
        _FoamScale("Foam size (m)", Float) = 2.5
        _FoamColor("Foam colour", Color) = (0.92, 0.94, 0.95, 1)
        // Runtime per-surface values must also be ShaderLab properties for SRP Batcher compatibility.
        [HideInInspector] _RiverFlowInfo("_RiverFlowInfo", Vector) = (0,0,0,0)
        [HideInInspector] _RiverFall0("_RiverFall0", Vector) = (0,0,0,0)
        [HideInInspector] _RiverFall1("_RiverFall1", Vector) = (0,0,0,0)
        [HideInInspector] _RiverFall2("_RiverFall2", Vector) = (0,0,0,0)
        [HideInInspector] _RiverFall3("_RiverFall3", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook0("_InlandLook0", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook1("_InlandLook1", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook2("_InlandLook2", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook3("_InlandLook3", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook4("_InlandLook4", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook5("_InlandLook5", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook6("_InlandLook6", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook7("_InlandLook7", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook8("_InlandLook8", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook9("_InlandLook9", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook10("_InlandLook10", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook11("_InlandLook11", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook12("_InlandLook12", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook13("_InlandLook13", Vector) = (0,0,0,0)
        [HideInInspector] _InlandLook14("_InlandLook14", Vector) = (0,0,0,0)
        [HideInInspector] _RiverFallCount("_RiverFallCount", Float) = 0
        [HideInInspector] _InlandLookOn("_InlandLookOn", Float) = 0
        [HideInInspector] _RiverFlowField("_RiverFlowField", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-99"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // After the sea (Transparent-100), so a river fading into the sea is drawn over it; before
        // everything that floats in it. No ZWrite and no shadow caster, as the sea.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        // Each pixel of inland water once: the first mesh drawn there (the lake, then the larger river)
        // claims it, and a second covering it is not composited over the first. Nor inside a floating hull
        // (FishHullLid.shader sets bit 128 across its open top). Both bits read: 64 > (stencil & 192) only where
        // neither is set, and the pass writes 64 alone.
        Stencil
        {
            Ref 64
            ReadMask 192
            WriteMask 64
            Comp Greater
            Pass Replace
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex InlandVertex
            #pragma fragment InlandFragment
            #pragma target 3.5

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "Assets/Prefabs/Client/Weather/Shaders/FishShadowVariants.hlsl"
            #include_with_pragmas "Assets/Prefabs/Client/Weather/Shaders/FishLightCookieVariants.hlsl"
            // The sea's global quality keywords, set against what the running URP asset provides.
            #pragma multi_compile_fragment _ _WATER_DEPTH
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishInlandWater.hlsl"
            ENDHLSL
        }

        // The water's current and the foam it makes, drawn from above into the simulated foam's window
        // (WaterFoamField draws it with a command buffer; no camera draws a pass of this light mode).
        Pass
        {
            Name "FoamSource"
            Tags { "LightMode" = "FishFoamSource" }
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Off
            Stencil
            {
                Ref 0
                Comp Always
                Pass Keep
            }

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex FoamSourceVertex
            #pragma fragment FoamSourceFragment
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishInlandWater.hlsl"
            ENDHLSL
        }
    }
}
