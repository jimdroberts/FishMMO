// Moons, planets, comets, meteors and asteroids: camera-facing quads placed on the far sky,
// lit by the real star direction (phase), reddened in eclipses, and faded at the horizon. Textured bodies are drawn one at a time with _BodyTex; the rest share one mesh.
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
            // The cloud volume's own buffer: rgb scattered light, alpha the transmittance left
            // along that ray. The bodies read it to know what stands in front of them.
            TEXTURE2D(_FishCloudBuffer);
            SAMPLER(sampler_FishCloudBuffer);
            float4 _FishCloudScreen;   // x: 1 while the buffer holds this frame's clouds, else 0
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
                float4 extra : TEXCOORD4;        // x rank in the far-to-near order: hidden by any disc ranked above it
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
                float rank : TEXCOORD8;
                float4 ring : TEXCOORD7;         // rings only: x how open (signed), y inner rim / outer, z bands, w outer rim in body radii
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
                else if (kind > 0.5 && kind < 1.5)
                {
                    // Rings: a square as wide as the outer rim, turned so that its up is the body's
                    // pole as it appears on the sky. The ring plane is the body's equator; seen from
                    // here it is an ellipse whose long axis lies across the pole and whose short one
                    // is as long as the viewer stands above the plane — edge-on a line, face-on a
                    // circle. The fragment stage draws that ellipse inside this square.
                    //
                    // It used to be a fixed 2.3 by 0.7 ellipse, always level with the horizon, with
                    // its band at 0.78 of the quad: whichever way the planet's axis pointed and
                    // wherever it was seen from, the same shape, laid over the whole disc.
                    float3 pole = normalize(input.axis.xyz);
                    float outer = max(1.0, input.axis.w);
                    float3 across = pole - dir * dot(pole, dir);
                    // Pole straight at the viewer: the ring is a circle and any up will do.
                    float3 ringUp = dot(across, across) > 1e-6 ? normalize(across) : upAxis;
                    float3 ringRight = normalize(cross(ringUp, dir));
                    offset = (ringRight * input.corner.x + ringUp * input.corner.y) * size * outer;
                    right = ringRight;
                    upAxis = ringUp;
                    output.ring = float4(dot(pole, -dir), input.shape.w / outer, input.shape.z, outer);
                }
                else
                {
                    offset = (right * input.corner.x + upAxis * input.corner.y) * size;
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
                output.rank = input.extra.x;

                float horizon = saturate(dir.y * 20.0 + 0.5);
                float fog = 1.0 - saturate(1.0 - dir.y * 7.0) * _FishSkyFogColor.a;
                output.visibility = horizon * fog;
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
                    // A body crossing the sun is a silhouette, not an absence: it must block the
                    // light whatever the sky is doing, or an eclipse shows only its rings.
                    float3 toSun = _FishSunDir[0].xyz;
                    float sunAngle = acos(clamp(dot(normalize(input.forward * -1.0), toSun), -1.0, 1.0));
                    float sunRadius = max(_FishSunDir[0].w, 0.002);
                    float crossing = _FishSkyEclipse.x * (1.0 - smoothstep(sunRadius * 2.0, sunRadius * 6.0, sunAngle));
                    float shown = saturate(max(max(dot(color, float3(0.3, 0.59, 0.11)) * 6.0, dark), crossing));
                    alpha *= shown;
                    // The silhouette is the body's night side: no light comes off it.
                    color = lerp(color, color * 0.05, crossing);
                }
                else if (kind < 1.5)
                {
                    // Rings. c runs -1..1 across a square as wide as the outer rim: x along the ring's
                    // long axis, y along the body's pole as it lies on the sky.
                    float opening = input.ring.x;                    // sine of the viewer's height above the ring plane
                    float squash = max(abs(opening), 0.015);            // never quite a line: a pixel of ring stays a pixel
                    // Where this pixel is ON the ring plane, in units of the outer rim.
                    float onPlane = length(float2(c.x, c.y / squash));
                    float t = (onPlane - input.ring.y) / max(1e-4, 1.0 - input.ring.y);
                    float solid;
                    float3 material = color;
                    if (_UseTexture > 0.5)
                    {
                        // A strip read across the rings: inner rim on the left, outer on the right.
                        float4 strip = SAMPLE_TEXTURE2D(_BodyTex, sampler_BodyTex, float2(saturate(t), 0.5));
                        material *= strip.rgb;
                        solid = strip.a * step(0.0, t) * step(t, 1.0);
                    }
                    else
                    {
                        solid = FishRingBands(t, input.ring.z);
                    }
                    // The far half goes behind the planet. Looking down on the north face the pole
                    // leans toward the viewer, and the half of the ellipse up the pole's side is the
                    // far one; looking up at the south face it is the other half. Inside the body's
                    // own disc the far half is hidden and the near half crosses in front.
                    float bodyRadius = 1.0 / max(1.0, input.ring.w);
                    bool farHalf = c.y * opening > 0.0;
                    bool behind = farHalf && dot(c, c) < bodyRadius * bodyRadius;
                    alpha = behind ? 0.0 : solid * input.lighting.w;
                    color = material;
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

                // Behind the ring of the world underfoot, which is nearer than anything else up here.
                // Asked per pixel and not once for the body: a ring's edge crossing a big planet's
                // disc has to cut across the disc, not switch the whole planet off. A meteor is the
                // one thing drawn here that is nearer still — it burns in the air — and is left alone.
                if (kind < 2.5 || kind > 3.5)
                {
                    float reachAcross = tan(min(input.shape.x, 1.2)) * (kind > 0.5 && kind < 1.5 ? max(1.0, input.ring.w) : 1.0);
                    float3 toPixel = normalize(-input.forward + (input.right * c.x + input.up * c.y) * (kind < 1.5 ? reachAcross : 0.0));
                    float3 unusedLight;
                    alpha *= 1.0 - FishOwnRing(toPixel, unusedLight);

                    // Behind any nearer body's disc, lit or not. Drawing the nearest last puts a lit
                    // disc on top, but by day a body's unlit side is drawn clear — what is seen there
                    // is the air in front of it — so a far planet drawn first showed straight through
                    // the dark limb of a nearer crescent. It is behind that moon whichever part of the
                    // moon is lit. Not drawn there, what is left is the sky that was behind them both.
                    int occluders = (int)min(_FishOccluderCount, 32.0);
                    for (int o = 0; o < occluders; o++)
                    {
                        if (_FishOccluderRanks[o].x > input.rank + 0.5)
                        {
                            float across = acos(clamp(dot(toPixel, _FishOccluders[o].xyz), -1.0, 1.0));
                            float limb = _FishOccluders[o].w;
                            alpha *= smoothstep(limb * 0.985, limb, across);
                        }
                    }
                }

                // What the cloud in front of this body lets through. These quads sit in the
                // transparent queue, and the cloud volume is composited before the transparent
                // queue runs, so a body drawn now lands *on top* of the cloud that should be
                // hiding it — a moon in front of an overcast. Reading the volume's own
                // transmittance puts it back behind, and does it properly: thin cirrus dims a
                // planet rather than either hiding it completely or not at all.
                //
                // The flag matters. With no clouds drawn this frame the buffer is unbound and
                // samples as zero, which would mean "fully occluded" and take every body out of
                // the sky, so nothing is applied unless the buffer is known to hold this frame.
                float2 screenUV = input.positionCS.xy / _ScreenParams.xy;
                float through = SAMPLE_TEXTURE2D(_FishCloudBuffer, sampler_FishCloudBuffer, screenUV).a;
                alpha *= lerp(1.0, saturate(through), saturate(_FishCloudScreen.x));
                return half4(color * alpha * _FishSkyParams.w, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
