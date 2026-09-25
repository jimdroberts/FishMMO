// Caustics: sunlight focused onto whatever lies under the water by the waves above it.
//
// Drawn as a PROJECTED pass, like the shore: it reads the depth buffer, reconstructs each pixel's
// world position, and where that lies under the water it multiplies the light already there. So
// the sea bed, a rock, a pier's piles and a swimmer's legs all take the same light, with no mesh.
//
// The light itself — made by the sea overhead, not drawn as a pattern — is FishWaterCausticLight
// in FishWaterCausticsCommon.hlsl, shared with the sea: water that refracts draws a copy of the
// frame taken before this pass runs, so it lights the sea bed it refracts itself. Its settings
// come from WaterSurface as one global, so the two cannot disagree.
Shader "FishMMO/Water/Caustics"
{
    SubShader
    {
        // Before the shore (-150) and the sea (-100), which are drawn over what this lights.
        Tags { "Queue" = "Transparent-160" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Caustics"
            Tags { "LightMode" = "UniversalForward" }

            // Caustics are light focused onto a surface, so they SCALE its lit colour: brighter on
            // the focal lines, darker between them. Anything not under water is multiplied by one.
            Blend DstColor Zero
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            // Set by WaterSurface when the pipeline makes a depth texture; without one there is
            // nothing to project onto.
            #pragma multi_compile_fragment _ _WATER_DEPTH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // The FFT's slope maps and the sea's state, published by WaterSurface.
            TEXTURE2D(_FishWaterDerivatives1);
            TEXTURE2D(_FishWaterDerivatives2);
            float4 _FishWaterPatch;         // xyz the cascades' tile sizes in metres
            float _FishWaterLevel;          // the sea's surface now, tide included
            float _FishWaterCloudShadow;    // 1 under open sky, 0 under cloud

            #include "FishWaterCausticsCommon.hlsl"
            #include "FishWaterFog.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                // Straight to clip space: a full-screen triangle that must not be moved by
                // whatever transform its object happens to have.
                output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.screenUV = input.positionOS.xy * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                    output.screenUV.y = 1.0 - output.screenUV.y;
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half light = 1.0;
                #if defined(_WATER_DEPTH)
                    float rawDepth = SampleSceneDepth(input.screenUV);
                    // The sky is at 0 on a reversed buffer and at 1 on OpenGL.
                    #if UNITY_REVERSED_Z
                        bool sky = rawDepth <= 1e-7;
                    #else
                        bool sky = rawDepth >= 1.0 - 1e-7;
                    #endif
                    if (!sky)
                    {
                        float3 positionWS = FishWaterSceneWorldPosition(input.screenUV, rawDepth);
                        light = FishWaterCausticLight(positionWS);
                        /* This multiplies the frame as it stands, and the fog has already been laid
                         * over it: scaled whole, the caustics would brighten the fog as well as the
                         * sea bed under it. They fade by what the fog lets through instead. */
                        half fogKeep;
                        FishWaterAirFog(half3(0.0, 0.0, 0.0), positionWS, input.screenUV, fogKeep);
                        light = 1.0 + (light - 1.0) * fogKeep;
                    }
                #endif
                return half4(light, light, light, 1.0);
            }
            ENDHLSL
        }
    }
}
