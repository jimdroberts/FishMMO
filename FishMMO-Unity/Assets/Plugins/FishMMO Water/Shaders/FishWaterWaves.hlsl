#ifndef FISHMMO_WATER_WAVES_INCLUDED
#define FISHMMO_WATER_WAVES_INCLUDED

#include "FishWaterInput.hlsl"

/// <summary>
/// Surface gravity of the world this sea is on, in m/s². Driven from the celestial body.
/// </summary>
/// <remarks>
/// Not a constant, and that is the point. Deep-water waves travel at sqrt(g/k), so on a moon at a
/// sixth of a gravity the same swell moves at 40% of the speed and takes six times the wavelength
/// to reach the same height. A sea that ran at 9.81 everywhere would look identical on every world
/// in the system, which is the opposite of what this project is for.
/// </remarks>
float _FishWaterGravity;

struct FishWaterSurface
{
	float3 positionWS;
	float3 normalWS;
	/// Height above still water, in metres. Drives scattering on the crests.
	float height;
	/// Determinant of the horizontal displacement. At or below zero the surface would fold, which
	/// is where a real wave breaks.
	float jacobian;
	/// How hard the shore is breaking the waves here: 0 open water, 1 full surf.
	float surf;
	/// Metres of water over the ground here. 1000 in open ocean.
	float depth;
};

/// <summary>Metres of water over the ground now, or open ocean where the scene has no shore field.</summary>
/// <remarks>
/// The field holds the depth at MEAN sea level; the tide is added here. Without it the waves died
/// at the mean waterline whatever the tide was doing, which at high tide left a strip of calm,
/// waveless water between the surf and the swash.
/// </remarks>
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
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).r + (_FishWaterLevel - _FishWaterMeanLevel);
}

/// <summary>Signed metres to the water's edge: positive at sea, negative on dry land.</summary>
float FishWaterShoreDistance(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return 1000.0;
	}
	float2 uv = (xz - _FishWaterShoreRect.xy) / _FishWaterShoreRect.zw;
	if (any(uv < 0.0) || any(uv > 1.0))
	{
		return 1000.0;
	}
	return SAMPLE_TEXTURE2D_LOD(_FishWaterShore, sampler_FishWaterShore, uv, 0).g;
}

/// <summary>
/// Which way the ground falls away toward the shore, as a unit vector; zero where there is none.
/// </summary>
/// <remarks>
/// Waves slow in shallow water, so a crest arriving at an angle is held back at its inshore end
/// and swings round until it is nearly parallel with the beach. It is why surf arrives square onto
/// a shore whatever the wind is doing, and its absence is why a sea with the wind blowing along a
/// coast looked like corrugated iron sliding sideways past it.
/// </remarks>
float2 FishWaterShoreGradient(float2 xz)
{
	if (_FishWaterShoreRect.z < 1.0)
	{
		return float2(0.0, 0.0);
	}
	// Over a span rather than a texel: the wanted gradient is the shape of the beach, not the
	// noise of one heightmap sample against its neighbour.
	const float Step = 14.0;
	float east = FishWaterSeabedDepth(xz + float2(Step, 0.0)) - FishWaterSeabedDepth(xz - float2(Step, 0.0));
	float north = FishWaterSeabedDepth(xz + float2(0.0, Step)) - FishWaterSeabedDepth(xz - float2(0.0, Step));
	float2 gradient = -float2(east, north);   // toward shore is where depth DECREASES
	float length2 = dot(gradient, gradient);
	return length2 > 1e-8 ? gradient * rsqrt(length2) : float2(0.0, 0.0);
}

/// <summary>How much of its height a wave keeps at this distance from the camera.</summary>
/// <remarks>
/// A correctness fix, not a saving. A wave whose crests are closer together on screen than a pixel
/// cannot be sampled; it aliases into a crawling moiré no anti-aliasing touches, because the
/// geometry itself is under-sampled. Flattening the far sea and letting the reflection carry it is
/// why the horizon of a good ocean is calm.
/// </remarks>
float FishWaterAmplitudeFade(float distanceToCamera)
{
	return 1.0 - smoothstep(_WaveFadeStart, max(_WaveFadeEnd, _WaveFadeStart + 1.0), distanceToCamera);
}

