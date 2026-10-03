#ifndef FISHMMO_LAVA_INCLUDED
#define FISHMMO_LAVA_INCLUDED

// Lava: molten rock standing open to the sky — a lava lake, or the lowlands of a magma ocean.
//
// It is NOT a sea, and none of the ocean's machinery applies. Basaltic melt is some ten million
// times as viscous as water, so it carries no wind waves, no surf and no white caps; it is opaque,
// so there is nothing to refract, no depth to absorb through and no caustics on a bed nobody can see.
// What a lava lake actually shows — Halemaʻumaʻu, Erta Ale, Nyiragongo — is its SKIN: a crust of
// quenched glass a few centimetres thick, broken into plates that ride the convecting melt beneath,
// torn apart where the melt wells up and swallowed where it sinks, with the melt glowing through the
// tears between them.
//
// What this models, and why each part is there:
//   1. Convection: cells of upwelling and downwelling a few tens of metres across. The crust flows
//      from where the melt rises to where it sinks.
//   2. Plates riding it, advected by a two-phase flow map. Their borders are TORN, not cut: the
//      plate field is warped at two scales and jagged by a baked tear map, so a crack zig-zags the
//      way glass tears. Some neighbouring plates are welded into compound plates along old sutures,
//      and some split into smaller ones mid-cycle — a range of sizes from a few metres to a few tens.
//   3. Age: of each plate from how far it has drifted, of the rind accreted along each crack, of the
//      skin over melt that a foundering plate or a bursting bubble has uncovered. One age, one law.
//   4. Temperature from age — a conductively cooling half-space, T = floor + ΔT/√(1 + t/τ) — and the
//      light that temperature emits, from Planck's law and the CIE observer. No glow colour is
//      chosen by hand.
//   5. The crust as a material: glass. Black, F0 0.04, smooth when fresh and dulled by age and
//      fallout, with ropy pāhoehoe folds, vesicles and fracture facets from a BAKED texture
//      (LavaTextureBaker) — detail no handful of noise octaves per pixel can afford.
//   6. Events on the flow's own clock: plates founder and sink, bubbles burst and throw spatter.
//
// Every material constant lives in the one block below, declared once, so every pass has an
// identical UnityPerMaterial (the SRP Batcher drops a shader whose passes differ) and every member
// is in the shader's Properties (a member that is not reads zero — FishWaterMaterialTests pins both).

CBUFFER_START(UnityPerMaterial)
	// ── Heat ──
	float _MeltKelvin;
	float _CrustFloorKelvin;
	float _CoolingSeconds;
	half _Exposure;
	half _Response;
	half _DayResponse;
	half _DaylightIlluminance;

	// ── Crust ──
	half4 _CrustColor;
	half _CrustSmoothness;
	half _AgedSmoothness;
	float _DullingSeconds;
	half _CrustTile;
	half _CrustDetail;

	// ── Plates ──
	half _PlateSize;
	half _CrackWidth;
	half _CrustRelief;
	half _PlateJag;
	half _FounderChance;

	// ── Surface ──
	half _Spatter;
	half _SwellHeight;
	half _SwellLength;

	// ── Flow ──
	half _FlowSpeed;
	half _CellSize;
	float _FlowPeriod;
	float _OverturnSeconds;

	// ── Glow on the surroundings and fumes ── (drawn by FishMMO/Water/Lava Light, whose material
	// copies this one's, so the light it casts is the light this surface shows)
	half _GroundAlbedo;
	float _FumeDensity;
	half _FumeHeight;
	half _FumeScale;
	half _FumeRise;

	// ── Development ──
	half _DebugView;
CBUFFER_END

// The baked crust (LavaTextureBaker, FishMMO/Water/Bake Lava Textures). Outside the constant buffer,
// as every texture is. Normal: the crust's small-scale relief. Mask: r and b two independent tear
// fields that jag the plate borders, g roughness (glassy crests, ashy troughs, rough vesicle walls),
// a cavity (how far a texel lies below its surroundings).
TEXTURE2D(_CrustNormal);
SAMPLER(sampler_CrustNormal);
TEXTURE2D(_CrustMask);
SAMPLER(sampler_CrustMask);

/// <summary>Texels across the baked crust maps (LavaTextureBaker.Size).</summary>
#define FISH_LAVA_DETAIL_TEXELS 512.0
/// <summary>
/// Mean squared slope of the baked crust normal map (LavaTextureBaker logs it on every bake). What the
/// mip chain averages away at a distance goes back in as roughness, so far crust is not a mirror.
/// </summary>
#define FISH_LAVA_DETAIL_VARIANCE 0.084
/// <summary>The baked mask's mean roughness and cavity: the crust where its detail is finer than a pixel.</summary>
#define FISH_LAVA_DETAIL_MEAN_ROUGH 0.34
#define FISH_LAVA_DETAIL_MEAN_CAVITY 0.97
/// <summary>Kaplanyan's screen-space variance scale and the most roughness (α²) it may add.</summary>
#define FISH_LAVA_SPECULAR_AA_VARIANCE 0.25
#define FISH_LAVA_SPECULAR_AA_LIMIT 0.18
/// <summary>Share of plate borders that are old welded sutures rather than open cracks.</summary>
#define FISH_LAVA_WELDED 0.3

// ── Globals, set by WaterSurface ───────────────────────────────────────
//
// The same two the ocean reads, published by the same component every camera: the lava is drawn on
// the sea's camera-centred disc and stands at the sea's level. Declared here rather than by including
// FishWaterInput.hlsl, which carries the OCEAN's UnityPerMaterial — a shader can have only one.

