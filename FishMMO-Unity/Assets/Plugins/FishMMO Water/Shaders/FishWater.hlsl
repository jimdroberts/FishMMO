#ifndef FISHMMO_WATER_INCLUDED
#define FISHMMO_WATER_INCLUDED

// Gerstner waves and the material constants, shared by every pass of the water shader.
//
// ONE cbuffer, declared once, included everywhere. The SRP Batcher requires that every pass of a
// shader declare an IDENTICAL UnityPerMaterial layout; the previous shader declared the same
// properties in two different orders in its two passes, which silently dropped the whole shader
// out of batching. Putting the block in a header is the only way that cannot drift again.

CBUFFER_START(UnityPerMaterial)
	half4 _ShallowColor;
	half4 _DeepColor;
	half4 _ScatterColor;
	half4 _FoamColor;
	half4 _ShoreColor;
	half _ShoreDepth;
	half _Clarity;
	half _ClarityScale;
	half _GustScale;
	half _GustStrength;
	half _ShoreBreak;
	half _ShoreSurf;
	half _SwashMetres;
	half _GroupDepth;
	half _ShoreRefraction;
	half _AbsorptionDepth;
	half _ScatterStrength;
	half _Smoothness;
	half _RefractionStrength;
	half _DetailStrength;
	half _DetailScale;
	half _DetailSpeed;
	half _FoamDepth;
	half _FoamCrest;
	half _FoamSharpness;
	half _FoamScale;
	half _EdgeFade;
	half _SpecularStrength;
	half _MaxAlpha;
	half _WaveFadeStart;
	half _WaveFadeEnd;
	half _DetailFadeDistance;
CBUFFER_END

// ── The sea state ─────────────────────────────────────────────────────
//
// Globals rather than material properties, because there is one sea and every piece of it has to
// agree about where the crests are. They are also arrays, which a material property cannot be.
// Globals do not disturb SRP batching — only material constants have to live in the block above.

#define FISHMMO_MAX_WAVES 8

float4 _FishWaterWave[FISHMMO_MAX_WAVES];    // xy unit direction, z wave number k, w amplitude
float4 _FishWaterMotion[FISHMMO_MAX_WAVES];  // x angular frequency, y steepness term Q
float _FishWaterWaveCount;
float _FishWaterLevel;
/// Seconds, wrapped by the CPU. NOT _Time.y: that is a float counting from level load, so after a
/// few hours of a session its resolution is coarser than a frame and the sea judders.
float _FishWaterTime;
/// 1 under open sky, 0 fully under cloud. Driven by the weather system when there is one.
float _FishWaterCloudShadow;
/// The wind, as a unit vector in world XZ. The ripples run with it.
float4 _FishWaterWind;
/// 0 below a stiff breeze, 1 in a gale. Lifts the white-cap threshold; see the shader's foam block.
float _FishWaterWhitecap;

// ── The shore ─────────────────────────────────────────────────────────
//
// R holds the metres of water over the ground at that point: positive in the sea, negative on dry
// land. Built at load from the scene's terrains by WaterShoreField. The rect is the world-space
// area it covers, and a zero width means there is no shore in this scene — open ocean.
TEXTURE2D(_FishWaterShore);
SAMPLER(sampler_FishWaterShore);
float4 _FishWaterShoreRect;   // xy world minimum, zw size
float _FishWaterShoreRange;

/// <summary>Metres of water over the ground, or open ocean where the scene has no shore field.</summary>
float FishWaterSeabedDepth(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return 1000.0;
	}
	float2 uv = (xz - _FishWaterShoreRect.xy) / _FishWaterShoreRect.zw;
	// Outside the scene's ground is open water, not the clamped edge repeated to the horizon.
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 1000.0;
	}
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).r;
}

