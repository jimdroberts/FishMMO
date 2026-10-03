#ifndef FISHMMO_LAVA_LIGHT_INCLUDED
#define FISHMMO_LAVA_LIGHT_INCLUDED

// What the lava does to everything around it: the glow it throws up onto the crater walls, the rocks
// and anyone standing by it, and the fumes hanging over it, lit from beneath.
//
// One PROJECTED pass, as the caustics are: a full-screen triangle that reads the depth buffer,
// reconstructs each pixel's world position, and adds light where the lava reaches it. A light
// component would light only what the renderer's per-object light list happens to include, could
// not follow the shape of the lake, and a lake is not a point.
//
// It draws in the transparent queue, AFTER the weather's clouds and fog have been laid over the
// frame, so everything it adds goes through them itself (FishWaterFog.hlsl), exactly as the sea does.
//
// Its material is a copy of the lava's (WaterSurface copies it across every frame), so the light it
// casts is computed from the very surface that is drawn: the same melt, the same cooling, the same
// exposure and response.

#include "FishLava.hlsl"
#include "FishWaterFog.hlsl"

// The molten mask (_FishLavaMask and its rect and info) and FishLavaMolten live in FishLava.hlsl:
// the surface reads the mask too, for how near the lake's margin a bubble bursts.

/// <summary>World position under a screen point, on either depth convention (the editor here is OpenGL).</summary>
float3 FishLavaScenePosition(float2 screenUV, float rawDepth)
{
	#if UNITY_REVERSED_Z
		float ndcDepth = rawDepth;
	#else
		float ndcDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
	#endif
	return ComputeWorldSpacePosition(screenUV, ndcDepth, UNITY_MATRIX_I_VP);
}

/// <summary>
/// The surface normal at a pixel, from the depth buffer.
/// </summary>
/// <remarks>
/// From depth rather than _CameraNormalsTexture, which exists only where SSAO runs a depth-normals
/// prepass (HighFidelity) — and a texture URP has not bound is not black, it is whatever was last in
/// that slot. Each axis takes whichever neighbour is nearer in depth, so a silhouette edge takes the
/// slope of the surface it belongs to instead of the jump to the one behind it.
/// </remarks>
float3 FishLavaSceneNormal(float2 screenUV, float3 centre, float rawCentre)
{
	float2 texel = 1.0 / max(1.0, _ScreenParams.xy);
	float here = LinearEyeDepth(rawCentre, _ZBufferParams);
	float rawE = SampleSceneDepth(screenUV + float2(texel.x, 0.0));
	float rawW = SampleSceneDepth(screenUV - float2(texel.x, 0.0));
	float rawN = SampleSceneDepth(screenUV + float2(0.0, texel.y));
	float rawS = SampleSceneDepth(screenUV - float2(0.0, texel.y));
	bool east = abs(LinearEyeDepth(rawE, _ZBufferParams) - here) < abs(LinearEyeDepth(rawW, _ZBufferParams) - here);
	bool north = abs(LinearEyeDepth(rawN, _ZBufferParams) - here) < abs(LinearEyeDepth(rawS, _ZBufferParams) - here);
	float3 across = east
		? FishLavaScenePosition(screenUV + float2(texel.x, 0.0), rawE) - centre
		: centre - FishLavaScenePosition(screenUV - float2(texel.x, 0.0), rawW);
	float3 up = north
		? FishLavaScenePosition(screenUV + float2(0.0, texel.y), rawN) - centre
		: centre - FishLavaScenePosition(screenUV - float2(0.0, texel.y), rawS);
	float3 normal = cross(up, across);
	float length2 = dot(normal, normal);
	normal = length2 > 1e-12 ? normal * rsqrt(length2) : float3(0.0, 1.0, 0.0);
	// Toward the camera, whichever way the screen's axes run on this platform.
	return dot(normal, _WorldSpaceCameraPos - centre) < 0.0 ? -normal : normal;
}

// ── Fume density ───────────────────────────────────────────────────────

float FishLavaHash13(float3 p)
{
	p = frac(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return frac((p.x + p.y) * p.z);
}

/// <summary>Value noise in 0..1 over three dimensions.</summary>
float FishLavaNoise3(float3 p)
{
	float3 i = floor(p);
	float3 f = p - i;
	float3 u = f * f * (3.0 - 2.0 * f);
	float a = FishLavaHash13(i);
	float b = FishLavaHash13(i + float3(1.0, 0.0, 0.0));
	float c = FishLavaHash13(i + float3(0.0, 1.0, 0.0));
	float d = FishLavaHash13(i + float3(1.0, 1.0, 0.0));
	float e = FishLavaHash13(i + float3(0.0, 0.0, 1.0));
	float g = FishLavaHash13(i + float3(1.0, 0.0, 1.0));
	float h = FishLavaHash13(i + float3(0.0, 1.0, 1.0));
	float k = FishLavaHash13(i + float3(1.0, 1.0, 1.0));
	return lerp(lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y), lerp(lerp(e, g, u.x), lerp(h, k, u.x), u.y), u.z);
}

