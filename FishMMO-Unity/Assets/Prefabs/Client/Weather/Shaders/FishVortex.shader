// A tornado: its condensation funnel and its debris cloud, ray-marched through a box round the vortex
// — and a dust devil, which is its dust alone.
//
// It was a mesh: a tube pushed out to the condensation radius, opaque, its ragged edge cut from noise,
// wound with bands in a flat grey-brown. Seen from anywhere it was a hard-edged horn hanging in the sky,
// and from underneath its flare was sheets of brown the size of the sky. A funnel is not a surface. It is
// cloud: the water the vortex's low pressure condenses out of the air it spins, densest in the core where
// the pressure has fallen furthest and thinning to nothing at an edge that is only where the air has
// been taken just past its dew point. So it is marched as cloud is — how much water every point holds,
// solved from the vortex (FishVortex.hlsl, VortexPhysics), seen through by Beer and Lambert and lit as
// the storm's own cloud is lit — and its edge, its taper, the tip that fades out and the flare that
// becomes the wall cloud all come out of that on their own. The dust it lifts is in the same march, so
// the two are one thing: the debris cloud wraps the funnel's foot and darkens it, and shades it from the
// sun as it would. Its striations are the inflow's moisture: streamlines of damper air condense further
// out all the way up the wall, so it is ridged along its helices at the pitch its updraught and its wind
// give the air, in bands sized by the funnel, kept only while a pixel can hold them and only where the
// spin keeps the streamlines from mixing — not where it flares into the wall cloud (VortexCondensate) —
// and lit as ledges: through taps near enough to see one shade the next, and on faces tilted by them
// (VortexSource).
//
// The box is drawn from both sides and every pixel keeps the side the ray leaves by, so it works from
// outside and inside alike, and every fragment is pinned to the far plane, so a tornado twenty
// kilometres off, beyond the camera's far clip, is still seen. Inside the box the march is kept to the
// vortex's own shape — a tube round its axis and a disc under the cloud where the funnel flares — and
// stops at the world in front of it.
Shader "FishMMO/Weather/Vortex"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent+15" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // The scene's depth is read in the march, not tested: the box's far side is usually behind the
        // world, and that is exactly where the march has to stop, not whether it runs at all.
        ZTest Always
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FishVortex.hlsl"

            // The most samples one ray takes: the strides, and the short ones through an edge just found
            // and through anything dense enough that a long one would jump a whole optical depth.
            #define VORTEX_MOST_STEPS 96

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                float4 positionCS = TransformWorldToHClip(output.positionWS);
                // On the far plane, always: a tornado past the far clip would otherwise lose the far side
                // of its box and with it every pixel of the march. Only what is behind the eye is cut.
                #if UNITY_REVERSED_Z
                    positionCS.z = positionCS.w * 1e-6;
                #else
                    positionCS.z = positionCS.w * (1.0 - 1e-6);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            /// <summary>
            /// Where a ray is inside an upright cylinder round the vortex's axis, sheared along its lean:
            /// the stretch of the ray, in metres from the eye, between two heights and within a radius.
            /// </summary>
            bool VortexCylinder(float3 origin, float3 direction, float2 shear, float radius, float bottom, float top, out float near, out float far)
            {
                near = -1e9;
                far = 1e9;
                // In the frame where the leaning axis stands upright over the foot. The shear is a linear
                // map, so the ray is still a line in it, and still measured in the world's metres.
                float2 o = origin.xz - _VortexCentre.xz - shear * (origin.y - _VortexCentre.y);
                float2 d = direction.xz - shear * direction.y;
                float a = dot(d, d);
                float b = dot(o, d);
                float c = dot(o, o) - radius * radius;
                if (a < 1e-10)
                {
                    if (c > 0.0)
                    {
                        return false;
                    }
                }
                else
                {
                    float disc = b * b - a * c;
                    if (disc < 0.0)
                    {
                        return false;
                    }
                    float root = sqrt(disc);
                    near = (-b - root) / a;
                    far = (-b + root) / a;
                }
                if (abs(direction.y) < 1e-6)
                {
                    if (origin.y < bottom || origin.y > top)
                    {
                        return false;
                    }
                }
                else
                {
                    float t0 = (bottom - origin.y) / direction.y;
                    float t1 = (top - origin.y) / direction.y;
                    near = max(near, min(t0, t1));
                    far = min(far, max(t0, t1));
                }
                return far > max(near, 0.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 origin = _WorldSpaceCameraPos.xyz;
                float3 toFragment = input.positionWS - origin;
                float fragmentDistance = length(toFragment);
                float3 direction = toFragment / max(fragmentDistance, 1e-4);

                // Where the ray is inside the box: in the box's own space it is the unit cube, and the
                // ray's parameter there is its distance in the world.
                float4x4 worldToObject = GetWorldToObjectMatrix();
                float3 ro = mul(worldToObject, float4(origin, 1.0)).xyz;
                float3 rd = mul((float3x3)worldToObject, direction);
                float3 reciprocal = 1.0 / (rd + 1e-9 * (step(0.0, rd) * 2.0 - 1.0));
                float3 a = (-0.5 - ro) * reciprocal;
                float3 b = (0.5 - ro) * reciprocal;
                float3 lo = min(a, b);
                float3 hi = max(a, b);
                float enter = max(max(max(lo.x, lo.y), lo.z), 0.0);
                float leave = min(min(hi.x, hi.y), hi.z);
                // The box is drawn from both sides; the side the ray leaves by does the work, whether the
                // camera is outside or in.
                if (leave <= enter || fragmentDistance < 0.5 * (enter + leave))
                {
                    discard;
                }

                // And within it, only where the vortex can be: the tube round its axis — the funnel below
                // its flare, the sub-vortices, the debris — and the disc under the cloud the funnel flares
                // into. A ray through the box's empty corners is not marched at all.
                float top = _VortexCentre.y + _VortexCentre.w;
                float2 shear = _VortexLean.xy / max(1.0, _VortexCentre.w);
                // The axis bows off the straight line from its foot to its top by a quarter of its lean at
                // most, and wanders; and past the top it stands still while the sheared line goes on.
                float lean = length(_VortexLean.xy);
                float inflate = 0.25 * lean + _VortexLean.z + lean * _VortexLean.w / max(1.0, _VortexCentre.w);
                float roof = max(top + _VortexLean.w, _VortexCentre.y + 1.35 * _VortexDust.y) + 10.0;
                float tubeNear, tubeFar, flareNear, flareFar;
                bool tube = VortexCylinder(origin, direction, shear, _VortexDustShape.z, _VortexCentre.y - 40.0, roof, tubeNear, tubeFar);
                bool flare = _VortexSky.w > 0.0 && _VortexLook.a > 0.0
                    && VortexCylinder(origin, direction, shear, _VortexSky.w + inflate, top - _VortexMarch.w, top + _VortexLean.w, flareNear, flareFar);
                if (!tube && !flare)
                {
                    discard;
                }
                float near = 1e9;
                float far = -1e9;
                if (tube)
                {
                    near = min(near, tubeNear);
                    far = max(far, tubeFar);
                }
                if (flare)
                {
                    near = min(near, flareNear);
                    far = max(far, flareFar);
                }

                // And it stops at the world. Nothing drawn is the sky, which does not stop it.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float rawDepth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool isSky = rawDepth <= 1e-6;
                #else
                    bool isSky = rawDepth >= 1.0 - 1e-6;
                #endif
                float3 cameraForward = -UNITY_MATRIX_V[2].xyz;
                float sceneDistance = isSky ? 1e9 : LinearEyeDepth(rawDepth, _ZBufferParams) / max(1e-4, dot(direction, cameraForward));
                float start = max(max(enter, near), _VortexMarch.y);
                float end = min(min(leave, far), sceneDistance);
                if (end <= start)
                {
                    discard;
                }

                // How wide a pixel is per metre away, for the noise's mip.
                float perMetre = 2.0 / max(1e-4, abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y);
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);

                // Lit as the clouds are, when the sky has said how; by the scene's own light otherwise.
                float3 toSun;
                float3 sunColour;
                float3 ambient;
                if (_VortexFlow.w > 0.5)
                {
                    toSun = normalize(_FishCloudSunDir.xyz);
                    sunColour = _FishCloudSunColor.rgb * _FishCloudSunDir.w;
                    ambient = _FishCloudAmbient.rgb;
                }
                else
                {
                    Light light = GetMainLight();
                    toSun = light.direction;
                    sunColour = light.color;
                    ambient = max(0.0, SampleSH(half3(0.0, 1.0, 0.0)));
                }
                float cosAngle = dot(direction, toSun);

                float strides = max(4.0, _VortexMarch.x);
                float stride = (end - start) / strides;
                float fineStride = stride * 0.12;
                float t = start + stride * jitter;
                float transmittance = 1.0;
                float3 scattered = 0.0;
                float weighted = 0.0;
                // As if the march had just come from the start: something dense right there is backed up
                // to and entered finely like anything else, not jumped by the jitter's first stride.
                float lastStep = stride * jitter;
                float lastExtinction = 0.0;
                float refineUntil = -1.0;
                int backs = 0;
                // The inflow's grain where the march last read the funnel: what its bands were like
                // there, for the light's near taps round the point it lights.
                float4 grain = float4(0.5, 0.5, 0.5, 0.5);
                UNITY_LOOP
                for (int i = 0; i < VORTEX_MOST_STEPS; i++)
                {
                    if (t >= end || transmittance < 0.004)
                    {
                        break;
                    }
                    float3 position = origin + direction * t;
                    float footprint = t * perMetre;
                    float2 offset;
                    float4 slope;
                    float2 media = VortexMedia(position, footprint, true, true, grain, offset, slope);
                    float extinction = media.x + media.y;
                    /* Out of clear air into the vortex on a long stride: go back and come through its
                     * edge in short ones. A funnel's wall is opaque within a few tens of metres, and a
                     * stride of a hundred put it wherever the stride happened to land — a different depth
                     * in every pixel, which is noise, not cloud. */
                    if (extinction > 0.0 && lastExtinction <= 0.0 && lastStep > fineStride * 1.5 && backs < 3)
                    {
                        refineUntil = t;
                        t = max(start, t - lastStep) + fineStride * jitter;
                        lastStep = 0.0;
                        backs++;
                        continue;
                    }
                    float advance = t < refineUntil ? fineStride
                        : (extinction > 0.0 ? clamp(0.35 / extinction, fineStride, stride) : stride);
                    advance = min(advance, end - t);
                    if (extinction > 1e-6)
                    {
                        float through = exp(-extinction * advance);
                        float3 source = VortexSource(position, media, offset, toSun, sunColour, ambient, cosAngle, footprint, grain, slope);
                        // The light scattered in over the stride, of which what gets out is the share the
                        // stride itself did not take: exact for a stride of one medium.
                        float taken = transmittance * (1.0 - through);
                        scattered += source * taken;
                        weighted += t * taken;
                        transmittance *= through;
                    }
                    lastStep = advance;
                    lastExtinction = extinction;
                    t += advance;
                }

                float alpha = 1.0 - transmittance;
                if (alpha <= 1e-4)
                {
                    discard;
                }
                float3 color = scattered / alpha;
                // Seen as far off as what was seen of it is, on average: fogged by the scene's fog like
                // the ground under it, and turned into the air in front of it as the clouds are.
                float seenAt = weighted / alpha;
                float3 seen = origin + direction * seenAt;
                color = MixFog(color, ComputeFogFactor(TransformWorldToHClip(seen).z));
                if (_VortexFlow.w > 0.5)
                {
                    float haze = saturate(seenAt / max(1.0, _FishCloudHaze.a));
                    color = lerp(color, _FishCloudHaze.rgb, haze * haze * 0.86);
                }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