/// The surface's height in world metres.
float _FishWaterLevel;
/// Seconds, wrapped by WaterSurface at 10 000. NOT _Time.y, which loses a frame's resolution after a
/// few hours of session.
float _FishWaterTime;

/// <summary>Seconds at which WaterSurface wraps _FishWaterTime (its WrapSeconds).</summary>
#define FISH_LAVA_CLOCK_WRAP 10000.0

// The MOLTEN MASK (LavaMask.cs, set while the glow-and-fume pass runs): 1 where the ground is below
// the lava's level (so the lava shows), 0 where it stands above, built from the scene's terrains and
// MIPMAPPED, so a single tap at the right level is the share of the ground within a given reach that
// is molten. The surface reads it for the lake's margin, the glow pass for where the lava is.
TEXTURE2D(_FishLavaMask);
SAMPLER(sampler_FishLavaMask);
float4 _FishLavaMaskRect;   // xy world minimum, zw size (square)
float4 _FishLavaMaskInfo;   // x metres per texel, y 1 when the mask is live, z fumes (0..1), w 1 to light the surroundings

/// <summary>
/// The share of the ground around a point that is molten, averaged over about <paramref name="reach"/>
/// metres. Off the mask — past every terrain — the disc is lava as far as it goes.
/// </summary>
float FishLavaMolten(float2 xz, float reach)
{
	if (_FishLavaMaskInfo.y < 0.5)
	{
		return 1.0;
	}
	float2 uv = (xz - _FishLavaMaskRect.xy) / max(1e-3, _FishLavaMaskRect.zw);
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 1.0;
	}
	float lod = log2(max(1.0, reach / max(1e-3, _FishLavaMaskInfo.x)));
	return SAMPLE_TEXTURE2D_LOD(_FishLavaMask, sampler_FishLavaMask, uv, lod).r;
}

// ── Black-body light ───────────────────────────────────────────────────
//
// What a surface at temperature T emits, as linear Rec.709 (the frame's own primaries), computed
// rather than looked up. Planck's law over the visible band, weighted by the CIE 1931 2° colour
// matching functions, then XYZ to RGB.
//
// SIXTEEN BINS EVENLY SPACED IN WAVENUMBER (1/λ), from 380 to 780 nm. Below 2000 K every visible
// wavelength is deep in the Wien tail — hc/λkT is at least 9, so the "−1" in Planck's denominator is
// worth less than 1e-4 — and the radiance is exp(−c₂ν/T). On an even grid of ν that is a geometric
// series: two exp() calls a temperature, then a multiply a bin. Each bin's weight folds in the CMF
// value, λ⁻⁵ and the Jacobian λ² of dλ = λ² dν, normalised so the largest is 1.
//
// Checked offline against a 1 nm Planck × CMF integration (Wyman, Sloan and Shirley's 2013
// multi-lobe fit of the CIE 1931 observer, which is where the weights come from) over 700–2100 K:
// chromaticity within 0.001 and luminance ratios within 0.02%. Eight bins were NOT enough (0.36
// chromaticity error); twelve were marginal. At 1420 K it gives linear (1, 0.126, 0) at unit peak —
// sRGB #FF6400, the tabulated black-body colour of 1400 K.

static const float FishLavaFirstWavenumber = 0.00132422402;   // nm⁻¹: the bin centred on 755 nm
static const float FishLavaWavenumberStep = 8.43454791e-05;    // nm⁻¹ between bins
static const float FishLavaSecondRadiation = 1.4388e7;         // c₂ = hc/k, in nm·K

static const float3 FishLavaColourMatch[16] =
{
	float3(0.000000, 0.000002, 0.000000), // 755 nm
	float3(0.000265, 0.000262, 0.000000), // 710 nm
	float3(0.013541, 0.006017, 0.000000), // 670 nm
	float3(0.111419, 0.043792, 0.000000), // 634 nm
	float3(0.238970, 0.138338, 0.000008), // 602 nm
	float3(0.214980, 0.245411, 0.000262), // 573 nm
	float3(0.114071, 0.298555, 0.003656), // 546 nm
	float3(0.030369, 0.260538, 0.025518), // 522 nm
	float3(0.000829, 0.130703, 0.105742), // 500 nm
	float3(0.044949, 0.062098, 0.362201), // 480 nm
	float3(0.136063, 0.029939, 0.825823), // 461 nm
	float3(0.203189, 0.013478, 1.000000), // 444 nm
	float3(0.155719, 0.005719, 0.785228), // 428 nm
	float3(0.049680, 0.002328, 0.210285), // 413 nm
	float3(0.007849, 0.000922, 0.043130), // 399 nm
	float3(0.000708, 0.000360, 0.011682)  // 386 nm
};

// CIE XYZ to linear Rec.709 / sRGB primaries, D65 white.
static const float3x3 FishLavaXYZToRGB = float3x3(
	 3.2406, -1.5372, -0.4986,
	-0.9689,  1.8758,  0.0415,
	 0.0557, -0.2040,  1.0570);