/// <summary>
/// Samples one FFT cascade.
/// </summary>
/// <param name="xz">World position in metres.</param>
/// <param name="patch">The cascade's tile size in metres.</param>
/// <param name="weight">How much of this cascade to take, for resolution fading.</param>
void FishWaterCascade(Texture2D displacementMap, Texture2D derivativeMap, float2 xz, float patch,
	float weight, inout float3 displacement, inout float2 slope, inout float folding)
{
	if (weight <= 0.001)
	{
		return;
	}
	float2 uv = xz / max(1.0, patch);
	float4 d = SAMPLE_TEXTURE2D_LOD(displacementMap, sampler_FishWaterDisplacement0, uv, 0);
	float4 n = SAMPLE_TEXTURE2D_LOD(derivativeMap, sampler_FishWaterDisplacement0, uv, 0);
	displacement += d.xyz * weight;
	slope += n.xy * weight;
	// Folding below one is a compressing surface; at or under zero it has turned inside out, which
	// is physically where a wave breaks. Accumulated as a deficit so cascades can agree.
	folding += max(0.0, 1.0 - n.z) * weight;
}

/// <summary>
/// The sea surface at a point: displacement, normal, and where it is breaking.
/// </summary>
/// <param name="flatPositionWS">The undisplaced point; Y is the still-water level.</param>
/// <param name="amplitudeScale">Distance fade, from <see cref="FishWaterAmplitudeFade"/>.</param>
/// <param name="footprintMetres">
/// How much ground one pixel covers. A cascade whose tile is finer than the footprint cannot be
/// resolved and is faded out rather than point-sampled into aliasing; the vertex stage passes zero
/// and keeps them all, because the geometry must not shrink.
/// </param>
/// <remarks>
/// <b>This replaces a sum of six Gerstner waves.</b> Six components cannot look like an ocean —
/// the periodicity is always visible and no tuning removes it. Three FFT cascades carry about
/// 200,000 components between them, and cost LESS per pixel than the sum did, because sampling
/// three textures is cheaper than re-evaluating six sines and their derivatives.
/// </remarks>
FishWaterSurface FishWaterDisplace(float3 flatPositionWS, float amplitudeScale, float footprintMetres)
{
	FishWaterSurface surface;
	surface.positionWS = flatPositionWS;
	surface.depth = FishWaterSeabedDepth(flatPositionWS.xz);
	// Which way the beach lies. Zero out at sea, where there is no shore to roll toward.
	float2 shoreward = FishWaterShoreGradient(flatPositionWS.xz);

	float3 displacement = 0.0;
	float2 slope = 0.0;
	float folding = 0.0;

	/* A cascade is dropped once one pixel covers more than about a quarter of its tile: below
	 * that it is being point-sampled far under its own Nyquist limit and returns noise, which is
	 * the classic FFT-ocean horizon sparkle. */
	float3 resolved = 1.0;
	if (footprintMetres > 0.0)
	{
		resolved.x = saturate(_FishWaterPatch.x / (footprintMetres * 4.0) - 1.0);
		resolved.y = saturate(_FishWaterPatch.y / (footprintMetres * 4.0) - 1.0);
		resolved.z = saturate(_FishWaterPatch.z / (footprintMetres * 4.0) - 1.0);
	}

	FishWaterCascade(_FishWaterDisplacement0, _FishWaterDerivatives0, flatPositionWS.xz,
		_FishWaterPatch.x, resolved.x, displacement, slope, folding);
	FishWaterCascade(_FishWaterDisplacement1, _FishWaterDerivatives1, flatPositionWS.xz,
		_FishWaterPatch.y, resolved.y, displacement, slope, folding);
	FishWaterCascade(_FishWaterDisplacement2, _FishWaterDerivatives2, flatPositionWS.xz,
		_FishWaterPatch.z, resolved.z, displacement, slope, folding);

	displacement *= amplitudeScale;
	slope *= amplitudeScale;

	/* The shore.
	 *
	 * The FFT is a deep-water model and knows nothing about a bottom, so shoaling, breaking and
	 * swash are applied to its output: the height grows as the water shallows (Green's law), and
	 * is then limited to what the depth can carry, which is what makes the wave rear up and
	 * spill. The horizontal displacement is cut at the same time, because a wave that has run out
	 * of water cannot keep throwing itself forward. */
	surface.surf = 0.0;
	if (surface.depth < 900.0)
	{
		float reference = max(1.0, _FishWaterPatch.y * 0.5);
		float shallow = max(0.35, surface.depth);
		float gain = clamp(pow(reference / shallow, 0.25), 1.0, 2.0);

		float grown = abs(displacement.y) * gain;
		float limit = max(0.0, surface.depth) * _ShoreBreak;
		/* NO run-up here any more.
		 *
		 * The swash — the sheet that runs up the sand and drains back — belongs to the shore pass,
		 * which owns the beach and can conform to it. Letting the ocean's surface rise above the
		 * waterline instead made it poke through every dip in the sand independently and strand
		 * disconnected puddles up the beach, because a displaced plane has no way to know that a
		 * swash is ONE body of water. Here the sea simply converges on the waterline, and the
		 * depth buffer hides whatever is under the ground.
		 */
		float allowed = limit;

		surface.surf = saturate((grown - allowed) / max(0.05, grown));
		float scale = grown > 1e-4 ? min(grown, allowed) / grown : 1.0;
		displacement.y *= gain * scale;
		// Shoreward crests keep their slope; the lateral throw dies with the depth.
		// The lateral throw dies with the depth: a wave that has run out of water underneath it
		// cannot keep hurling itself forward.
		displacement.xz *= saturate(surface.depth * 0.5);
		slope *= gain * scale;
	}

	/* ── The shore train: waves that roll in, rear up and pitch over ──
	 *
	 * The FFT is a deep-water model: it knows nothing about a bottom, so on its own the sea simply
	 * gets shallower and flatter toward a beach. Everything recognisable about surf — the long
	 * parallel lines arriving one after another, the face that steepens to vertical, the crest
	 * that throws forward and curls — comes from the bottom, and has to be added against it.
	 *
	 * Fronts are lines of constant DISTANCE from the water's edge, so they arrive parallel to the
	 * beach whatever the wind is doing, and they travel shoreward at the shallow-water speed.
	 *
	 * The barrel is a forward SHEAR, and it is the same mechanism that cusps a Gerstner crest:
	 * displace the water toward the beach in proportion to how high it stands, and the front face
	 * leans. Past the point where that lean exceeds the face's own slope the crest overhangs the
	 * trough — which is a plunging breaker, drawn rather than faked. It is switched on by how hard
	 * the wave is breaking, so it happens at the break line and nowhere else.
	 */
	if (surface.depth < 900.0 && _ShoreWaveHeight > 0.01 && dot(shoreward, shoreward) > 0.5)
	{
		float wavelength = max(4.0, _ShoreWaveLength);
		float k = 6.2831853 / wavelength;
		float distance = FishWaterShoreDistance(flatPositionWS.xz);
		// Travelling shoreward: a crest sits where the phase is constant, and its distance from
		// the edge falls as time runs.
		float phase = k * distance + sqrt(max(0.05, _FishWaterGravity) * k) * _FishWaterTime;

		/* The beach, measured over twenty metres across the shoreward direction: what decides both
		 * whether a surf train forms at all and how its waves break. */
		const float Span = 10.0;
		float beachSlope = abs(FishWaterSeabedDepth(flatPositionWS.xz - shoreward * Span)
			- FishWaterSeabedDepth(flatPositionWS.xz + shoreward * Span)) / (2.0 * Span);
		/* A surf train is a BEACH's: waves shoal across a gentle bottom and arrive as lines of
		 * breakers. Against a steep bank or a cliff there is no shoaling zone to cross — the waves
		 * run into the face and are thrown back — so the train fades out on a coast steeper than
		 * about one in four, and the deep-water sea meets the rock by itself. */
		float reflective = smoothstep(0.25, 0.6, beachSlope);

		// A wave feels the bottom from about half a wavelength of depth.
		float feel = saturate(1.0 - surface.depth / (wavelength * 0.5));
		// Green's law, then the depth limit that breaks it.
		float amplitude = _ShoreWaveHeight * (1.0 + feel * 1.6) * feel * (1.0 - reflective);
		float limit = max(0.0, surface.depth) * 0.78;
		float breaking = saturate((amplitude - limit) / max(0.05, amplitude));
		amplitude = min(amplitude, limit);
		// Gone by the waterline; the shore pass owns everything past it.
		amplitude *= saturate(surface.depth * 1.2);

		float s, c;
		sincos(phase, s, c);
		float crest = saturate(s);

		displacement.y += amplitude * s;
		/* The lean. Only on the crest, and only once the wave is breaking — and scaled by the
		 * WAVELENGTH, not the height.
		 *
		 * To overhang, the crest has to be thrown past the trough in front of it, which is about a
		 * quarter wavelength away. Scaled by height, as this first was, the throw came to 0.7 m on
		 * a 40 m wave against the ~10 m needed: the face steepened slightly and never went past
		 * vertical, so the surf rolled but did not curl. Cubing the crest keeps the throw on the
		 * very top of the wave, which is the part that pitches — the base stays where it is and
		 * the lip goes over it. */
		/* Whether it plunges at all is decided by the BEACH, not by a slider.
		 *
		 * The Iribarren number, ξ = tan β / sqrt(H/L), is what separates breaker types in the
		 * real surf zone: below about 0.5 the wave SPILLS, its crest crumbling down its own face;
		 * between 0.5 and 3.3 it PLUNGES, throwing its lip out into a barrel. A small wave on a
		 * gentle beach therefore spills however the throw is set, and forcing a curl onto it is
		 * the look of a wave machine rather than a coast. Measured on the test beach — a 1:33
		 * slope under 0.65 m of sea — ξ is about 0.17, which is exactly why it spills. Put the
		 * same sea against a 1:10 shelf and ξ rises past 0.5, and the barrels appear by
		 * themselves.
		 */
		float waveHeight = max(0.05, amplitude * 2.0);
		float iribarren = beachSlope / sqrt(max(1e-4, waveHeight / wavelength));
		/* And past about 3.3 it SURGES: the slope is too steep for the wave to break at all, and it
		 * runs up the face as a swell and slides back. Plunging was left on for every slope
		 * steeper than a beach, so against a steep bank the Iribarren number of three to six
		 * threw a barrel of water horizontally into the terrain on every wave. */
		float plunging = smoothstep(0.35, 0.9, iribarren) * (1.0 - smoothstep(2.5, 3.5, iribarren));

		float throwMetres = _ShoreWavePitch * plunging * breaking * crest * crest * crest * wavelength * 0.11;
		displacement.xz += shoreward * throwMetres;
		// Gradient of the added height along the shoreward direction.
		slope += shoreward * (-k * amplitude * c);

		surface.surf = max(surface.surf, breaking * crest * crest * _ShoreWaveFoam);
	}

	surface.positionWS += displacement;
	surface.height = displacement.y;
	surface.normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
	surface.jacobian = 1.0 - saturate(folding);

	/* Surf only where the wave is up. The break test is true across the whole shallow zone, so
	 * ungated it whitens the entire nearshore into a sheet; a surf zone is BANDS, because
	 * breaking happens on the front of the crest and the troughs between are green water. */
	float crestBand = saturate(surface.height / max(0.15, surface.depth * 0.30));
	surface.surf *= crestBand * crestBand;
	return surface;
}

#endif
