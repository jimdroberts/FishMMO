Shader "Hidden/FishMMO/Weather/HeightFog"
{
    // Height fog: fog that lies in the low ground instead of filling the world evenly.
    //
    // Unity's built-in fog is a function of distance alone, so a valley and a mountaintop at the
    // same range from the camera are equally foggy. That is the one thing everybody notices is
    // wrong about it: mist pools, it does not hang at altitude. This pass reconstructs the world
    // position behind every pixel from the depth buffer and integrates an exponential height
    // falloff along the view ray, which is what makes a valley fill up and a ridge stand clear.
    //
    // ANALYTIC, not marched. The integral of exp(-y/H) along a straight ray has a closed form, so
    // this costs one full-screen pass with no loop at all — a few instructions a pixel against the
    // cloud march's twenty-eight to seventy-two samples. Volumetric fog (the froxel path) is a
    // separate, later thing; this is the cheap half that works on every tier and on WebGL2.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FishHeightFog"
            Blend One SrcAlpha   // rgb adds the fog's own light; alpha carries (1 - fog) to dim the scene

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ FISH_FOG_SUN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4x4 _FishFogInverseVP;
            float4   _FishFogParams;     // x density, y falloff height (m), z base height (m), w max opacity
            float4   _FishFogColor;      // rgb fog colour, a unused
            float4   _FishFogSun;        // xyz direction toward the light, w inscatter strength
            float4   _FishFogSunColor;   // rgb the light's colour, a horizon bias
            float4   _FishFogRange;      // x start distance, y end distance, z camera Y, w noise amount

            // World position behind a pixel, from the depth buffer.
            float3 WorldAt(float2 uv, float rawDepth)
            {
                float4 clip = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                float4 world = mul(_FishFogInverseVP, clip);
                return world.xyz / world.w;
            }

            /* The integral of density * exp(-(y - base)/H) along the ray, in closed form.
             *
             * Writing the ray as y(t) = y0 + t*dy, the integral of exp(-(y - base)/H) dt from 0 to d
             * is H/dy * exp(-(y0 - base)/H) * (1 - exp(-d*dy/H)). As dy goes to zero that form is
             * 0/0, and a horizontal ray is the common case — anyone looking at the horizon — so the
             * limit (d * exp(-(y0-base)/H)) is taken explicitly rather than left to the hardware to
             * produce a NaN across the middle of the screen.
             */
            float FogAmount(float3 origin, float3 dir, float distance, float density, float falloff, float base)
            {
                float h = max(1.0, falloff);
                float atOrigin = exp(-(origin.y - base) / h);
                float dy = dir.y;
                float integral;
                if (abs(dy) < 1e-4)
                {
                    integral = distance * atOrigin;
                }
                else
                {
                    integral = (h / dy) * atOrigin * (1.0 - exp(-distance * dy / h));
                }
                return 1.0 - exp(-max(0.0, density * integral));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);

                float density = _FishFogParams.x;
                if (density <= 1e-5)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float3 world = WorldAt(uv, rawDepth);
                float3 camera = GetCameraPositionWS();
                float3 toPixel = world - camera;
                float distance = length(toPixel);
                float3 dir = distance > 1e-5 ? toPixel / distance : float3(0.0, 0.0, 1.0);

                /* The sky is not "infinitely foggy". Depth is at the far plane where nothing was
                 * drawn, and integrating to there would wall the horizon off behind solid fog. The
                 * ray is clamped to the fog's own end distance, so the sky keeps its own colour and
                 * the fog thickens toward it instead of replacing it. */
                distance = min(distance, _FishFogRange.y);
                distance = max(0.0, distance - _FishFogRange.x);

                float fog = FogAmount(camera, dir, distance, density, _FishFogParams.y, _FishFogParams.z);
                fog = min(fog, _FishFogParams.w);

                float3 color = _FishFogColor.rgb;
                #ifdef FISH_FOG_SUN
                    /* Looking toward the sun through fog is brighter than looking away from it —
                     * the light scatters forward. Without this the fog is a flat grey sheet and
                     * reads as a screen effect rather than as air. */
                    float toward = saturate(dot(dir, normalize(_FishFogSun.xyz)));
                    float forward = pow(toward, 8.0) * _FishFogSun.w;
                    color = lerp(color, _FishFogSunColor.rgb, saturate(forward));
                #endif

                // rgb adds the fog's light, alpha keeps (1 - fog) of what was there.
                return float4(color * fog, 1.0 - fog);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