/// <summary>CIE XYZ of a black body, to a common scale. Only ratios between temperatures mean anything.</summary>
float3 FishLavaBlackbodyXYZ(float kelvin)
{
	// Floored at 400 K: below it nothing is visible, and at 300 K the smallest bin is 1e-33,
	// within a few decades of a float's smallest normal.
	float k = FishLavaSecondRadiation / max(kelvin, 400.0);
	float radiance = exp(-k * FishLavaFirstWavenumber);
	float step = exp(-k * FishLavaWavenumberStep);
	float3 xyz = 0.0;
	UNITY_UNROLL
	for (int i = 0; i < 16; i++)
	{
		xyz += FishLavaColourMatch[i] * radiance;
		radiance *= step;
	}
	return xyz;
}

/// <summary>
/// How the eye takes in the glow under the light it is adapted to: <c>_Response</c> in the dark,
/// rising to <c>_DayResponse</c> as the light falling on the lake reaches <c>_DaylightIlluminance</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the response follows the daylight.</b> A black body's visible light is enormously steep in
/// temperature: a 1000 K rind is about a thousandth of the luminance of 1420 K melt. At night the eye
/// adapts to the lava itself and takes in both at once — a dull red rind beside a blazing seam — so
/// the glow is compressed hard. In sunlight it adapts to the sunlit scene, and a sunlit black crust is
/// about as bright as the melt (some 10³ cd/m² each): the faint rinds sink below it and only the
/// cracks and freshly opened melt glow. Compression of 1 is that: the true ratio.
/// </para>
/// <para>
/// The light measured is the illuminance on a level surface, main light by its elevation plus the
/// ambient — what a lake actually receives, and the same for this surface and for the glow pass.
/// </para>
/// </remarks>
half FishLavaAdaptedResponse()
{
	Light light = GetMainLight();
	half3 weights = half3(0.2126h, 0.7152h, 0.0722h);
	half illuminance = dot(light.color, weights) * saturate(light.direction.y) + dot(_GlossyEnvironmentColor.rgb, weights);
	half day = saturate(illuminance / max(1e-3h, _DaylightIlluminance));
	return lerp(_Response, _DayResponse, day);
}

/// <summary>
/// The melt's own glow and the luminance every other glow is measured against. Per material and per
/// frame, so the vertex stage works it out once and hands it on.
/// </summary>
/// <param name="meltGlow">The melt's colour, its brightest channel at <c>_Exposure</c>.</param>
/// <param name="reference">x the melt's luminance (Y), y the scale that puts a glow's peak where the
/// melt's is, z the response the eye has under the present light (FishLavaAdaptedResponse).</param>
void FishLavaReference(out float3 meltGlow, out float3 reference)
{
	float3 xyz = FishLavaBlackbodyXYZ(_MeltKelvin);
	float3 rgb = max(0.0, mul(FishLavaXYZToRGB, xyz));
	float peak = max(1e-30, max(rgb.r, max(rgb.g, rgb.b)));
	meltGlow = rgb / peak * _Exposure;
	reference = float3(max(xyz.y, 1e-30), _Exposure * xyz.y / peak, FishLavaAdaptedResponse());
}

/// <summary>
/// The light a surface at this temperature gives off, on the melt's scale.
/// </summary>
/// <remarks>
/// <b>The colour is physical; the brightness is compressed, and has to be.</b> 900 K is 1/15 000 the
/// luminance of 1420 K and 800 K 1/400 000. With no tonemapper in this project the compression
/// happens here: the luminance relative to the melt is raised to the adapted response
/// (<c>reference.z</c>). Out-of-gamut channels (anything below ~1000 K is redder than the sRGB red
/// primary) are clipped at zero.
/// </remarks>
float3 FishLavaGlow(float kelvin, float3 reference)
{
	float3 xyz = FishLavaBlackbodyXYZ(kelvin);
	float luminance = max(xyz.y, 1e-30);
	float3 rgb = max(0.0, mul(FishLavaXYZToRGB, xyz));
	// rgb / Y is the colour at unit luminance; times Y_ref · _Exposure / peak_ref (reference.y), the
	// melt itself comes out at exactly meltGlow.
	return rgb / luminance * pow(luminance / reference.x, reference.z) * reference.y;
}

/// <summary>
/// What a glow brighter than the frame can hold looks like: its excess spills into the weaker
/// channels, so an incandescent core reads yellow-orange and its cooler lips orange-red.
/// </summary>
/// <remarks>
/// A saturated sensor or retina does this, and a tonemapper would; this project has none. Applied to
/// what the surface SHOWS only — the glow pass lights its surroundings with the true colour.
/// </remarks>
float3 FishLavaSpill(float3 glow)
{
	float over = max(0.0, glow.r - 1.0);
	return glow + float3(0.0, 0.22, 0.04) * over;
}

/// <summary>
/// A crust's surface temperature after it has stood this many seconds.
/// </summary>
/// <remarks>
/// A conductively cooling half-space, the surface falling as 1/√(1 + t/τ) from the melt toward the
/// floor the crust settles at. With τ 3 s, a 1420 K melt and a 550 K floor: 1170 K at 3 s — the dull
/// red-black skin that closes over open melt within seconds — 840 K at half a minute, 690 K at two
/// minutes: the run measured on Hawaiʻian pāhoehoe crusts (Hon et al. 1994; Keszthelyi and Denlinger
/// 1996) and on lake crusts in thermal imagery, where only the seams stay above 1000 K.
/// </remarks>
float FishLavaCrustKelvin(float ageSeconds)
{
	return _CrustFloorKelvin + (_MeltKelvin - _CrustFloorKelvin) * rsqrt(1.0 + max(0.0, ageSeconds) / max(0.1, _CoolingSeconds));
}

// ── Noise ──────────────────────────────────────────────────────────────

