#ifndef FISHMMO_INLAND_WATER_INCLUDED
#define FISHMMO_INLAND_WATER_INCLUDED

// Lakes and rivers. Its own material block (a shader has exactly one UnityPerMaterial, and it must
// match the Properties block), the sea's ideas without the sea's globals: a scene with no sea still
// has lakes, and there is no wind or FFT to lean on. The one exception is the sea's level, tide in
// (_FishWaterLevel), read only by rivers that drain to the sea, which only a scene with a sea has.

#if defined(_WATER_DEPTH) || defined(_WATER_REFRACTION)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif
#if defined(_WATER_REFRACTION)
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#endif
// The weather's clouds and fog, laid over the frame before the water was drawn: the same as the sea's.
#include "FishWaterFog.hlsl"
#include "FishWaterFoam.hlsl"

CBUFFER_START(UnityPerMaterial)
	half4 _ShallowColor;
	half4 _DeepColor;
	float4 _WaterDensity;
	half _Turbidity;
	half4 _SiltColor;
	float _NormalScale;
	half _NormalStrength;
	float _NormalFadeDistance;
	float _FlowCycle;
	half _Smoothness;
	half _ReflectionStrength;
	half _SpecularStrength;
	half _RefractionStrength;
	float _EdgeFade;
	half _MaxAlpha;
	float _FoamScale;
	half4 _FoamColor;
CBUFFER_END

TEXTURE2D(_NormalMap);
SAMPLER(sampler_NormalMap);
TEXTURE2D(_FoamTexture);
SAMPLER(sampler_FoamTexture);

/// The inland water's clock, seconds: the shared world-motion clock wrapped at FISH_INLAND_CLOCK_WRAP
/// (InlandWaterRenderer).
float _FishInlandTime;
/// Where InlandWaterRenderer wraps _FishInlandTime (WorldMotion.ShaderWrapSeconds). A period that divides
/// it comes round to its start at the wrap, so whatever cycles on the clock snaps its period to do so.
#define FISH_INLAND_CLOCK_WRAP 10000.0
float _FishInlandCameraUnder;
/// 1 while the camera is under the sea (InlandWaterRenderer.MarkCamera): no inland water is drawn then.
float _FishInlandCameraInSea;
// Debug (InlandWaterRenderProbe FISHMMO_INLAND_DEBUG): foam (1) the foam's inputs as colour, r the river's churn, g the
// fall's, b stones and the shore, instead of the water; noglint (2) the water without the sun's glints.
float _FishInlandDebugFoam;
/// The sea's surface now, tide included (WaterSurface).
float _FishWaterLevel;
/// Where the scene's falls land (InlandWaterRenderer.Falls): xyz the foot, w how far round it the pool churns.
float4 _FishFalls[16];
/// What each of those falls brings to its pool (the same index): x the curtain's thickness where it lands (m), y its
/// power (MW), z how high the boil heaves (m), w how white it churns the pool (0 … 1, by its power). All 0 where the
/// renderer has not published them (FallPool then keeps the older look: churn as white as before, no boil).
float4 _FishFallsB[16];
float _FishFallCount;
/// The falls on this river (InlandWaterRenderer, per renderer; count 0 on lakes and on rivers with none): x metres
/// along the river where the curtain lands, y how far downstream its foam is carried (m), z how strongly (0 … 1),
/// w metres across the river at the landing (the riverUV frame).
float4 _RiverFalls[4];
float _RiverFallCount;
/// A river's solved flow (InlandWaterRenderer.Flow, per renderer): along and across its line over its mean speed,
/// and its length in metres (0: no solved flow, the blended one is used).
TEXTURE2D(_RiverFlowField);
SAMPLER(sampler_RiverFlowField);
float4 _RiverFlowInfo;
/// What moves in the rivers near the camera (InlandWaterRenderer.Wakes): xyz where it meets the surface, w its radius;
/// and its velocity (xy, world xz).
float4 _FishWakes[16];
float4 _FishWakeVelocities[16];
float _FishWakeCount;

struct Attributes
{
	float4 positionOS : POSITION;
	float4 color : COLOR;          // r: depth over 4 m; g: claims its pixels (0 in a bank's margin); b: 1 − the least water column / 8 m; a: how much water this is
	float2 uv : TEXCOORD0;         // metres along a river, metres across it (lakes: world xz)
	float2 flow : TEXCOORD1;       // the scene's flow here, m/s, world xz
	float2 state : TEXCOORD2;      // x how broken the water is (0 … 1), y how strongly a river reaches here
	float2 tide : TEXCOORD3;       // x how far the river falls over its fade into the sea, metres; y 1 where it drains to the sea
	float4 bank : TEXCOORD4;       // rivers: x half its width bank to bank (m), y its mean speed (m/s), zw its way downstream (world xz)
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
	float4 positionCS : SV_POSITION;
	float3 positionWS : TEXCOORD0;
	float4 screenPos : TEXCOORD1;
	float2 flow : TEXCOORD2;
	float4 state : TEXCOORD3;      // x broken, y river reach, z alpha, w claims its pixels
	float fogFactor : TEXCOORD4;
	float2 riverUV : TEXCOORD5;    // metres along and across a river; a lake's world xz
	float depth : TEXCOORD6;       // the water's depth here, metres
	float minColumn : TEXCOORD7;   // the least water column it is drawn with, metres (a river draped on the far backdrop)
	float4 bank : TEXCOORD8;       // as the attribute
	UNITY_VERTEX_OUTPUT_STEREO
};

Varyings InlandVertex(Attributes input)
{
	Varyings output = (Varyings)0;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
	float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
	/* A river that drains to the sea meets it wherever the tide stands. Its channel runs on across the
	 * shore the low tide bares (P4), so at low water it flows down to the sea as it is. Higher up, the sea
	 * floods its lower course: there the water stands at the sea's level, and the river fades out into
	 * the sea over what its surface falls in a few widths (its own fade into the sea, wherever that now
	 * is). */
	half tideAlpha = 1.0;
	if (input.tide.y > 0.5)
	{
		float under = _FishWaterLevel - positionWS.y;
		tideAlpha = saturate(1.0 - under / max(0.05, input.tide.x));
		positionWS.y = max(positionWS.y, _FishWaterLevel);
		output.depth = max(0.0, under);
	}
	output.positionWS = positionWS;
	output.positionCS = TransformWorldToHClip(positionWS);
	output.screenPos = ComputeScreenPos(output.positionCS);
	output.flow = input.flow;
	output.riverUV = input.uv;
	output.bank = input.bank;
	output.depth += input.color.r * 4.0;
	/* The far backdrop's rivers lie on its coarse ground with no channel cut for them, so the depth texture
	 * sees a few centimetres of water and the river would draw as nearly clear. They carry the depth their
	 * flow has instead (blue below 1); everything else carries 1, nothing. */
	output.minColumn = (1.0 - input.color.b) * 8.0;
	output.state = float4(input.state.x, input.state.y, input.color.a * tideAlpha, input.color.g);
	output.fogFactor = ComputeFogFactor(output.positionCS.z);
	return output;
}

