Shader "FishMMO/Water/Waterfall"
{
    // A fall's sheet of water (InlandWaterRenderer.Falls). Glassy where it bends over the lip, whitening as it
    // falls and takes in air, streaked down its length by the water racing through it, and fraying into strands
    // at its sides and low down where a free-hanging curtain breaks up.
    Properties
    {
        _WaterColor("Water colour (glassy, at the lip)", Color) = (0.10, 0.27, 0.26, 1)
        _FoamColor("Aerated water colour", Color) = (0.93, 0.95, 0.96, 1)
        [NoScaleOffset] _FoamTexture("Streaks (the inland water's foam)", 2D) = "black" {}
        [NoScaleOffset] _NormalMap("Ripples", 2D) = "bump" {}
        _StreakScale("Streak size (m)", Float) = 0.7
        _AerationMetres("Metres fallen to turn white", Float) = 1.0
        _Smoothness("Smoothness", Range(0, 1)) = 0.86
        _Opacity("Opacity", Range(0, 1)) = 0.96
        _Breakup("How much a free curtain frays", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-98"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // After the lakes and rivers (Transparent-99), over the pool it falls into; seen from both sides.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // A closed column (InlandWaterRenderer.SheetMesh): its outside only, so the far side does not show through the near.
        Cull Back

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex FallVertex
            #pragma fragment FallFragment
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _WATER_REFRACTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_WATER_REFRACTION)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif
            #include "FishWaterFog.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor;
                half4 _FoamColor;
                float _StreakScale;
                float _AerationMetres;
                half _Smoothness;
                half _Opacity;
                half _Breakup;
            CBUFFER_END

            TEXTURE2D(_FoamTexture);
            SAMPLER(sampler_FoamTexture);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            /// The inland water's clock, seconds (InlandWaterRenderer): the shared world-motion clock, wrapped here.
            float _FishInlandTime;
            #define FISH_INLAND_CLOCK_WRAP 10000.0
            /// The streaks' and ripples' cycle: 1.1 s, snapped to a whole fraction of the clock's wrap so the wrap
            /// lands on a cycle's start (1.1 s is not one: every 10 000 s the whole fall would jump).
            #define FISH_FALL_CYCLE (FISH_INLAND_CLOCK_WRAP / round(FISH_INLAND_CLOCK_WRAP / 1.1))
            float _FishFallDebug;           // debug: alpha, fallen and the brink as colour
            float _FishInlandCameraInSea;   // 1 under the sea (InlandWaterRenderer.MarkCamera): no falls are seen from there

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;       // a: how solid the sheet is across it (thinning at its sides)
                float2 uv0 : TEXCOORD0;     // x 0…1 across, y metres fallen from the lip
                float2 uv1 : TEXCOORD1;     // x width (m), y how free of the rock it hangs (0 riding it, 1 airborne)
                float2 uv2 : TEXCOORD2;     // x the water's speed at the lip (m/s), y the fall's whole drop (m)
                float2 uv3 : TEXCOORD3;     // x the sheet's thickness here (m), y metres above the lip (on the river's surface)
                float4 tangentOS : TANGENT; // across the curtain: the way it sways
                float2 uv4 : TEXCOORD4;     // x how churned it is from riding rock (1 in contact, fading below), y held still by rock
                float2 uv5 : TEXCOORD5;     // x metres along the water's path from the lip (negative above it)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 fall : TEXCOORD2;    // x across 0…1, y fallen m, z width m, w airborne
                float3 extra : TEXCOORD3;   // x lip speed, y drop, z side solidity
                float4 screenPos : TEXCOORD4;
                float fogFactor : TEXCOORD5;
                float4 sheetDepth : TEXCOORD7; // x thickness (m), y metres above the lip, z churned by rock, w metres along the path from the lip
                float3 flowShare : TEXCOORD6; // x how much water goes over the lip here, over the river's mean (1 average, 0 none); y how open the curtain's edge is here; z 1 on a side face
            };

            /// An angular speed snapped so a whole number of turns fits the clock's 10 000 s wrap: no jump at the wrap.
            float FallOmega(float hertz) { return 6.2831853 * round(hertz * FISH_INLAND_CLOCK_WRAP) / FISH_INLAND_CLOCK_WRAP; }

            Varyings FallVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                /* The curtain sways: slow waves travelling down it, larger the further it has fallen free (the air pushes a
                 * long sheet about), and its open edges flutter faster on top of that. Across the curtain only, so it never
                 * swings into the rock it hangs in front of. */
                float fallenHere = max(0.0, input.uv0.y);
                float freeHere = saturate(input.uv1.y);
                float acrossMetres = input.uv0.x * input.uv1.x;
                float t = _FishInlandTime;
                float swayAmp = freeHere * saturate(fallenHere / 8.0) * min(0.9, 0.03 + 0.012 * fallenHere) * (1.0 - saturate(input.uv4.y));
                float sway = 0.65 * sin(FallOmega(0.11) * t - fallenHere * 0.16 + acrossMetres * 0.05)
                           + 0.35 * sin(FallOmega(0.23) * t - fallenHere * 0.37 + acrossMetres * 0.21 + 1.7);
                float flutter = input.color.g * (1.0 - 0.7 * saturate(input.uv4.y)) * freeHere * saturate(fallenHere / 3.0) * min(0.35, 0.06 + 0.006 * fallenHere)
                    * sin(FallOmega(0.9) * t - fallenHere * 0.95 + acrossMetres * 2.3);
                float3 acrossWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.positionWS += acrossWS * (swayAmp * sway + flutter);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fall = float4(input.uv0.x, input.uv0.y, input.uv1.x, input.uv1.y);
                output.extra = float3(input.uv2.x, input.uv2.y, input.color.a);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.flowShare = float3(input.color.r * 2.0, input.color.g, input.color.b);
                output.sheetDepth = float4(input.uv3, input.uv4.x, input.uv5.x);
                return output;
            }

            /// The streak pattern carried down the sheet: two copies moving at the falling speed, half a cycle apart
            /// and cross-faded, so the water accelerating down the fall never tears the pattern apart over time.
            half2 Streaks(float2 sheet, float speed, float stretch)
            {
                const float cycle = FISH_FALL_CYCLE;
                float time = _FishInlandTime / cycle;
                float phaseA = frac(time);
                float phaseB = frac(time + 0.5);
                half blend = abs(2.0 * phaseA - 1.0);
                float2 scale = float2(_StreakScale, _StreakScale * stretch);
                float2 uvA = (sheet - float2(0.0, -speed * phaseA * cycle)) / scale;
                float2 uvB = (sheet - float2(0.0, -speed * phaseB * cycle)) / scale + float2(0.41, 0.23);
                half a = lerp(SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvA).r, SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvB).r, blend);
                half b = lerp(SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvA * float2(2.7, 1.9) + 0.17).r,
                    SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, uvB * float2(2.7, 1.9) + 0.53).r, blend);
                return half2(a, b);
            }

            float FallHash(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            /// <summary>
            /// The jets: water leaving a ledge breaks into columns a couple of hand-spans wide, each a train of packets
            /// racing down it, dense at the head and drawn out behind as they speed up. Each column its own spacing and
            /// phase; carried as the streaks are (two copies half a cycle apart, cross-faded), so water that accelerates
            /// down the fall never tears the pattern apart over time. x the packets' density, y how far into one's tail.
            /// </summary>
            half2 Jets(float acrossMetres, float along, float fallen, float speed)
            {
                const float columnMetres = 0.45;
                float column = floor(acrossMetres / columnMetres);
                float inColumn = frac(acrossMetres / columnMetres);
                float h1 = FallHash(column * 1.7 + 3.1), h2 = FallHash(column * 2.3 + 7.9);
                float spacing = lerp(1.8, 4.5, h1) * (1.0 + 0.04 * fallen);
                float tail = 0.5 + 0.06 * fallen;
                const float cycle = FISH_FALL_CYCLE;
                float time = _FishInlandTime / cycle;
                float phaseA = frac(time), phaseB = frac(time + 0.5);
                half blend = abs(2.0 * phaseA - 1.0);
                float pa = frac((along - speed * phaseA * cycle) / spacing + h2);
                float pb = frac((along - speed * phaseB * cycle) / spacing + h2 + 0.5);
                // Distance behind the head (metres), in the water's own direction of travel.
                float behindA = (1.0 - pa) * spacing, behindB = (1.0 - pb) * spacing;
                /* Rounded at its front (rising over the first 0.7 m behind the head, a packet's leading mass) and fading down its
                 * tail to nothing before the next. Faded only at the tail's far end, the front was a step from full to none:
                 * hard horizontal edges across the curtain at every packet. */
                half headA = exp(-behindA / tail) * smoothstep(0.0, 0.7, behindA) * smoothstep(spacing, spacing - 0.8, behindA);
                half headB = exp(-behindB / tail) * smoothstep(0.0, 0.7, behindB) * smoothstep(spacing, spacing - 0.8, behindB);
                // Round across the column: a jet is thickest in its middle.
                half body = smoothstep(0.0, 0.5, inColumn) * smoothstep(1.0, 0.5, inColumn);
                return half2(lerp(headA, headB, blend) * lerp(0.55, 1.0, body), lerp(behindA, behindB, blend) / spacing);
            }

            half4 FallFragment(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                if (_FishInlandCameraInSea > 0.5)
                {
                    discard;
                }
                float3 positionWS = input.positionWS;
                float3 view = _WorldSpaceCameraPos - positionWS;
                float distanceToCamera = max(1e-4, length(view));
                view /= distanceToCamera;
                float3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);

                float across = input.fall.x;
                float fallen = max(0.0, input.fall.y);
                float width = input.fall.z;
                half airborne = saturate(input.fall.w);
                float lipSpeed = input.extra.x;
                float drop = max(0.1, input.extra.y);

                // ── The water racing down it ──────────────────────────────────
                const float g = 9.81;
                // The river's own spread across the lip (its solved flow): fast where it runs fast, nothing behind a rock.
                half share = saturate(input.flowShare.x);
                half openEdge = saturate(input.flowShare.y);
                float speed = sqrt(lipSpeed * lipSpeed + 2.0 * g * fallen) * lerp(0.75, 1.15, share);
                // Streaks lengthen as the water speeds up.
                float stretch = 3.0 + 0.25 * fallen;
                // Laid out along the water's own path (metres from the lip), which it travels at `speed`: down the fall everywhere,
                // the river above the lip included. The vertical drop is still what aerates and breaks it.
                float along = input.sheetDepth.w;
                float2 sheet = float2(across * width, -along);
                half2 streak = Streaks(sheet, speed, stretch);
                // Clumps: coarser masses of white tumbling a little faster than the fine streaks, so the sheet never stripes evenly.
                half clump = Streaks(sheet * 0.38 + float2(3.1, 0.0), speed * 1.15, 1.3 + 0.12 * fallen).x;
                half strand = saturate(streak.x * 0.5 + streak.y * 0.22 + clump * 0.5);
                /* Lower down it breaks into jets (Jets): packets with dense heads racing down their columns, the sheet between
                 * them thinning to lace. How far it has broken: none at the lip, most of it a third of the way down. */
                half broken = saturate(fallen / max(2.0, 0.3 * drop)) * lerp(0.4, 1.0, airborne);
                // The columns wander as they fall (the clumps' own noise bends them), and the streaks break up each packet.
                half2 jet = Jets(across * width + (clump - 0.5) * 0.5, along, fallen, speed);
                jet.x *= lerp(0.55, 1.15, streak.x);
                strand = lerp(strand, saturate(strand * 0.6 + jet.x * 0.6), broken);

                /* Air taken in as it falls (riding the rock churns it in sooner): glassy and clear where it leaves the lip,
                 * white further down. A tall fall stays clear longer before it whitens (a tenth of its drop, at least the
                 * material's metres): at a metre or two every fall was white from its brink. */
                float aerationMetres = max(_AerationMetres, 0.1 * drop);
                half aeration = saturate(fallen / max(0.2, aerationMetres * lerp(0.5, 1.0, airborne)));
                aeration = 1.0 - (1.0 - aeration) * (1.0 - aeration);
                half white = smoothstep(0.58 - 0.5 * aeration, 0.9 - 0.32 * aeration, strand + 0.25 * aeration);
                /* Water riding rock (struck by it, or turned aside round it) is churned white and broken, for a few metres below
                 * where it touched: the parting round an obstacle reads as white water pouring off it, not a clean cut. */
                half churned = saturate(input.sheetDepth.z);
                white = max(white, churned * smoothstep(0.15, 0.6, strand + 0.3 * churned));

                /* Ripples racing down it catch the light. Carried as the streaks are, two copies half a cycle apart:
                 * moved by speed × clock, a pattern on water that speeds up as it falls is stretched further every
                 * second until it is a smear of lines, and a half could not hold the clock past a few seconds. */
                float rippleTime = _FishInlandTime / FISH_FALL_CYCLE;
                float ripplePhaseA = frac(rippleTime);
                float ripplePhaseB = frac(rippleTime + 0.5);
                half rippleBlend = abs(2.0 * ripplePhaseA - 1.0);
                float2 rippleScale = float2(_StreakScale * 0.8, _StreakScale * stretch * 0.6);
                float rippleTravel = speed * FISH_FALL_CYCLE / rippleScale.y;
                float2 rippleUVA = sheet / rippleScale + float2(0.0, rippleTravel * ripplePhaseA);
                float2 rippleUVB = sheet / rippleScale + float2(0.29, 0.71 + rippleTravel * ripplePhaseB);
                half3 ripple = UnpackNormalScale(lerp(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, rippleUVA),
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, rippleUVB), rippleBlend), 0.8 * (1.0 - white * 0.6));
                float3 tangentX = normalize(cross(float3(0.0, 1.0, 0.0), normalWS) + float3(1e-4, 0.0, 0.0));
                float3 tangentY = cross(normalWS, tangentX);
                float3 bumped = normalize(normalWS + tangentX * ripple.x + tangentY * ripple.y);

                // ── Light ─────────────────────────────────────────────────────
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half3 lightColor = mainLight.color * mainLight.shadowAttenuation;
                half NdotL = dot(bumped, mainLight.direction);
                half3 ambient = _GlossyEnvironmentColor.rgb;

                // Glassy water near the lip: the sky in it, and what is behind it through it.
                half NdotV = saturate(dot(bumped, view));
                half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                float3 reflectVector = reflect(-view, bumped);
                // Falling water is rough: the sky in it is blurred and weak, never a mirror.
                half3 reflection = GlossyEnvironmentReflection(reflectVector, positionWS, max(0.25, 1.0 - _Smoothness), 1.0h) * 0.45;
                half3 body = _WaterColor.rgb * (lightColor * saturate(NdotL * 0.5 + 0.5) + ambient * 0.8);
                half glassAlpha = 0.4;
                #if defined(_WATER_REFRACTION)
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    half3 behind = SampleSceneColor(screenUV + bumped.xz * 0.02 / max(1.0, distanceToCamera * 0.1));
                    /* Clear water is mostly what is behind it, bent and tinted: the rock through the sheet. At 70 % water
                     * colour it read as teal paint. More of its own colour where it is deeper (further down, thicker). */
                    /* Water lets through what is behind it by how much of it there is: the whole river's depth rolling over
                     * the brink is green and deep, a thin sheet lower down nearly clear. */
                    half absorb = 1.0 - exp(-max(0.02, input.sheetDepth.x) * 2.2);
                    body = lerp(behind * lerp(half3(0.95, 0.99, 0.98), half3(0.72, 0.86, 0.85), absorb), body, lerp(0.12, 0.7, absorb));
                    glassAlpha = 1.0;
                #endif
                half3 halfVector = SafeNormalize(mainLight.direction + view);
                half glint = pow(saturate(dot(bumped, halfVector)), 180.0) * 3.0 * (1.0 - white);
                half3 glassy = lerp(body, reflection, fresnel) + lightColor * glint;

                // Aerated water: white, lit through as well as on (a falling sheet glows with the sun behind it).
                half wrap = saturate(NdotL * 0.5 + 0.5);
                half through = pow(saturate(dot(-view, mainLight.direction)), 6.0) * 0.6;
                half3 foamLit = _FoamColor.rgb * (lightColor * (wrap * 0.85 + through) + ambient * 0.9 + 0.05);
                // Darker in the hollows between strands, so the sheet has depth rather than a flat white; brightest at a jet's head.
                foamLit *= lerp(0.55, 1.1, strand) * lerp(1.0, lerp(0.85, 1.12, jet.x), broken);
                /* Seen face on, the light has come through more water than at a thin edge: the body of a thick curtain is a
                 * blue-grey, its edges and the lace between the jets bright. */
                half faceOn = saturate(abs(dot(normalize(input.normalWS), view)));
                foamLit *= lerp(1.0, lerp(1.05, 0.82, faceOn), saturate(fallen / 6.0) * (1.0 - openEdge * 0.6));

                half3 color = lerp(glassy, foamLit, white);

                // ── How solid it is ───────────────────────────────────────────
                // A curtain hanging free frays into strands at its sides and as it falls; riding the rock it stays whole.
                half side = input.extra.z;
                half fray = _Breakup * airborne * saturate(fallen / max(1.0, 0.6 * drop));
                /* Frayed where it is seen edge-on, from whichever side: the column's silhouette breaks into strands while its
                 * body stays whole, so it is a mass of water from the front and the side alike. And thin where little water
                 * goes over the lip: the curtain parts round a rock standing in it. */
                /* Only the curtain's own edges: its outer sides, and either side of where it parts round rock. Each strip's side
                 * inside the sheet (InlandWaterRenderer's strips overlap there) frayed too, and the curtain read as a row of cords. */
                half rim = 1.0 - saturate(abs(dot(normalize(input.normalWS), view)));
                // Across a face too, toward an open edge: the vertices there carry 1, the joined side 0, so the edge of a cut frays.
                half edge = max(smoothstep(0.6, 0.97, rim), smoothstep(0.45, 1.0, openEdge) * 0.7) * openEdge;
                // Where little water goes over the lip the sheet thins, once it is falling: at the brink it is the river's whole width.
                half thin = (1.0 - smoothstep(0.08, 0.45, share)) * saturate(fallen / 1.5);
                // And open between the jets where it has broken: lace, the rock seen through it.
                half lace = broken * saturate(1.0 - jet.x * 1.8) * 0.18 + saturate(input.sheetDepth.z) * saturate(0.5 - strand) * 0.4;
                half threshold = saturate(fray * 0.75 + (1.0 - side) * 0.6 + edge * 0.55 + thin * 0.85 + lace);
                /* How much water is here, softly: where it is thinning toward a hole or a frayed edge it fades, as a sheet of water
                 * does, rather than switching off at a line. And how much of what is behind it the water lets through: clear
                 * water most of it (the refraction above carries the rock), aerated white water little. */
                half field = strand + 0.25 * (1.0 - threshold) - threshold;
                half solid = smoothstep(-0.3, 0.3, field);
                half density = lerp(0.55, 0.94, white) * lerp(0.75, 1.0, saturate(fallen / 3.0));
                half alpha = _Opacity * solid * density * lerp(glassAlpha, 1.0, white) * smoothstep(0.0, 0.05, side);
                // Above the lip it lies on the river's own water: fading in toward the brink, so the two are one surface.
                alpha *= smoothstep(1.5, 0.4, input.sheetDepth.y);
                // A side face shows only as far as that side has opened: shut against its neighbour it lies inside the sheet.
                alpha *= lerp(1.0, smoothstep(0.05, 0.6, openEdge), saturate(input.flowShare.z));

                if (_FishFallDebug > 0.5)
                {
                    return half4(saturate(alpha * 2.0), saturate(fallen / 5.0), saturate(input.sheetDepth.y), 1.0);
                }
                color = MixFog(color, input.fogFactor);
                float2 fogUV = input.screenPos.xy / input.screenPos.w;
                color = FishWaterBehindClouds(color, fogUV);
                if (_FishAirFogRange.z < 0.5)
                {
                    half fogKeep;
                    color = FishWaterAirFog(color, positionWS, fogUV, fogKeep);
                }
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