/// <summary>Dave Hoskins' sine-free hash: stable over the ±15 km of world coordinates the disc covers.</summary>
float FishLavaHash12(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.x + p3.y) * p3.z);
}

float2 FishLavaHash22(float2 p)
{
	float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return frac((p3.xx + p3.yz) * p3.zy);
}

/// <summary>Value noise in 0..1 with its analytic gradient (yz), quintic so the gradient is smooth too.</summary>
float3 FishLavaNoise(float2 p)
{
	float2 i = floor(p);
	float2 f = p - i;
	float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
	float a = FishLavaHash12(i);
	float b = FishLavaHash12(i + float2(1.0, 0.0));
	float c = FishLavaHash12(i + float2(0.0, 1.0));
	float d = FishLavaHash12(i + float2(1.0, 1.0));
	float k1 = b - a, k2 = c - a, k4 = a - b - c + d;
	return float3(a + k1 * u.x + k2 * u.y + k4 * u.x * u.y,
		du * float2(k1 + k4 * u.y, k2 + k4 * u.x));
}

// ── Convection ─────────────────────────────────────────────────────────

struct FishLavaFlow
{
	float upwelling;    // 0 where the melt sinks, 1 where it rises
	float young;        // how freshly the crust here was made: 1 over an upwelling
	float2 velocity;    // the crust's drift, metres per second
	float phaseOffset;  // staggers the flow map's resets across the lake
};

/// <summary>
/// The melt's convection at a point: where it rises, and how the crust on it drifts.
/// </summary>
/// <remarks>
/// <para>
/// Two octaves of noise about <c>_CellSize</c> across stand for the cells. The crust drifts DOWN the
/// gradient of the upwelling — away from where melt rises, toward where it sinks — which is the whole
/// of a lava lake's surface circulation; a little rotational flow (the perpendicular of the finer
/// octave's gradient, so divergence-free) keeps the drift from running in straight lines.
/// </para>
/// <para>
/// <b>The cells turn over, slowly.</b> Both octaves drift round small circles once per
/// <c>_OverturnSeconds</c>, in opposite senses, so the pattern changes shape rather than sliding.
/// A full circle per period is also what keeps the motion seamless across the clock's wrap — when
/// the period divides 10 000 s.
/// </para>
/// <para>
/// The stagger of the flow map's resets varies over one and a half cells: slowly enough that a plate
/// keeps one clock across itself, so its events (a split, a foundering) happen to all of it at once.
/// </para>
/// </remarks>
FishLavaFlow FishLavaConvection(float2 xz, float footprint)
{
	float cell = max(1.0, _CellSize);
	float turn = 6.2831853 * frac(_FishWaterTime / max(1.0, _OverturnSeconds));
	float2 orbit = float2(cos(turn), sin(turn));
	float fineCell = cell * 0.47;
	float3 broad = FishLavaNoise(xz / cell + orbit * 0.6);
	float3 fine = FishLavaNoise(xz / fineCell - orbit.yx * 0.8 + 17.0);

	float upwelling = broad.x * 0.7 + fine.x * 0.3;
	float2 gradient = broad.yz * (0.7 / cell) + fine.yz * (0.3 / fineCell);
	float2 swirl = float2(fine.z, -fine.y) * (0.25 / fineCell);
	/* Cells finer than a pixel alias into crawling blotches, so far away they fade to their mean: an
	 * even, slowly stirred crust. */
	float resolve = 1.0 - smoothstep(0.2 * cell, 0.6 * cell, footprint);

	FishLavaFlow flow;
	flow.upwelling = lerp(0.5, upwelling, resolve);
	flow.young = smoothstep(0.4, 0.8, flow.upwelling);
	float2 velocity = (swirl - gradient) * cell * _FlowSpeed * resolve;
	float speed = length(velocity);
	// Noise gradients have a long tail; no crust outruns twice the lake's own speed.
	flow.velocity = speed > 2.0 * _FlowSpeed ? velocity * (2.0 * _FlowSpeed / speed) : velocity;
	flow.phaseOffset = FishLavaNoise(xz / (cell * 1.5) + 41.0).x;
	return flow;
}

// ── Plates ─────────────────────────────────────────────────────────────

struct FishLavaPlate
{
	float edge;      // metres to the plate's border
	float2 away;     // unit direction of increasing distance: into the plate, away from the border
	float2 id;       // the plate's cell
	float2 toSeed;   // metres from the point to the plate's centre
	float value;     // a value unique to the plate
	float weld;      // a value unique to the border nearest the point, the same on both its sides
};

