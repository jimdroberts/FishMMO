// A distant rain (snow, sand, ash) curtain under a storm cell: an open cylinder with falling
// streaks, soft at its edges and top, tinted by what falls. Drawn for the nearest cells beyond
// the particle field.
Shader "FishMMO/Weather/Curtain"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishSkyCommon.hlsl"

            float4 _CurtainColor;   // rgb tint, a strength
            float4 _CurtainParams;  // x fall speed (m/s), y time (s), z streak scale, w snow-ness (0 streaks … 1 flakes)

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float fogCoord : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 view = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                // Thin at grazing angles, so the cylinder reads as a soft volume, not a tube.
                float facing = abs(dot(normalize(input.normalWS), view));
                float edge = pow(saturate(facing), 0.7);
                float top = smoothstep(1.0, 0.7, input.uv.y) * smoothstep(0.0, 0.08, input.uv.y);
                // What falls, falls at its own speed in metres: laid on the world's height, not the
                // cylinder's 0..1. Scrolled in the cylinder's units, rain came down a 1300 m curtain at
                // 260 m/s and snow at 35, and the body drifted UP at 22. A streak is ~5 km tall in
                // rain and ~430 m in snow, as before; the body, the shaft's thicker and thinner
                // stretches, comes down with it.
                float fallen = input.positionWS.y + _CurtainParams.y * _CurtainParams.x;
                float streakMetres = lerp(5400.0, 430.0, _CurtainParams.w);
                float streaks = FishNoise(float2(input.uv.x * _CurtainParams.z, fallen / streakMetres), 2);
                float body = FishNoise(float2(input.uv.x * 2.0, fallen / 2200.0), 0);
                float alpha = saturate(streaks * 0.6 + body * 0.6) * edge * top * _CurtainColor.a;
                Light mainLight = GetMainLight();
                half3 light = SampleSH(half3(0, 1, 0)) + mainLight.color * 0.25;
                half3 color = _CurtainColor.rgb * light;
                color = MixFog(color, input.fogCoord);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
