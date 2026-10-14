// A floating hull's water lid: a sheet across its open top, just under the gunwale (StructurePieces.TryWaterLid, laid
// by PointOfInterestHarbour under the drawn hull, so it bobs, rolls and heels with it). It draws nothing; where it is
// seen (depth tested against the opaques, so not through the hull's side or anything aboard) it sets stencil bit 128,
// and the sea, inland water, caustics and shore skip those pixels: inside the planking the water plane lies above the
// hull's floor, and the boats took on the sea. From over the water every water fragment in the lid's footprint is
// behind it (the lid stands over the waterline), so marking the pixels is enough; no depth is written, so nothing
// transparent aboard (a torch's flame, a spell, the rain) is hidden. A wave crest over a small boat's rim is the one
// exception, cut where it crosses the lid. After the opaques and before every water pass; no shadow caster, no depth
// prepass.
Shader "FishMMO/Water/Hull Lid"
{
    SubShader
    {
        // Before the lava light (-170), the caustics (-160), the shore (-150) and the sea (-100).
        Tags { "Queue" = "Transparent-180" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HullLid"
            Tags { "LightMode" = "UniversalForward" }

            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Off
            // Bit 128: FishWater, FishInlandWater, FishWaterCaustics and FishWaterShore skip it. Inland water's draw-once
            // bit is 64 (its Greater compare reads both), the XR motion vectors' 1.
            Stencil
            {
                Ref 128
                WriteMask 128
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

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