/// <summary>
/// The crust plate a point lies on.
/// </summary>
/// <remarks>
/// The exact distance to the Voronoi border (Quílez's two-pass method), not F2 − F1: the cracks are
/// drawn by thresholding this distance and must have the width they are given. The seeds' jitter is
/// held inside the middle 84% of each cell so a 3×3 second pass is enough. The second pass also
/// notes the neighbour across the nearest border, for a value both plates agree on (whether that
/// border is an open crack or a welded suture).
/// </remarks>
FishLavaPlate FishLavaPlates(float2 position, float size)
{
	float2 q = position / size;
	float2 n = floor(q);
	float2 f = q - n;

	float2 nearestCell = 0.0;
	float2 nearest = 0.0;
	float closest = 8.0;
	UNITY_UNROLL
	for (int j = -1; j <= 1; j++)
	{
		UNITY_UNROLL
		for (int i = -1; i <= 1; i++)
		{
			float2 g = float2(i, j);
			float2 r = g + 0.08 + 0.84 * FishLavaHash22(n + g) - f;
			float d = dot(r, r);
			if (d < closest)
			{
				closest = d;
				nearest = r;
				nearestCell = g;
			}
		}
	}

	float edge = 8.0;
	float2 away = float2(0.0, 1.0);
	float2 other = nearestCell;
	UNITY_UNROLL
	for (int jj = -1; jj <= 1; jj++)
	{
		UNITY_UNROLL
		for (int ii = -1; ii <= 1; ii++)
		{
			float2 g = nearestCell + float2(ii, jj);
			float2 r = g + 0.08 + 0.84 * FishLavaHash22(n + g) - f;
			float2 between = r - nearest;
			if (dot(between, between) > 1e-5)
			{
				float2 across = normalize(between);
				float d = dot(0.5 * (nearest + r), across);
				if (d < edge)
				{
					edge = d;
					away = -across;
					other = g;
				}
			}
		}
	}

	FishLavaPlate plate;
	plate.id = n + nearestCell;
	float2 neighbour = n + other;
	plate.edge = edge * size;
	plate.away = away;
	plate.toSeed = nearest * size;
	plate.value = FishLavaHash12(plate.id);
	// Symmetric in the pair: the sum and the absolute difference are the same from either side.
	plate.weld = FishLavaHash12(0.5 * (plate.id + neighbour) + abs(plate.id - neighbour) * 7.13 + 0.37);
	return plate;
}

/// <summary>
/// Distance to the nearest border of a finer jittered grid, as half of F2 − F1: near enough for the
/// thin cracks of a plate breaking up, at half the price of the exact two-pass distance.
/// </summary>
float FishLavaFractures(float2 position, float size)
{
	float2 q = position / size;
	float2 n = floor(q);
	float2 f = q - n;
	float f1 = 8.0;
	float f2 = 8.0;
	UNITY_UNROLL
	for (int j = -1; j <= 1; j++)
	{
		UNITY_UNROLL
		for (int i = -1; i <= 1; i++)
		{
			float2 g = float2(i, j);
			float2 r = g + FishLavaHash22(n + g + 71.3) - f;
			float d = length(r);
			f2 = d < f1 ? f1 : min(f2, d);
			f1 = min(f1, d);
		}
	}
	return 0.5 * (f2 - f1) * size;
}

/// <summary>
/// How much of a pixel a line of this half-width covers, at this distance from its middle.
/// </summary>
/// <remarks>
/// A crack narrower than the pixel is still in the pixel: the line is drawn at least a pixel wide and
/// dimmed by how much of that pixel it really fills, so its light is conserved instead of the crack
/// sparkling in and out as it crosses pixel centres.
/// </remarks>
float FishLavaLine(float distance, float halfWidth, float footprint)
{
	float f = max(footprint, 1e-4);
	float wide = max(halfWidth, 0.5 * f);
	return (1.0 - smoothstep(wide - 0.5 * f, wide + 0.5 * f, distance)) * (halfWidth / max(wide, 1e-5));
}

struct FishLavaCrust
{
	float3 glow;      // emitted light, frame units
	float2 slope;     // height gradient of the crust, for the normal
	float fresh;      // 1 new glassy rind, 0 old dull crust
	float crack;      // share of the pixel that is open melt
	float kelvin;     // the surface's temperature
	float age;        // seconds since the crust here formed
	float rough;      // the baked roughness: glassy crests 0, rough vesicle walls 1
	float cavity;     // the baked cavity: 1 open, less in pits and troughs
	float event;      // 1 where a plate is foundering or a fracture is fresh, for the debug view
};