/// <summary>
/// Which way the ground falls away toward the shore here, as a unit vector, or zero where there is
/// no shore field.
/// </summary>
/// <remarks>
/// Waves refract: they slow where the water is shallow, so a crest arriving at an angle is held
/// back at its inshore end and swings round until it is nearly parallel with the beach. It is why
/// waves arrive square onto a shore whatever direction the wind is, and its absence is why a sea
/// with the wind blowing along the coast looked like corrugated iron sliding sideways past it.
/// </remarks>
float2 FishWaterShoreGradient(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return float2(0.0, 0.0);
	}
	// Sampled over a span rather than a texel: the gradient wanted is the shape of the beach, not
	// the noise of one heightmap sample against its neighbour.
	const float Step = 12.0;
	float east = FishWaterSeabedDepth(xz + float2(Step, 0.0)) - FishWaterSeabedDepth(xz - float2(Step, 0.0));
	float north = FishWaterSeabedDepth(xz + float2(0.0, Step)) - FishWaterSeabedDepth(xz - float2(0.0, Step));
	// Toward the shore is the direction the depth DECREASES.
	float2 gradient = -float2(east, north);
	float length2 = dot(gradient, gradient);
	return length2 > 1e-8 ? gradient * rsqrt(length2) : float2(0.0, 0.0);
}

struct FishWaterSurface
{
	float3 positionWS;
	float3 normalWS;
	/// The surface's own height above still water, in metres. Drives scattering on the crests.
	float height;
	/// Determinant of the horizontal displacement. Below 1 the surface is compressing; at or below
	/// zero it would fold over, which is where a real wave breaks and throws foam.
	float jacobian;
	/// How hard the shore is breaking the waves here, 0 open water to 1 full surf.
	float surf;
};

/// <summary>Displaces a flat point of sea by every wave, with the exact normal.</summary>
/// <remarks>
/// The normal is the cross product of the analytic derivatives, not the mesh normal. Displacing
/// the vertices and lighting them with the flat normal — which is what the shader this replaces
/// did — leaves the waves visible in silhouette and invisible in shading, so a sea in bright sun
/// reads as a flat sheet with a lumpy edge.
/// </remarks>
/// <summary>
/// How much taller a wave stands, and how much of the water's depth it is allowed to fill, once it
/// reaches shallow ground.
/// </summary>
/// <remarks>
/// <para>
/// Green's law: a wave entering shallow water slows, so its energy piles into a shorter, taller
/// wave — amplitude grows as the fourth root of the falling depth. That is why a swell that is
/// imperceptible out at sea rears up into a breaker a hundred metres from the beach, and it is the
/// whole reason a shoreline looks different from open water.
/// </para>
/// <para>
/// The second half is the limit, and it is what actually makes the surf. A wave cannot be taller
/// than the water carrying it: past roughly 0.78 of the depth it breaks. Clamping the amplitude to
/// that fraction makes the wave rear up, flatten off, and spill — and leaves a band, parallel to
/// the shore and moving with the sets, where the clamp is biting. That band IS the surf line, so
/// the foam is drawn where the physics says the wave broke rather than where a texture was painted.
/// </para>
/// </remarks>
void FishWaterShoal(float depth, float wavelength, inout float amplitude, out float breaking, out float steepen)
{
	steepen = 1.0;
	breaking = 0.0;
	// Open water only. Land is a NEGATIVE depth and must fall through to the run-up below.
	if (depth >= 900.0)
	{
		return;
	}
	// Shoaling starts at about half a wavelength of depth, which is where a deep-water wave first
	// feels the bottom.
	float reference = max(1.0, wavelength * 0.5);
	float shallow = max(0.35, depth);
	float gain = clamp(pow(reference / shallow, 0.25), 1.0, 2.0);
	float grown = amplitude * gain;
	/* A shoaling wave does not just get taller, it gets LOPSIDED: the crest outruns the trough,
	 * the front face steepens toward vertical and the top pitches forward. That asymmetry is what
	 * reads as a wave crashing rather than a wave swelling, and in a Gerstner sum it is the
	 * horizontal term — so the steepness rises with the same factor the height does. */
	steepen = clamp(gain * gain, 1.0, 1.35);

	float limit = max(0.0, depth) * _ShoreBreak;

	/* The run-up, and without it there is no swash at all.
	 *
	 * The break limit goes to zero as the water does, so a wave clamped by it converges exactly on
	 * the waterline and the sea stops dead at a fixed edge — which is what the beach looked like.
	 * A real wave does not stop when it breaks; the bore keeps going and runs up the sand well
	 * above still water, then drains back. So the limit gets a floor that survives past the
	 * waterline and fades out over the next half-metre of HEIGHT — which on a gentle beach is tens
	 * of metres of sand, and is why the water's edge sweeps back and forth instead of sitting
	 * still. The terrain's own depth buffer decides where the sheet stops; nothing has to clip it.
	 */
	float runup = _SwashMetres * saturate(1.0 + depth / max(0.05, _SwashMetres));

	breaking = saturate((grown - limit) / max(0.05, grown));
	amplitude = min(grown, max(limit, runup));
}

