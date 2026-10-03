#ifndef FISHMMO_LAVA_PASSES_INCLUDED
#define FISHMMO_LAVA_PASSES_INCLUDED

// The lava's three passes: the lit surface, and the depth and depth-normals prepasses URP asks an
// opaque for. All three place a vertex with LavaPositionWS, so a prepass's depth and the colour pass
// agree to the bit and the colour pass's ZTest never fights the depth already laid down.

#include "FishLava.hlsl"

struct LavaAttributes
{
	float4 positionOS : POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

/// <summary>
/// Where a vertex of the disc is: under the camera in XZ (the mesh is dragged there), at the level
/// WaterSurface publishes in Y.
/// </summary>
/// <remarks>
/// The level is a global, not the mesh's height, for the ocean's reason: the transform follows the
/// camera every frame and must not carry the surface with it. Flat — a viscous melt carries no
/// wind waves, and the crust's relief and the slow swell, a few tens of centimetres, are in the
/// normal, not the geometry (so WaterSurface.HeightAt stays exact for anything floating on it).
/// </remarks>
float3 LavaPositionWS(float3 positionOS)
{
	float3 positionWS = TransformObjectToWorld(positionOS);
	positionWS.y = _FishWaterLevel;
	return positionWS;
}

// ── The lit surface ────────────────────────────────────────────────────

struct LavaVaryings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	// The melt's glow (rgb) and the luminance glows are measured against (w): the same for every
	// vertex, worked out here so the fragment does not repeat sixteen bins of Planck for it per pixel.
	float4 melt : TEXCOORD1;
	// x the scale that puts a glow's peak on the melt's, y fog, z the eye's adapted response
	float3 heat : TEXCOORD2;
	// The lake's mean glow for old, middling and young crust (FishLavaMeanGlow), for where the plates
	// are finer than a pixel: per material and per frame, so worked out here rather than per pixel.
	half3 meanOld : TEXCOORD3;
	half3 meanMiddle : TEXCOORD4;
	half3 meanYoung : TEXCOORD5;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

LavaVaryings LavaVertex(LavaAttributes input)
{
	LavaVaryings output = (LavaVaryings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

	float3 positionWS = LavaPositionWS(input.positionOS.xyz);
	output.positionWS = positionWS;
	output.positionCS = TransformWorldToHClip(positionWS);

	float3 meltGlow;
	float3 reference;
	FishLavaReference(meltGlow, reference);
	output.melt = float4(meltGlow, reference.x);
	output.heat = float3(reference.y, ComputeFogFactor(output.positionCS.z), reference.z);
	output.meanOld = (half3)FishLavaMeanGlow(0.0, reference, meltGlow);
	output.meanMiddle = (half3)FishLavaMeanGlow(0.5, reference, meltGlow);
	output.meanYoung = (half3)FishLavaMeanGlow(1.0, reference, meltGlow);
	return output;
}

/// <summary>Adds one phase of the flow map into the running blend.</summary>
void LavaAccumulate(inout FishLavaCrust sum, FishLavaCrust phase, float weight)
{
	sum.glow += phase.glow * weight;
	sum.slope += phase.slope * weight;
	sum.fresh += phase.fresh * weight;
	sum.crack += phase.crack * weight;
	sum.kelvin += phase.kelvin * weight;
	sum.age += phase.age * weight;
	sum.rough += phase.rough * weight;
	sum.cavity += phase.cavity * weight;
	sum.event += phase.event * weight;
}

half4 LavaFragment(LavaVaryings input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

	float3 positionWS = input.positionWS;
	float2 xz = positionWS.xz;
	float3 meltGlow = input.melt.rgb;
	float3 reference = float3(input.melt.w, input.heat.x, input.heat.z);
	// How much ground one pixel covers: what decides which cracks and plates can be resolved, and
	// (as the gradients) which mip of the crust maps.
	float2 dxz = ddx(xz);
	float2 dyz = ddy(xz);
	float footprint = max(length(dxz), length(dyz));

	FishLavaFlow flow = FishLavaConvection(xz, footprint);

	/* The flow map: two copies of the crust, each advected along the drift for one period and then
	 * re-dealt, half a period apart, so one is always well into its drift while the other resets
	 * unseen. Each owns the picture for half its cycle and they cross-fade over the other half — a
	 * plain triangle blend shows both patterns half the time, and the double image reads as a ghost.
	 * The reset is staggered across the lake (phaseOffset) so the whole surface never pulses at once.
	 *
	 * The cycle count wraps with WaterSurface's clock: at 10 000 s every phase and every deal comes
	 * back to where it started, so the wrap is seamless — when _FlowPeriod divides 10 000. */
	float period = max(1.0, _FlowPeriod);
	float cycles = max(1.0, round(FISH_LAVA_CLOCK_WRAP / period));
	float clock = _FishWaterTime / period + flow.phaseOffset;
	float phaseA = frac(clock);
	float phaseB = frac(clock + 0.5);
	float weightA = smoothstep(0.25, 0.75, 1.0 - abs(2.0 * phaseA - 1.0));
	float cycleA = fmod(floor(clock), cycles);
	float cycleB = fmod(floor(clock + 0.5), cycles) + 977.0;

	/* Plates finer than a pixel alias however their edges are filtered — a per-plate age is a per-plate
	 * brightness, and a field of them under a pixel crawls. Past a few plates a pixel the lake is drawn
	 * as its average light and an even crust — and once the plates are gone entirely (the far lake,
	 * most of a big lake's pixels) neither phase is worked out at all. */
	float size = max(0.5, _PlateSize);
	float detail = 1.0 - smoothstep(0.12 * size, 0.5 * size, footprint);

	FishLavaCrust crust = (FishLavaCrust)0;
	crust.rough = FISH_LAVA_DETAIL_MEAN_ROUGH;
	crust.cavity = FISH_LAVA_DETAIL_MEAN_CAVITY;
	UNITY_BRANCH
	if (detail > 0.001)
	{
		crust.rough = 0.0;
		crust.cavity = 0.0;
		UNITY_BRANCH
		if (weightA > 0.001)
		{
			LavaAccumulate(crust, FishLavaPhase(xz, flow, phaseA, cycleA, footprint, dxz, dyz, reference, meltGlow), weightA);
		}
		UNITY_BRANCH
		if (weightA < 0.999)
		{
			LavaAccumulate(crust, FishLavaPhase(xz, flow, phaseB, cycleB, footprint, dxz, dyz, reference, meltGlow), 1.0 - weightA);
		}
	}

	UNITY_BRANCH
	if (detail < 0.999)
	{
		float3 mean = FishLavaMeanAt(flow.young, input.meanOld, input.meanMiddle, input.meanYoung);
		crust.glow = lerp(mean, crust.glow, detail);
		crust.slope *= detail;
		crust.fresh = lerp(0.2, crust.fresh, detail);
		crust.crack *= detail;
	}

	/* The swell: the lake is not dead flat. Slow bulges a few tens of metres across and a few tens of
	 * centimetres high, where gas gathers under the crust and the melt sloshes in its basin. In the
	 * NORMAL only: at this height and length it moves no silhouette a player could see, and keeping
	 * the geometry at the level keeps the depth prepasses, HeightAt and everything floating on the lava
	 * exact. Two octaves orbiting in opposite senses once per 400 s (which divides the clock's wrap). */
	float swellLength = max(5.0, _SwellLength);
	float swellTurn = 6.2831853 * frac(_FishWaterTime / 400.0);
	float2 swellOrbit = float2(cos(swellTurn), sin(swellTurn));
	float3 swellBroad = FishLavaNoise(xz / swellLength + swellOrbit * 0.5);
	float3 swellFine = FishLavaNoise(xz / (0.45 * swellLength) - swellOrbit.yx * 0.7 + 5.3);
	float swellResolve = 1.0 - smoothstep(0.1 * swellLength, 0.4 * swellLength, footprint);
	float2 slope = crust.slope + (swellBroad.yz * (_SwellHeight / swellLength)
		+ swellFine.yz * (0.35 * _SwellHeight / (0.45 * swellLength))) * swellResolve;

	// Bubble bursts and spatter, over whatever the crust is doing there.
	float3 burstGlow;
	float2 burstSlope;
	float burstKelvin;
	float burst = FishLavaBurst(xz, footprint, reference, burstGlow, burstSlope, burstKelvin);
	slope += burstSlope;
	float3 glow = FishLavaSpill(lerp(crust.glow, burstGlow, burst));
	float molten = max(crust.crack, burst);
	float3 normalWS = normalize(float3(-slope.x, 1.0, -slope.y));

	if (_DebugView > 0.5)
	{
		int term = (int)round(_DebugView);
		half3 shown = 0.0;
		if (term == 1) shown = saturate((lerp(crust.kelvin, burstKelvin, burst) - 500.0) / (_MeltKelvin - 500.0)).xxx;
		else if (term == 2) shown = saturate(log2(1.0 + crust.age) / 11.0).xxx;
		else if (term == 3) shown = half3(saturate(length(flow.velocity) / (2.0 * max(_FlowSpeed, 1e-3))), flow.young, 0.0);
		else if (term == 4) shown = molten.xxx;
		else if (term == 5) shown = crust.fresh.xxx;
		else if (term == 6) shown = normalWS * 0.5 + 0.5;
		else if (term == 7) shown = half3(crust.event, burst, 0.0);
		return half4(shown, 1.0);
	}

	// ── Light on the crust ────────────────────────────────────────────
	//
	// Quenched basalt is black glass. Fresh, it is smooth — a dark mirror with a silvery sheen toward a
	// low sun or a bright sky; with age it dulls, as fallout (tephra, Pele's hair) settles on it and the
	// surface crazes. Glass reflects 4% head on (F0 0.04), rising by Fresnel toward grazing — which is
	// where the sheen comes from — capped by roughness so old rough crust never turns into a mirror at
	// the horizon. The open melt in a seam reflects too, but its glow is far brighter than anything it
	// reflects, so the lit term is the crust's alone.
	float3 view = normalize(_WorldSpaceCameraPos - positionWS);
	float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
	float4 shadowCoord;
	#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
		shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
	#else
		shadowCoord = TransformWorldToShadowCoord(positionWS);
	#endif
	Light mainLight = GetMainLight(shadowCoord);
	half3 lightColor = mainLight.color * mainLight.shadowAttenuation;
	half NdotL = saturate(dot(normalWS, mainLight.direction));
	half NdotV = saturate(dot(normalWS, view));

	half crustShare = 1.0h - (half)molten;
	half fresh = (half)crust.fresh;
	half cavity = (half)crust.cavity;
	// Fresh glass is blacker than the weathered, ash-dusted crust it becomes; pits hold shadow.
	// Albedo 0.04 fresh to 0.065 old: the silvery look is the specular sheen, never the albedo.
	half3 albedo = _CrustColor.rgb * (1.0h - 0.4h * fresh) * cavity;
	half smoothness = lerp(_AgedSmoothness, _CrustSmoothness, fresh) * (1.0h - 0.7h * (half)crust.rough);
	half perceptualRoughness = 1.0h - smoothness;
	half roughness = perceptualRoughness * perceptualRoughness;
	/* What the crust map's mip chain averaged away goes back in as roughness (Toksvig): from a texel
	 * a pixel, by the map's own slope variance, so the far crust is a dull sheen and not a mirror. */
	float texel = max(0.1, _CrustTile) / FISH_LAVA_DETAIL_TEXELS;
	half lost = saturate(log2(max(footprint / texel, 1.0)) / 6.0);
	half a2 = max(roughness * roughness + FISH_LAVA_DETAIL_VARIANCE * 0.5 * _CrustDetail * _CrustDetail * lost, 1e-6h);
	/* Specular anti-aliasing (Kaplanyan et al. 2016, projected-space NDF filtering): the slope is the
	 * normal in projected space, and how much it changes across the pixel is the spread of normals the
	 * pixel averages, which widens the lobe. Without it a glassy plate under a bright sky shimmers and
	 * shows its folds as moiré rings — the mips only know about the texture, not the plate, the swell
	 * and the domes on top of it. */
	float2 slopeDx = ddx(slope);
	float2 slopeDy = ddy(slope);
	half kernel = (half)min(2.0 * FISH_LAVA_SPECULAR_AA_VARIANCE * (dot(slopeDx, slopeDx) + dot(slopeDy, slopeDy)), FISH_LAVA_SPECULAR_AA_LIMIT);
	a2 = saturate(a2 + kernel);
	roughness = sqrt(a2);
	perceptualRoughness = sqrt(roughness);

	half fresnelView = Pow4(1.0h - NdotV);
	// From the FILTERED roughness: a pixel of averaged facets does not mirror the horizon either.
	half grazing = saturate(1.04h - perceptualRoughness);
	half3 diffuse = albedo * (lightColor * NdotL + _GlossyEnvironmentColor.rgb) * 0.96h;

	// GGX with URP's minimal visibility term, and Fresnel on the half-angle: at a low sun the glass
	// mirrors ten times what it does head on, the silvery sheen of a lava lake in the late afternoon.
	half3 halfVector = SafeNormalize(mainLight.direction + view);
	half NdotH = saturate(dot(normalWS, halfVector));
	half LdotH = saturate(dot(mainLight.direction, halfVector));
	half d = NdotH * NdotH * (a2 - 1.0h) + 1.00001h;
	half specular = a2 / ((d * d) * max(0.1h, LdotH * LdotH) * (roughness * 4.0h + 2.0h));
	half fresnelLight = 0.04h + 0.96h * Pow4(1.0h - LdotH) * (1.0h - LdotH);
	half3 sun = lightColor * NdotL * (fresnelLight * min(specular, 100.0h));

	// URP's environment BRDF for a dielectric of F0 0.04.
	half3 reflection = GlossyEnvironmentReflection(reflect(-view, normalWS), positionWS,
		perceptualRoughness, cavity, screenUV)
		* (lerp(0.04h, grazing, fresnelView) / (a2 + 1.0h)) * cavity;

	half3 color = (diffuse + sun + reflection) * crustShare + glow;

	/* Opaque, so it draws before the weather's fog and clouds and takes both from the passes that
	 * draw them, through the depth it writes — unlike the transparent sea, which has to lay them back
	 * over itself (FishWaterFog.hlsl). Only Unity's own fog is applied here, as any opaque does. */
	color = MixFog(color, input.heat.y);
	return half4(color, 1.0);
}

// ── Depth prepasses ────────────────────────────────────────────────────
//
// Not optional for an opaque here. The HighFidelity renderer's SSAO takes depth AND normals, so URP
// runs a depth-normals prepass and the camera's depth texture comes FROM it: a shader with no
// DepthNormals pass is missing from that texture, and the height fog, the volumetric fog and the
// clouds — all of which read it — would see straight through the lava to the bed beneath.

struct LavaDepthVaryings
{
	float4 positionCS : SV_POSITION;
	UNITY_VERTEX_INPUT_INSTANCE_ID
	UNITY_VERTEX_OUTPUT_STEREO
};

LavaDepthVaryings LavaDepthVertex(LavaAttributes input)
{
	LavaDepthVaryings output = (LavaDepthVaryings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
	output.positionCS = TransformWorldToHClip(LavaPositionWS(input.positionOS.xyz));
	return output;
}

half LavaDepthFragment(LavaDepthVaryings input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	return input.positionCS.z;
}

/// <summary>
/// The surface's normal for SSAO: straight up. A lake is flat at every scale ambient occlusion
/// works at; the crust's relief is far finer than its kernel.
/// </summary>
half4 LavaDepthNormalsFragment(LavaDepthVaryings input) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	return half4(0.0, 1.0, 0.0, 0.0);
}

#endif