/// <summary>
/// The crust in one phase of the flow map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Age is what everything follows.</b> A plate is as old as the time it has spent drifting from
/// the upwelling that made it — the cell's size over the crust's speed, less over an upwelling, more
/// toward a sink, and varied plate to plate. Along each crack the plates grow by freezing melt onto
/// their edges, at a quarter of the drift speed, so the crust is YOUNGEST against the crack and ages
/// into the plate: the incandescent seam, the orange rind fading to dull red, the black plate.
/// </para>
/// <para>
/// <b>Torn borders.</b> The plate field is warped at its own scale and at a third of it (the long
/// curving lines), then displaced and jagged by the baked tear fields (the zig-zag of torn glass, a
/// few decimetres). The displacement moves the border itself — which plate a point is on — so the
/// texture and the age change at the jagged line, not at a straight Voronoi edge beside it.
/// </para>
/// <para>
/// <b>Events, each on the phase's own clock</b> (0 to <c>_FlowPeriod</c> seconds, the same across a
/// plate): a few plates founder — tip toward one side and go under over five seconds, uncovering melt
/// that skins over by the cooling law; some split into smaller plates, their new cracks opening over
/// four seconds, glowing, and skinning over into grooves. Splits are commoner on young thin crust,
/// founderings over sinks, where the old thick crust is drawn down.
/// </para>
/// </remarks>
FishLavaCrust FishLavaPhase(float2 xz, FishLavaFlow flow, float phase, float cycle, float footprint,
	float2 dxz, float2 dyz, float3 reference, float3 meltGlow)
{
	float size = max(0.5, _PlateSize);
	float speed = max(_FlowSpeed, 1e-3);
	float tau = max(0.1, _CoolingSeconds);
	float period = max(1.0, _FlowPeriod);
	float tile = max(0.1, _CrustTile);
	float localTime = phase * period;

	// Advected back along the drift, and re-dealt at each reset so a phase never repeats the last.
	float2 dealt = (FishLavaHash22(float2(cycle, cycle * 1.37 + 3.1)) - 0.5) * size * 7.0;
	float2 position = xz - flow.velocity * localTime + dealt;

	// ── Torn borders ──
	float tearScale = 1.0 / (1.7 * tile);
	float4 tearMask = SAMPLE_TEXTURE2D_GRAD(_CrustMask, sampler_CrustMask, frac(position * tearScale), dxz * tearScale, dyz * tearScale);
	float2 tear = tearMask.br - 0.5;
	float3 warpBroad = FishLavaNoise(position / (0.9 * size) + 7.7);
	float3 warpFine = FishLavaNoise(position / (0.33 * size) + 3.1);
	float2 warped = position + warpBroad.yz * (0.32 * size) + warpFine.yz * (0.08 * size) + tear * (0.8 * _PlateJag);
	FishLavaPlate plate = FishLavaPlates(warped, size);
	float edge = plate.edge + _PlateJag * 1.4 * tear.x;

	// ── Age ──
	float young = flow.young;
	bool welded = plate.weld < FISH_LAVA_WELDED;
	float plateAge = (0.15 + 0.85 * (1.0 - young)) * max(1.0, _CellSize) / speed * (0.6 + 0.8 * plate.value);
	float halfWidth = welded ? 0.0 : _CrackWidth * (0.5 + young) * (0.6 + 0.8 * FishLavaHash12(plate.id + 5.7));
	float rindAge = welded ? plateAge : max(0.0, edge - halfWidth) / (0.25 * speed);
	float age = min(plateAge, rindAge);

	// ── Foundering ──
	float2 sinkDraw = FishLavaHash22(plate.id + 11.9);
	float sinkAt = sinkDraw.y * 0.7 * period;
	bool sinks = sinkDraw.x < _FounderChance * (0.3 + 1.4 * (1.0 - young));
	float sinkAngle = 6.2831853 * FishLavaHash12(plate.id + 2.1);
	float2 sinkDirection = float2(cos(sinkAngle), sin(sinkAngle));
	// The waterline of the going-under sweeps across the plate in five seconds, ragged as the plate.
	float across = dot(-plate.toSeed, sinkDirection) / (0.6 * size) + tear.y * 0.6;
	float openAt = sinkAt + saturate(0.5 * (across + 1.0)) * 5.0;
	bool opened = sinks && localTime > openAt;
	bool tipping = sinks && localTime > sinkAt && !opened;
	float skinAge = localTime - openAt;
	age = opened ? skinAge : age;

	float kelvin = FishLavaCrustKelvin(age);
	float3 crustGlow = FishLavaGlow(kelvin, reference);

	// ── Fracture ──
	float2 splitDraw = FishLavaHash22(plate.id + 23.1);
	float splitAt = splitDraw.y * 0.8 * period;
	float sinceSplit = localTime - splitAt;
	float subOpen = 0.0;
	float splitting = 0.0;
	UNITY_BRANCH
	if (splitDraw.x < 0.25 + 0.5 * young && sinceSplit > 0.0 && !opened && footprint < 0.2 * size)
	{
		float subEdge = FishLavaFractures(warped + 13.1, 0.42 * size) + 0.35 * _PlateJag * tear.x;
		float subWidth = max(halfWidth, 0.5 * _CrackWidth) * 0.55 * saturate(sinceSplit / 4.0);
		float subCrack = FishLavaLine(subEdge, subWidth, footprint);
		// Open melt while the crack is still widening, then a skin that cools like any other.
		float subKelvin = FishLavaCrustKelvin(max(0.0, sinceSplit - 6.0));
		crustGlow = lerp(crustGlow, FishLavaGlow(subKelvin, reference), subCrack);
		kelvin = lerp(kelvin, subKelvin, subCrack);
		subOpen = subCrack * exp(-max(0.0, sinceSplit - 6.0) / (3.0 * tau));
		splitting = subCrack;
	}

	// ── The seam ──
	float crack = opened ? 0.0 : FishLavaLine(edge, halfWidth, footprint);
	// The melt is hottest mid-seam and cooled by the crust at its lips.
	float lip = saturate(max(0.0, edge) / max(halfWidth, 1e-3));
	float3 seamGlow = meltGlow * (1.0 - 0.5 * lip * lip);

	FishLavaCrust crust;
	crust.glow = lerp(crustGlow, seamGlow, crack);
	crust.kelvin = lerp(kelvin, _MeltKelvin, crack);
	crust.crack = max(crack, subOpen);
	crust.age = age;
	crust.fresh = exp(-age / max(1.0, _DullingSeconds));
	crust.event = max(sinks && localTime > sinkAt ? 1.0 : 0.0, splitting);

	// ── Relief ──
	/* Each plate a shallow dome — the crust sags into the seams it floats between — and each tilted
	 * its own way, so neighbouring plates catch the light differently, as rafts do. A welded suture
	 * has no sag, only a narrow groove; a foundering plate tips toward the side going under. */
	float dome = 0.5 * size;
	float t = saturate(edge / dome);
	float rise = welded ? 0.0 : _CrustRelief * 6.0 * t * (1.0 - t) / dome;
	float suture = welded ? 1.0 - smoothstep(0.0, max(0.06, footprint), edge) : 0.0;
	float2 tilt = (FishLavaHash22(plate.id + 9.2) - 0.5) * (2.0 * _CrustRelief / size);
	float tip = tipping ? saturate((localTime - sinkAt) / 5.0) * 0.35 : 0.0;
	float2 slope = opened ? float2(0.0, 0.0) : plate.away * rise * (1.0 - crack) + tilt + sinkDirection * tip;

	// ── Detail, riding on the plate ──
	/* Plate-relative coordinates, so the texture moves with its plate and is small enough to be
	 * exact; the folds turned across the drift that made them (ropes form crosswise to the flow), give
	 * or take a plate's own twist. A skin over newly opened melt is the same glass at a finer scale, and
	 * smooth until the crust around it has had time to crumple it. Sampled with the pixel's own
	 * footprint, so a plate border's jump in coordinates never drops the mip to its smallest. */
	float2 drift = flow.velocity + float2(1e-4, 0.0);
	drift *= rsqrt(dot(drift, drift));
	float twist = (FishLavaHash12(plate.id + 4.4) - 0.5) * 1.2;
	float twistCos = cos(twist), twistSin = sin(twist);
	float2 vAxis = float2(drift.x * twistCos - drift.y * twistSin, drift.x * twistSin + drift.y * twistCos);
	float2 uAxis = float2(vAxis.y, -vAxis.x);
	float detailScale = (opened ? 2.2 : 1.0) / tile;
	/* The ropes WANDER: real folds bend round obstacles and fan out as the crust stretches, so the
	 * plate-relative coordinate is bent by the plate's own fine warp (metres over metres) and by the
	 * tear field (decimetres over a metre). Without it the folds stay parallel across a whole plate
	 * and, mirrored in a bright sky, read as a fingerprint of concentric rings. */
	float2 local = -plate.toSeed + warpFine.yz * (0.03 * size) + tear * 0.3;
	float2 uv = float2(dot(local, uAxis), dot(local, vAxis)) * detailScale + FishLavaHash22(plate.id + 6.6) * 7.0;
	float2 du = float2(dot(dxz, uAxis), dot(dxz, vAxis)) * detailScale;
	float2 dv = float2(dot(dyz, uAxis), dot(dyz, vAxis)) * detailScale;
	half3 detailNormal = UnpackNormal(SAMPLE_TEXTURE2D_GRAD(_CrustNormal, sampler_CrustNormal, uv, du, dv));
	half4 detailMask = SAMPLE_TEXTURE2D_GRAD(_CrustMask, sampler_CrustMask, uv, du, dv);
	/* Fresh crust is STRETCHED smooth as it spreads from a seam; its folds grow as it ages and is
	 * crowded toward the sinks, which is where ropes form. So the glassiest plates — the ones that
	 * mirror the sky — carry a third of the relief, and their reflection is not a fingerprint of folds. */
	float folded = lerp(0.35, 1.0, 1.0 - crust.fresh);
	float2 detailSlope = -detailNormal.xy / max(detailNormal.z, 0.05h) * (_CrustDetail * folded);
	float wrinkled = opened ? saturate(skinAge / 20.0) : 1.0;
	slope += (uAxis * detailSlope.x + vAxis * detailSlope.y) * ((1.0 - crack) * wrinkled);

	crust.slope = slope;
	crust.rough = saturate(detailMask.g * wrinkled + 0.3 * suture);
	crust.cavity = (1.0 - (1.0 - detailMask.a) * wrinkled) * (1.0 - 0.5 * suture);
	return crust;
}

