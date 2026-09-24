using UnityEngine;

namespace FishMMO.Water
{
	/// <summary>
	/// One Gerstner wave: a direction, a wavelength, and how much it leans.
	/// </summary>
	/// <remarks>
	/// Packed exactly as the shader reads it, so the array can be handed to the GPU with no
	/// conversion and the CPU can evaluate the same sum for buoyancy. A wave that the physics and
	/// the picture disagree about is a boat that floats a metre above the sea.
	/// </remarks>
	public struct WaterWave
	{
		/// <summary>Unit direction the wave travels, in world XZ.</summary>
		public Vector2 Direction;
		/// <summary>Crest to crest, in metres.</summary>
		public float Wavelength;
		/// <summary>Crest to trough, in metres — the full height, not the half.</summary>
		public float Amplitude;
		/// <summary>0 is a sine, 1 is a sharp crest. Above the sum's limit the surface folds.</summary>
		public float Steepness;
		/// <summary>Radians per metre.</summary>
		public float WaveNumber => 2f * Mathf.PI / Mathf.Max(0.01f, Wavelength);
		/// <summary>Radians per second, from deep-water dispersion.</summary>
		public float AngularFrequency => Mathf.Sqrt(Mathf.Max(0.05f, WaterWaves.Gravity) * WaveNumber);

		/// <summary>As the shader wants it: xy direction, z wave number, w amplitude.</summary>
		public Vector4 Packed => new Vector4(Direction.x, Direction.y, WaveNumber, Amplitude);
		/// <summary>Where this wave's GROUP envelope starts, in radians.</summary>
		/// <remarks>
		/// Without a per-wave offset every group in the sea peaks at the same moment and the whole
		/// surface breathes in unison, which is worse than having no groups at all.
		/// </remarks>
		public float GroupPhase;

		/// <summary>Second half: x angular frequency, y the steepness term Q, z the group phase.</summary>
		public Vector4 PackedMotion
		{
			get
			{
				float k = WaveNumber;
				// Q is the horizontal pull as a fraction of 1/(k·A): at 1 the crest is a cusp and
				// anything beyond it turns the surface inside out.
				float q = Amplitude > 1e-4f ? Steepness / (k * Amplitude) : 0f;
				return new Vector4(AngularFrequency, q, GroupPhase, 0f);
			}
		}
	}

	/// <summary>
	/// The sea state: a handful of Gerstner waves derived from a wind, evaluated identically on the
	/// CPU and in the shader.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Derived from wind, not authored per wave.</b> Four sliders somebody can reason about —
	/// how hard the wind blows, from where, how spread out the swell is, how sharp the crests — and
	/// the waves fall out of them. Hand-authored wave lists are how a sea ends up with two waves of
	/// the same wavelength beating against each other in a standing pattern that never moves.
	/// </para>
	/// <para>
	/// <b>Real gravity, real metres.</b> Deep-water waves travel at √(g/k), so a 60 m swell moves
	/// at 9.7 m/s and a 4 m chop at 2.5 m/s, and the long waves visibly overtake the short ones.
	/// That only holds because one world unit is one metre; at any other scale the dispersion is
	/// wrong and the sea moves like a bath.
	/// </para>
	/// </remarks>
	public static class WaterWaves
	{
		/// <summary>Earth's surface gravity, the default when no world says otherwise.</summary>
		public const float EarthGravity = 9.81f;

		/// <summary>
		/// The surface gravity the waves are built against, in m/s².
		/// </summary>
		/// <remarks>
		/// Driven from the celestial body, because it changes what the sea looks like more than any
		/// other single number. Deep-water waves travel at sqrt(g/k): at a sixth of a gravity the
		/// same swell moves at 40% of the speed and needs six times the wavelength to stand as
		/// tall, so a low-gravity moon has long, slow, lazy rollers and a heavy world has short
		/// steep chop. Left at 9.81 every world in the system has an identical sea.
		/// </remarks>
		public static float Gravity = EarthGravity;

		/// <summary>The most waves the shader will sum. Kept small: every one costs a sin and a cos per vertex.</summary>
		public const int MaximumWaves = 8;

