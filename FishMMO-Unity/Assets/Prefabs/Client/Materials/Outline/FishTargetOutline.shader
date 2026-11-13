// The target outline (TargetOutlineFeature): a flat mask of the outlined renderers, the nearest mask
// pixel along each row, and the ring blended onto the frame. Every read is a texel load at the pixel's
// own coordinate; the textures are RGBA8 and R8 only.
Shader "Hidden/FishMMO/TargetOutline"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_FishOutlineDepth);
        TEXTURE2D(_FishOutlineMask);
        TEXTURE2D(_FishOutlineNearest);

        // Mask pass: rgb the outline colour (linear), a its width / _FishOutlineSize.y.
        float4 _FishOutlineMaskColor;
        // x: 1 to draw the whole silhouette over whatever is in front of it.
        float4 _FishOutlineMaskParams;
        // The pixels the screen passes work in: x min, y min, x max, y max (max exclusive).
        float4 _FishOutlineRect;
        // x: search radius in pixels, y: the width a mask alpha of 1 holds, z: target width, w: target height.
        float4 _FishOutlineSize;

        bool FishOutlineOutside(int2 p)
        {
            return p.x < (int)_FishOutlineRect.x || p.y < (int)_FishOutlineRect.y ||
                   p.x >= (int)_FishOutlineRect.z || p.y >= (int)_FishOutlineRect.w;
        }
        ENDHLSL

        // 0: the mask. Each outlined renderer drawn flat in its outline's colour; the parts behind the
        // scene's depth are left out unless the outline shows through walls.
        Pass
        {
            Name "Mask"
            ZTest Always ZWrite Off Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex MaskVert
            #pragma fragment MaskFrag
            #pragma target 3.5

            struct MaskAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct MaskVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            MaskVaryings MaskVert(MaskAttributes input)
            {
                MaskVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MaskFrag(MaskVaryings input) : SV_Target
            {
                if (_FishOutlineMaskParams.x < 0.5)
                {
                    float scene = LOAD_TEXTURE2D(_FishOutlineDepth, uint2(input.positionCS.xy)).r;
                    float self = input.positionCS.z;
                    if (unity_OrthoParams.w > 0.5)
                    {
                        #if UNITY_REVERSED_Z
                        if (scene - self > 1e-4) discard;
                        #else
                        if (self - scene > 1e-4) discard;
                        #endif
                    }
                    else
                    {
                        // Its own surface is in the depth texture too: a little slack for the copy and for MSAA edges.
                        float sceneEye = LinearEyeDepth(scene, _ZBufferParams);
                        float selfEye = LinearEyeDepth(self, _ZBufferParams);
                        if (selfEye > sceneEye * 1.005 + 0.05) discard;
                    }
                }
                return half4(_FishOutlineMaskColor);
            }
            ENDHLSL
        }

        // 1: nearest in the row. The signed column offset to the nearest mask pixel within the radius,
        // stored as (offset + 128) / 255; 0 is none.
        Pass
        {
            Name "Nearest"
            ZTest Always ZWrite Off Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment NearestFrag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 NearestFrag(Varyings input) : SV_Target
            {
                int2 p = int2(input.positionCS.xy);
                if (FishOutlineOutside(p))
                {
                    discard;
                }
                int radius = (int)_FishOutlineSize.x;
                int width = (int)_FishOutlineSize.z;
                int best = 1000;
                [loop]
                for (int k = -radius; k <= radius; k++)
                {
                    int x = p.x + k;
                    if (x >= 0 && x < width && abs(k) < abs(best) && LOAD_TEXTURE2D(_FishOutlineMask, int2(x, p.y)).a > 0.0)
                    {
                        best = k;
                    }
                }
                float encoded = best == 1000 ? 0.0 : (best + 128) / 255.0;
                return float4(encoded, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        // 2: the ring. Outside the mask, the nearest mask pixel over the column (offset² + row²), and
        // its colour blended in where the distance is within that pixel's width, with a one-pixel soft edge.
        Pass
        {
            Name "Ring"
            ZTest Always ZWrite Off Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment RingFrag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 RingFrag(Varyings input) : SV_Target
            {
                int2 p = int2(input.positionCS.xy);
                if (FishOutlineOutside(p) || LOAD_TEXTURE2D(_FishOutlineMask, p).a > 0.0)
                {
                    discard;   // outside the work area, or on the object itself
                }
                int radius = (int)_FishOutlineSize.x;
                int height = (int)_FishOutlineSize.w;
                int none = 1 << 20;
                int bestD2 = none;
                int2 source = p;
                [loop]
                for (int k = -radius; k <= radius; k++)
                {
                    int y = p.y + k;
                    if (y < 0 || y >= height)
                    {
                        continue;
                    }
                    float h = LOAD_TEXTURE2D(_FishOutlineNearest, int2(p.x, y)).r;
                    if (h <= 0.0)
                    {
                        continue;
                    }
                    int dx = (int)round(h * 255.0) - 128;
                    int d2 = dx * dx + k * k;
                    if (d2 < bestD2)
                    {
                        bestD2 = d2;
                        source = int2(p.x + dx, y);
                    }
                }
                if (bestD2 >= none)
                {
                    discard;
                }
                float4 mask = LOAD_TEXTURE2D(_FishOutlineMask, source);
                float width = mask.a * _FishOutlineSize.y;
                float cover = saturate(width + 0.5 - sqrt((float)bestD2));
                if (cover <= 0.0)
                {
                    discard;
                }
                return half4(mask.rgb, cover);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