/// <summary>
/// How much the fume at a point billows: 0 clear air, about 1 in the thick of a plume.
/// </summary>
/// <remarks>
/// Two octaves rising at <c>_FumeRise</c>, the finer faster (small eddies turn over quicker). Not
/// blown sideways: the drift a breeze gives a plume is the weather's business, and a still plume is
/// right under a crater rim, where the lakes are. The clock wraps at 10 000 s, so once every few hours
/// the pattern re-deals — a breath of the plume, not a seam in it.
/// </remarks>
float FishLavaBillow(float3 p, float resolve)
{
	float scale = max(1.0, _FumeScale);
	float rise = _FumeRise * _FishWaterTime;
	float broad = FishLavaNoise3(float3(p.x, p.y - rise, p.z) / scale);
	float fine = FishLavaNoise3(float3(p.x + 31.7, p.y - rise * 1.6, p.z - 17.3) / (scale * 0.43));
	float billow = saturate((broad * 0.65 + fine * 0.35) * 2.2 - 0.6);
	// A step longer than a billow cannot see its shape: what it can see is the average.
	return lerp(0.42, billow, resolve);
}

// ── The pass ───────────────────────────────────────────────────────────

struct LavaLightAttributes { float4 positionOS : POSITION; };

struct LavaLightVaryings
{
	float4 positionCS : SV_POSITION;
	float2 screenUV : TEXCOORD0;
	// The lake's mean radiance (rgb) — the same at all three vertices, worked out here so no pixel
	// repeats it.
	float3 radiance : TEXCOORD1;
};

LavaLightVaryings LavaLightVertex(LavaLightAttributes input)
{
	LavaLightVaryings output;
	// Straight to clip space: a full-screen triangle that must not be moved by its object's transform.
	output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
	output.screenUV = input.positionOS.xy * 0.5 + 0.5;
	#if UNITY_UV_STARTS_AT_TOP
		output.screenUV.y = 1.0 - output.screenUV.y;
	#endif
	float3 meltGlow;
	float3 reference;
	FishLavaReference(meltGlow, reference);
	output.radiance = FishLavaLakeRadiance(reference, meltGlow);
	return output;
}

/// <summary>
/// Henyey-Greenstein, normalised over the sphere. Droplet haze scatters strongly forward.
/// </summary>
float FishLavaPhase(float cosine, float g)
{
	float g2 = g * g;
	return (1.0 - g2) / (12.5663706 * pow(max(1e-4, 1.0 + g2 - 2.0 * g * cosine), 1.5));
}

/// <summary>Fume march: how many steps through the slab, and the furthest it is marched.</summary>
#define FISH_LAVA_FUME_STEPS 8
#define FISH_LAVA_FUME_REACH 3000.0

