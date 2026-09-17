// Moons, planets, comets, meteors and asteroids: camera-facing quads placed on the far sky,
// lit by the real star direction (phase), reddened in eclipses, hidden by clouds and faded at
// the horizon. Textured bodies are drawn one at a time with _BodyTex; the rest share one mesh.
Shader "FishMMO/Sky Body"
{
    Properties
    {
        _BodyTex ("Surface (equirectangular)", 2D) = "white" {}
        [Toggle] _UseTexture ("Use texture", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-450" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "SkyBody"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FishSkyCommon.hlsl"

            TEXTURE2D(_BodyTex);
            SAMPLER(sampler_BodyTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BodyTex_ST;
                float _UseTexture;
            CBUFFER_END

            // Kinds: 0 disc, 1 ring, 2 comet tail, 3 meteor, 4 point
            struct Attributes
            {
                float3 direction : POSITION;     // direction from the viewer
                float2 corner : TEXCOORD0;       // −1..1
                float4 shape : TEXCOORD1;        // x angular radius (rad), y kind, z illumination, w eclipse shadow
                float4 lighting : TEXCOORD2;     // xyz direction to the star, w brightness
                float4 axis : TEXCOORD3;         // xyz streak direction, w streak length (rad)
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                float4 shape : TEXCOORD1;
                float4 lighting : TEXCOORD2;
                float3 right : TEXCOORD3;
                float3 up : TEXCOORD4;
                float3 forward : TEXCOORD5;
                float4 color : COLOR;
                float visibility : TEXCOORD6;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 dir = normalize(input.direction);
                float kind = input.shape.y;
                float distance = 4000.0;
                float3 centre = _WorldSpaceCameraPos.xyz + dir * distance;

                // At least a couple of pixels wide, so small bodies stay visible.
                float pixelAngle = 2.0 / (max(abs(UNITY_MATRIX_P[1][1]), 1e-3) * max(_ScreenParams.y, 1.0));
                float radius = max(input.shape.x, pixelAngle * (kind > 3.5 ? 1.2 : 1.6));
                float size = distance * tan(min(radius, 1.2));

                float3 right = normalize(cross(float3(0, 1, 0), dir) + float3(1e-4, 0, 0));
                float3 upAxis = cross(dir, right);
                float3 offset;
                if (kind > 1.5 && kind < 3.5)
                {
                    // Streaks: from the head along the axis.
                    float3 along = normalize(input.axis.xyz - dir * dot(input.axis.xyz, dir) + float3(0, 1e-4, 0));
                    float3 side = normalize(cross(along, dir));
                    float length01 = distance * tan(min(max(input.axis.w, radius * 2.0), 1.2));
                    offset = side * input.corner.x * size + along * (input.corner.y * 0.5 + 0.5) * length01;
                    right = side;
                    upAxis = along;
                }
                else
                {
                    float2 stretch = kind > 0.5 && kind < 1.5 ? float2(2.3, 0.7) : float2(1, 1);
                    offset = (right * input.corner.x * stretch.x + upAxis * input.corner.y * stretch.y) * size * (kind > 3.5 ? 1.0 : 1.0);
                }

                float4 clip = TransformWorldToHClip(centre + offset);
                // Pin to the far plane so everything in the scene is in front.
                #if UNITY_REVERSED_Z
                    clip.z = clip.w * 1e-6;
                #else
                    clip.z = clip.w * 0.999999;
                #endif
                output.positionCS = clip;
                output.corner = input.corner;
                output.shape = input.shape;
                output.lighting = input.lighting;
                output.right = right;
                output.up = upAxis;
                output.forward = -dir;
                output.color = input.color;

                float4 clouds = FishSkyClouds(dir, _WorldSpaceCameraPos.xyz);
                float horizon = saturate(dir.y * 20.0 + 0.5);
                float fog = 1.0 - saturate(1.0 - dir.y * 7.0) * _FishSkyFogColor.a;
                output.visibility = (1.0 - clouds.a) * horizon * fog;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float kind = input.shape.y;
                float2 c = input.corner;
                float alpha;
                float3 color = input.color.rgb;

                if (kind < 0.5)
                {
                    // A lit sphere.
                    float r2 = dot(c, c);
                    if (r2 > 1.0) discard;
                    float aa = fwidth(r2) + 1e-4;
                    alpha = 1.0 - smoothstep(1.0 - aa * 2.0, 1.0, r2);
                    float z = sqrt(saturate(1.0 - r2));
                    float3 normal = normalize(input.right * c.x + input.up * c.y + input.forward * z);
                    float lambert = saturate(dot(normal, normalize(input.lighting.xyz)));
                    float3 surface = color;
                    if (_UseTexture > 0.5)
                    {
                        float3 n = float3(c.x, c.y, z);
                        float2 uv = float2(atan2(n.x, n.z) / (2.0 * PI) + 0.5, asin(clamp(n.y, -1.0, 1.0)) / PI + 0.5);
                        surface *= SAMPLE_TEXTURE2D(_BodyTex, sampler_BodyTex, TRANSFORM_TEX(uv, _BodyTex)).rgb;
                    }
                    float limb = lerp(0.75, 1.0, z);
                    float3 lit = surface * (lambert * limb * input.lighting.w + 0.015);
                    // An eclipse turns the moon dark red.
                    lit = lerp(lit, surface * float3(0.28, 0.07, 0.03) * 0.35, input.shape.w);
                    // Atmosphere rim for bodies with air (colour alpha < 1 marks it).
                    float rim = pow(1.0 - z, 3.0) * (1.0 - input.color.a) * saturate(lambert * 3.0 + 0.2);
                    color = lit + float3(0.45, 0.6, 1.0) * rim;
                    // In a lit sky the air in front hides the dark side: only the lit part shows.
                    // Under a dark (or airless) sky the whole disc covers the stars.
                    float dark = max(_FishSkyParams.x, _FishSkyEclipse.z);
                    float shown = saturate(max(dot(color, float3(0.3, 0.59, 0.11)) * 6.0, dark));
                    alpha *= shown;
                }
                else if (kind < 1.5)
                {
                    // Rings: an ellipse band.
                    float r = length(c);
                    alpha = saturate(1.0 - abs(r - 0.78) / 0.14) * 0.55 * (abs(c.y) > 0.28 || r > 0.62 ? 1.0 : 0.25);
                    color *= input.lighting.w;
                }
                else if (kind < 2.5)
                {
                    // A comet tail: bright at the head, fading along and across.
                    float along = c.y * 0.5 + 0.5;
                    alpha = (1.0 - along) * (1.0 - along) * exp(-c.x * c.x * 3.0) * input.lighting.w;
                    alpha = saturate(alpha);
                }
                else if (kind < 3.5)
                {
                    // A meteor: a streak with a hot head.
                    float along = c.y * 0.5 + 0.5;
                    alpha = saturate((1.0 - along) * exp(-c.x * c.x * 8.0) * input.lighting.w);
                    color = lerp(color, 1.0, (1.0 - along) * (1.0 - along));
                }
                else
                {
                    // A point: planets too small to show a disc, and asteroids.
                    float r2 = dot(c, c);
                    alpha = exp(-r2 * 3.5) * input.lighting.w;
                }

                alpha *= input.visibility;
                return half4(color * alpha * _FishSkyParams.w, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
