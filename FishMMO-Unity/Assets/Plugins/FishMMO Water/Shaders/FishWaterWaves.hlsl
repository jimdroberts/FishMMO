#ifndef FISHMMO_WATER_WAVES_INCLUDED
#define FISHMMO_WATER_WAVES_INCLUDED

#include "FishWaterInput.hlsl"
// The shore's clock and its phase along the beach: the surf train keeps time with the swash.
#include "FishWaterSurf.hlsl"

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
/// The direction to the nearest shore, as a unit vector, and how sure it is: 1 almost everywhere,
/// 0 on a ridge midway between two shores, where it flips from one to the other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Down the distance field, not the depth.</b> The surf's crests are contours of the distance to
/// the water's edge, so the direction across them — the way a crest travels and is thrown — is that
/// field's gradient, and nothing else is square to them. It was the slope of the seabed over
/// fourteen metres either side, which across an island narrower than twenty-eight metres straddles
/// it: measured around the small islands of Cov Viaduct it was 33 degrees off the way to the shore
/// at the median and worse than 45 over two-fifths of the water, so every crest was thrown partly
/// along itself.
/// </para>
/// <para>
/// The confidence is the gradient's own length. An exact distance field slopes at one metre per
/// metre everywhere except on those ridges, where the difference taken across them cancels.
/// </para>
/// </remarks>
float FishWaterShoreFacing(float2 xz, out float2 towardShore)
{
	towardShore = float2(0.0, 0.0);
	float confidence = 0.0;
	if (_FishWaterShoreRect.z >= 1.0)
	{
		float step = max(1.0, _FishWaterShoreTexel);
		float east = FishWaterShoreDistance(xz + float2(step, 0.0)) - FishWaterShoreDistance(xz - float2(step, 0.0));
		float north = FishWaterShoreDistance(xz + float2(0.0, step)) - FishWaterShoreDistance(xz - float2(0.0, step));
		float2 gradient = -float2(east, north) / (2.0 * step);   // toward the shore: where the distance falls
		float length2 = dot(gradient, gradient);
		if (length2 > 1e-8)
		{
			float slope = sqrt(length2);
			towardShore = gradient / slope;
			confidence = saturate((slope - 0.35) / 0.4);
		}
	}
	return confidence;
}

