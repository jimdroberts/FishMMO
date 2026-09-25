// Near precipitation: a pre-built field of quads that the vertex shader moves, wraps around the
// camera, stretches along the fall and hides under roofs. No geometry or compute shaders, so it
// runs the same on WebGPU, WebGL2 and desktop.
Shader "FishMMO/Weather/Precipitation"
{
    Properties
    {
        _MainTex ("Atlas (4 x 8 tiles)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "Precipitation"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWeather.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
            CBUFFER_END

            // Per draw, from a MaterialPropertyBlock.
            float4 _PrecipOrigin;   // xyz camera position, w time in seconds
            float4 _PrecipBox;      // xyz size of the wrapping box around the camera, w near fade distance
            float4 _PrecipFall;     // xyz the kind's velocity now at its typical particle, m/s, wind included
            float4 _PrecipTravel;   // x, z metres the air has carried everything, y metres the typical particle has fallen; wrapped
            float4 _PrecipSpread;   // x slowest, y fastest particle against the typical one, z 1 when it falls from cloud
            float4 _PrecipShape;    // x visible share of particles, y size in metres, z stretch, w atlas row
            float4 _PrecipFlutter;  // x sway in metres, y sway frequency, z alpha, w brightness
            float4 _PrecipColor;    // tint for this draw

            struct Attributes
            {
                float3 seed : POSITION;     // particle position in the unit box, shared by its 4 corners
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // x show threshold, y size jitter, z speed jitter, w tile/phase
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float fogCoord : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float time = _PrecipOrigin.w;
                float3 box = max(_PrecipBox.xyz, 1.0);


                // Each particle falls at its own speed; positions wrap inside a box that follows the
                // camera, but in world space, so walking does not drag the particles along.
                // Each drop's own fall. The field used to move as one sheet — every particle carried
                // by the same vector, speeds within a fifth of each other, every streak parallel and
                // the same length — which the eye reads as a pattern sliding past. A big drop falls
                // faster than a small one (mostly the same random that sets its size sets its speed,
                // so the two agree), the air it falls through is never still, and no two streaks are
                // quite alike. Sideways, though, every particle goes with the air.
                //
                // Where it has got to is the travel PrecipitationField.Advance integrates, never the
                // velocity times the time: that is only right while nothing changes, and the wind
                // never stops changing. The speed is a whole number of 64ths of the typical one, which
                // is what lets the fall be wrapped there without moving anything.
                float share = saturate(input.random.y * 0.8 + input.random.z * 0.2);
                float speed = round(lerp(_PrecipSpread.x, _PrecipSpread.y, share) * 64.0) / 64.0;
                float3 travelled = float3(_PrecipTravel.x, -_PrecipTravel.y * speed, _PrecipTravel.z);
                // Turbulence: a slow lateral wander, its own phase and rate to every drop, scaled by
                // the wind — a still day's rain falls straight, a gusty one's is thrown about.
                float gustiness = 0.15 + 0.25 * saturate(length(_PrecipFall.xz) / max(1.0, abs(_PrecipFall.y)));
                float2 wander = float2(sin(time * lerp(0.7, 1.9, input.random.z) + input.random.w * 6.2831), cos(time * lerp(0.9, 2.3, input.random.w) + input.random.x * 6.2831)) * gustiness;
                float3 local = frac(input.seed + (travelled - _PrecipOrigin.xyz) / box) * box - box * 0.5;
                float3 centre = _PrecipOrigin.xyz + local + float3(wander.x, 0.0, wander.y);

                // Sway for snow and ash.
                float phase = input.random.w * 6.2831 + time * _PrecipFlutter.y;
                centre.xz += float2(sin(phase), cos(phase * 1.3)) * _PrecipFlutter.x;

                /* HOW MANY fall here: the kind's share, times how much of it the cloud above can
                 * shed. The field's weather says how hard it is raining where it rains; the cloud
                 * standing over this spot says where, and a thin patch of it sheds less than the
                 * deck's thick heart — so a gap in the deck is a gap in the rain, a thinning is a
                 * thinning, and a shower moves with the cloud it falls from. It FADED the drops
                 * instead, which under a thin patch left the full number of them hanging half there,
                 * like a ghost of rain. A drop near its turn fades over a narrow band rather than
                 * blinking as the cloud moves past it. Sand is lifted off the ground by the wind and
                 * owes the sky nothing. */
                float shedding = lerp(1.0, smoothstep(0.08, 0.35, FishCloudOver(centre)), saturate(_PrecipSpread.z));
                float visible = saturate((_PrecipShape.x * shedding - input.random.x) * 25.0);

                float3 toCamera = _WorldSpaceCameraPos.xyz - centre;
                float distance = length(toCamera);
                float3 view = toCamera / max(distance, 1e-3);

                float size = _PrecipShape.y * lerp(0.7, 1.3, input.random.y) * step(0.001, visible);
                float3 axis;
                float3 side;
                float length01 = size;
                if (_PrecipShape.z > 1.01)
                {
                    // Streaks: along the fall direction, facing the camera — each leaning a little its
                    // own way, and its own length, since a drop's streak is its speed over the frame.
                    // Along its OWN motion: the same wind and its own fall, so a fast drop streaks
                    // steeper than a slow one in the same gust.
                    float3 own = float3(_PrecipFall.x, _PrecipFall.y * speed, _PrecipFall.z);
                    float3 lean = float3(input.random.z - 0.5, 0.0, input.random.w - 0.5) * 0.12 * length(own);
                    axis = normalize(own + lean + float3(0, -1e-3, 0));
                    side = normalize(cross(axis, view) + float3(1e-4, 0, 0));
                    float quick = saturate((speed - _PrecipSpread.x) / max(1e-3, _PrecipSpread.y - _PrecipSpread.x));
                    length01 = size * _PrecipShape.z * lerp(0.85, 1.15, quick);
                }
                else
                {
                    // Flakes and grit: camera-facing.
                    side = normalize(cross(float3(0, 1, 0), view) + float3(1e-4, 0, 0));
                    axis = cross(view, side);
                }
                float3 worldPos = centre + side * (input.corner.x - 0.5) * size + axis * (input.corner.y - 0.5) * length01;

                // Fade near the camera, toward the box edge, and under anything overhead.
                float nearFade = saturate((distance - 0.3) / max(_PrecipBox.w, 0.1));
                float farFade = saturate((box.x * 0.5 - distance) / (box.x * 0.2));
                float open = FishSkyOpen(centre);

                // Soft ambient light from above plus a little of the main light.
                Light mainLight = GetMainLight();
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 light = ambient + mainLight.color * 0.35;

                output.positionCS = TransformWorldToHClip(worldPos);
                float tile = floor(frac(input.random.w * 7.13) * 4.0);
                float row = _PrecipShape.w;
                output.uv = float2((input.corner.x + tile) * 0.25, 1.0 - (row + 1.0 - input.corner.y) * 0.125);
                output.color = float4(_PrecipColor.rgb * _Tint.rgb * light * _PrecipFlutter.w, _PrecipColor.a * _Tint.a * _PrecipFlutter.z * nearFade * farFade * open * visible);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 color = half4(input.color.rgb * texel.rgb, input.color.a * texel.a);
                clip(color.a - 0.002);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
