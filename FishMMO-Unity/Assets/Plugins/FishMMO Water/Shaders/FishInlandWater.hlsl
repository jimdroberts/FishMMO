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
	float _FoamSpeed;
	float _ShoreFoam;
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
/// The sea's surface now, tide included (WaterSurface).
float _FishWaterLevel;
/// Where the scene's falls land (InlandWaterRenderer.Falls): xyz the foot, w how far round it the pool churns.
float4 _FishFalls[16];
float _FishFallCount;
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
				white = max(white, saturate(ridge * (1.0 - a / (radius * 6.0 + 0.5))) * saturate((speed - 0.5) / 1.0));
			}
		}
	}
	white *= show;
	return gradient * show;
}

/// <summary>
/// The foam's pattern streaked out along the current: several taps of the carried texture laid back
/// upstream from the point (a line-integral smear, by the strongest tap), so the foam is drawn out in the way the
/// water moves it, faster water the longer. In world space, from the scene's one flow field, so where two
/// pieces of water meet the streaks run on across the join.
/// </summary>
half StreakFoam(float2 xz, float2 flow, float scale, float cycle)
{
	float speed = length(flow);
	float2 along = speed > 1e-3 ? flow / speed : float2(1.0, 0.0);
	float stretch = scale * (0.6 + 1.6 * saturate(speed / 2.0));
	/* The strongest of the taps, each weaker the further upstream it was taken: every clump of foam keeps
	 * its own white and trails a fading tail down the current. An average would grey the sparse clumps away. */
	/* Taps close enough that a clump's tail is one streak, not a row of copies of it: five taps a quarter of the
	 * stretch apart drew every clump of fine foam as a line of dots down the current. The fine detail only at the
	 * head, where the clump is; the tail is the coarse foam drawn out. */
	half fine = FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), xz, flow, scale * 0.33, cycle, float2(0.43, 0.17)).r;
	half streak = 0.0;
	[unroll]
	for (int k = 0; k < 8; k++)
	{
		float2 at = xz - along * (stretch * (k / 7.0));
		half coarse = FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), at, flow, scale, cycle, float2(0.0, 0.0)).r;
		streak = max(streak, coarse * (1.0 - 0.09 * k));
	}
	return saturate(streak * 0.7 + fine * 0.35 * streak + fine * 0.12);
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
	half broad = FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), at, flow, scale * 1.4, cycle * 0.8, float2(0.37, 0.83)).r;
	half fine = FlowSample(TEXTURE2D_ARGS(_FoamTexture, sampler_FoamTexture), at * 1.07 + 3.3, flow * 0.6, scale * 0.5, cycle * 1.3, float2(0.61, 0.07)).r;
	return saturate(broad * 0.75 + fine * 0.35 + broad * fine * 0.4);
}