		/// <summary>
		/// Builds a sea state.
		/// </summary>
		/// <param name="waves">Filled in; its length decides how many waves are made, up to <see cref="MaximumWaves"/>.</param>
		/// <param name="windDirectionDegrees">Which way the wind blows TOWARD, clockwise from +Z.</param>
		/// <param name="windSpeed">Metres per second. 5 is a breeze, 15 is a gale.</param>
		/// <param name="spread">0 every wave runs with the wind, 1 they fan across ±60°.</param>
		/// <param name="choppiness">0 rolling sine swell, 1 sharp crests.</param>
		/// <param name="scale">Multiplies every amplitude, for a calm bay or a storm.</param>
		public static int Build(WaterWave[] waves, float windDirectionDegrees, float windSpeed,
			float spread, float choppiness, float scale)
		{
			if (waves == null || waves.Length == 0)
			{
				return 0;
			}
			int count = Mathf.Min(waves.Length, MaximumWaves);

			/* The longest wave the wind can raise, from the fully-developed sea relation
			 * L = 2π·U²/g — a 10 m/s wind builds a 64 m swell. Shorter waves come from halving it
			 * repeatedly, which spaces them across the spectrum without any two landing together. */
			float longest = Mathf.Max(2f, 2f * Mathf.PI * windSpeed * windSpeed / Mathf.Max(0.05f, Gravity));
			float steepness = Mathf.Clamp01(choppiness);

			// The wind blows toward this; +Z is north, clockwise, to match every other heading in
			// the project.
			float radians = windDirectionDegrees * Mathf.Deg2Rad;
			var wind = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));

			/* How steep the sea gets, which is what decides whether it has white caps at all.
			 *
			 * The Jacobian of the Gerstner sum only goes near zero — a breaking crest — when the
			 * TOTAL steepness approaches one, and the total depends on nothing but this number. So
			 * if it did not move with the wind, a gale and a flat calm would break identically,
			 * which is the bug this replaces: the old code divided every wave by the sum of the
			 * steepnesses, which made the total exactly 1.0 for every setting and left the
			 * choppiness slider with no effect whatsoever.
			 *
			 * Whitecapping starts in the real world around 6 m/s and is general by 15.
			 */
			float windFactor = Mathf.Clamp01((windSpeed - 3f) / 12f);
			float targetSteepness = Mathf.Clamp(steepness * (0.2f + 0.8f * windFactor), 0f, 0.95f);

			float total = 0f;
			for (int i = 0; i < count; i++)
			{
				float t = count > 1 ? i / (float)(count - 1) : 0f;
				float wavelength = longest * Mathf.Pow(0.5f, i * 0.72f);

				/* Amplitude falls with wavelength, but not as fast as the wavelength does: a sea
				 * whose short waves are proportionally as tall as its swell is a field of spikes.
				 * The exponent is what keeps the crests of the small waves riding the big ones. */
				float amplitude = 0.021f * wavelength * Mathf.Pow(wavelength / longest, 0.25f);

				/* Fanned alternately either side of the wind, never symmetrically in pairs: two
				 * waves of one wavelength at mirrored angles interfere into a standing checker
				 * pattern that sits still while everything else moves. */
				float side = (i % 2 == 0 ? 1f : -1f) * (0.35f + 0.65f * t);
				float angle = side * spread * 60f * Mathf.Deg2Rad;
				float cos = Mathf.Cos(angle);
				float sin = Mathf.Sin(angle);
				var direction = new Vector2(wind.x * cos - wind.y * sin, wind.x * sin + wind.y * cos);

				waves[i] = new WaterWave
				{
					Direction = direction.normalized,
					Wavelength = wavelength,
					Amplitude = amplitude * scale,
					Steepness = amplitude * scale,
					// Spread by the golden angle so no two groups ever line up.
					GroupPhase = i * 2.39996f,
				};
				total += waves[i].Steepness;
			}

