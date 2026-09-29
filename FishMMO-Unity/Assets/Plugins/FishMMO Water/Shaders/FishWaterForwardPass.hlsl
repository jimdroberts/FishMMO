#ifndef FISHMMO_WATER_FORWARD_INCLUDED
#define FISHMMO_WATER_FORWARD_INCLUDED

// The open ocean: the FFT sea on the camera-centred disc. Its shading is FishWaterShading.hlsl,
// shared with the breakers; what is here is where each point of the sea is, and the whitewater the
// breakers leave on the still water inshore of them.

#include "FishWaterShading.hlsl"
#include "FishWaterBreakerCommon.hlsl"

struct Attributes
{
	float4 positionOS : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	// The point on the FLAT sea this vertex came from. The fragment re-evaluates the wave sum
	// here, so the normal is per pixel rather than interpolated across a triangle tens of metres
	// wide — which is what made the sea render as flat blue lozenges.
	float2 flatXZ : TEXCOORD1;
	float4 screenPos : TEXCOORD2;
	// x amplitude fade, y distance to camera, z fog
	float3 surface : TEXCOORD3;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings WaterVertex(Attributes input)
{
	Varyings output = (Varyings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	float3 flatWS = TransformObjectToWorld(input.positionOS.xyz);
	// The still-water plane is a global, not the mesh's own height: the mesh is dragged under the
	// camera every frame and must not carry the sea level with it.
	flatWS.y = _FishWaterLevel;

	float distanceToCamera = distance(flatWS, _WorldSpaceCameraPos);
	float fade = FishWaterAmplitudeFade(distanceToCamera);
	/* How far this vertex is from its neighbours (WaterSurface: the ring's radius times the ring gap),
	 * so a cascade whose waves the vertices are too far apart to carry is left to the normals. They were
	 * all kept, "because the geometry must not shrink": 27 m waves on vertices three to seven metres
	 * apart from forty metres out — two to four a wave — and the disc slides with the camera, so each
	 * vertex sampled a different part of each wave from frame to frame and the surface between them
	 * changed shape: the sea wiggled whenever the camera moved, most when it strafed, which slides the
	 * vertices round every ring. The fragment still shades every cascade the pixel can resolve. */
	float vertexGap = _FishWaterMeshSpacing.x * max(length(input.positionOS.xz), _FishWaterMeshSpacing.y);
	FishWaterSurface surface = FishWaterDisplace(flatWS, fade, vertexGap);

	output.positionWS = surface.positionWS;
	output.flatXZ = flatWS.xz;
	output.positionCS = TransformWorldToHClip(surface.positionWS);
	output.screenPos = ComputeScreenPos(output.positionCS);
	output.surface = float3(fade, distanceToCamera, ComputeFogFactor(output.positionCS.z));
	return output;
}

/// <summary>
/// The still water inshore of the break line, as the breakers leave it: whitewater (0 to 1, before
/// the material's strength) from the bores running in and what the foam memory kept of the ones before
/// them — and the tilt of the water across each bore's front, which is added to the normal.
/// </summary>
float WaterWhitewater(float2 xz, float depth, inout float3 normalWS)
{
	if (!FishWaterSurfKeepsTime() || depth >= _FishWaterBreakDepth.x || _FishWaterShoreRect.z < 1.0)
	{
		return 0.0;
	}
	float2 shoreward;
	float confidence = FishWaterShoreFacing(xz, shoreward);
	float3 bore = FishWaterBoreFoam(xz, depth, FishWaterShoreDistance(xz), shoreward, confidence);
	// Rising toward the shore across a front: the water leans back toward the sea it came from.
	normalWS = normalize(normalWS - float3(shoreward.x, 0.0, shoreward.y) * bore.z);
	float2 uv = (xz - _FishWaterShoreRect.xy) / _FishWaterShoreRect.zw;
	float kept = SAMPLE_TEXTURE2D_LOD(_FishWaterFoamMemory, sampler_FishWaterFoamMemory, uv, 0).g;
	return saturate(max(bore.x, kept * 0.85));
}

half4 WaterFragment(Varyings input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	// How much ground one pixel covers — what decides which waves and ripples can be resolved.
	float footprint = max(length(ddx(input.positionWS.xz)), length(ddy(input.positionWS.xz)));
	FishWaterSurface wave = FishWaterDisplace(
		float3(input.flatXZ.x, _FishWaterLevel, input.flatXZ.y), input.surface.x, footprint);
	wave.surf = WaterWhitewater(input.flatXZ, wave.depth, wave.normalWS) * _SurfStrength;

	return FishWaterShade(input.positionWS, input.flatXZ, input.screenPos, input.surface.y, input.surface.z, wave);
}

#endif
