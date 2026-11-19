#ifndef FISHMMO_WATER_BREAKER_PASS_INCLUDED
#define FISHMMO_WATER_BREAKER_PASS_INCLUDED

// The breakers' sheet: its vertices, shared by both passes so their depths agree to the bit, and its
// two fragment stages.
//
// The mesh (WaterBreakers) carries nothing but where the break line is: every vertex of one point of
// the line sits ON that point, at the still water, and knows only which branch of the cross-section it
// belongs to and how far along it. The shape is made here, every frame, from the shore's clock — so a
// breaker costs no CPU while it rises, curls and falls, and every client sees the same one.

#include "FishWaterShading.hlsl"
#include "FishWaterBreakerCommon.hlsl"

struct BreakerAttributes
{
	/// xz: the point of the break line, world metres. The object's transform is ignored: the water
	/// the mesh hangs under follows the camera, and the break line must not.
	float4 positionOS : POSITION;
	/// xz: unit direction to the shore.
	float3 normalOS : NORMAL;
	/// x how far along its branch, 0 to 1; y the branch: 0 the back, 1 the lip, 2 the face.
	float4 profile : TEXCOORD0;
	/// x metres from the break line to the water's edge; y how sure the shoreward direction is;
	/// z metres to the nearest end of an open break line; w metres shoreward it may reach without
	/// meeting another breaker (FishWaterBreakerAt's space).
	float4 shore : TEXCOORD1;
};

struct BreakerVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	/// Where the textures are read: the point of the sea this part of the sheet unrolls onto.
	float2 flatXZ : TEXCOORD1;
	float4 screenPos : TEXCOORD2;
	float3 normalWS : TEXCOORD3;
	/// x whitewater, y how much of the sheet is drawn here, z metres above the still water, w metres of water under it.
	float4 state : TEXCOORD4;
	/// x metres from the camera, y fog.
	float2 fog : TEXCOORD5;
};

BreakerVaryings BreakerVertex(BreakerAttributes input)
{
	BreakerVaryings output = (BreakerVaryings)0;

	float2 breakXZ = input.positionOS.xz;
	float2 shoreward = input.normalOS.xz;
	shoreward = dot(shoreward, shoreward) > 1e-8 ? normalize(shoreward) : float2(0.0, 1.0);

	float room = input.shore.x;
	FishWaterBreakerFollow(breakXZ, shoreward, room);
	FishWaterBreakerState breaker = FishWaterBreakerAt(breakXZ, shoreward, room, input.shore.y,
		_BreakerHeight, _BreakerCurl, input.shore.w);
	int branch = (int)(input.profile.y + 0.5);
	FishWaterBreakerPoint section = FishWaterBreakerProfile(breaker, branch, input.profile.x, max(0.05, _BreakerEdgeFade));

	/* No shore keeping time is no clock, and a breaker with no clock would freeze mid-curl; the
	 * sheet stays flat on the water, drawn nowhere. An open break line fades out over its last few
	 * metres rather than ending in a cut-off wall of water.
	 *
	 * The SHAPE is never gated on how much is drawn — only on the clock. The face's last vertex is
	 * always fully faded (it runs out into the still water), and flattening faded vertices threw it
	 * back to the break line, stretching a sliver of sheet across the water behind every breaker. */
	bool keepsTime = FishWaterSurfKeepsTime();
	float drawn = keepsTime ? section.edge * smoothstep(0.0, 6.0, input.shore.z) : 0.0;
	float2 shape = keepsTime ? section.position : 0.0;

	float3 across = float3(shoreward.x, 0.0, shoreward.y);
	float3 positionWS = float3(breakXZ.x, _FishWaterLevel, breakXZ.y) + across * shape.x + float3(0.0, shape.y, 0.0);

	// Square to the branch within the cross-section. Which side is the eye's is settled per pixel.
	float2 tangent = section.tangent;
	float3 normalWS = dot(tangent, tangent) > 1e-10
		? normalize(across * -tangent.y + float3(0.0, tangent.x, 0.0))
		: float3(0.0, 1.0, 0.0);

	output.positionWS = positionWS;
	output.flatXZ = breakXZ + shoreward * (keepsTime ? section.flatX : 0.0);
	output.positionCS = TransformWorldToHClip(positionWS);
	output.screenPos = ComputeScreenPos(output.positionCS);
	output.normalWS = normalWS;
	output.state = float4(section.foam, drawn, shape.y, FishWaterSeabedDepth(output.flatXZ));
	output.fog = float2(distance(positionWS, _WorldSpaceCameraPos), ComputeFogFactor(output.positionCS.z));
	return output;
}

/// <summary>
/// The depth pre-pass: the sheet's own depth, so a curling lip hides the face behind it and the far
/// side of the barrel. Only where it is mostly drawn — a faded edge that wrote depth would cut a hole
/// in whatever is behind it.
/// </summary>
half4 BreakerDepthFragment(BreakerVaryings input) : SV_Target
{
	clip(input.state.y - 0.5);
	return 0.0;
}

/// <summary>The sheet, shaded exactly as the sea is (FishWaterShade), from its own shape.</summary>
half4 BreakerFragment(BreakerVaryings input) : SV_Target
{
	/* Two-sided. A sheet has no inside: from under the lip or behind the face the eye sees its other
	 * side, and that side's normal is the one to light. The shading turns every normal over when the
	 * eye is under the sea, as it must for the ocean's surface, so one seen from below is handed over
	 * already turned. */
	float3 view = _WorldSpaceCameraPos - input.positionWS;
	float3 normal = normalize(input.normalWS);
	normal = dot(normal, view) < 0.0 ? -normal : normal;
	if (_WorldSpaceCameraPos.y < _FishWaterLevel)
	{
		normal = -normal;
	}

	FishWaterSurface wave;
	wave.positionWS = input.positionWS;
	wave.normalWS = normal;
	wave.height = input.state.z;
	// Its white is its own, from the breaker: no white caps on a sheet with no FFT in it.
	wave.jacobian = 1.0;
	wave.breakingKept = 0.0;
	wave.surf = saturate(input.state.x) * _BreakerFoam;
	wave.calm = 0.0;
	wave.depth = input.state.w;

	// Development: where the sheet is (WaterDebugView.BreakerSheet), which its sea-matched shading hides.
	if (_DebugView > 12.5)
	{
		half3 shade = 0.55 + 0.45 * saturate(dot(normal, normalize(float3(0.3, 1.0, 0.2))));
		return half4(half3(1.0, 0.0, 1.0) * shade, saturate(input.state.y));
	}

	half4 color = FishWaterShade(input.positionWS, input.flatXZ, input.screenPos, input.fog.x, input.fog.y, wave);
	color.a *= saturate(input.state.y);
	return color;
}

#endif
