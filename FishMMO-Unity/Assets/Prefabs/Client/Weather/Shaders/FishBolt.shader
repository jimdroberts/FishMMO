// A lightning bolt: a camera-facing ribbon with a hot core, fading over its short life.
Shader "FishMMO/Weather/Bolt"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent+30" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BoltColor; // rgb, a brightness

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float across = abs(input.uv.x * 2.0 - 1.0);
                float core = exp(-across * across * 30.0);
                float glow = exp(-across * across * 4.0) * 0.35;
                float a = (core + glow) * input.color.a;
                return half4(_BoltColor.rgb * _BoltColor.a * (1.0 + core * 2.0), saturate(a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