/// Eye depth of a raw depth sample, orthographic cameras included.
float InlandEyeDepth(float rawDepth)
{
	#if UNITY_REVERSED_Z
		float ortho = lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
	#else
		float ortho = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
	#endif
	return lerp(LinearEyeDepth(rawDepth, _ZBufferParams), ortho, unity_OrthoParams.w);
}

/// One sample of a texture carried by the flow: two copies, each moved for one cycle, half a cycle
/// apart and cross-faded so neither is ever further from home than one cycle's travel. A flow that turns
/// or changes speed — round a bend, into a lake — then never tears the texture apart over time.
half4 FlowSample(TEXTURE2D_PARAM(tex, samplerTex), float2 xz, float2 flow, float scale, float cycle, float2 shift)
{
	// A whole number of cycles in the clock's wrap, so the wrap lands on a cycle's start and does not show.
	cycle = FISH_INLAND_CLOCK_WRAP / max(1.0, round(FISH_INLAND_CLOCK_WRAP / max(0.05, cycle)));
	float time = _FishInlandTime / cycle;
	float phaseA = frac(time);
	float phaseB = frac(time + 0.5);
	half blend = abs(2.0 * phaseA - 1.0);
	float2 uvA = (xz - flow * phaseA * cycle) / scale + shift;
	float2 uvB = (xz - flow * phaseB * cycle) / scale + shift + float2(0.37, 0.61);
	return lerp(SAMPLE_TEXTURE2D(tex, samplerTex, uvA), SAMPLE_TEXTURE2D(tex, samplerTex, uvB), blend);
}

/// A hash of a cell, 0 … 1, four ways.
float4 InlandHash4(float2 cell)
{
	float4 p = frac(float4(cell.xyxy) * float4(0.1031, 0.1030, 0.0973, 0.1099));
	p += dot(p, p.wzxy + 33.33);
	return frac((p.xxyz + p.yzzw) * p.zywx);
}

/// <summary>
/// The surface over stones on the bed: a smooth hump right over each, and a V of small waves trailing
/// downstream from it at the Kelvin angle, dying away; with a fleck of white behind it where the water is
/// fast and shallow. Stones scattered over a world grid, more of them and nearer the surface the shallower
/// and faster the water, so a riffle shows a scatter of them and a deep pool none. Returns the surface's
/// slope (a height gradient) and, through <paramref name="white"/>, how much foam they throw.
/// </summary>
float2 BedStones(float2 xz, float2 along, float speed, float depth, out half white)
{
	white = 0.0;
	float2 across = float2(-along.y, along.x);
	// How much the bed shows through: none past a metre and a half, or in still water.
	half show = saturate(1.0 - depth / 1.5) * saturate(speed / 0.6);
	if (show <= 0.01)
	{
		return float2(0.0, 0.0);
	}
	const float cellSize = 2.2;
	float2 base = floor(xz / cellSize);
	float2 gradient = float2(0.0, 0.0);
	// A stone's wake reaches downstream, so the cells upstream of the point matter most: look two back.
	[unroll]
	for (int j = -2; j <= 1; j++)
	{
		[unroll]
		for (int i = -2; i <= 1; i++)
		{
			float2 cell = base + float2(i, j);
			float4 h = InlandHash4(cell);
			// Not every cell holds a stone; shallower water, more of them.
			if (h.w > 0.25 + 0.6 * show)
			{
				continue;
			}
			float2 stone = (cell + h.xy) * cellSize;
			float radius = 0.15 + 0.35 * h.z;
			float2 d = xz - stone;
			float a = dot(d, along);
			float c = dot(d, across);
			// The hump over it.
			float r2 = (a * a + c * c) / (radius * radius * 4.0);
			float hump = exp(-r2) * radius * 0.25;
			gradient += -2.0 * d / (radius * radius * 4.0) * hump;
			// The V downstream: crests where |across| ≈ along · tan 19.5°, fading over a few stone sizes.
			if (a > 0.0)
			{
				float spread = abs(c) - a * 0.354;
				float trail = exp(-a / (radius * 8.0 + speed * 1.5));
				float ridge = exp(-spread * spread / (radius * radius * 0.35)) * trail;
				float waveLength = max(0.25, radius * 1.2);
				float crestPhase = 6.2831853 * a / waveLength;
				gradient += along * (cos(crestPhase) * ridge * 0.45);
				/* White only behind the larger stones, as a short pillow trailing downstream of the stone, not along the
				 * V's arms: every stone's white V on a 2.2 m grid read from above as rows of ticks, fish scales over every
				 * riffle (2026-10-07). The V stays in the surface's slope. */
				if (h.z > 0.55)
				{
					float pillow = exp(-c * c / (radius * radius * 1.5)) * exp(-a / (radius * 4.0 + 0.3));
					white = max(white, pillow * saturate((h.z - 0.55) / 0.3) * saturate((speed - 0.5) / 1.0));
				}
			}
		}
	}
	white *= show;
	return gradient * show;
}

/// <summary>
/// The foam's LACE (the map's alpha, FishWaterFoam.hlsl) carried by a flow, as <see cref="FlowSample"/> carries the
/// map: two copies half a cycle apart, but blended so the blend keeps the lace's contrast. A plain blend of two laces
/// is a grey smear mid-cycle, and the cut through it lost every hole twice a cycle.
/// </summary>
/// <remarks>
/// Read <see cref="FISH_RIVER_LACE_MIP"/> mips down: a river's white water is clumps of froth with holes, never the
/// lace's web of bubble walls. Those walls are a texel or three wide, and every cut under about a third covers little
/// else, so a river's thin foam drew as hairlines, stretched along the current into rows of parallel dotted streaks
/// (the cut keeps only the top of a texel-wide ridge, and that wanders as the wall crosses texel centres; Jim's
/// close-up, 2026-10-09). Averaged over 4×4 texels a wall falls below any cut, while the patches and their holes stay,
/// and the cut still covers what it is asked (a fifth covers 19 %).
/// </remarks>
#define FISH_RIVER_LACE_MIP 2.0
half FlowLace(float2 xz, float2 flow, float scale, float cycle, float2 shift)
{
	cycle = FISH_INLAND_CLOCK_WRAP / max(1.0, round(FISH_INLAND_CLOCK_WRAP / max(0.05, cycle)));
	float time = _FishInlandTime / cycle;
	float phaseA = frac(time);
	float phaseB = frac(time + 0.5);
	half blend = abs(2.0 * phaseA - 1.0);
	float2 uvA = (xz - flow * phaseA * cycle) / scale + shift;
	float2 uvB = (xz - flow * phaseB * cycle) / scale + shift + float2(0.37, 0.61);
	return FishFoamBlend(
		SAMPLE_TEXTURE2D_BIAS(_FoamTexture, sampler_FoamTexture, uvA, FISH_RIVER_LACE_MIP).a,
		SAMPLE_TEXTURE2D_BIAS(_FoamTexture, sampler_FoamTexture, uvB, FISH_RIVER_LACE_MIP).a, blend);
}

