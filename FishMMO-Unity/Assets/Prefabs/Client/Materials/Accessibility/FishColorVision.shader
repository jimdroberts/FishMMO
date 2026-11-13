// The colour-vision correction (ColorVisionFeature): daltonisation of the frame after post-processing.
// The colour a dichromat sees is simulated (Machado, Oliveira and Fernandes 2009, severity 1, linear RGB),
// what is lost (the original less the simulation) is moved into channels that eye does see, and the
// result is blended over the original by the player's strength.
Shader "Hidden/FishMMO/ColorVision"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Blend Off

        Pass
        {
            Name "Daltonise"

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment DaltoniseFrag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // The simulation matrix's rows, linear RGB.
            float4 _CvdSimR;
            float4 _CvdSimG;
            float4 _CvdSimB;
            // x: strength 0 to 1. y: 1 to move the loss into red and green (blue-yellow), 0 into green and blue (red-green).
            float4 _CvdParams;

            half4 DaltoniseFrag(Varyings input) : SV_Target
            {
                float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                float3 c = source.rgb;
                float3 seen = float3(dot(_CvdSimR.rgb, c), dot(_CvdSimG.rgb, c), dot(_CvdSimB.rgb, c));
                float3 lost = c - seen;
                float3 moved = _CvdParams.y > 0.5
                    ? float3(lost.r + 0.7 * lost.b, lost.g + 0.7 * lost.b, 0.0)
                    : float3(0.0, lost.g + 0.7 * lost.r, lost.b + 0.7 * lost.r);
                float3 corrected = max(c + moved, 0.0);
                return half4(lerp(c, corrected, saturate(_CvdParams.x)), source.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