FishWaterSurface FishWaterDisplace(float3 flatPositionWS, float amplitudeScale, float footprintMetres)
{
	FishWaterSurface surface;
	surface.positionWS = flatPositionWS;
	surface.height = 0.0;

	// Derivatives of the displaced position with respect to the undisplaced x and z.
	float3 tangent = float3(1.0, 0.0, 0.0);
	float3 binormal = float3(0.0, 0.0, 1.0);
	// The 2x2 horizontal Jacobian, for where the surface is pinching into a breaking crest.
	float jxx = 1.0;
	float jzz = 1.0;
	float jxz = 0.0;
	surface.surf = 0.0;

	float depth = FishWaterSeabedDepth(flatPositionWS.xz);
	float2 shoreward = FishWaterShoreGradient(flatPositionWS.xz);

	int count = (int)_FishWaterWaveCount;
	[loop]
	for (int i = 0; i < count; i++)
	{
		float4 wave = _FishWaterWave[i];
		float4 motion = _FishWaterMotion[i];
		float2 direction = wave.xy;
		float k = wave.z;
		float a = wave.w * amplitudeScale;
		float q = motion.y;
		float wavelength = 6.2831853 / max(1e-4, k);

		/* Waves finer than the pixel are dropped, exactly as the ripples are.
		 *
		 * Moving the normal to per-pixel fixed the faceting and introduced this: the wave sum has
		 * detail down to a couple of metres, and once a pixel covers more than that the normal is
		 * being point-sampled below its Nyquist limit. It aliases into a herringbone that crawls
		 * over every wave face. The vertex stage passes zero here and keeps every wave, because
		 * the geometry must not shrink — only what is asked of the SHADING is filtered.
		 */
		a *= saturate(wavelength / max(1e-4, footprintMetres * 4.0) - 1.0);

		/* Refraction: the crest swings round to face the beach as it feels the bottom, starting at
		 * about half a wavelength of depth — the same place shoaling starts, because it is the
		 * same cause. */
		float feel = depth < 900.0 ? saturate(1.0 - depth / max(1.0, wavelength * 0.5)) : 0.0;
		float bend = feel * _ShoreRefraction;
		if (bend > 0.001 && dot(shoreward, shoreward) > 0.5)
		{
			float2 turned = lerp(direction, shoreward, bend);
			float turnedLength2 = dot(turned, turned);
			direction = turnedLength2 > 1e-8 ? turned * rsqrt(turnedLength2) : direction;
		}

		/* Wave GROUPS, which is what stops an ocean reading as corrugated iron.
		 *
		 * A real sea arrives in sets: a run of big waves, then a lull, then more. It happens
		 * because components of nearby frequency beat against one another, and the envelope that
		 * results travels at HALF the speed of the waves inside it — deep-water group velocity is
		 * exactly c/2. So the envelope here is itself a wave, seven times longer and obeying the
		 * same dispersion, which is why crests appear to march forward through their own group and
		 * die at the front of it.
		 *
		 * Without this every crest in the scene is the same height as every other, which is the
		 * single most artificial thing an ocean shader can do.
		 */
		float groupK = k / max(1.0, _GroupDepth);
		float groupPhase = groupK * dot(direction, flatPositionWS.xz)
			- sqrt(9.81 * groupK) * _FishWaterTime + motion.z;
		float envelope = 0.30 + 0.70 * (0.5 + 0.5 * sin(groupPhase));
		a *= envelope;

		// Shoaling, per wave: the long swell feels the bottom far out and the short chop does not
		// feel it until it is almost ashore, which is why a beach sorts the sea into neat lines.
		float breaking;
		float steepen;
		FishWaterShoal(depth, wavelength, a, breaking, steepen);
		q *= steepen;
		surface.surf = max(surface.surf, breaking * saturate(wave.w * amplitudeScale));

		float phase = k * dot(direction, flatPositionWS.xz) - motion.x * _FishWaterTime;
		float s, c;
		sincos(phase, s, c);

		surface.positionWS.xz += q * a * direction * c;
		surface.positionWS.y += a * s;
		surface.height += a * s;

		float wa = k * a;
		tangent.x -= q * wa * direction.x * direction.x * s;
		tangent.y += wa * direction.x * c;
		tangent.z -= q * wa * direction.x * direction.y * s;

		binormal.x -= q * wa * direction.x * direction.y * s;
		binormal.y += wa * direction.y * c;
		binormal.z -= q * wa * direction.y * direction.y * s;

		jxx -= q * wa * direction.x * direction.x * s;
		jzz -= q * wa * direction.y * direction.y * s;
		jxz -= q * wa * direction.x * direction.y * s;
	}

	/* Foam only where the wave is actually up.
	 *
	 * The break test is true across the whole shallow zone — every wave in there is taller than
	 * the water can carry — so on its own it whitens the entire nearshore into a sheet. What a surf
	 * zone actually looks like is bands: the breaking happens on the FRONT OF THE CREST, and the
	 * troughs between are green water. Gating by the surface's own height above still water turns
	 * the constant test into lines of white rolling shoreward with the sets.
	 */
	float crestBand = saturate(surface.height / max(0.15, depth * 0.30));
	surface.surf *= crestBand * crestBand;

	surface.normalWS = normalize(cross(binormal, tangent));
	// Seen from below the cross product points the other way; the fragment stage decides which
	// side it is on, so keep it consistently up here.
	surface.normalWS.y = abs(surface.normalWS.y);
	surface.jacobian = jxx * jzz - jxz * jxz;
	return surface;
}

