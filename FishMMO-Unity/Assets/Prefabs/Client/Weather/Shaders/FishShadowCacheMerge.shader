// The cached main-light shadows (CachedMainLightShadows.cs). Pass 0 merges a cascade's window of the static-caster cache
// into URP's shadow map: one clip-space triangle over URP's tile, each texel reading the window's texel it maps to (a whole-
// texel shift: CachedMainLightShadows.TryMap) and writing that depth remapped to URP's depth range, kept by the depth test only
// where nearer to the light than what URP drew (the live casters) — the same depth drawing both together gives. Texels the
// window holds no caster on are discarded. Pass 1 clears a cache tile to the far plane before its casters are redrawn. No
// colour: shadow maps are depth only.
Shader "Hidden/FishMMO/Shadow Cache Merge"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _FishShadowCacheZTest("Merge depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Merge"
            ZWrite On
            ZTest [_FishShadowCacheZTest]
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex FishShadowCacheVertex
            #pragma fragment FishShadowCacheMerge
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            // The cache is a depth texture read by texel (point sampled at texel centres: no filtering, no comparison).
            TEXTURE2D_FLOAT(_FishShadowCache);
            SAMPLER(sampler_PointClamp);
            // The cascade's map (CachedMainLightShadows.Mapping): cache atlas uv = URP NDC xy * xy + zw; and its depth: x scale, y offset.
            float4 _FishShadowCacheMap;
            float4 _FishShadowCacheDepthMap;
            // What a cache texel with no caster holds (the far plane, as the clear pass wrote it).
            float _FishShadowCacheClearDepth;

            struct FishShadowCacheVaryings
            {
                float4 position : SV_POSITION;
                float2 ndc : TEXCOORD0;
            };

            FishShadowCacheVaryings FishShadowCacheVertex(uint id : SV_VertexID)
            {
                // One triangle over the viewport (URP's cascade tile), carrying its own NDC.
                float2 uv = float2((id << 1) & 2, id & 2);
                FishShadowCacheVaryings o;
                o.ndc = uv * 2.0 - 1.0;
                o.position = float4(o.ndc, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
                return o;
            }

            float FishShadowCacheMerge(FishShadowCacheVaryings i) : SV_Depth
            {
                float cached = SAMPLE_TEXTURE2D_LOD(_FishShadowCache, sampler_PointClamp, i.ndc * _FishShadowCacheMap.xy + _FishShadowCacheMap.zw, 0).r;
                if (abs(cached - _FishShadowCacheClearDepth) < 1e-6)
                {
                    discard;
                }
                // A caster nearer the light than URP's near plane clamps to it, as URP's own casters do (pancaking).
                return saturate(cached * _FishShadowCacheDepthMap.x + _FishShadowCacheDepthMap.y);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Clear"
            ZWrite On
            ZTest Always
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex FishShadowCacheVertex
            #pragma fragment FishShadowCacheClear
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            float4 FishShadowCacheVertex(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2.0 - 1.0, UNITY_RAW_FAR_CLIP_VALUE, 1.0);
            }

            // The far plane's raw depth (set per frame by the cache pass; a diagnostic may set another).
            float _FishShadowCacheClearDepth;

            float FishShadowCacheClear(float4 position : SV_POSITION) : SV_Depth
            {
                return _FishShadowCacheClearDepth;
            }
            ENDHLSL
        }
    }
}
