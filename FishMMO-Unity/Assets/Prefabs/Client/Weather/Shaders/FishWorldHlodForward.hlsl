// Retain the indirect prop's contact/weather branches without reading instance buffers.
#define FISH_INDIRECT_INSTANCED 1
static float FishIndirectLodFade = 0.0;
#include "FishWorldHlodSetup.hlsl"
#include "FishWeatherLitForwardPass.hlsl"
struct HlodAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float4 lookAndOrigin : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
Varyings HlodVertex(HlodAttributes input)
{
    Attributes original = (Attributes)0;
    original.positionOS = input.positionOS;
    original.normalOS = input.normalOS;
    original.tangentOS = input.tangentOS;
    original.texcoord = input.texcoord;
    original.fishLook = float2(input.lookAndOrigin.x, 0.0);
    UNITY_TRANSFER_INSTANCE_ID(input, original);
    Varyings output = LitPassVertex(original);
    output.fishRockOrigin = input.lookAndOrigin.yzw;
    output.fishContact = FishContactHeight(input.positionOS.xyz, output.fishRockOrigin);
    return output;
}