			/* Gerstner steepness is shared, not per wave: each wave pulls the surface horizontally
			 * by Q/k, and it is the SUM that has to stay under 1. Left per-wave, a choppiness of 1
			 * with eight waves overlaps them eightfold and the surface turns inside out — the
			 * classic Gerstner artefact where crests fold over and render as black creases.
			 *
			 * Shared out in proportion to amplitude, so the long swell carries most of the lean and
			 * the small chop rides on it, rather than every wavelength being equally cusped.
			 */
			if (total > 0f)
			{
				for (int i = 0; i < count; i++)
				{
					waves[i].Steepness = targetSteepness * waves[i].Steepness / total;
				}
			}
			return count;
		}

		/// <summary>
		/// Where a point of the flat sea ends up once the waves have moved it.
		/// </summary>
		/// <param name="waves">The sea state.</param>
		/// <param name="count">How many of them are in use.</param>
		/// <param name="position">The undisplaced point in world XZ, and the still-water level in Y.</param>
		/// <param name="time">Seconds.</param>
		/// <param name="normal">The surface normal there, from the analytic derivatives.</param>
		/// <remarks>
		/// The same arithmetic as the shader's vertex stage, deliberately written the same way. A
		/// Gerstner wave moves a point horizontally as well as vertically, so this is a
		/// displacement and not a height — which is why <see cref="SampleHeight"/> has to search
		/// for the point that LANDS where it was asked about, rather than evaluating there.
		/// </remarks>
		public static Vector3 Displace(WaterWave[] waves, int count, Vector3 position, float time, out Vector3 normal)
		{
			Vector3 displaced = position;
			// Derivatives of the displaced position with respect to the undisplaced x and z; the
			// normal is their cross product, which is exact rather than sampled.
			Vector3 tangent = new Vector3(1f, 0f, 0f);
			Vector3 binormal = new Vector3(0f, 0f, 1f);

			for (int i = 0; i < count; i++)
			{
				WaterWave wave = waves[i];
				float k = wave.WaveNumber;
				float a = wave.Amplitude;
				if (a <= 1e-5f)
				{
					continue;
				}
				float q = a > 1e-4f ? wave.Steepness / (k * a) : 0f;
				Vector2 d = wave.Direction;
				float phase = k * (d.x * position.x + d.y * position.z) - wave.AngularFrequency * time;
				float sin = Mathf.Sin(phase);
				float cos = Mathf.Cos(phase);

				displaced.x += q * a * d.x * cos;
				displaced.z += q * a * d.y * cos;
				displaced.y += a * sin;

				float wa = k * a;
				tangent.x -= q * wa * d.x * d.x * sin;
				tangent.y += wa * d.x * cos;
				tangent.z -= q * wa * d.x * d.y * sin;

				binormal.x -= q * wa * d.x * d.y * sin;
				binormal.y += wa * d.y * cos;
				binormal.z -= q * wa * d.y * d.y * sin;
			}

			normal = Vector3.Normalize(Vector3.Cross(binormal, tangent));
			if (normal.y < 0f)
			{
				normal = -normal;
			}
			return displaced;
		}

		/// <summary>
		/// The sea's height directly above or below a world position.
		/// </summary>
		/// <param name="waves">The sea state.</param>
		/// <param name="count">How many of them are in use.</param>
		/// <param name="worldPosition">Where to ask. Only XZ is read.</param>
		/// <param name="stillWaterLevel">Y of the flat sea.</param>
		/// <param name="time">Seconds.</param>
		/// <param name="iterations">How many times to refine. Three is within a centimetre.</param>
		/// <remarks>
		/// <b>An inverse, not an evaluation.</b> A Gerstner surface moves water sideways, so the
		/// point that ends up above a given XZ started somewhere else — evaluating the sum AT the
		/// query point answers about a different piece of water, and the error is the horizontal
		/// displacement, which at any real choppiness is metres. Fixed-point iteration converges
		/// quickly because the displacement is small compared to the wavelength; it is the standard
		/// solution and it is why buoyancy on a Gerstner sea is not simply a height lookup.
		/// </remarks>
		public static float SampleHeight(WaterWave[] waves, int count, Vector3 worldPosition,
			float stillWaterLevel, float time, int iterations = 3)
		{
			var guess = new Vector3(worldPosition.x, stillWaterLevel, worldPosition.z);
			for (int i = 0; i < iterations; i++)
			{
				Vector3 displaced = Displace(waves, count, guess, time, out _);
				guess.x += worldPosition.x - displaced.x;
				guess.z += worldPosition.z - displaced.z;
			}
			return Displace(waves, count, guess, time, out _).y;
		}
	}
}
