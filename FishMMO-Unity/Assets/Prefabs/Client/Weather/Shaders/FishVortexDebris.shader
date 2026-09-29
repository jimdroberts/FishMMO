// The flecks a vortex throws round: the pieces of what it has torn up — clods, leaves, splinters, grit —
// orbiting inside its debris cloud, seen only close to. The cloud itself, the fine dust and soil that
// hides them further off, is part of the vortex's volume (FishVortex.shader); these are what a camera near
// enough sees move in it.
//
// Stateless particles, like the surf's spray: each is a quad carrying only which corner it is and a few
// random numbers (VortexPresenter.BuildDebris), and everything else is worked out here from the vortex in
// the property block and the clock. Each climbs the column over its life and goes round the axis at the
// vortex's own angular speed at its radius — fast and together in the core, lagging further out — which is
// the spiral: nothing is drawn as a spiral, the air simply turns as a Rankine vortex does. They stand on the
// same axis as the funnel and its dust (VortexAxis), so they lean and wander with them.
Shader "FishMMO/Weather/Vortex Debris"
{
    Properties { }
    SubShader
    {
        // After the vortex's volume: a fleck is dark and small, and one drawn over the cloud reads as a
        // piece of debris in front of it, where one hidden under the cloud's whole depth would not be seen
        // at all.
        Tags { "Queue" = "Transparent+16" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishVortex.hlsl"

            // x height the flecks climb to (m); y radius at the ground, z at the top, in core radii; w how fast they climb (m/s)
            float4 _VortexDebris;
            // x how bottom-heavy (1 even, more keeps them low); y fleck size (m); z where they have faded out (m from the camera); w unused
            float4 _VortexDebrisLook;
            // rgb their colour; a the share of them the wind lifts
            float4 _VortexFleck;

            struct Attributes { float3 positionOS : POSITION; float4 corner : TEXCOORD0; float4 random : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                // Off the screen until it is shown to be there: every corner of a dead quad lands here.
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                float4 r = input.random;
                // A share of the particles, as much as the vortex lifts.
                if (r.x >= _VortexFleck.a)
                {
                    return output;
                }

                float height = max(1.0, _VortexDebris.x);
                float life = height / max(0.5, _VortexDebris.w);
                float climbed = frac(_VortexTime.x / life + r.y);
                float h01 = pow(climbed, max(1.0, _VortexDebrisLook.x));
                float y = h01 * height;

                float core = max(0.5, _VortexWind.x);
                float spread = core * lerp(_VortexDebris.y, _VortexDebris.z, sqrt(h01));
                float radius = spread * (0.25 + 0.75 * sqrt(r.z));
                // Round at this radius's own rate: the Rankine vortex's angular speed.
                float omega = VortexAngularSpeed(radius, core, _VortexWind.y);
                float angle = VORTEX_TAU * frac(r.w + _VortexWind.w * omega * _VortexTime.x / VORTEX_TAU);
                float2 axis = VortexAxis(y);
                float3 positionWS = float3(axis.x + cos(angle) * radius, _VortexCentre.y + y, axis.y + sin(angle) * radius);

                // Faded in and out of its life, and out with distance, where the cloud round them is all that is seen.
                float alpha = 0.55 * smoothstep(0.0, 0.08, climbed) * (1.0 - smoothstep(0.65, 1.0, climbed)) * (1.0 - 0.5 * h01);
                float size = _VortexDebrisLook.y * (0.4 + 1.2 * input.corner.w * input.corner.w);

                float distanceToCamera = length(positionWS - _WorldSpaceCameraPos);
                float fadeOut = max(10.0, _VortexDebrisLook.z);
                alpha *= 1.0 - smoothstep(0.5 * fadeOut, fadeOut, distanceToCamera);
                /* Never smaller than a pixel and a half, its light shared out over the extra: far debris drawn
                 * at its true size falls between the pixels and sparkles. */
                float pixel = 2.0 * distanceToCamera / max(1e-3, UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                float drawn = max(size, pixel * 1.5);
                alpha *= (size / drawn) * (size / drawn);
                if (alpha < 0.002)
                {
                    return output;
                }

                float2 corner = input.corner.xy * 2.0 - 1.0;
                // Tumbling: each turns over at its own rate.
                float turn = r.y * VORTEX_TAU + _VortexTime.x * (1.0 + 3.0 * r.z);
                float s, c;
                sincos(turn, s, c);
                // Torn pieces, not discs: squashed along one side.
                float2 shaped = corner * float2(1.0, 0.45 + 0.4 * r.w);
                float2 turned = float2(shaped.x * c - shaped.y * s, shaped.x * s + shaped.y * c);
                positionWS += (UNITY_MATRIX_V[0].xyz * turned.x + UNITY_MATRIX_V[1].xyz * turned.y) * drawn * 0.5;

                // Lit by what reaches the vortex: the sun the storm lets through, the sky under its base.
                float3 toSun;
                float3 sunColour;
                float3 sky;
                if (_VortexFlow.w > 0.5)
                {
                    toSun = normalize(_FishCloudSunDir.xyz);
                    sunColour = _FishCloudSunColor.rgb * _FishCloudSunDir.w;
                    sky = _FishCloudAmbient.rgb;
                }
                else
                {
                    Light sun = GetMainLight();
                    toSun = sun.direction;
                    sunColour = sun.color;
                    sky = SampleSH(half3(0.0, 1.0, 0.0));
                }
                half3 light = sunColour * (0.35 + 0.35 * saturate(dot(toSun, float3(0.0, 1.0, 0.0)))) * _VortexSky.x + sky * 0.5 * _VortexSky.y;

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.corner.xy;
                output.color = half4(_VortexFleck.rgb * light, saturate(alpha));
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = input.uv * 2.0 - 1.0;
                float r2 = dot(c, c);
                if (r2 > 1.0)
                {
                    discard;
                }
                half alpha = input.color.a * saturate((1.0 - r2) * 3.0);
                return half4(MixFog(input.color.rgb, input.fogCoord), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