/// <summary>
/// The foam's pattern streaked out along the current: the carried texture read in the river's own frame (metres across,
/// metres along over the stretch), so each clump is drawn out the way the water moves it, faster water the longer.
/// <paramref name="frame"/> is x across, y along; <paramref name="speed"/> the current along it (m/s).
/// </summary>
/// <remarks>
/// Not world space turned to the flow's direction: river water lies hundreds of metres from the origin, and there the
/// slightest turn of the flow slid the turned coordinate by metres from one pixel to the next. The texture was squeezed
/// and aliased into rows of white ticks bending in arcs, and tore along triangle edges (Jim's screenshots, 2026-10-07).
/// The river's frame bends with the river and turns nowhere.
/// </remarks>
half StreakFoam(float2 frame, float speed, float scale, float cycle)
{
	// Moving water draws its foam out into streaks along the current (windrows), the faster the longer.
	float stretch = 1.6 + 2.4 * saturate(speed / 2.0);
	float2 q = float2(frame.x, frame.y / stretch);
	float2 carried = float2(0.0, speed / stretch);
	return FlowLace(q, carried, scale, cycle, float2(0.0, 0.0));
}

/// <summary>
/// The boil of a plunge pool: patches of white welling up and spreading, with no direction to them. The foam texture
/// read through a warp made of itself (so no clump repeats in a ring round the foot or lines up with its neighbours),
/// at two scales drifting with the river's own flow. Streaked foam had the pool's outward drift to stretch along, and
/// drew every pool as a sunburst of dashes.
/// </summary>
half BoilFoam(float2 xz, float2 flow, float scale, float cycle)
{
	float2 warp = float2(
		FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), xz, flow * 0.5, scale * 3.7, cycle * 1.9, float2(0.13, 0.57)).r,
		FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), xz, flow * 0.5, scale * 3.1, cycle * 2.3, float2(0.71, 0.29)).r) - 0.5;
	float2 at = xz + warp * (scale * 2.2);
	// The lace through the warp: froth welling up in patches with no direction, holes and strands where it thins.
	return FlowLace(at, flow, scale * 1.4, cycle * 0.8, float2(0.37, 0.83));
}

/// A texture offset that drifts at <paramref name="perSecond"/> (texture units a second) on the inland clock, its rate
/// rounded to a whole number of units over the clock's wrap: at the wrap the offset is a whole number of tiles, the
/// same texture as at the start, so the drift does not jump. Wrapped to 0 … 1 so the coordinate stays small.
float2 InlandDrift(float2 perSecond)
{
	float2 rate = round(perSecond * FISH_INLAND_CLOCK_WRAP) / FISH_INLAND_CLOCK_WRAP;
	return frac(_FishInlandTime * rate);
}

/// What the falls do to the water here (FallPool).
struct FallPoolState
{
	half churn;        // how near a landing, 0 … 1 (the old churn: the boil's foam pattern and the outward drift take it)
	half stir;         // that churn by the fall's power (_FishFallsB.w): how broken, rough and milky the water is
	half white;        // how much foam the churn carries: the churn squared by the fall's power
	float2 outward;    // the way the water runs out from the nearest landing
	half plume;        // the bubble plume under the surface, 0 … 1, strongest over its core
	half crown;        // the crowns of the boil's domes, 0 … 1: where the bubbles break the surface
	float2 slope;      // the surface's slope from the boil's domes and the surges spreading out (a height gradient)
};

