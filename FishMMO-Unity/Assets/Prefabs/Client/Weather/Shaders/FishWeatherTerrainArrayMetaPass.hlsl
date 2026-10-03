// The lightmapper's view of FishMMO/Weather Terrain Array: the blended ground's albedo, so baked bounce light
// takes the colour of the ground it bounced off. TerrainLit's meta pass reads the engine's basemap, which this
// shader does not have (see the shader's header), so it samples the arrays at the texel's world position.
#ifndef FISHMMO_WEATHER_TERRAIN_ARRAY_META_PASS_INCLUDED
#define FISHMMO_WEATHER_TERRAIN_ARRAY_META_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float2 uv0          : TEXCOORD0;
    float2 uv1          : TEXCOORD1;
    float2 uv2          : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD0;
#ifdef EDITOR_VISUALIZATION
    float2 VizUV        : TEXCOORD1;
    float4 LightCoord   : TEXCOORD2;
#endif
    float3 positionWS   : TEXCOORD3;
};

Varyings TerrainVertexMeta(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    TerrainInstancing(input.positionOS, input.normalOS, input.uv0);
    // For some reason, uv1 and uv2 are not populated for instanced terrain. Use uv0 (as TerrainLit does).
    input.uv1 = input.uv2 = input.uv0;
    output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
    output.uv = input.uv0;
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
#ifdef EDITOR_VISUALIZATION
    UnityEditorVizData(input.positionOS.xyz, input.uv0, input.uv1, input.uv2, output.VizUV, output.LightCoord);
#endif
    return output;
}

half4 TerrainFragmentMeta(Varyings input) : SV_Target
{
#ifdef _ALPHATEST_ON
    ClipHoles(input.uv);
#endif
    FishArraySurface ground = FishSampleArraySurface(input.uv, input.positionWS);

    // InitializeBRDFData takes alpha inout, so it needs a variable.
    half alpha = 1.0h;
    BRDFData brdfData;
    InitializeBRDFData(ground.albedo, ground.metallic, half3(0.0h, 0.0h, 0.0h), ground.smoothness, alpha, brdfData);

    MetaInput metaInput;
    metaInput.Albedo = brdfData.diffuse + brdfData.specular * brdfData.roughness * 0.5;
    metaInput.Emission = half3(0.0h, 0.0h, 0.0h);
#ifdef EDITOR_VISUALIZATION
    metaInput.VizUV = input.VizUV;
    metaInput.LightCoord = input.LightCoord;
#endif
    return UnityMetaFragment(metaInput);
}

#endif