// ── The lake's average ─────────────────────────────────────────────────

/// <summary>
/// The light a stretch of lake gives off on average, for where its plates are finer than a pixel.
/// </summary>
/// <remarks>
/// <para>
/// A plate field has edges 2/size metres long per square metre, and 1 − FISH_LAVA_WELDED of them are
/// open, so seams of half-width w cover 4w(1 − welded)/size of it; the glowing rinds beside them, out
/// to where the rind is 2τ old, a further share at the temperature of a τ-old rind; and the rest is
/// plate at its typical age — except the plates that have foundered, whose new skin glows by its own
/// age. That skin's share is the foundering chance times its glow averaged over its exposure,
/// ∫ glow(a) da / period, taken in three spans of age.
/// </para>
/// <para>
/// Averaging the LIGHT, not the temperature: emission is so steep in temperature that the glow of the
/// mean is far darker than the mean of the glow. Fractures and bursts are left out: both are brief
/// and their glow is a few per cent of the seams'.
/// </para>
/// </remarks>
float3 FishLavaMeanGlow(float young, float3 reference, float3 meltGlow)
{
	float size = max(0.5, _PlateSize);
	float speed = max(_FlowSpeed, 1e-3);
	float tau = max(0.1, _CoolingSeconds);
	float period = max(1.0, _FlowPeriod);
	float open = 1.0 - FISH_LAVA_WELDED;
	float seams = saturate(4.0 * open * _CrackWidth * (0.5 + young) / size);
	float rinds = min(1.0 - seams, saturate(4.0 * open * (0.25 * speed * 2.0 * tau) / size));
	float plateAge = (0.15 + 0.85 * (1.0 - young)) * max(1.0, _CellSize) / speed;
	float founder = saturate(_FounderChance * (0.3 + 1.4 * (1.0 - young)));
	float3 skins = (FishLavaGlow(FishLavaCrustKelvin(0.4 * tau), reference) * tau
		+ FishLavaGlow(FishLavaCrustKelvin(2.5 * tau), reference) * (3.0 * tau)
		+ FishLavaGlow(FishLavaCrustKelvin(8.0 * tau), reference) * max(0.0, 0.5 * period - 4.0 * tau)) * (founder / period);
	float rest = 1.0 - seams - rinds;
	return seams * meltGlow
		+ rinds * FishLavaGlow(FishLavaCrustKelvin(tau), reference)
		+ rest * (1.0 - founder) * FishLavaGlow(FishLavaCrustKelvin(plateAge), reference)
		+ rest * skins;
}