/// <summary>
/// The plunge pools round the scene's falls: where the curtain lands the water churns, a plume of bubbles glows pale
/// under the surface, the surface heaves in domes as the plume wells up, and surges spread out across the pool. Only
/// water near a landing's own level is touched, so a lake far below a fall, or a river running past above it, is not.
/// <paramref name="dxzX"/>, <paramref name="dxzY"/> are the pixel's screen derivatives of world xz (taken outside the
/// loop: a texture read with implicit derivatives inside a loop that can break early makes FXC unroll it or refuse),
/// and <paramref name="footprint"/> the metres one pixel spans, which fades the surges where they would alias.
/// </summary>
/// <remarks>
/// Every pattern is laid out round the landing in metres from it (positionWS.xz − the landing: small numbers, read in
/// place, never turned), so nothing here slides with the distance from the world's origin.
/// </remarks>
FallPoolState FallPool(float3 positionWS, float2 dxzX, float2 dxzY, float footprint)
{
	FallPoolState pool = (FallPoolState)0;
	int count = (int)_FishFallCount;
	[loop]
	for (int i = 0; i < 16; i++)
	{
		if (i >= count)
		{
			break;
		}
		float4 fall = _FishFalls[i];
		float churnRadius = max(0.5, fall.w);
		float2 d = positionWS.xz - fall.xz;
		float distanceSq = dot(d, d);
		// The surges run on past the churn, out to two and a half of its radii; nothing reaches further.
		float reach = 2.5 * churnRadius;
		if (distanceSq >= reach * reach || abs(positionWS.y - fall.y) > 0.5 * fall.w + 1.0)
		{
			continue;
		}
		float distance = sqrt(distanceSq);
		float2 radial = distance > 1e-3 ? d / distance : float2(0.0, 0.0);
		float4 more = _FishFallsB[i];
		/* Unpublished (power 0: the renderer's smallest is a kilowatt): the churn as white as it always was, a plume
		 * sized off the churn, and no boil or surge of any height, rather than a pool that suddenly has nothing. */
		bool known = more.y > 0.0;
		float thickness = known ? max(0.05, more.x) : 0.12 * churnRadius;
		float boilHeight = known ? max(0.0, more.z) : 0.0;
		half whiteness = known ? saturate(more.w) : 1.0;
		// A different stretch of the noise for each fall, so two pools never boil in step.
		float salt = (float)i * 0.618;

		// ── The churn, as before, but only as white as the fall is strong ──
		/* A trickle over a high ledge lands with almost no power: its pool barely stirs, where it used to churn as
		 * white as a river's. The churn's reach stays (the pattern's shape); how white, broken and milky it is, the
		 * power says. */
		half k = saturate(1.0 - distance / churnRadius);
		k = k * k * (3.0 - 2.0 * k);
		if (k > pool.churn)
		{
			pool.churn = k;
			pool.outward = radial;
		}
		pool.stir = max(pool.stir, k * whiteness);
		pool.white = max(pool.white, k * k * whiteness);

		/* ── The bubble plume ──
		 * The curtain drives air deep into the pool, and the bubbles rise back round the landing as a cloud a few
		 * curtain-thicknesses wide: under the surface, not on it, so it is seen through the water as a pale glow
		 * (the water above tints it turquoise; the fragment does that). Its edge is ragged and boils: two drifts of
		 * the noise against each other, so the shape changes rather than slides. Strongest at the core. */
		float plumeRadius = min(churnRadius, 3.0 * thickness + 0.3 * churnRadius);
		if (distance < 1.6 * plumeRadius)
		{
			float plumeScale = 0.3 / (1.2 * plumeRadius);
			float2 plumeUV = d * plumeScale + float2(0.19, 0.53) + salt;
			half drift = SAMPLE_TEXTURE2D_GRAD(_FoamTexture, sampler_FoamTexture, plumeUV + InlandDrift(float2(0.021, 0.013)), dxzX * plumeScale, dxzY * plumeScale).g;
			half churning = SAMPLE_TEXTURE2D_GRAD(_FoamTexture, sampler_FoamTexture, plumeUV * 1.9 + InlandDrift(float2(-0.027, 0.019)), dxzX * (plumeScale * 1.9), dxzY * (plumeScale * 1.9)).g;
			// The edge wanders between three quarters and one and a third of the radius.
			float edge = plumeRadius * (0.75 + 0.6 * drift);
			half core = saturate(1.0 - distance / max(0.1, edge));
			core = core * core * (3.0 - 2.0 * core);
			// Puffs: the cloud thickens and thins as it boils, more at its edge than its core, which is always full.
			half puff = lerp(0.55 + 0.6 * churning, 1.0, core * core);
			// A trickle's plume is small (its thickness) and faint; a river's dense.
			pool.plume = max(pool.plume, core * puff * lerp(0.4, 1.0, whiteness));
		}

		/* ── The boil ──
		 * Where the plume reaches the surface it lifts it in domes that swell, spread and settle, each on its own
		 * clock and in its own place: a scatter of domes over a jittered grid round the landing, each rising fast and
		 * flattening out as it spreads over a cycle of 1.6 to 3.8 s. Not rings: the flow-mapped ripples once ran out
		 * radially and pulsed as concentric rings, a swirl disc on every pool, and the boil must not do the same. */
		float boilRadius = 1.3 * plumeRadius;
		if (boilHeight > 0.001 && distance < 1.6 * boilRadius)
		{
			// Strongest over the core, gone by the boil's edge.
			float boilShare = exp(-distanceSq / (boilRadius * boilRadius));
			float cell = max(0.6, 0.6 * plumeRadius);
			float2 g = d / cell;
			float2 base = floor(g);
			[unroll]
			for (int j = -1; j <= 1; j++)
			{
				[unroll]
				for (int m = -1; m <= 1; m++)
				{
					float2 c = base + float2(m, j);
					float4 h = InlandHash4(c + salt * 31.0);
					float2 centre = c + 0.25 + 0.5 * h.xy;
					// A whole number of cycles in the clock's wrap, as FlowSample's.
					float domePeriod = 1.6 + 2.2 * h.z;
					domePeriod = FISH_INLAND_CLOCK_WRAP / max(1.0, round(FISH_INLAND_CLOCK_WRAP / domePeriod));
					float life = frac(_FishInlandTime / domePeriod + h.w);
					half rise = smoothstep(0.0, 0.18, life) * (1.0 - life) * (1.0 - life);
					float sigma = 0.22 + 0.33 * life;
					float2 e = g - centre;
					float r2 = dot(e, e) / (sigma * sigma);
					float dome = rise * exp(-r2);
					// d(height)/d(world) = d/dg over the cell: the dome's own slope, as high as the fall's boil there.
					pool.slope += (-2.0 / (sigma * sigma * cell)) * e * (dome * boilHeight * boilShare);
					pool.crown = max(pool.crown, dome * saturate(1.0 - r2) * boilShare * saturate(boilHeight / 0.15));
				}
			}
		}

		/* ── The surges ──
		 * The fall's pulses send trains of waves out across the pool: crests spreading out from the plume, dying away
		 * over one or two churn radii. Their period grows with the fall's power (a thundering river heaves slowly,
		 * a trickle shivers), their length from deep water's dispersion (λ = gT²/2π). Broken up by the noise so they
		 * are not perfect circles: the crests wander by most of a wavelength and come in trains with gaps between.
		 * Added to the surface's slope analytically, faded where a pixel spans a quarter of a wave (they would alias
		 * into moiré) and with the ripples' own fade. */
		float ringStart = 0.6 * plumeRadius;
		half ringShare = smoothstep(0.5 * ringStart, 1.5 * ringStart, distance)
			* exp(-max(0.0, distance - ringStart) / (1.2 * churnRadius))
			* saturate(1.0 - distance / reach);
		float period = clamp(0.8 + 0.35 * sqrt(max(0.0, more.y)), 0.8, 2.2);
		period = FISH_INLAND_CLOCK_WRAP / max(1.0, round(FISH_INLAND_CLOCK_WRAP / period));
		float waveLength = 1.56 * period * period;
		ringShare *= saturate(1.0 - footprint * 4.0 / waveLength);
		if (ringShare > 0.001)
		{
			float wobbleScale = 0.37 / (2.5 * waveLength);
			float trainScale = 0.29 / (1.5 * waveLength);
			half wobble = SAMPLE_TEXTURE2D_GRAD(_FoamTexture, sampler_FoamTexture, d * wobbleScale + float2(0.71, 0.11) + salt + InlandDrift(float2(0.006, -0.004)), dxzX * wobbleScale, dxzY * wobbleScale).g;
			half gaps = SAMPLE_TEXTURE2D_GRAD(_FoamTexture, sampler_FoamTexture, d * trainScale + float2(0.43, 0.89) + salt + InlandDrift(float2(-0.005, 0.007)), dxzX * trainScale, dxzY * trainScale).g;
			half trains = smoothstep(0.2, 0.7, gaps);
			// The frac keeps the cosine's argument small; the crests run outward at λ/T.
			float phase = 6.2831853 * ((distance + (wobble - 0.5) * 0.9 * waveLength) / waveLength - frac(_FishInlandTime / period));
			// A slope, not a height: 0.03 for any fall, up to 0.28 for one that heaves its pool half a metre.
			half ringSlope = (0.03 + 0.25 * saturate(boilHeight / 0.4)) * lerp(0.3, 1.0, whiteness);
			pool.slope += radial * (-sin(phase) * ringSlope * ringShare * trains);
		}
	}
	return pool;
}

