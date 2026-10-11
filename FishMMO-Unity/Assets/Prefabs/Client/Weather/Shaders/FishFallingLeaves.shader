// Falling leaves: a pre-built field of quads (PrecipitationField.BuildMesh) that the vertex shader wraps round
// the camera, lets fall, flutters or tumbles, turns and lights — the precipitation's pattern, with a leaf's
// physics (FallingLeaves.cs). No geometry or compute shaders, so it runs the same on WebGPU, WebGL2 and desktop.
//
// WHERE A LEAF IS SHOWN. Every particle exists everywhere in its box; it is drawn only where a crown could
// have let it go. It looks back upwind by as far as the air has carried it since it left a crown halfway up
// (FallingLeaves.Downwind), reads that crown's cover off the canopy map (LeafCanopyMap: r deciduous, g
// evergreen, b crown top, a ground), and is shown as often as the cover times the season's rate — the same
// leaf-loss curve that strips the crowns (FishVegetationPasses.hlsl). Above the crown it came from it has not
// left yet; below the ground it has landed, and lies there flat where it touched down for a few seconds,
// fading, before the field's wrap brings it back to the top of the box.
Shader "FishMMO/Weather/Falling Leaves"
{
    Properties { }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+40"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "FallingLeaves"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            // The cloud shadow cookie on the sun, as the precipitation reads it.
            #pragma multi_compile _ _LIGHT_COOKIES
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishWeather.hlsl"
            #include "FishAmbient.hlsl"
            #include "FishFogLayer.hlsl"

            TEXTURE2D(_LeafCanopy);
            SAMPLER(sampler_LeafCanopy);

            // Per draw, from a MaterialPropertyBlock (FallingLeavesPresenter).
            float4 _LeafOrigin;     // xyz camera position, w the shared motion clock (s)
            float4 _LeafBox;        // xyz the wrapping box (m), w how far above the camera it is centred
            float4 _LeafFall;       // xyz the typical leaf's velocity (m/s, the air's drift included), w its fall speed
            float4 _LeafTravel;     // x, z metres the air has carried everything, y metres the typical leaf has fallen; wrapped
            float4 _LeafSpread;     // x slowest, y fastest flutterer against the typical leaf; z the share that tumble; w how much faster a tumbler falls
            float4 _LeafRates;      // x share shown under a full deciduous canopy, y under an evergreen one; z the share of falling deciduous leaves turned; w the share of those browned
            float4 _LeafSize;       // x smallest, y largest leaf (m); z opacity; w seconds a landed leaf lies
            float4 _LeafColor;      // a fresh leaf's colour, the trees' tint in it (linear)
            float4 _LeafAutumn;     // the trees' autumn multiplier (linear), as the vegetation shader turns them
            float4 _LeafDry;        // a dead leaf's colour (linear)
            float4 _LeafCanopyRect; // xy the map's world x/z minimum, z one over its size, w the height its ground is stored against
            float4 _LeafWind;       // xy the wind's direction, z the air's speed among the trees (m/s), w the gust now

            struct Attributes
            {
                float3 seed : POSITION;     // the leaf's place in the unit box, shared by its 4 corners
                float2 corner : TEXCOORD0;  // 0/1 corner of the quad
                float4 random : TEXCOORD1;  // x show threshold, y speed, z kind, w phase and colour
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float fogCoord : TEXCOORD2;
                float3 shape : TEXCOORD3;   // x lobing, y lobes along the leaf, z 1 its shape drawn .. 0 a soft dot (smaller than the pixels)
            };

            // The canopy map at a world xz: r deciduous cover, g evergreen, b crown top above the ground (m), a the ground (world m).
            float4 LeafCanopyAt(float2 xz)
            {
                float2 uv = (xz - _LeafCanopyRect.xy) * _LeafCanopyRect.z;
                float4 c = SAMPLE_TEXTURE2D_LOD(_LeafCanopy, sampler_LeafCanopy, saturate(uv), 0);
                float inside = all(uv > 0.0) && all(uv < 1.0) ? 1.0 : 0.0;
                return float4(c.rg * inside, c.b * inside, c.a + _LeafCanopyRect.w);
            }

            // The main light's cloud-shadow cookie at a point (FishPrecipitation.shader's PrecipSunCookie).
            float3 LeafSunCookie(float3 positionWS)
            {
                #if defined(_LIGHT_COOKIES)
                    if (IsMainLightCookieEnabled())
                    {
                        float2 uv = ComputeLightCookieUVDirectional(_MainLightWorldToLight, positionWS, float4(1.0, 1.0, 0.0, 0.0), URP_TEXTURE_WRAP_MODE_NONE);
                        float4 c = SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv, 0);
                        return IsMainLightCookieTextureRGBFormat() ? c.rgb : (IsMainLightCookieTextureAlphaFormat() ? c.aaa : c.rrr);
                    }
                #endif
                return float3(1.0, 1.0, 1.0);
            }

            // Metres of the world one pixel spans at a point (FishPrecipitation.shader's PrecipPixelMetres).
            float LeafPixelMetres(float3 positionWS)
            {
                float focal = max(abs(UNITY_MATRIX_P._m11), 1e-4) * max(_ScreenParams.y, 1.0) * 0.5;
                float depth = max(-TransformWorldToView(positionWS).z, 1e-3);
                return unity_OrthoParams.w > 0.5 ? 1.0 / focal : depth / focal;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                // Off the screen until it is shown to be there.
                output.positionCS = float4(2.0, 2.0, 2.0, 1.0);
                float4 r = input.random;
                float time = _LeafOrigin.w;
                float3 box = max(_LeafBox.xyz, 1.0);

                /* How it falls. A tumbler turns end over end and comes down faster; a flutterer swings side to
                 * side. Its speed is a whole 64th of the typical leaf's, so the field's fall wraps without
                 * moving anything (PrecipitationField.Advance). */
                bool tumble = frac(r.z * 3.7) < _LeafSpread.z;
                float speed = lerp(_LeafSpread.x, _LeafSpread.y, r.y) * (tumble ? _LeafSpread.w : 1.0);
                speed = max(1.0, round(speed * 64.0)) / 64.0;
                float fall = max(0.1, _LeafFall.w * speed);

                float3 travelled = float3(_LeafTravel.x, -_LeafTravel.y * speed, _LeafTravel.z);
                float3 boxCentre = _LeafOrigin.xyz + float3(0.0, _LeafBox.w, 0.0);
                float3 centre = boxCentre + frac(input.seed + (travelled - boxCentre) / box) * box - box * 0.5;

                /* Where it is against the ground and the trees. Landed (below the ground) it lies where it came
                 * down — back along the air's drift by the time since — and its swing and turn stop there. */
                float4 canopyHere = LeafCanopyAt(centre.xz);
                float height = centre.y - canopyHere.a;
                float lie = max(0.01, _LeafSize.w * fall);
                if (height < -lie)
                {
                    return output;
                }
                bool landed = height < 0.0;
                float since = landed ? -height / fall : 0.0;
                float when = time - since;
                if (landed)
                {
                    centre.xz -= _LeafFall.xz * since;
                    canopyHere = LeafCanopyAt(centre.xz);
                }

                /* Where it came from: upwind, by as far as the air has carried it since it left a crown halfway up
                 * (FallingLeaves.Downwind). Looked up at the crown overhead first, then at the one it points to. */
                float crown = max(canopyHere.b, 4.0);
                float drop = max(0.0, 0.6 * crown - max(height, 0.0));
                float2 source = centre.xz - _LeafWind.xy * (_LeafWind.z * drop / fall);
                float4 canopy = LeafCanopyAt(source);
                drop = max(0.0, 0.6 * max(canopy.b, 2.0) - max(height, 0.0));
                source = centre.xz - _LeafWind.xy * (_LeafWind.z * drop / fall);
                canopy = LeafCanopyAt(source);
                float deciduous = canopy.r * _LeafRates.x;
                float density = deciduous + canopy.g * _LeafRates.y;
                float visible = saturate((density - r.x) * 25.0);
                // Not yet let go above the crown it came from.
                visible *= saturate(canopy.b - max(height, 0.0) + 1.0);
                // Nor under the sea.
                visible *= FishWaterOpen(centre);
                if (visible <= 0.002)
                {
                    return output;
                }

                /* Its motion: a pendulum swing (flutter) or a turn end over end (tumble), about a swing direction
                 * that slowly yaws round as it falls. Each rate snapped to whole turns in the clock's wrap. */
                float phase = r.w * 6.2831853;
                float yaw = phase + when * FishWrapAngular(lerp(-1.2, 1.2, frac(r.w * 5.31)));
                float2 e = float2(cos(yaw), sin(yaw));
                // All of it at the moment it touched down, once landed, so it lies where it was and does not jump.
                float tilt;
                if (tumble)
                {
                    float spin = phase + when * FishWrapAngular(6.2831853 * lerp(2.0, 4.5, frac(r.y * 3.31)));
                    centre.xz += e * 0.08 * sin(spin);
                    tilt = spin;
                }
                else
                {
                    // The swing: out to each side, pitched up at the ends where it stalls and climbs a little,
                    // level and quick through the middle.
                    float swing = when * FishWrapAngular(6.2831853 * lerp(0.6, 1.1, frac(r.y * 7.13))) + phase * 3.1;
                    float reach = lerp(0.12, 0.35, frac(r.z * 13.7 + r.w));
                    centre.xz += e * reach * sin(swing);
                    centre.y += reach * 0.18 * cos(2.0 * swing);
                    tilt = 0.75 * sin(swing);
                }
                // And the air's own turbulence, harder in a wind.
                float gusty = 0.15 + 0.35 * saturate(_LeafWind.z / 4.0 + _LeafWind.w);
                float wx = FishWrapAngular(lerp(0.5, 1.4, r.z)), wz = FishWrapAngular(lerp(0.6, 1.7, r.w));
                centre.xz += float2(sin(when * wx + r.w * 6.2831), cos(when * wz + r.x * 6.2831)) * gusty;
                if (landed)
                {
                    // Flat on the ground.
                    tilt = 0.0;
                    centre.y = canopyHere.a + 0.015;
                }

                float3 swingDir = float3(e.x, 0.0, e.y);
                float3 across = float3(-e.y, 0.0, e.x);
                float st, ct;
                sincos(tilt, st, ct);
                float3 along = swingDir * ct + float3(0.0, st, 0.0);
                float3 normal = float3(0.0, ct, 0.0) - swingDir * st;

                // Its size, and never under two pixels: smaller, its light is kept over the area it gained.
                float own = lerp(_LeafSize.x, _LeafSize.y, frac(r.w * 9.73));
                float pixel = LeafPixelMetres(centre);
                float drawn = max(own, 2.0 * pixel);
                float gained = own / drawn;
                float resolved = saturate((own / pixel - 2.0) / 3.0);
                float3 toCamera = _WorldSpaceCameraPos.xyz - centre;
                float distance = length(toCamera);
                float3 view = toCamera / max(distance, 1e-3);
                if (resolved < 1.0)
                {
                    // Too small to show its shape: turned to the camera, so an edge-on one does not vanish.
                    float3 side = normalize(cross(float3(0.0, 1.0, 0.0), view) + float3(1e-4, 0.0, 0.0));
                    along = lerp(cross(view, side), along, resolved);
                    across = lerp(side, across, resolved);
                }
                float width = lerp(0.55, 0.8, frac(r.z * 17.9));
                float3 worldPos = centre + along * (input.corner.y - 0.5) * drawn + across * (input.corner.x - 0.5) * drawn * width;

                // Faded toward the box's sides, top and bottom, and at the camera.
                float2 offsetXZ = abs(centre.xz - _LeafOrigin.xz);
                float edge = 1.0 - smoothstep(0.32, 0.48, max(offsetXZ.x, offsetXZ.y) / box.x);
                edge *= saturate((0.5 * box.y - abs(centre.y - boxCentre.y)) / 1.5);
                edge *= saturate((distance - 0.3) / 0.5);
                float lying = landed ? saturate(1.0 + height / lie) : 1.0;

                /* Lit as a thin leaf: the sky round its face (FishTrilight, never SampleSH), the sun on the side it
                 * turns to the light, and a third of it through the leaf from behind — and in the crown's shade
                 * while it is still under it. */
                float3 n = dot(normal, view) < 0.0 ? -normal : normal;
                Light mainLight = GetMainLight();
                float3 sun = mainLight.color * LeafSunCookie(centre);
                float cover = saturate(canopyHere.r + canopyHere.g);
                float shaded = height < canopyHere.b ? cover : 0.0;
                float facing = dot(n, mainLight.direction);
                float3 light = max(0.0, FishTrilight(n)) * (1.0 - 0.4 * shaded)
                    + sun * (max(0.0, facing) + 0.35 * max(0.0, -facing)) * (1.0 - 0.75 * shaded);

                /* Its colour. From an evergreen it is an old leaf, dead. From a deciduous tree it has turned —
                 * the autumn tint the trees wear, some leaves further than others — or browned through, or (in
                 * the summer trickle) is still green. */
                float fromDeciduous = deciduous / max(1e-5, density);
                float pick = frac(r.y * 17.31 + r.w * 3.17);
                float jitter = lerp(0.8, 1.2, frac(r.z * 11.3));
                float3 green = _LeafColor.rgb * jitter;
                float3 turned = green * pow(max(_LeafAutumn.rgb, 1e-3), lerp(0.7, 1.25, frac(r.w * 23.1)));
                float3 dry = _LeafDry.rgb * jitter;
                float3 albedo = frac(r.x * 41.7 + r.z) > fromDeciduous ? dry
                    : (pick < _LeafRates.z ? (frac(r.z * 29.7) < _LeafRates.w ? dry : turned) : green);
                float3 lit = albedo * light;

                // The fog between it and the camera (FishPrecipitation.shader says why the layer is needed here).
                if (_FishFogLayerShape.w > 0.5 && _FishFogLayer.x > 0.0)
                {
                    FishFogColumn column = FishFogColumnAt(centre.xz);
                    float3 eye = _WorldSpaceCameraPos.xyz;
                    float through = exp(-_FishFogLayer.x * FishFogPath(eye.y, -view.y, 0.0, distance, column));
                    float3 inFog = FishFogLight(0.5 * (eye.y + centre.y), -view, column, _FishFogLayer.x, FishTerrainSunlit(centre), FishFogSunShare(centre));
                    lit = lit * through + inFog * (1.0 - through);
                }

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = input.corner;
                output.color = float4(lit, _LeafSize.z * visible * edge * lying * gained * gained);
                output.shape = float3(frac(r.y * 5.7) < 0.4 ? frac(r.w * 3.9) : 0.0, floor(lerp(3.0, 7.0, frac(r.z * 7.7))), resolved);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                /* A leaf, drawn: x across, y from the stalk (-1) to the tip (1). The blade's half-width swells from
                 * the base and narrows to a point; some leaves are lobed (oak, maple), the rest entire. A
                 * darker midrib, a stalk. Edges antialiased over a pixel. */
                float2 p = input.uv * 2.0 - 1.0;
                float t = saturate((p.y + 0.8) / 1.8);
                float halfWidth = 0.95 * pow(sin(PI * t), 0.8) * pow(1.0 - 0.45 * t, 0.6);
                halfWidth *= 1.0 - input.shape.x * 0.35 * (0.5 + 0.5 * cos(input.shape.y * PI * t * 2.0));
                float blade = abs(p.x) - halfWidth;
                // The stalk: a thin strip below the blade's base.
                float stalk = p.y < -0.8 ? max(abs(p.x) - 0.05, -1.0 - p.y) : 1.0;
                float inside = min(blade, stalk);
                float aa = max(fwidth(inside), 1e-4);
                float alpha = saturate(0.5 - inside / aa);
                float vein = 1.0 - 0.25 * saturate(1.0 - abs(p.x) / 0.06) * step(-0.8, p.y);

                // Smaller than its pixels: the leaf's mean coverage as a soft dot (FishPrecipitation.shader's sub-pixel rule).
                float spot = 0.55 * saturate(1.0 - dot(p, p));
                alpha = lerp(spot, alpha, input.shape.z);
                vein = lerp(1.0, vein, input.shape.z);

                half4 color = half4(input.color.rgb * vein, input.color.a * alpha);
                clip(color.a - 0.002);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