/// <summary>
/// How hard the water churns here under a fall (0 … 1), and the way it boils out from the nearest foot: white,
/// pale and rough where the curtain lands, easing out to the pool's edge. Only water near a foot's own level
/// churns, so a lake far below a fall is not stirred by it.
/// </summary>
half FallChurn(float3 positionWS, out float2 outward)
{
	outward = float2(0.0, 0.0);
	half churn = 0.0;
	int count = (int)_FishFallCount;
	[loop]
	for (int i = 0; i < 16; i++)
	{
		if (i >= count)
		{
			break;
		}
		float4 fall = _FishFalls[i];
		float2 d = positionWS.xz - fall.xz;
		float distance = length(d);
		if (distance >= fall.w || abs(positionWS.y - fall.y) > 0.5 * fall.w + 1.0)
		{
			continue;
		}
		half k = saturate(1.0 - distance / max(0.5, fall.w));
		k = k * k * (3.0 - 2.0 * k);
		if (k > churn)
		{
			churn = k;
			outward = distance > 1e-3 ? d / distance : float2(0.0, 0.0);
		}
	}
	return churn;
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
			// And between them, the transverse waves: crests across the wake, weaker, filling the wedge.
			half inside = saturate(1.0 - abs(c) / (a * 0.354 + radius));
			gradient += along * (crests * inside * trail * 0.12 * moving);
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
	/* The solved flow, inside a river's channel: the current round its boulders and slack at its banks, its own
	 * metres along and across read straight off its field. Faded out over its ends, where it meets a lake, the sea or
	 * the river it joins and the blended flow (the same on both sides of the join) carries on. */
	if (_RiverFlowInfo.x > 0.0 && input.bank.x > 0.05)
	{
		float lengthMetres = _RiverFlowInfo.x;
		float2 fieldUV = float2(input.riverUV.x / lengthMetres, saturate(input.riverUV.y / input.bank.x * 0.5 + 0.5));
		half2 local = SAMPLE_TEXTURE2D(_RiverFlowField, sampler_RiverFlowField, fieldUV).rg;
		float2 downstream = normalize(input.bank.zw + float2(1e-5, 0.0));
		float2 leftBank = float2(-downstream.y, downstream.x);
		float2 solved = (downstream * local.x + leftBank * local.y) * input.bank.y;
		float ends = saturate(min(input.riverUV.x, lengthMetres - input.riverUV.x) / max(4.0, 4.0 * input.bank.x));
		half inside = saturate(1.0 - abs(input.riverUV.y) / max(0.1, input.bank.x)) > 0.0 ? 1.0 : 0.0;
		flow = lerp(flow, solved, ends * inside * saturate(input.state.w));
	}
	// Under a fall the water boils out from where the curtain lands: that flow, stirred in, and broken water.
	float2 outward;
	half churn = FallChurn(positionWS, outward);
	// The river's own speed, before the boil: the silt it carries is its own, not the fall's.
	float riverSpeed = length(flow);
	float2 riverFlow = flow;
	/* A little outward drift from where the curtain lands, not a strong one: the ripples and foam ride the flow by
	 * flow-mapping, and a strong radial flow drew them as pulsing concentric rings, a swirl disc on every pool. */
	flow += outward * (0.6 * churn);
	float speed = length(flow);
	half broken = saturate(max(input.state.x, churn));

	// ── Ripples, riding the current ───────────────────────────────────
	// Two scales: the current's own, and a finer one the air stirs that hardly moves. Faster water is
	// rougher, broken water roughest.
	half detailFade = saturate(1.0 - distanceToCamera / max(1.0, _NormalFadeDistance));
	half strength = _NormalStrength * detailFade * (1.0 + saturate(speed / 2.0) + broken + 1.5 * churn);
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
	float phase = 6.2831853 * (input.riverUV.x / (wavelength * (0.8 + 0.4 * wander)))
		+ 2.2 * sin(6.2831853 * input.riverUV.y / (1.3 * wavelength) + wander * 6.2831853);
	half crest = saturate(sin(phase) * 0.5 + 0.5);
	crest = crest * crest * crest * train;
	half waveSlope = broken * saturate(speed / 1.5) * 0.28 * detailFade * train;
	float2 tilt = along * (waveSlope * cos(phase));
	// And the stones on a shallow bed, each with its hump and its V.
	half stoneWhite;
	float2 stones = BedStones(positionWS.xz, along, speed, input.depth, stoneWhite) * detailFade;
	tilt += stones;
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
	float3 geometric = cross(ddx(positionWS), ddy(positionWS));
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
	half silt = _Turbidity * (0.3 + 0.7 * saturate(riverSpeed / 1.5)) * input.state.y * (1.0 - churn);
	/* Bubbles under a fall scatter the light back out: milky where the curtain lands, clearing fast with distance (the
	 * churn squared), so the pool reads as a boil round the foot rather than a pale disc. */
	half milk = churn * churn;
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

	// ── Reflection, Fresnel and glint ─────────────────────────────────
	half NdotV = saturate(dot(normalWS, view));
	half fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
	half perceptualRoughness = saturate((1.0 - _Smoothness) + (1.0 - detailFade) * 0.06 + broken * 0.2);
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
	half3 color = below ? behind : lerp(behind, reflection, fresnel) + glint;

	// ── Foam: on the crests of fast water's standing waves, streaked down the current, and where moving
	//    water thins over a bar ─────────────────────────────────────────────────────────────
	half foam = 0.0;
	if (!below)
	{
		half lace = StreakFoam(positionWS.xz, flow, max(0.3, _FoamScale), _FlowCycle);
		// Round a fall's foot the pool boils rather than streaks.
		lace = lerp(lace, BoilFoam(positionWS.xz, riverFlow, max(0.3, _FoamScale), _FlowCycle), saturate(1.6 * churn));
		half shore = 0.0;
		#if defined(_WATER_DEPTH)
			shore = (1.0 - saturate(waterColumn / max(0.02, _ShoreFoam))) * saturate(speed / 0.8) * 0.5;
		#endif
		// How much white the water can carry here: breaking crests in rapids, aeration under a fall, churn in fast water.
		half fallChurn = churn;
		churn = saturate(broken * (0.45 + 0.8 * crest) + saturate((speed - _FoamSpeed) / max(0.1, 2.0 * _FoamSpeed)) * 0.25);
		// Under a fall the pool boils white: most of it foam near the foot, thinning to its edge.
		half amount = max(max(max(max(churn, shore), stoneWhite * 0.8), fallChurn), wakeWhite);
		// Thinned softly, never cut into specks: below a little churn the pattern fades out rather than leaving its peaks.
		half cut = lerp(0.72, 0.35, amount);
		foam = smoothstep(cut - 0.08, cut + 0.22, lace) * smoothstep(0.03, 0.35, amount);
		half3 foamLit = _FoamColor.rgb * (lightColor * (NdotL * 0.6 + 0.3) + _GlossyEnvironmentColor.rgb * 0.5 + 0.1);
		color = lerp(color, foamLit, foam * 0.9);
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
		if (_FishAirFogRange.z < 0.5)
		{
			half fogKeep;
			color = FishWaterAirFog(color, positionWS, screenUV, fogKeep);
		}
	}
	return half4(color, alpha);
}

#endif
