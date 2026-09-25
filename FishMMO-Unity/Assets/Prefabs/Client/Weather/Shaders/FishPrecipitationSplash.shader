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
    // floor underneath it. And the sea: where the ground is under water the water is the highest
    // surface (FishSkyOcclusionHeight lays it over the map), so a drop lands on the sea, not on
    // the sand three metres below it.
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWeather.hlsl"

            float4 _FishSplashOrigin;   // xyz camera, w time
            float4 _FishSplashParams;   // x radius, y density 0..1, z widest ring across (m), w lifetime seconds
            float4 _FishSplashColor;    // rgb tint, a alpha
            float4 _FishSplashHeat;     // x dryness 0..1, y how far a corner may drape, zw unused

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

                // The surface a drop falling here would hit: roof, ledge, ground or the sea.
                float centreGround = FishSkyOcclusionHeight(at);

                /* Thinned by how hard it is raining. Splashes that exist but are invisible still
                 * cost a quad each, so the ones that are not wanted are collapsed to zero size and
                 * the rasteriser throws them away. */
                float alive = step(Hash11(index * 1.37 + cycle), _FishSplashParams.y);

                /* On the open sea a ring sits at the STILL level, and the waves carry the real
                 * surface away from it: above a trough it would hang in the air, and under a crest
                 * it would show through the water, which writes no depth to hide it. So rings on the
                 * sea only where it is calm enough for the still level to be where the surface is —
                 * a millpond, a sheltered cove. Rain on a rough sea is a hiss across the whole
                 * surface, not rings, anyway. */
                float onWater = step(FishSkyOcclusionGround(at) + 0.01, centreGround);
                float calm = 1.0 - smoothstep(0.08, 0.3, _FishOcclusionWater.z);
                alive *= lerp(1.0, calm, onWater);

                /* A ring that widens and fades, which is what a drop hitting water looks like: from
                 * a fifth of its width to all of it, the SIZE the profile gives. The quad's half-width
                 * is half that, since the ring's outer edge is the quad's edge. It used to run the
                 * half-width from 0.35 to 2.0 times the size, so a ring profiled at 22 cm came out up
                 * to 1.2 m across — a hoop on the ground, not a raindrop. */
                float grow = 0.2 + 0.8 * age;

                /* Hot ground takes a splash away quickly — a drop landing on sun-baked stone is
                 * gone almost as soon as it lands, where the same drop on cold ground sits. The
                 * FADE is pulled earlier rather than the lifetime being shortened: every splash's
                 * phase comes from time / lifetime, so changing the lifetime would jump all of them
                 * at once the moment the temperature moved. */
                float dry = saturate(_FishSplashHeat.x);
                float fadeFrom = lerp(0.45, 0.10, dry);
                float fadeTo = lerp(1.00, 0.42, dry);
                float fade = 1.0 - smoothstep(fadeFrom, fadeTo, age);

                float size = 0.5 * _FishSplashParams.z * grow * alive * fade;

                /* Camera-facing is wrong for these: a splash lies ON the ground. But a quad that is
                 * FLAT sinks into any ground that is not, and the depth test then clips whichever
                 * half went under — which is why splashes were being cut off on slopes, and only
                 * sometimes. So every corner asks the heightfield for its OWN ground and the quad
                 * drapes over the surface instead of hovering across it.
                 *
                 * Clamped, because at a ledge one corner can be metres below the others and the
                 * quad would stretch into a spike reaching down the drop. Beyond the clamp it is
                 * better for a splash to clip a little than to become a streak. */
                float3 corner = at + float3(input.positionOS.x, 0.0, input.positionOS.z) * size;
                float cornerGround = FishSkyOcclusionHeight(corner);
                float drape = max(0.02, _FishSplashHeat.y * max(size, 0.05));
                corner.y = clamp(cornerGround, centreGround - drape, centreGround + drape);
                // Lifted off the surface, by a little more when the splash is bigger and further.
                corner.y += 0.015 + size * 0.05;
                float3 world = corner;

                /* Lit like the drops that made it: the sky's light from above and a little of the
                 * main light, as FishPrecipitation lights them. It was the tint alone, unlit, so at
                 * night every splash glowed a moonlit blue at full daytime brightness on black
                 * ground. */
                Light mainLight = GetMainLight();
                half3 light = SampleSH(half3(0.0, 1.0, 0.0)) + mainLight.color * 0.35;

                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                output.tint = float4(_FishSplashColor.rgb * light, _FishSplashColor.a * fade * alive);
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
