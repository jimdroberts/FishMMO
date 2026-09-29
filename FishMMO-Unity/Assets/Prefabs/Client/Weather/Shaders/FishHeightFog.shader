Shader "Hidden/FishMMO/Weather/HeightFog"
{
    // The weather's fog layer, analytically: the fog lying in the low ground with a top, and not a
    // wash filling the world evenly.
    //
    // Unity's built-in fog is a function of distance alone, so a valley and a mountaintop at the
    // same range from the camera are equally foggy, a mist hides the sky as much as the ground, and a
    // mist, a fog and a dense fog are three shades of one flat grey. This pass reconstructs the world
    // position behind every pixel from the depth buffer and integrates the fog layer
    // (FishFogLayer.hlsl) along the view ray: a valley fills up and a ridge stands clear, a mist lies
    // along the ground under an open sky, the horizon goes white inside a fog and its top shines.
    //
    // ANALYTIC, not marched. The layer's mean profile is a trapezoid in altitude, and the integral of
    // anything that depends on altitude alone along a straight ray has a closed form — one full-screen
    // pass with no loop, a few instructions a pixel against the cloud march's twenty-eight to
    // seventy-two samples.
    //
    // THE FALLBACK. The fog is cloud whose base is the ground, and the cloud march draws it as one
    // (FishCloudVolume.hlsl), with its banks, billowed top and drift at every distance. This pass
    // draws the same layer only where that march has not run for the camera (FogLayerView.
    // MarchedThisFrame): a renderer without the cloud feature, or a sky without its volumes. Drawn
    // alone as it used to be, from the froxel volume's edge to 44 km, it was one smooth trapezoid —
    // the textureless band round every horizon that was most of what a fog ever showed.
    //
    // WHAT IT COVERS, as the fallback. Where the froxel volume runs (compute: desktop and WebGPU) it
    // draws the fog from the camera to its far edge, and this pass carries the same layer on from
    // there to the horizon. Where it does not (WebGL2), this pass is the whole of it.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "FishHeightFog"
            Blend One SrcAlpha   // rgb adds the fog's own light; alpha carries what it lets through

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "FishFogLayer.hlsl"

            float4x4 _FishFogInverseVP;
            // x the eye depth this pass starts at (m): the froxel volume's far edge where it runs, 0
            // where it does not. y how far a ray into open sky is followed (m). zw unused.
            float4   _FishFogRange;
            // xyz the camera's forward, for turning that eye depth into a distance along each ray.
            float4   _FishFogForward;

            // World position behind a pixel, from the depth buffer.
            float3 WorldAt(float2 uv, float rawDepth)
            {
                /* OpenGL's clip space runs -1 to 1 in depth where the texture holds 0 to 1. Passed
                 * straight through, every point came back about twice as far away as it is — so on
                 * the Linux editor, which is OpenGL Core, the fog was integrated over double the
                 * distance and came out much thicker than on any other API. */
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float4 clip = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                float4 world = mul(_FishFogInverseVP, clip);
                return world.xyz / world.w;
            }

            /// Two columns of the layer as one: the layer as the ray finds it at each end, averaged.
            FishFogColumn Between(FishFogColumn a, FishFogColumn b)
            {
                FishFogColumn c;
                c.top = 0.5 * (a.top + b.top);
                c.soft = 0.5 * (a.soft + b.soft);
                c.baseSoft = 0.5 * (a.baseSoft + b.baseSoft);
                c.base = min(0.5 * (a.base + b.base), c.top - c.soft - c.baseSoft);
                c.hollow = 0.5 * (a.hollow + b.hollow);
                return c;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float extinction = _FishFogLayer.x;
                if (extinction <= 1e-7 || _FishFogLayer.y <= 0.0)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float3 camera = GetCameraPositionWS();
                float3 toPixel = WorldAt(uv, rawDepth) - camera;
                float distance = length(toPixel);
                float3 ray = distance > 1e-5 ? toPixel / distance : float3(0.0, 0.0, 1.0);

                /* Nothing drawn here: open sky. The ray is followed out to where the sky itself ends,
                 * not to the far plane: a ray that climbs out of the layer has crossed all of it in
                 * a few hundred metres and the sky shows through, and one that runs level inside it
                 * never leaves, so the horizon goes to the fog's own colour — which is what a
                 * horizon in a mist is. */
                #if UNITY_REVERSED_Z
                    bool sky = rawDepth <= 1e-6;
                #else
                    bool sky = rawDepth >= 1.0 - 1e-6;
                #endif
                if (sky)
                {
                    distance = _FishFogRange.y;
                }

                float along = 1.0 / max(0.05, dot(ray, _FishFogForward.xyz));
                float start = _FishFogRange.x > 0.0 ? _FishFogRange.x * along : 0.0;
                if (distance <= start)
                {
                    return float4(0.0, 0.0, 0.0, 1.0);
                }

                // The layer where this stretch starts and a few kilometres on, or where it ends if
                // sooner: over the valley a ray runs down into, not only the hill it leaves from.
                float3 from = camera + ray * start;
                float3 to = camera + ray * min(distance, start + 3000.0);
                FishFogColumn column = Between(FishFogColumnAt(from.xz), FishFogColumnAt(to.xz));

                float path = FishFogPath(camera.y, ray.y, start, distance, column);
                float transmittance = exp(-extinction * path);

                // Lit as the fog this stretch crosses is lit: at the middle of the part of it that
                // lies inside the layer, so a ray climbing out through the top sees the bright top.
                float low = column.base - column.baseSoft;
                float high = column.top + column.soft;
                float lit = 0.5 * (clamp(camera.y + ray.y * start, low, high) + clamp(camera.y + ray.y * distance, low, high));
                // And by the cloud over where that light mostly comes from: in-scattering weighted by
                // what gets back to the eye, e^−τ, comes on average from one optical depth in (1/β),
                // or the end of the stretch if it is nearer. The cloud's shadow there, as the ground
                // there is lit (FishFogSunShare) — one read for the whole stretch, since this pass
                // integrates it in closed form.
                float3 litAt = camera + ray * min(distance, start + 1.0 / extinction);
                float3 light = FishFogLight(lit, ray, column, extinction, 1.0, FishFogSunShare(litAt));

                // rgb adds the fog's light, alpha keeps what it lets through.
                return float4(light * (1.0 - transmittance), transmittance);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