/// <summary>
/// How much foam the falls on this river carry downstream to here (0 … 1): the boil's foam swept away by the current,
/// carried a long way below a big fall, spreading across the river as it goes and thinning out along it. Laid out in
/// the river's own frame (<paramref name="riverUV"/> metres along, metres across), which bends with the river and
/// turns nowhere; stops at the banks (<paramref name="halfWidth"/>). The patches themselves are the carried foam
/// texture, which the fragment thresholds with this as the allowance: the envelope stays put while the foam runs
/// through it, as a real trail below a fall does.
/// </summary>
half FallTrail(float2 riverUV, float halfWidth)
{
	int count = (int)_RiverFallCount;
	// Inside the banks only, eased in over the last few percent toward each (no seam at the bank's line).
	half inBanks = smoothstep(0.0, 0.12, 1.0 - abs(riverUV.y) / max(0.1, halfWidth));
	half trail = 0.0;
	if (count <= 0 || inBanks <= 0.0)
	{
		return 0.0;
	}
	[loop]
	for (int i = 0; i < 4; i++)
	{
		if (i >= count)
		{
			break;
		}
		float4 fall = _RiverFalls[i];
		float run = riverUV.x - fall.x;
		float trailLength = max(1.0, fall.y);
		if (run < -2.0 || run > trailLength)
		{
			continue;
		}
		/* Spread across by the current's turbulence, which widens a trail as the root of how far it has run, from a
		 * third of the river at the landing; and thinned as it spreads (the same foam over more water), though not
		 * all the way, as the current gathers it into lines. */
		float startWidth = max(1.0, 0.3 * halfWidth);
		float spread = startWidth + 0.7 * sqrt(max(0.0, run) * max(1.0, halfWidth));
		float off = riverUV.y - fall.w;
		half profile = exp(-off * off / (spread * spread));
		half fade = saturate(1.0 - run / trailLength);
		fade *= fade;
		half start = smoothstep(-2.0, 2.0, run);
		trail = max(trail, saturate(fall.z) * profile * fade * start * saturate(0.4 + 0.6 * startWidth / spread));
	}
	return trail * inBanks;
}

/// <summary>
/// The wakes of what moves in a river: from the water running past each body (the current less the body's own
/// velocity), a Kelvin V of crests trailing downstream of it, rings spreading round it (most where it barely moves
/// through the water: bobbing, treading, just wading in) and white water heaped at its bow. Returns the surface's slope,
/// and through <paramref name="white"/> the foam.
/// </summary>
float2 Wakes(float3 positionWS, float2 flow, out half white)
{
	white = 0.0;
	float2 gradient = float2(0.0, 0.0);
	int count = (int)_FishWakeCount;
	[loop]
	for (int i = 0; i < 16; i++)
	{
		if (i >= count)
		{
			break;
		}
		float4 wake = _FishWakes[i];
		float radius = max(0.15, wake.w);
		float2 d = positionWS.xz - wake.xz;
		float distanceSq = dot(d, d);
		float reach = radius * 30.0 + 8.0;
		if (distanceSq > reach * reach || abs(positionWS.y - wake.y) > 1.5)
		{
			continue;
		}
		float2 past = flow - _FishWakeVelocities[i].xy;
		float speed = length(past);
		float2 along = speed > 1e-3 ? past / speed : float2(1.0, 0.0);
		float2 across = float2(-along.y, along.x);
		float a = dot(d, along);
		float c = dot(d, across);
		float r = sqrt(distanceSq);
		/* Rings, spreading out and dying away: only round a body barely moving through the water (bobbing, treading,
		 * just stepping in). Anything the current runs past trails a V instead; rings there drew bars across the river. */
		// 1.6 rings a second: 16 000 in the clock's wrap, so it is seamless; the frac keeps the cosine's argument small.
		float ringPhase = 6.2831853 * (r / max(0.25, radius * 1.2) - frac(_FishInlandTime * 1.6));
		half rings = exp(-r / (radius * 3.0 + 0.5)) * saturate(1.0 - speed / 0.3);
		gradient += d / max(r, 1e-3) * (cos(ringPhase) * rings * 0.08);
		// The V downstream: crests where |across| ≈ along · tan 19.5°, spaced by deep-water dispersion.
		if (a > 0.0)
		{
			float spread = abs(c) - a * 0.354;
			float trail = exp(-a / (radius * 10.0 + speed * 4.0));
			// The arms widen as they run: a wake spreads its energy as it goes, so it is broad and soft far back.
			float width = radius * 0.8 + a * 0.08;
			float ridge = exp(-spread * spread / (width * width)) * trail;
			float waveLength = max(0.3, 6.2831853 * speed * speed / 9.81);
			half moving = saturate(speed / 0.4);
			float crests = cos(6.2831853 * a / waveLength);
			gradient += along * (crests * ridge * 0.35 * moving);
			// And between them, the transverse waves: crests across the wake, weaker, filling the wedge, and broken along
			// their length (whole, they drew sets of straight white lines in a low sun).
			half inside = saturate(1.0 - abs(c) / (a * 0.354 + radius));
			half breakUp = smoothstep(0.35, 0.8, SAMPLE_TEXTURE2D_LOD(_FoamTexture, sampler_FoamTexture, float2(c / (1.3 * waveLength), a / (2.0 * waveLength)) * 0.27 + float2(0.62, 0.19), 0).g);
			gradient += along * (crests * inside * trail * 0.07 * moving * breakUp);
			white = max(white, ridge * saturate((speed - 0.6) / 1.2) * saturate(1.0 - a / (radius * 8.0 + 1.0)));
		}
		// The bow: white heaped where the water meets the body.
		white = max(white, exp(-distanceSq / (radius * radius * 2.5)) * saturate((speed - 0.3) / 1.0));
	}
	return gradient;
}

