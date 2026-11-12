// Include immediately before Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl wherever a DOTS-capable
// pass reaches it first. In DXC (Vulkan) variants with DOTS_INSTANCING_ON it runs Core.hlsl's own prelude and then
// FishDxcDotsInput.hlsl in place of URP's Input.hlsl (same include guard, so Core.hlsl skips URP's), which fixes how DXC
// indexes the DOTS builtin metadata; see the edit marked there. Every other variant compiles exactly as before.
// It must come before Core.hlsl, not first in the pass: Input.hlsl reads defines that earlier headers set
// (FishShadowVariants.hlsl's LIGHT_SHADOWS_NO_CBUFFER).
#ifndef FISHMMO_DXC_DOTS_CORE_INCLUDED
#define FISHMMO_DXC_DOTS_CORE_INCLUDED

#if defined(UNITY_COMPILER_DXC) && defined(DOTS_INSTANCING_ON)
    #ifdef UNIVERSAL_INPUT_INCLUDED
        #error "URP's Input.hlsl was included before FishDxcDotsCore.hlsl: include it ahead of the first path to Core.hlsl, or DXC reads the wrong DOTS metadata."
    #endif

    // URP 17.6 Core.hlsl, everything ahead of its Input.hlsl include (it repeats these harmlessly afterwards).
    #define FORCE_VIRTUAL_TEXTURING_OFF 1
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ForwardPlusKeyword.deprecated.hlsl"
    #if defined(_CLUSTER_LIGHT_LOOP)
        #define USE_CLUSTER_LIGHT_LOOP 1
    #else
        #define USE_CLUSTER_LIGHT_LOOP 0
    #endif
    #if USE_CLUSTER_LIGHT_LOOP
        #define _ADDITIONAL_LIGHTS 1
        #undef _ADDITIONAL_LIGHTS_VERTEX
    #endif
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Version.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"
    #include "Assets/Prefabs/Client/Weather/Shaders/FishDxcDotsInput.hlsl"
#endif

#endif