/// <summary>
/// How much of the wave height a point this far from the camera should keep.
/// </summary>
/// <remarks>
/// Not a saving — a correctness fix. A wave whose crests are closer together on screen than a
/// pixel cannot be sampled; it aliases into a crawling moiré that no amount of anti-aliasing
/// touches, because the geometry itself is under-sampled. Flattening the far sea and letting the
/// reflection carry it is what every production ocean does, and it is why the horizon of a good
/// one is calm.
/// </remarks>
float FishWaterAmplitudeFade(float distanceToCamera)
{
	return 1.0 - smoothstep(_WaveFadeStart, max(_WaveFadeEnd, _WaveFadeStart + 1.0), distanceToCamera);
}

/// <summary>One stable pseudo-random value per lattice point.</summary>
float FishWaterHash(float2 p)
{
	return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

/// <summary>Value noise in world metres, for mottling foam. No texture, so it never tiles.</summary>
float FishWaterNoise(float2 p)
{
	float2 cell = floor(p);
	float2 f = frac(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = FishWaterHash(cell);
	float b = FishWaterHash(cell + float2(1.0, 0.0));
	float c = FishWaterHash(cell + float2(0.0, 1.0));
	float d = FishWaterHash(cell + float2(1.0, 1.0));
	return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

/// <summary>
/// Two octaves of value noise, for the slow large-scale fields — how clear the water is here, and
/// where the wind is ruffling it.
/// </summary>
float FishWaterFbm(float2 p)
{
	return FishWaterNoise(p) * 0.65 + FishWaterNoise(p * 2.13 + 7.3) * 0.35;
}

/// <summary>
/// The slope of the ripples that are too small for the mesh to carry.
/// </summary>
/// <remarks>
/// <para>
/// More of the same spectrum, evaluated per pixel instead of per vertex, and returned as a
/// GRADIENT rather than a height — so it perturbs the wave normal directly and needs no tangent
/// frame. The mesh has no meaningful tangents, and inventing one from the world up puts a seam
/// wherever a wave face turns past vertical.
/// </para>
/// <para>
/// <b>No texture, deliberately.</b> A scrolling normal map over an ocean repeats, and the repeat
/// is the thing the eye finds first on a flat horizon; it also has to be authored at some metre
/// scale and then stretches or tiles at any other. These wavelets run at true wavelengths — 2.1 m
/// down to 0.4 m — travel at their own dispersion speed like the waves under them, and cost three
/// sines.
/// </para>
/// </remarks>
float2 FishWaterRippleSlope(float2 xz, float fade, float footprintMetres)
{
	float2 slope = 0.0;
	float2 direction = normalize(_FishWaterWind.xy + float2(1e-4, 0.0));
	float wavelength = 2.1 * max(0.05, _DetailScale);
	// A slope, not a height: 0.05 is about three degrees of tilt on the longest ripple, which is
	// what a light breeze actually does to a water surface.
	float slopeAmplitude = 0.05 * fade;

	[unroll]
	for (int i = 0; i < 4; i++)
	{
		/* Turned 137 degrees each step — the golden angle — and given a phase that is not a
		 * multiple of anything. Turned by a small angle instead, as this was, every octave keeps
		 * roughly the same crest line and the four sum into corduroy: a regular diagonal ribbing
		 * that reads as fabric, not water. A real capillary field has no preferred phase. */
		direction = float2(direction.x * -0.7314 - direction.y * 0.6819,
			direction.x * 0.6819 + direction.y * -0.7314);
		float offset = i * 2.399963;

		/* Each octave is faded out once this pixel covers more than about a fifth of its
		 * wavelength.
		 *
		 * MEASURED, not guessed at: a fixed fade distance left a crosshatch of crawling moiré over
		 * every wave face out to the horizon, because whether a ripple can be resolved has nothing
		 * to do with how far away it is — it is how much ground one pixel covers, which also
		 * depends on the field of view and the resolution. ddx/ddy answer that exactly, for free,
		 * and are the only reading that stays right when somebody changes either.
		 */
		float resolved = saturate(wavelength / max(1e-4, footprintMetres * 5.0) - 1.0);

		float k = 6.2831853 / wavelength;
		// Their own dispersion speed, so the small ripples crawl and the larger ones run — the
		// same relation the waves beneath them obey.
		/* The phase is WARPED by a slow noise field, and that is the difference between water and
		 * corduroy. Four pure sinusoids stay in step with themselves across the whole sea and read
		 * as woven fabric — the crests line up into long regular ribs that no amount of lowering
		 * the amplitude hides, because the eye is finding the REGULARITY, not the strength. A real
		 * capillary field has no coherence beyond a few wavelengths. One noise sample per octave
		 * scatters the phase over a few wavelengths and the ribbing becomes patches. */
		float warp = FishWaterNoise(xz / (wavelength * 2.5)) * 6.2831853;
		float phase = k * dot(direction, xz) - sqrt(9.81 * k) * _FishWaterTime * _DetailSpeed + offset + warp;
		slope += direction * (slopeAmplitude * resolved * cos(phase));
		wavelength *= 0.52;
		slopeAmplitude *= 0.78;
	}
	return slope * _DetailStrength;
}

#endif
