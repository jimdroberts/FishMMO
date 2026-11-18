// The face of a scene teleporter (PortalSurface): the destination's baked view (a cubemap, PortalViewBaker) looked up by the
// direction from the eye through the quad, turned to the arrival's facing, under a slow swirl; a glowing rim, and glow over
// everything as the eye comes close (where a baked view, with no near parallax, would give itself away). Dimmed with the
// local sky's light, so a portal is not a noon window at night. Opaque, both sides, cut to an arch: square below, a half
// circle as wide as the quad on top (the quad's size is read from its transform, so any opening works).
Shader "FishMMO/Portal"
{
    Properties
    {
        _GlowColour("Glow (linear)", Color) = (0.45, 0.75, 1.0, 1.0)
        _Swirl("Swirl (radians)", Range(0.0, 0.3)) = 0.06
        _RimMetres("Rim width (metres)", Range(0.02, 1.0)) = 0.3
        [NoScaleOffset] _PortalCube("Destination view", Cube) = "black" {}
        [HideInInspector] _PortalYaw("Yaw (radians)", Float) = 0
        [HideInInspector] _PortalHasView("Has view", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "Portal"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex PortalVertex
            #pragma fragment PortalFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/FishMMO/Shaders/Surface/FishAmbient.hlsl"

            TEXTURECUBE(_PortalCube);
            SAMPLER(sampler_PortalCube);

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColour;
                float _Swirl;
                float _RimMetres;
                float _PortalYaw;
                float _PortalHasView;
            CBUFFER_END

            struct PortalAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct PortalVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fog : TEXCOORD2;
                float2 sizeMetres : TEXCOORD3;
            };

            PortalVaryings PortalVertex(PortalAttributes input)
            {
                PortalVaryings o = (PortalVaryings)0;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                float4x4 m = GetObjectToWorldMatrix();
                o.sizeMetres = float2(length(float3(m._m00, m._m10, m._m20)), length(float3(m._m01, m._m11, m._m21)));
                return o;
            }

            // About up by a, as Quaternion.Euler(0, a, 0) turns a direction (+Z toward +X).
            float3 PortalTurn(float3 d, float a)
            {
                float s, c;
                sincos(a, s, c);
                return float3(d.x * c + d.z * s, d.y, -d.x * s + d.z * c);
            }

            half4 PortalFragment(PortalVaryings input) : SV_Target
            {
                float t = _Time.y;
                // Metres from the centre, and how far inside the arch this pixel is (negative: outside, cut away).
                float2 size = max(input.sizeMetres, 0.01);
                float2 p = (input.uv - 0.5) * size;
                float halfWidth = size.x * 0.5;
                float spring = size.y * 0.5 - halfWidth;
                float inside = p.y > spring
                    ? halfWidth - length(float2(p.x, p.y - spring))
                    : min(halfWidth - abs(p.x), p.y + size.y * 0.5);
                clip(inside);
                float rim = 1.0 - smoothstep(0.0, _RimMetres, inside);
                // 0 at the heart of the opening, 1 at its edge.
                float edgeDistance = 1.0 - saturate(inside / halfWidth);
                float2 c = p / halfWidth;

                float3 eye = GetCameraPositionWS();
                float3 look = normalize(input.positionWS - eye);
                // The swirl: stronger toward the rim, turning slowly.
                float swirl = _Swirl * (0.35 + edgeDistance) * sin(t * 0.6 + edgeDistance * 7.0);
                float3 dir = PortalTurn(look, _PortalYaw + swirl);
                dir.y += 0.03 * sin(t * 0.9 + c.x * 5.0) * edgeDistance;
                float3 view = SAMPLE_TEXTURECUBE_LOD(_PortalCube, sampler_PortalCube, normalize(dir), 0).rgb * _PortalHasView;

                // Dimmed with the local sky: night here darkens the window too (the bake is lit at noon).
                float sky = dot(_FishAmbientSky.rgb, float3(0.299, 0.587, 0.114));
                view *= clamp(sky * 3.0, 0.3, 1.1);

                // Glow: the rim, a slow shimmer, everything as the eye comes within a few metres, and all of it when there
                // is no baked view to show.
                float near = 1.0 - saturate((distance(eye, input.positionWS) - 1.2) / 3.0);
                float shimmer = 0.06 * (0.5 + 0.5 * sin(t * 2.3 + edgeDistance * 11.0));
                float glow = saturate(rim + near * 0.75 + shimmer + (1.0 - _PortalHasView) * 0.65);
                float3 colour = lerp(view, _GlowColour.rgb * 1.4, glow);
                colour += _GlowColour.rgb * rim * 0.8;
                colour = MixFog(colour, input.fog);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        // In the depth texture too, cut to the same arch: the fog and the depth-read effects behind it must see the portal,
        // not the ground beyond.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma target 3.5
            #pragma vertex PortalDepthVertex
            #pragma fragment PortalDepthFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct PortalDepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct PortalDepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 sizeMetres : TEXCOORD1;
            };

            PortalDepthVaryings PortalDepthVertex(PortalDepthAttributes input)
            {
                PortalDepthVaryings o = (PortalDepthVaryings)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                float4x4 m = GetObjectToWorldMatrix();
                o.sizeMetres = float2(length(float3(m._m00, m._m10, m._m20)), length(float3(m._m01, m._m11, m._m21)));
                return o;
            }

            half PortalDepthFragment(PortalDepthVaryings input) : SV_Target
            {
                float2 size = max(input.sizeMetres, 0.01);
                float2 p = (input.uv - 0.5) * size;
                float halfWidth = size.x * 0.5;
                float spring = size.y * 0.5 - halfWidth;
                clip(p.y > spring ? halfWidth - length(float2(p.x, p.y - spring)) : min(halfWidth - abs(p.x), p.y + size.y * 0.5));
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
