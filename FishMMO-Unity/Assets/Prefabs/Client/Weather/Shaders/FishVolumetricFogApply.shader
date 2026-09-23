Shader "Hidden/FishMMO/Weather/VolumetricFogApply"
{
    // Reads the integrated froxel volume at (uv, depth) and composites it over the frame.
    //
    // All the work happened in the compute passes; this is a lookup. The volume already holds
    // accumulated in-scattered light and transmittance per froxel, so a pixel only has to find its
    // slice and blend.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FishVolumetricFogApply"
            Blend One SrcAlpha   // rgb adds the scattered light; alpha carries transmittance

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE3D(_FishVolFogVolume);
            SAMPLER(sampler_FishVolFogVolume);

            float4 _FishVolFogRange;   // x near, y far, z depth curve exponent, w slices

            // The inverse of the compute pass's exponential slice spacing: a distance back to the
            // 0..1 slice coordinate it was written at. The two MUST agree, or the fog sits at the
            // wrong depth and objects swim in and out of it as they move.
            float SliceOf(float distance)
            {
                float near = _FishVolFogRange.x;
                float far = _FishVolFogRange.y;
                float clamped = clamp(distance, near, far);
                float ratio = log(clamped / near) / log(far / near);
                return pow(saturate(ratio), 1.0 / max(1e-3, _FishVolFogRange.z));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float eye = LinearEyeDepth(rawDepth, _ZBufferParams);

                /* Nothing drawn here: the sky. Sampled at the far slice so distant fog still tints
                 * the horizon, rather than skipped, which would leave a hard line where the terrain
                 * ends and the sky begins. */
                float slice = SliceOf(eye);

                float4 fog = SAMPLE_TEXTURE3D_LOD(_FishVolFogVolume, sampler_FishVolFogVolume, float3(uv, slice), 0);
                return float4(fog.rgb, saturate(fog.a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
