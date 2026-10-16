// A floating hull's shore mark: the drawn hull's own meshes drawn a second time, after the opaques, writing nothing but
// stencil bit 32 where the hull is what the eye sees. The shore pass (FishWaterShore.shader) skips those pixels.
//
// That pass projects the swash, its foam lip, the foam left behind and the wet-sand band onto WHATEVER the depth buffer
// holds near the sea's level: by design, so it hugs the terrain, rocks and pier piles with no mesh of its own. A boat's
// planking at the waterline is exactly such a surface, and every moored boat and passing ship wore a beach's swash band
// up its side. A hull meets the sea through the sea's own surface (its soft edge where the water thins against it),
// not a shoreline's; it is not ground the waves run up and drain from.
//
// Marked rather than told apart in the shore pass: nothing there can tell planking from a rock standing in the surf
// (both are surfaces above the sea bed the shore field holds), and rocks and pier piles keep their surf. Laid on by
// FloatingBoats (one mark per drawn hull renderer, in its LOD group), so a scene cut before this needs no re-cut.
//
// Depth tested against the opaques, pulled a hair toward the eye so it meets its own surface (LEqual on an identical
// mesh is not guaranteed bit-exact once the GPU resident drawer draws the hull from its own instance data); anything in
// front of the hull hides the mark as it hides the hull. No depth written, no colour, no shadow caster, no depth or
// motion-vector pass. Bits in use: 1 XR motion vectors, 32 this, 64 inland water's draw-once, 128 a hull's water lid
// (FishHullLid.shader).
Shader "FishMMO/Water/Hull Shore Mark"
{
    SubShader
    {
        // After every opaque (the hull is in the depth buffer), before the shore (-150) that reads the mark.
        Tags { "Queue" = "Transparent-185" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HullShoreMark"
            Tags { "LightMode" = "UniversalForward" }

            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            // As the ship kit's materials draw (Structure_*.mat, _Cull 2): a back face marked could stand in for a pixel
            // the hull does not cover.
            Cull Back
            Stencil
            {
                Ref 32
                WriteMask 32
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma use_dxc vulkan
            #pragma vertex Vert
            #pragma fragment Frag
            // Drawn as the hull is: by the GPU resident drawer where it takes the hull (the mark sits in the hull's LOD
            // group), instanced otherwise.
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