half4 InlandFragment(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
{
	UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
	// From under the sea no lake or river is seen: none lies below its surface, and one above is beyond it.
	if (_FishInlandCameraInSea > 0.5)
	{
		discard;
	}
	float3 positionWS = input.positionWS;
	float2 screenUV = input.screenPos.xy / input.screenPos.w;
	float surfaceEye = input.screenPos.w;
	float3 view = _WorldSpaceCameraPos - positionWS;
	float distanceToCamera = max(1e-4, length(view));
	view /= distanceToCamera;

	float2 flow = input.flow;
	// How sharply the current changes across the river here (s⁻¹), from the solved field: roughens the water (below).
	half shear = 0.0;
	/* The solved flow, inside a river's channel: the current round its boulders and slack at its banks, its own
	 * metres along and across read straight off its field. Faded out over its ends, where it meets a lake, the sea or
	 * the river it joins and the blended flow (the same on both sides of the join) carries on. */
	if (_RiverFlowInfo.x > 0.0 && input.bank.x > 0.05)
	{
		float lengthMetres = _RiverFlowInfo.x;
		float2 fieldUV = float2(input.riverUV.x / lengthMetres, saturate(input.riverUV.y / input.bank.x * 0.5 + 0.5));
		/* The field and its neighbours a cell to either side across (16 cells bank to bank). The ripples and foam ride
		 * the current smoothed over those three: carried on the raw field, the two sides of a sharp shear slid their
		 * patterns metres apart in one flow-map cycle, squeezing the ripples into bright lines along the flow. */
		const float fieldCells = 16.0;
		float2 oneCell = float2(0.0, 1.0 / fieldCells);
		half2 local = SAMPLE_TEXTURE2D(_RiverFlowField, sampler_RiverFlowField, fieldUV).rg;
		half2 localLeft = SAMPLE_TEXTURE2D(_RiverFlowField, sampler_RiverFlowField, saturate(fieldUV + oneCell)).rg;
		half2 localRight = SAMPLE_TEXTURE2D(_RiverFlowField, sampler_RiverFlowField, saturate(fieldUV - oneCell)).rg;
		half2 smoothed = (localLeft + 2.0 * local + localRight) * 0.25;
		float2 downstream = normalize(input.bank.zw + float2(1e-5, 0.0));
		float2 leftBank = float2(-downstream.y, downstream.x);
		float2 solved = (downstream * smoothed.x + leftBank * smoothed.y) * input.bank.y;
		float cellMetres = max(0.05, 2.0 * input.bank.x / fieldCells);
		shear = abs(localLeft.x - localRight.x) * input.bank.y / (2.0 * cellMetres);
		float ends = saturate(min(input.riverUV.x, lengthMetres - input.riverUV.x) / max(4.0, 4.0 * input.bank.x));
		// Into the channel over its last fifth toward each bank, not switched on at the bank's line (a seam along it).
		half inside = smoothstep(0.0, 0.2, 1.0 - abs(input.riverUV.y) / max(0.1, input.bank.x));
		flow = lerp(flow, solved, ends * inside * saturate(input.state.w));
		shear *= ends * inside;
	}
	/* Under a fall the water boils out from where the curtain lands: that flow, stirred in, broken water, the plume
	 * under it, the boil's domes and the surges. The pixel's footprint and world derivatives are taken here, outside
	 * FallPool's loop, for its texture reads (and reused for the geometric normal below). */
	float3 positionDdx = ddx(positionWS);
	float3 positionDdy = ddy(positionWS);
	float footprint = max(length(positionDdx.xz), length(positionDdy.xz));
	FallPoolState pool = FallPool(positionWS, positionDdx.xz, positionDdy.xz, footprint);
	half churn = pool.churn;
	// The river's own speed, before the boil: the silt it carries is its own, not the fall's.
	float riverSpeed = length(flow);
	float2 riverFlow = flow;
	/* A little outward drift from where the curtain lands, not a strong one: the ripples and foam ride the flow by
	 * flow-mapping, and a strong radial flow drew them as pulsing concentric rings, a swirl disc on every pool. As
	 * strong as the fall: a trickle's pool barely moves. */
	flow += pool.outward * (0.6 * pool.stir);
	float speed = length(flow);
	half broken = saturate(max(input.state.x, pool.stir));

	// ── Ripples, riding the current ───────────────────────────────────
	// Two scales: the current's own, and a finer one the air stirs that hardly moves. Faster water is
	// rougher, broken water roughest.
	half detailFade = saturate(1.0 - distanceToCamera / max(1.0, _NormalFadeDistance));
	// Choppier along a shear line (the wavelets its turbulence raises), as well as duller (the roughness below).
	half strength = _NormalStrength * detailFade * (1.0 + 0.6 * saturate(speed / 2.0) + 0.6 * broken + 1.5 * pool.stir + 0.4 * saturate(shear / 2.0));
	half3 a = UnpackNormalScale(FlowSample(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), positionWS.xz, flow, max(0.2, _NormalScale), _FlowCycle, float2(0.0, 0.0)), strength);
	half3 b = UnpackNormalScale(FlowSample(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), positionWS.xz, flow * 0.25 + float2(0.05, 0.03), max(0.1, _NormalScale * 0.37), _FlowCycle * 1.7, float2(0.21, 0.13)), strength * 0.6);
	half3 ripple = normalize(half3(a.xy + b.xy, a.z * b.z));
	float3 normalWS = normalize(float3(ripple.x, ripple.z, ripple.y));

	/* Standing waves where the water is broken: crests across the current that stay where they are while
	 * the water runs through them, spaced by the water's speed (deep-water dispersion, λ = 2πv²/g) and
	 * bowed a little across the channel. Rivers only: they are laid out along the river (its metres along
	 * and across), and a lake's water is never broken. */
	float2 along = speed > 1e-3 ? flow / speed : float2(1.0, 0.0);
	float wavelength = clamp(6.2831853 * speed * speed / 9.81, 0.6, 4.0);
	/* In clusters, not corduroy: real standing waves stand in trains behind the rocks that raise them, so a
	 * slow noise over the river (its own metres) says where a train stands and how strong, and wanders its
	 * spacing and its line across the channel. A coherent sine over the whole reach reads as ribbing. */
	float2 clusterUV = input.riverUV / float2(4.0 * wavelength + 6.0, 3.0 * wavelength + 3.0);
	half cluster = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, clusterUV * 0.37 + float2(0.13, 0.71)).g;
	half train = smoothstep(0.35, 0.75, cluster);
	half wander = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, clusterUV * 0.21 + float2(0.57, 0.29)).g;
	/* Their spacing wandering more, and their lines bowed harder and broken by the cluster noise: a metre apart and
	 * near-parallel they caught the light as rows of glints from above and dark dashes from a bank (2026-10-07). */
	float phase = 6.2831853 * (input.riverUV.x / (wavelength * (0.6 + 0.8 * wander)))
		+ 3.0 * sin(6.2831853 * input.riverUV.y / (1.3 * wavelength) + wander * 6.2831853)
		+ 4.0 * (cluster - 0.5);
	/* Short-crested: each crest a hump a wavelength or two across, not a ridge running on across the channel. Long, even
	 * crests caught a low sun as rows of parallel white lines on the river (Jim, 2026-10-07, at dusk); real standing waves
	 * are separate haystacks that rise and fall. The humps drift slowly so they come and go. */
	float2 humpUV = float2(input.riverUV.x / (1.6 * wavelength), input.riverUV.y / (1.1 * wavelength + 0.4)) * 0.23
		+ float2(_FishInlandTime * 0.004, 0.0);
	half hump = smoothstep(0.4, 0.85, SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, humpUV + float2(0.31, 0.47)).g);
	half crest = saturate(sin(phase) * 0.5 + 0.5);
	crest = crest * crest * crest * train * hump;
	half waveSlope = broken * saturate(speed / 1.5) * 0.15 * detailFade * train * hump;
	float2 tilt = along * (waveSlope * cos(phase));
	// And the stones on a shallow bed, each with its hump and its V.
	half stoneWhite;
	float2 stones = BedStones(positionWS.xz, along, speed, input.depth, stoneWhite) * detailFade;
	tilt += stones;
	// And the boil's domes and the surges spreading across a plunge pool.
	tilt += pool.slope * detailFade;
	// And the wakes of anything moving in a river (rivers only: a lake's surface takes none).
	half wakeWhite = 0.0;
	if (input.bank.x > 0.05)
	{
		tilt += Wakes(positionWS, flow, wakeWhite) * detailFade;
	}
	normalWS = normalize(normalWS + float3(-tilt.x, 0.0, -tilt.y));
	// At a grazing angle a pixel spans many ripples and sees their average, the flat surface.
	half grazing = saturate(view.y * 3.0);
	normalWS = normalize(lerp(float3(0.0, 1.0, 0.0), normalWS, grazing));
	/* Seen from beneath, decided by the surface's own geometry rather than its winding (the face the rasteriser calls
	 * front is not reliably the top): the triangle's normal, from the position's screen derivatives, turned to face up
	 * as water always does, against the way to the camera. */
	float3 geometric = cross(positionDdx, positionDdy);
	geometric = geometric.y < 0.0 ? -geometric : geometric;
	bool fromBeneath = dot(geometric, _WorldSpaceCameraPos - positionWS) < 0.0;
	// The underside only from inside the water (InlandWaterRenderer.MarkCamera): from a dry gorge below a lip it is not seen.
	if (fromBeneath && _FishInlandCameraUnder < 0.5)
	{
		discard;
	}
	bool below = fromBeneath;
	if (below)
	{
		normalWS = -normalWS;
	}

	// ── Light ─────────────────────────────────────────────────────────
	float4 shadowCoord;
	#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
		shadowCoord = ComputeScreenPos(TransformWorldToHClip(positionWS));
	#else
		shadowCoord = TransformWorldToShadowCoord(positionWS);
	#endif
	Light mainLight = GetMainLight(shadowCoord);
	half3 lightColor = mainLight.color * mainLight.shadowAttenuation;
	half NdotL = saturate(dot(normalWS, mainLight.direction));
	// The water's colour is light that went in and came back out: multiplied by the light, with a floor.
	half3 waterLight = lightColor * saturate(mainLight.direction.y) * 0.9 + _GlossyEnvironmentColor.rgb * 0.8 + 0.12;

	// ── The water column behind the pixel ─────────────────────────────
	float waterColumn = 3.0;
	half edgeFade = 1.0;
	#if defined(_WATER_DEPTH)
		float rawDepth = SampleSceneDepth(screenUV);
		float sceneEye = InlandEyeDepth(rawDepth);
		waterColumn = max(input.minColumn, sceneEye - surfaceEye);
		edgeFade = saturate(waterColumn / max(0.01, _EdgeFade));
	#endif

	/* A river always carries some silt, and more the faster it runs: browner than the lake it runs into, as real ones
	 * are, and never the clear glass that let a slow river vanish over its bed when seen from above. */
	half silt = _Turbidity * (0.3 + 0.7 * saturate(riverSpeed / 1.5)) * input.state.y * (1.0 - pool.stir);
	/* Bubbles under a fall scatter the light back out: milky where the curtain lands, clearing fast with distance (the
	 * churn squared), so the pool reads as a boil round the foot rather than a pale disc; and only as milky as the fall
	 * is strong. */
	half milk = pool.white;
	half3 density = max(1e-3, _WaterDensity.rgb * (1.0 + silt * 3.0) + milk * 2.5);
	half3 transmittance = exp(-waterColumn * density);
	/* The bed is lit by light that came DOWN through the water as well as seen back UP through it: the sun's path to the
	 * bed (the depth over the sun's height) on top of the view's. A shallow bed seen from above is tinted by both. */
	float verticalDepth = waterColumn * saturate(view.y);
	half3 bedTransmittance = exp(-(waterColumn + verticalDepth / max(0.25, mainLight.direction.y)) * density);
	half3 open = lerp(_DeepColor.rgb, _ShallowColor.rgb, transmittance);
	open = lerp(open, _SiltColor.rgb, silt * (1.0 - transmittance.g));
	// Aerated water under a fall is pale and milky: the bubbles scatter the light back out.
	open = lerp(open, half3(0.70, 0.82, 0.80), milk * 0.6);
	half3 bodyColor = open * waterLight;

	half3 behind = bodyColor;
	half alpha = _MaxAlpha * lerp(0.35, 1.0, 1.0 - Luminance(transmittance));
	#if defined(_WATER_REFRACTION)
		float2 refractedUV = screenUV + normalWS.xz * _RefractionStrength / max(1.0, surfaceEye);
		#if defined(_WATER_DEPTH)
			// What is in front of the water at the bent position is not behind it: go straight.
			float refractedEye = InlandEyeDepth(SampleSceneDepth(refractedUV));
			refractedUV = refractedEye < surfaceEye ? screenUV : refractedUV;
		#endif
		half3 refracted = SampleSceneColor(refractedUV);
		/* A wet bed is darker and richer than the same stones dry (the water film keeps light in them): the reason a
		 * river's bed reads as a channel against the dry bars beside it. */
		half bedLuma = Luminance(refracted);
		refracted = max(0.0, lerp(bedLuma.xxx, refracted, 1.2)) * 0.72;
		behind = refracted * bedTransmittance + bodyColor * (1.0 - transmittance);
		alpha = 1.0;
	#endif
	/* The bubble plume under a plunge pool, seen through the water above it: a cloud of bubbles scatters the light
	 * that reaches it back up, pale turquoise-white, and hides the bed behind it. It lies deep at its ragged edge and
	 * welling up to just under the surface over its core (never below the bed), and the water over it tints it as
	 * it does the bed: the light's path down to it and back up. Where the boil's domes crown, the bubbles reach the
	 * surface and the glow is brightest. */
	half plumeCover = saturate(1.0 - exp(-2.5 * pool.plume));
	plumeCover = max(plumeCover, pool.crown * 0.5);
	if (plumeCover > 0.001)
	{
		float plumeDepth = min(lerp(1.2, 0.15, pool.plume), 0.6 * waterColumn);
		half3 plumeTint = exp(-plumeDepth * (1.0 + 1.0 / max(0.25, mainLight.direction.y)) * _WaterDensity.rgb);
		/* Lit only by what reaches it: the sun's light down through the water and the sky's. The water's own light
		 * carries a floor (+0.12) that keeps a dark river from going black; on bubbles that scatter it all back up it
		 * made every plunge pool glow turquoise at night (Jim's screenshot, 2026-10-08). */
		half3 plumeSource = lightColor * saturate(mainLight.direction.y) * 0.9 + _GlossyEnvironmentColor.rgb * 0.8;
		half3 plumeLight = half3(0.80, 0.95, 0.93) * plumeTint * plumeSource;
		behind = lerp(behind, plumeLight, plumeCover * 0.85);
		alpha = max(alpha, plumeCover * _MaxAlpha);
	}

	// ── Reflection, Fresnel and glint ─────────────────────────────────
	half NdotV = saturate(dot(normalWS, view));
	half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
	/* Broken water is rough: the sun spreads over it as a broad sheen, not a sparkle on every ripple. At + broken × 0.2
	 * each ripple of a rapid kept a pin-sharp glint, and the flow-mapped ripples lined them up: rows of white ticks
	 * that read as foam from above (FISHMMO_INLAND_DEBUG=noglint took every one away, 2026-10-07). */
	/* And where the current shears: the turbulence there chops the surface into small irregular wavelets, a duller
	 * band along the edge of every jet, as on a real river, rather than a polished line catching the sun. Up to 0.12
	 * at a shear of 2 per second (a metre a second across half a metre). */
	half perceptualRoughness = saturate((1.0 - _Smoothness) + (1.0 - detailFade) * 0.06 + broken * 0.5 + 0.12 * saturate(shear / 2.0));
	// Specular anti-aliasing, as the sea's: ripples finer than a pixel glint as their rougher average, not as specks.
	float3 dndx = ddx(normalWS), dndy = ddy(normalWS);
	half normalVariance = saturate(0.25 * (dot(dndx, dndx) + dot(dndy, dndy)));
	perceptualRoughness = saturate(sqrt(perceptualRoughness * perceptualRoughness + min(2.0 * normalVariance, 0.25)));
	float3 reflectVector = reflect(-view, normalWS);
	reflectVector.y = abs(reflectVector.y) * (below ? -1.0 : 1.0);
	half3 reflection = GlossyEnvironmentReflection(reflectVector, positionWS, perceptualRoughness, 1.0h, screenUV) * _ReflectionStrength;
	half3 halfVector = SafeNormalize(mainLight.direction + view);
	half NdotH = saturate(dot(normalWS, halfVector));
	half roughness = max(1e-3, perceptualRoughness * perceptualRoughness);
	half a2 = roughness * roughness;
	half d = (NdotH * NdotH) * (a2 - 1.0) + 1.0;
	half3 glint = lightColor * (a2 / max(1e-4, 3.14159 * d * d)) * _SpecularStrength * fresnel * NdotL;
	glint = _FishInlandDebugFoam > 1.5 ? half3(0.0, 0.0, 0.0) : glint;
	half3 color = below ? behind : lerp(behind, reflection, fresnel) + glint;

	/* ── Foam: only where the water breaks — the crests of a rapid's standing waves, the pillows over big stones, a
	 *    fall's boil and the trail it sends downstream, and wakes ───────────────────────────────────────────────
	 * Not on water for being fast, shallow or rippled: a river running smooth, however fast, is clear, and a riffle is a
	 * broken surface, not a white one. Speed alone (from 1.2 m/s) and shallow moving water at the banks used to give
	 * every quick reach a thin even foam, which the lace drew as streaks of white hairlines (Jim, 2026-10-09: "rivers
	 * don't typically have foam"). */
	half foam = 0.0;
	if (!below)
	{
		/* A river's foam in its own frame (riverUV: metres along, across); a lake's, whose water barely moves, in world xz
		 * as it lies, never turned. */
		bool onRiver = input.bank.x > 0.05;
		float2 foamFrame = onRiver ? float2(input.riverUV.y, input.riverUV.x) : positionWS.xz;
		float foamSpeed = onRiver ? max(0.0, dot(flow, normalize(input.bank.zw + float2(1e-5, 0.0)))) : 0.0;
		half lace = StreakFoam(foamFrame, foamSpeed, max(0.3, _FoamScale), _FlowCycle);
		/* The foam a fall's boil sends downstream: an allowance laid along the river below each landing, with the
		 * streaked foam running through it, gathered into rafts (the smooth field at three times the size) so the trail
		 * is drifting patches of more and less foam, not a white band. */
		half trail = 0.0;
		if (onRiver && _RiverFallCount > 0.5)
		{
			trail = FallTrail(input.riverUV, input.bank.x);
			half rafts = FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), foamFrame, float2(0.0, foamSpeed), max(0.3, _FoamScale) * 3.0, _FlowCycle * 3.0, float2(0.29, 0.61)).g;
			/* Gathered into rafts by a smooth modulation of how much foam there is, not a cut: cut, the broad noise drew
			 * every raft as a puffy cloud lying on the water (Jim's screenshot, 2026-10-08). The lace makes the holes. */
			trail *= 0.35 + 0.65 * smoothstep(0.2, 0.75, rafts);
		}
		// Round a fall's foot the pool boils rather than streaks.
		lace = FishFoamBlend(lace, BoilFoam(positionWS.xz, riverFlow, max(0.3, _FoamScale), _FlowCycle), saturate(1.6 * churn));
		// How much white the water can carry here: breaking crests in rapids, aeration under a fall.
		// The boil is thickest where the curtain lands and thins fast toward the pool's edge: patches, not an even carpet.
		half fallChurn = pool.white;
		// The river's own breaking water only (input.state.x): `broken` carries the fall's churn too, and through it the
		// whole pool read as churned as the impact, an even carpet of white to its edge.
		// The crests only lean on it: weighted 0.8, their bands about a metre apart drew white rows straight across every
		// rapid (fish scales from above, 2026-10-07); the streaks' own pattern is what should show.
		// Rapids and falls only (SceneWaterBodies.Broken: rapid 0.6, fall 1): a riffle's 0.15 is broken but not white.
		half rapid = input.state.x * smoothstep(0.25, 0.55, input.state.x);
		churn = saturate(rapid * (0.5 + 0.3 * crest));
		// Under a fall the pool boils white: most of it foam near the foot, thinning to its edge.
		// And on the crowns of the boil's domes, where its bubbles break the surface; and in the trail downstream.
		fallChurn = max(fallChurn, pool.crown * 0.6 * pool.stir);
		half amount = max(max(max(max(churn, stoneWhite * 0.8), fallChurn), wakeWhite), trail);
		/* As COVERAGE, the share of the water that is white (FishWaterFoam.hlsl): the lace says what that much foam
		 * looks like — froth with round holes where there is much, thinning to scattered clumps (the river reads the lace
		 * coarse, FlowLace, so never the web of walls: the resolve is told the coarser tile). Lit as foam is (the sun
		 * wrapped round it, the sky as ambient, no light of its own: it glowed at night), opaque where thick and
		 * lace over the water where thin, the water round it milky with bubbles. */
		FishFoam foamCut = FishFoamCut(lace, saturate(amount) * 0.85, length(fwidth(positionWS.xz)), max(0.3, _FoamScale) * exp2(FISH_RIVER_LACE_MIP));
		half3 foamLit = FishFoamLight(_FoamColor.rgb, lightColor, NdotL, 0.0h, _GlossyEnvironmentColor.rgb * 0.6);
		color = FishFoamOver(color, foamCut, foamLit, foam);
		if (_FishInlandDebugFoam > 0.5 && _FishInlandDebugFoam < 1.5)
		{
			color = half3(churn, max(fallChurn, trail), max(stoneWhite * 0.8, wakeWhite));
			foam = 1.0;
		}
	}

	alpha = saturate(alpha * edgeFade);
	alpha = max(alpha, foam * _MaxAlpha * edgeFade);
	/* The vertex alpha and the claim ramp together across the margin under a bank (0 there, 1 in the
	 * channel), so the alpha alone would be one half where the claim begins, a faint strip that a
	 * tributary running in could not show through: a seam across the water at a confluence. Over the
	 * claim, the margin's ramp cancels, and what is left is the fade into the sea (claim 1, alpha falling). */
	alpha *= saturate(input.state.z / max(input.state.w, 1e-3));
	/* Nothing drawn, nothing claimed: a fragment this faint would still write the draw-once stencil, and the
	 * river or lake drawn next could not show through where this one is invisible — the margin under a
	 * bank would cut a gap into the water a tributary runs in on. */
	clip(min(alpha - 0.02, input.state.w - 0.5));

	color = MixFog(color, input.fogFactor);
	if (!below)
	{
		color = FishWaterBehindClouds(color, screenUV);
		half fogKeep;
		color = FishWaterAirFogOver(color, positionWS, screenUV, fogKeep);
	}
	return half4(color, alpha);
}

#endif
