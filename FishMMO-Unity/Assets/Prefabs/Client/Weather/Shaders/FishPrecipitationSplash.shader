Shader "FishMMO/Weather/Precipitation Splash"
{
    // Where the rain lands (P5). Rings bursting on whatever surface is under the sky.
    //
    // GROUND COLLISION WITHOUT A SIMULATION. The sky occlusion map is already a top-down heightfield
    // of the highest surface around the camera — built from downward raycasts so rain stops at
    // roofs — and "the highest surface" is precisely where a falling drop ends up. So a splash does
    // not need a physics step or a particle to trace: it reads the landing height straight out of a
    // texture the vertex shader can already sample, on every target including WebGL2.
    //
    // That also gets roofs right for free. A drop over a barn lands on the barn, because that is
    // what the heightfield says is up there — no special case, and no splashes appearing on the
    // floor underneath it.
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "FishPrecipitationSplash"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishWeather.hlsl"

            float4 _FishSplashOrigin;   // xyz camera, w time
            float4 _FishSplashParams;   // x radius, y density 0..1, z size, w lifetime seconds
            float4 _FishSplashColor;    // rgb tint, a alpha

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;   // x splash index, y unused
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD1;
            };

            float Hash11(float n)
            {
                return frac(sin(n * 78.233) * 43758.5453);
            }

            float2 Hash21(float n)
            {
                return float2(Hash11(n), Hash11(n + 17.13));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float index = input.data.x;
                float time = _FishSplashOrigin.w;
                float lifetime = max(0.05, _FishSplashParams.w);

                /* Each splash keeps its own cell for its whole life, and only re-rolls its place
                 * when it restarts. Re-hashing every frame would have it teleport across the ground
                 * instead of bursting and fading where it landed. */
                float cycle = floor(time / lifetime + Hash11(index) * 7.0);
                float age = frac(time / lifetime + Hash11(index) * 7.0);
                float2 random = Hash21(index * 3.7 + cycle * 19.1);

                // Scattered over a disc around the camera, denser toward the middle where they show.
                float angle = random.x * 6.2831853;
                float distance = sqrt(random.y) * _FishSplashParams.x;
                float3 at = _FishSplashOrigin.xyz + float3(cos(angle) * distance, 0.0, sin(angle) * distance);

                // The surface a drop falling here would hit: roof, ledge or ground.
                at.y = FishSkyOcclusionHeight(at) + 0.02;

                /* Thinned by how hard it is raining. Splashes that exist but are invisible still
                 * cost a quad each, so the ones that are not wanted are collapsed to zero size and
                 * the rasteriser throws them away. */
                float alive = step(Hash11(index * 1.37 + cycle), _FishSplashParams.y);

                // A ring that widens and fades, which is what a drop hitting water looks like.
                float grow = 0.35 + 1.65 * age;
                float fade = 1.0 - smoothstep(0.45, 1.0, age);
                float size = _FishSplashParams.z * grow * alive * fade;

                // Camera-facing is wrong for these: a splash lies ON the ground, so the quad is
                // flat in world space and seen at whatever angle the viewer happens to be at.
                float3 world = at + float3(input.positionOS.x, 0.0, input.positionOS.z) * size;

                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.tint = float4(_FishSplashColor.rgb, _FishSplashColor.a * fade * alive);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A soft ring: bright at its edge, hollow in the middle.
                float2 centred = input.uv * 2.0 - 1.0;
                float r = length(centred);
                float ring = smoothstep(1.0, 0.75, r) * smoothstep(0.35, 0.72, r);
                return half4(input.tint.rgb, input.tint.a * ring);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