/// <summary>The direction to the nearest shore, as a unit vector; zero where there is none.</summary>
float2 FishWaterShoreGradient(float2 xz)
{
	float2 towardShore;
	FishWaterShoreFacing(xz, towardShore);
	return towardShore;
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
	// Which way the beach lies, and how sure that is. Zero out at sea, where there is no shore to roll toward.
	float2 shoreward;
	float shoreFacing = FishWaterShoreFacing(flatPositionWS.xz, shoreward);

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
		/* Green's law, from where it applies: a wave keeps its height (it dips a few per cent) until
		 * the water is about a twentieth of its deep-water wavelength deep, and only then grows as
		 * depth^-1/4. The reference was half the middle cascade's tile, 59 m, so every wave grew from
		 * 59 m of water — chop over a shoal 10 m down came out half as tall again as it should. The
		 * sea's deep-water wavelength is _ShoreWaveLength (the environment sets it from the peak
		 * period; 66 m at 8 m/s, so growth starts at about 3 m). */
		float reference = max(0.5, 0.05 * max(4.0, _ShoreWaveLength));
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
		float distance = FishWaterShoreDistance(flatPositionWS.xz);

		/* ONE WAVE WITH THE SWASH.
		 *
		 * With a shore keeping time, the train runs on the shore's clock and period, and takes its
		 * phase from the waterline this point faces — the phase the swash there runs on — so each
		 * crest reaches the edge at the instant its swash begins to rush up the beach. They used to
		 * keep separate clocks, and a wave rolled in, died at the waterline, and the run-up came at
		 * some other moment: nothing ever broke onto the shore.
		 *
		 * And at the surf zone's own speed. Waves slow as the water shallows, to the shallow-water
		 * speed sqrt(g·h); where these break, h is about H/0.78. Run at the deep-water speed, as it
		 * was, the surf raced in at ten metres a second with its lines forty metres apart; at the
		 * breaking depth's speed they come in at four or five and close up as they do on a beach.
		 * The deep-water wavelength still decides where a wave first feels the bottom and how it
		 * breaks: both are defined on it. */
		float deepWavelength;
		float wavelength;
		float phase;
		if (FishWaterSurfKeepsTime())
		{
			float period = max(0.5, _FishWaterSwashPeriod);
			deepWavelength = max(4.0, _FishWaterSwashSea.y);
			float breakingDepth = max(0.1, _FishWaterSwashSea.x) / 0.78;
			wavelength = max(4.0, period * sqrt(max(0.05, _FishWaterGravity) * breakingDepth));
			float2 waterline = flatPositionWS.xz + shoreward * max(0.0, distance);
			// A crest (sin = 1) at the waterline when the shore's cycle there is a whole number.
			phase = 6.2831853 * (FishWaterSurfCycles(waterline) + distance / wavelength) + 1.5707963;
		}
		else
		{
			// No shore to keep time with: the sea's own clock, at the deep-water speed.
			deepWavelength = max(4.0, _ShoreWaveLength);
			wavelength = deepWavelength;
			float k0 = 6.2831853 / wavelength;
			phase = k0 * distance + sqrt(max(0.05, _FishWaterGravity) * k0) * _FishWaterTime;
		}
		float k = 6.2831853 / wavelength;

		/* The beach, measured over twenty metres across the shoreward direction: what decides both
		 * whether a surf train forms at all and how its waves break. */
		const float Span = 10.0;
		float beachSlope = abs(FishWaterSeabedDepth(flatPositionWS.xz - shoreward * Span)
			- FishWaterSeabedDepth(flatPositionWS.xz + shoreward * Span)) / (2.0 * Span);
		/* Only a cliff turns the train away — a face steeper than about one in 1.6, where the waves
		 * slap the rock and are thrown back. It faded from one in four, and a generated coast is
		 * steep: measured on Cov Viaduct, the surf zone's median slope is one in three, so that
		 * took most of the surf off most of the coast. A steep bank still has waves running at it;
		 * what it must not have is a barrel thrown into it, and the surging test below sees to that. */
		float reflective = smoothstep(0.6, 1.0, beachSlope);

		// A wave feels the bottom from about half its deep-water wavelength of depth.
		float feel = saturate(1.0 - surface.depth / (deepWavelength * 0.5));
		// Green's law, then the depth limit that breaks it — on a shore the sea actually reaches.
		float exposure = FishWaterShoreExposure(shoreward, shoreFacing);
		float amplitude = _ShoreWaveHeight * (1.0 + feel * 1.6) * feel * (1.0 - reflective) * exposure;
		/* The same breaking index as the rest of the sea (_ShoreBreak, crest height over depth). It
		 * was 0.78 of the depth for this train's half-height — a wave twice as tall as the water can
		 * carry, since the classic limit, 0.78, is for the WHOLE height, crest to trough. */
		float limit = max(0.0, surface.depth) * _ShoreBreak;
		float breaking = saturate((amplitude - limit) / max(0.05, amplitude));
		amplitude = min(amplitude, limit);
		// Gone by the waterline; the shore pass owns everything past it.
		amplitude *= saturate(surface.depth * 1.2);

		float s, c;
		sincos(phase, s, c);
		float crest = saturate(s);

		displacement.y += amplitude * s;

		/* Whether it plunges is decided by the BEACH, not by a slider.
		 *
		 * The Iribarren number, ξ = tan β / sqrt(H/L0), separates the breakers of a real surf zone:
		 * below about 0.5 the wave SPILLS, its crest crumbling down its own face; between 0.5 and 3.3
		 * it PLUNGES, throwing its lip out into a barrel; past about 3.3 it SURGES, running up the
		 * face unbroken and sliding back — so a steep bank is never hit by a barrel of water thrown
		 * horizontally into it. */
		float waveHeight = max(0.05, amplitude * 2.0);
		float iribarren = beachSlope / sqrt(max(1e-4, waveHeight / deepWavelength));
		float plunging = smoothstep(0.35, 0.9, iribarren) * (1.0 - smoothstep(2.5, 3.5, iribarren));
		float spilling = 1.0 - smoothstep(0.35, 0.9, iribarren);

		/* The lean: a forward SHEAR of the crest, the mechanism that cusps a Gerstner crest — thrown
		 * past the face in front of it, the crest overhangs the trough, which is a plunging breaker
		 * drawn rather than faked. Only on the crest (cubed, so the lip goes over and the base stays)
		 * and only once the wave is breaking.
		 *
		 * CALIBRATED so the slider means something. For a throw T·crest³ the front face stretches
		 * by 1 - 3·T·k·s²·c, and s²·c peaks at 0.385: the face stands vertical at T = 0.138 of a
		 * wavelength and overhangs past it. The throw used to be 0.11 of a wavelength times the
		 * slider, whose 0.7 peaked at 56% of vertical on ANY wavelength — which is why nothing ever
		 * barrelled. Now a lean of 1 is exactly vertical: a plunging wave is thrown past it by
		 * _ShoreWavePitch, a spilling crest leans a third of the way — the forward roll of its white
		 * water, not a curl — and a surging one not at all. */
		float lean = _ShoreWavePitch * plunging + 0.33 * spilling;
		float throwMetres = lean * breaking * crest * crest * crest * wavelength * 0.1378 * shoreFacing;
		/* Never past the water's edge — nor more than halfway there. A lip is thrown ahead of its own
		 * crest, not across the beach: around a small island the throw of every crest in the ring
		 * closing on it, several metres, is more than the island is across, and the water from all
		 * sides was thrown into its middle and out the other side — the surface crossed itself in a
		 * star. Measured on a one-texel island of Cov Viaduct the surface folded thirty-six times over;
		 * capped here, it no longer folds at all. Half the distance keeps the stretch across the
		 * crest at least one half, however tightly the shore curves. */
		throwMetres = min(throwMetres, 0.5 * max(0.0, distance));
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
