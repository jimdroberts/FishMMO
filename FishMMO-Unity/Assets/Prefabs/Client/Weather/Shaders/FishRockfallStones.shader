// FALLING STONE (2026-10-25, phase I2u; decisions 79, 95): the stones of the falls down the scene's stored chutes and the glowing
// Strombolian bombs, thrown on their arcs, landing, the sciara's rolling on down its chute and cooling. One pre-built field of
// low-poly stones (RockfallField: ROCK_MAX_FALLS × ROCK_STONES_PER_FALL stone slots, then ROCK_MAX_BOMBS bomb slots) that the
// vertex shader places from each fall's and bomb's few numbers on the shared clock (FishRockfall.hlsl); an empty slot collapses to a
// point. Lit by the sun (its shadows and cloud cookie) and the sky's trilight; a bomb glows white-orange, cooling over a minute.
// Forward-only: it draws in the forward and the deferred renderer alike.
Shader "FishMMO/World/Rockfall Stones"
{
    Properties
    {
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "FishRockfall.hlsl"
    #include "FishGroundMap.hlsl"
    #include "FishGroundColour.hlsl"

    struct RockAttributes
    {
        float3 positionOS : POSITION;   // the stone's own shape, one unit across
        float3 normalOS : NORMAL;
        float2 slot : TEXCOORD0;        // x the slot (falls' stones, then bombs), y unused
    };

    struct RockVaryings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        // rgb its colour, a its glow (0..1)
        nointerpolation float4 albedo : TEXCOORD2;
        float fogCoord : TEXCOORD3;
    };

    // Turns v about a unit axis by an angle (Rodrigues).
    float3 RockTurn(float3 v, float3 axis, float angle)
    {
        float s = sin(angle);
        float c = cos(angle);
        return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
    }

    // A hashed unit axis from a seed.
    float3 RockAxis(uint seed)
    {
        float z = RockUnitOf(RockMix(1u, seed)) * 2.0 - 1.0;
        float a = RockUnitOf(RockMix(2u, seed)) * 6.2831853;
        float r = sqrt(saturate(1.0 - z * z));
        return float3(r * cos(a), z, r * sin(a));
    }

    // The ground under a point near the camera (the fine ground map), else the line's own height.
    float RockGround(float3 p)
    {
        bool covered;
        float4 g = FishMistGroundAt(p.xz, covered);
        return covered ? g.x + _FishMistGroundBase : p.y;
    }

    float3 RockColourUnder(float3 p, float3 fallback)
    {
        half4 c = FishGroundColourAt(p);
        // Fresh faces are paler than the weathered ground (the raw chips of a scar).
        return c.a > 0.01 ? lerp(fallback, saturate((float3)c.rgb * 1.25 + 0.04), c.a) : fallback;
    }

    // Where a slot's stone is: its centre, radius, turn (axis, angle, then its own hashed one), colour and glow; false for an empty slot.
    bool RockPlace(uint slot, out float3 centre, out float radius, out float3 rollAxis, out float rollAngle, out uint ownSeed, out float3 colour, out float glow)
    {
        centre = 0.0;
        radius = 0.0;
        rollAxis = float3(1.0, 0.0, 0.0);
        rollAngle = 0.0;
        ownSeed = slot;
        colour = float3(0.5, 0.5, 0.48);
        glow = 0.0;
        float gravity = max(0.5, _RockInfo.w);
        uint stoneSlots = (uint)(ROCK_MAX_FALLS * ROCK_STONES_PER_FALL);
        if (slot < stoneSlots)
        {
            uint f = slot / (uint)ROCK_STONES_PER_FALL;
            uint i = slot - f * (uint)ROCK_STONES_PER_FALL;
            float4 fall = _RockFalls[f];
            if ((float)f >= _RockInfo.x || (float)i >= fall.w)
            {
                return false;
            }
            uint c = (uint)fall.x;
            float4 chute = _RockChutes[c];
            float4 chuteB = _RockChutesB[c];
            bool sciara = chuteB.x > 0.5;
            uint seed = (uint)fall.z;
            RockStone s = RockStoneOf(seed, i, sciara, chute.z, chute.w, gravity);
            float4 st = RockStoneState(s, fall.y);
            float shown = st.w * chuteB.z;
            if (shown <= 0.0)
            {
                return false;
            }
            float2 dir;
            float3 p = RockChutePoint(c, st.x, dir);
            float3 right = float3(dir.y, 0.0, -dir.x);
            p += right * st.y;
            p.y = RockGround(p);
            radius = s.size;
            // Sinking at the end of its rest: down into the ground, not shrunk.
            centre = p + float3(0.0, radius * 0.75 + st.z - 2.2 * radius * (1.0 - shown), 0.0);
            rollAxis = normalize(cross(float3(0.0, 1.0, 0.0), float3(dir.x, 0.0, dir.y)));
            rollAngle = st.x / max(0.05, radius);
            ownSeed = RockMix(i, seed);
            colour = RockColourUnder(p, sciara ? float3(0.07, 0.07, 0.08) : float3(0.55, 0.55, 0.52));
            return true;
        }
        uint b = slot - stoneSlots;
        if (b >= (uint)ROCK_MAX_BOMBS || (float)b >= _RockInfo.y)
        {
            return false;
        }
        float4 A = _RockBombs[b];
        float4 B = _RockBombsB[b];
        float4 C = _RockBombsC[b];
        uint burst = min((uint)B.x, (uint)(ROCK_MAX_BURSTS - 1));
        radius = max(0.02, frac(B.x));
        float4 vent = _RockBursts[burst];
        float t = vent.w - A.w;
        if (t <= 0.0)
        {
            return false;
        }
        float flight = max(0.05, C.x);
        float3 up = float3(0.0, 1.0, 0.0);
        ownSeed = RockMix(b, (uint)C.y);
        float restFrom = flight;
        if (t < flight)
        {
            centre = vent.xyz + A.xyz * t - 0.5 * gravity * t * t * up;
            rollAxis = RockAxis(ownSeed ^ 0x5u);
            rollAngle = t * 9.0;
        }
        else
        {
            float3 land = vent.xyz + A.xyz * flight - 0.5 * gravity * flight * flight * up;
            float tl = t - flight;
            if (B.y >= 0.0 && C.z > 0.0)
            {
                // Down the sciara's chute from where it landed (Rockfall's run; stone 0 of its roll seed gives its speeds).
                uint c = (uint)B.y;
                float4 chute = _RockChutes[c];
                RockStone s = RockStoneOf((uint)C.y, 0u, true, chute.z, chute.w, gravity);
                RockRun run = RockRunOf(C.z, s.peak, s.accel, s.decel);
                float along = B.z + RockAlongAt(run, tl);
                float2 dir;
                float3 p = RockChutePoint(c, along, dir);
                float3 right = float3(dir.y, 0.0, -dir.x);
                p += right * B.w;
                p.y = RockGround(p);
                float hop = RockHopAt(s, along, RockSpeedAt(run, tl), run.peak);
                centre = p + up * (radius * 0.75 + hop);
                rollAxis = normalize(cross(up, float3(dir.x, 0.0, dir.y)));
                rollAngle = (along - B.z) / radius + flight * 9.0;
                restFrom = flight + RockRunTotal(run);
            }
            else
            {
                centre = land + up * (radius * 0.5);
                rollAxis = RockAxis(ownSeed ^ 0x5u);
                rollAngle = flight * 9.0;
            }
        }
        float shown = 1.0 - saturate((t - restFrom - BOMB_REST_SECONDS) / BOMB_SINK_SECONDS);
        if (shown <= 0.0)
        {
            return false;
        }
        centre.y -= 2.2 * radius * (1.0 - shown);
        colour = float3(0.075, 0.055, 0.05);
        glow = RockBombGlow(t) * saturate(_RockBurstsB[burst].x);
        return true;
    }

    RockVaryings RockVertex(RockAttributes input)
    {
        RockVaryings o = (RockVaryings)0;
        uint slot = (uint)(input.slot.x + 0.5);
        float3 centre;
        float radius;
        float3 rollAxis;
        float rollAngle;
        uint ownSeed;
        float3 colour;
        float glow;
        bool live = RockPlace(slot, centre, radius, rollAxis, rollAngle, ownSeed, colour, glow);
        // Its own hashed set, then its roll.
        float3 ownAxis = RockAxis(ownSeed);
        float ownAngle = RockUnitOf(RockMix(3u, ownSeed)) * 6.2831853;
        float3 local = RockTurn(RockTurn(input.positionOS, ownAxis, ownAngle), rollAxis, rollAngle);
        float3 n = RockTurn(RockTurn(input.normalOS, ownAxis, ownAngle), rollAxis, rollAngle);
        float3 positionWS = live ? centre + local * radius : centre;
        o.positionWS = positionWS;
        o.normalWS = n;
        o.positionCS = TransformWorldToHClip(positionWS);
        o.albedo = float4(colour, glow);
        o.fogCoord = ComputeFogFactor(o.positionCS.z);
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RockfallStones"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex RockVertex
            #pragma fragment RockFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include_with_pragmas "FishShadowVariants.hlsl"
            #include_with_pragmas "FishLightCookieVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "FishAmbient.hlsl"

            half4 RockFragment(RockVaryings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1.0, 1.0, 1.0, 1.0));
                float3 albedo = input.albedo.rgb;
                float3 colour = albedo * mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * saturate(dot(n, mainLight.direction));
                colour += albedo * FishTrilight((half3)n);
                // A bomb: white-hot to dull red as it cools (StromboliBombs.GlowAt), its crust cracking dark over the glow.
                float glow = input.albedo.a;
                float3 hot = lerp(float3(2.2, 0.35, 0.05), float3(9.0, 4.0, 1.2), glow * glow);
                colour += hot * glow;
                colour = MixFog(colour, input.fogCoord);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex RockVertex
            #pragma fragment RockDepthFragment

            half RockDepthFragment(RockVaryings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex RockVertex
            #pragma fragment RockDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 RockDepthNormalsFragment(RockVaryings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                float2 rg = saturate(oct * 0.5 + 0.5);
                return half4(PackFloat2To888(rg), 0.0);
                #else
                return half4(NormalizeNormalPerPixel(n), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