/// <summary>
/// The glow on what the camera sees, and the fumes in front of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The glow on a surface.</b> A surface over a plane glowing with radiance L receives E = πL·F,
/// where F is the view factor of the glowing part; a Lambertian surface of albedo ρ sends back ρE/π
/// = ρ·L·F. Over an endless molten plane F is (1 − n_y)/2: all of it facing straight down, half on a
/// wall, none facing up — so the light lands on crater walls, overhangs and the undersides of things,
/// and barely on the flat ground beside the lake, which is how lava light looks. With only part of
/// the ground molten, F is scaled by the molten share of what the surface faces: the mask, read at
/// the mip that averages over twice the height (half a down-facing surface's light comes from within
/// one height of it) and centred where the surface looks — a wall facing the lake reads the lake, one
/// facing away reads dry ground. Nothing between the two is tested: a ridge does not cast a glow
/// shadow. There is no albedo buffer in a forward renderer, so ρ is the material's assumption
/// (<c>_GroundAlbedo</c>), not the surface's own.
/// </para>
/// <para>
/// <b>The fumes</b> are a slab from the lava's level up a few scale heights, thickest at the surface,
/// only over molten ground (the mask under each sample, averaged wider as the fume rises — plumes
/// spread), billowing as they rise. Each step scatters three lights toward the eye: the lake's glow
/// from below — L over the molten share of the lower hemisphere, times an isotropic phase, σ·L·F/2 —
/// the sun, forward-scattered, and the sky. The droplets absorb next to nothing (albedo 0.95).
/// </para>
/// <para>
/// <b>Output, premultiplied, blended One SrcAlpha:</b> rgb = glow·T + in-scatter, a = T, so the frame
/// becomes (frame + glow)·T + in-scatter: the glow lands on the surface, then the fumes dim it and
/// add their own light. The frame already carries the clouds and fog, so both terms are first
/// faded by what the fog and the clouds in front of them let through.
/// </para>
/// </remarks>
half4 LavaLightFragment(LavaLightVaryings input) : SV_Target
{
	float level = _FishWaterLevel;
	float3 camera = _WorldSpaceCameraPos;
	// Underneath it there is nothing to light and nothing to see: the lava is opaque.
	if (camera.y < level)
	{
		return half4(0.0, 0.0, 0.0, 1.0);
	}

	float2 screenUV = input.screenUV;
	float3 radiance = input.radiance;
	half3 glow = 0.0;
	float3 scattered = 0.0;
	float transmittance = 1.0;

	#if defined(_WATER_DEPTH)
		float rawDepth = SampleSceneDepth(screenUV);
		#if UNITY_REVERSED_Z
			bool sky = rawDepth <= 1e-7;
		#else
			bool sky = rawDepth >= 1.0 - 1e-7;
		#endif
		float3 positionWS = sky ? camera : FishLavaScenePosition(screenUV, rawDepth);
		float3 toPoint = positionWS - camera;
		float sceneDistance = sky ? FISH_LAVA_FUME_REACH : length(toPoint);
		float3 direction = sky
			? normalize(ComputeWorldSpacePosition(screenUV, 0.5, UNITY_MATRIX_I_VP) - camera)
			: toPoint / max(1e-4, sceneDistance);

		// ── The glow on the surface ──
		float height = positionWS.y - level;
		UNITY_BRANCH
		if (!sky && _FishLavaMaskInfo.w > 0.5 && height > 0.05)
		{
			float3 normal = FishLavaSceneNormal(screenUV, positionWS, rawDepth);
			float facing = 0.5 * (1.0 - normal.y);
			float molten = FishLavaMolten(positionWS.xz + normal.xz * height, 2.0 * height);
			glow = _GroundAlbedo * radiance * (facing * molten);
			half fogKeep;
			FishWaterAirFog(half3(0.0, 0.0, 0.0), positionWS, screenUV, fogKeep);
			glow *= fogKeep * FishWaterCloudsInFront(screenUV).a;
		}

		// ── The fumes ──
		float fumes = _FishLavaMaskInfo.z * _FumeDensity;
		float scaleHeight = max(1.0, _FumeHeight);
		float top = level + 3.0 * scaleHeight;
		// Where the ray is inside the slab [level, top], clipped to what it hits.
		float t0 = 0.0;
		float t1 = min(sceneDistance, FISH_LAVA_FUME_REACH);
		if (abs(direction.y) > 1e-5)
		{
			float enterTop = (top - camera.y) / direction.y;
			float enterLevel = (level - camera.y) / direction.y;
			t0 = max(t0, min(enterTop, enterLevel));
			t1 = min(t1, max(enterTop, enterLevel));
		}
		else if (camera.y > top)
		{
			t1 = -1.0;
		}
		UNITY_BRANCH
		if (fumes > 1e-6 && t1 > t0)
		{
			// Skip rays that never pass over molten ground: three coarse taps, start, middle and end.
			float wide = 8.0 * scaleHeight;
			float3 start = camera + direction * t0;
			float3 end = camera + direction * t1;
			float anyMolten = max(FishLavaMolten(start.xz, wide),
				max(FishLavaMolten(0.5 * (start.xz + end.xz), wide), FishLavaMolten(end.xz, wide)));
			UNITY_BRANCH
			if (anyMolten > 0.005)
			{
				Light sun = GetMainLight();
				float sunPhase = FishLavaPhase(dot(direction, sun.direction), 0.6) * 3.14159265;
				float3 sunLight = sun.color * sunPhase;
				float3 skyLight = _GlossyEnvironmentColor.rgb * 0.5;

				float step = (t1 - t0) / FISH_LAVA_FUME_STEPS;
				float resolve = saturate(1.5 - step / max(1.0, _FumeScale));
				float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);
				UNITY_LOOP
				for (int i = 0; i < FISH_LAVA_FUME_STEPS; i++)
				{
					float t = t0 + (i + jitter) * step;
					float3 p = camera + direction * t;
					float above = max(0.0, p.y - level);
					// The mask under the sample, averaged wider as the fume rises: plumes spread.
					float under = FishLavaMolten(p.xz, 2.0 * above + 8.0);
					float density = fumes * exp(-above / scaleHeight) * under * FishLavaBillow(p, resolve);
					UNITY_BRANCH
					if (density > 1e-7)
					{
						// Lit from below by the molten share of the lower hemisphere, through an
						// isotropic phase: σ·L·F/2. And the sun and the sky from above.
						float3 light = radiance * (0.5 * FishLavaMolten(p.xz, 2.0 * above + 2.0)) + sunLight + skyLight;
						float absorbed = 1.0 - exp(-density * step);
						scattered += transmittance * absorbed * 0.95 * light;
						transmittance *= 1.0 - absorbed;
					}
				}
				// Seen through the fog and the clouds in front of it, measured at the middle of the slab.
				half fumeKeep;
				FishWaterAirFog(half3(0.0, 0.0, 0.0), camera + direction * (0.5 * (t0 + t1)), screenUV, fumeKeep);
				float through = fumeKeep * FishWaterCloudsInFront(screenUV).a;
				scattered *= through;
				transmittance = 1.0 - (1.0 - transmittance) * through;
			}
		}
	#endif

	return half4(glow * transmittance + scattered, transmittance);
}

#endif