/// <summary>
/// The mean glow at any youth, from the three the vertex stage worked out at 0, ½ and 1 (quadratic
/// through them): the far lake costs a pixel no black-body sums at all.
/// </summary>
float3 FishLavaMeanAt(float young, float3 old, float3 middle, float3 youngest)
{
	return old * ((1.0 - young) * (1.0 - 2.0 * young))
		+ middle * (4.0 * young * (1.0 - young))
		+ youngest * (young * (2.0 * young - 1.0));
}

/// <summary>
/// The radiance of the lake as a whole: <see cref="FishLavaMeanGlow"/> averaged over its convection
/// cells, young crust and old. What the lava lights its surroundings with.
/// </summary>
/// <remarks>
/// The upwelling field is two octaves of value noise, which sit about its middle: five evenly
/// weighted points from 0.3 to 0.7 cover the bulk of it, and through FishLavaFlow.young they span
/// old crust (0) to fresh (1). Per material, so the light pass works it out at its three vertices.
/// </remarks>
float3 FishLavaLakeRadiance(float3 reference, float3 meltGlow)
{
	float3 sum = 0.0;
	UNITY_UNROLL
	for (int i = 0; i < 5; i++)
	{
		sum += FishLavaMeanGlow(smoothstep(0.4, 0.8, 0.3 + 0.1 * i), reference, meltGlow);
	}
	return sum * 0.2;
}

// ── Bubble bursts ──────────────────────────────────────────────────────

/// <summary>
/// A gas bubble bursting through the crust: the skin domes over it for a second, tears, and the hole
/// of bright melt widens and closes again over three seconds, throwing spatter that cools in under
/// one. Drawn as its footprint on the surface — the dome, the torn disc and the landed clots — as an
/// opaque surface must; the airborne part of a fountain would need particles.
/// </summary>
/// <remarks>
/// One candidate a 6 m cell every 10 s (a window that divides the clock's 10 000 s wrap), so a burst
/// is found with one hash and no neighbours: each stays inside its own cell. Commonest at the lake's
/// MARGIN, where the crust is dragged under against the wall and gas escapes along it: the molten mask
/// at the burst's centre says how near the shore it is. Faded out once a burst is smaller than a few
/// pixels, so distant ones do not twinkle.
/// </remarks>
/// <returns>How much of the pixel is burst (0 when none); glow and slope are set only when it is not.</returns>
float FishLavaBurst(float2 xz, float footprint, float3 reference, out float3 glow, out float2 slope, out float kelvin)
{
	glow = 0.0;
	slope = 0.0;
	kelvin = 0.0;
	const float window = 10.0;
	const float cellSize = 6.0;
	float2 cell = floor(xz / cellSize);
	float draws = floor(_FishWaterTime / window);
	float timeIn = _FishWaterTime - draws * window;
	float draw = FishLavaHash12(cell + frac(draws * 0.618034) * 517.0);
	UNITY_BRANCH
	if (draw >= _Spatter)
	{
		return 0.0;
	}
	float2 centre = (cell + 0.3 + 0.4 * FishLavaHash22(cell + draws * 0.37 + 5.5)) * cellSize;
	float margin = saturate(2.0 * (1.0 - FishLavaMolten(centre, 8.0)));
	if (draw >= _Spatter * (0.12 + 0.88 * margin))
	{
		return 0.0;
	}
	float2 shape = FishLavaHash22(cell + draws * 0.53 + 9.1);
	float radius = 0.35 + 0.9 * shape.x;
	float t = timeIn - shape.y * 5.0;
	float2 offset = xz - centre;
	float distance = length(offset);

	// The dome: the skin lifted over the gas for its first second.
	float lift = t > 0.0 && t < 1.0 ? 0.25 * radius * t * exp(-(distance * distance) / (radius * radius)) : 0.0;
	slope = -offset * (2.0 * lift / (radius * radius));

	float sinceTear = t - 1.0;
	const float life = 3.0;
	if (sinceTear <= 0.0 || sinceTear >= life)
	{
		return 0.0;
	}
	float hole = radius * sqrt(sin(3.14159265 * sinceTear / life));
	float fade = saturate(radius / (3.0 * max(footprint, 1e-3)));
	float cover = 0.0;
	if (distance < hole)
	{
		// Melt opened by the tear, its surface as old as the tear, cooling as any other.
		kelvin = FishLavaCrustKelvin(sinceTear * 0.5);
		cover = 1.0;
	}
	else if (distance < 3.0 * radius)
	{
		// Spatter: clots a decimetre across, landed around the hole, thin, so cooling three times as fast.
		float clot = FishLavaHash12(floor(xz * 9.0)) > 0.8 ? 1.0 : 0.0;
		cover = clot * exp(-sinceTear / 0.8) * (1.0 - distance / (3.0 * radius));
		kelvin = FishLavaCrustKelvin(sinceTear * 3.0);
	}
	UNITY_BRANCH
	if (cover > 0.0)
	{
		glow = FishLavaGlow(kelvin, reference);
	}
	return cover * fade;
}

#endif
