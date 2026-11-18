using System;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What a vent's timing reads of the sky at a moment (2026-10-25, phase I2q-b): the shared motion clock and the sky's state the
	/// glows read (SceneGlowSky, itself a pure function of the world clock at the scene), so a vent and the glow beside it move as one.
	/// </summary>
	public struct VentSky
	{
		/// <summary>Seconds of the shared motion clock (WorldMotion.Seconds), unwrapped, in double.</summary>
		public double Seconds;
		/// <summary>The moon's way round its orbit, a fraction of a turn (SceneGlowSky.OrbitTurn; the 33 h fallback for a planet).</summary>
		public float OrbitTurn;
		/// <summary>The main sun's height, the sine of its elevation.</summary>
		public float SunHeight;
		/// <summary>The tidal stress's rotating axis (cos 2φ, sin 2φ): <c>_FishTidalStress</c>.</summary>
		public Vector2 TidalStress;

		/// <summary>The sky the glows read, at a moment of the motion clock.</summary>
		public static VentSky Of(double seconds, in SceneGlowSky sky)
		{
			Vector4 stress = sky.TidalStress;
			return new VentSky { Seconds = seconds, OrbitTurn = sky.OrbitTurn, SunHeight = sky.SunHeight, TidalStress = new Vector2(stress.x, stress.y) };
		}

		/// <summary>The motion clock wrapped to the shaders' 10 000 s, as the slot functions take it (every slot length divides it).</summary>
		public float Wrapped => (float)Celestial.WorldMotion.Repeat(Seconds, Celestial.WorldMotion.ShaderWrapSeconds);
	}

	/// <summary>
	/// How strong a vent is at a moment (2026-10-25, phase I2q-b; decisions 53, 66, 90, 93, 95): its biome's
	/// <see cref="BiomeVentTiming"/>, a pure function of the shared clock and the sky's state, in double precision where it reads
	/// days, every slot length a divisor of the motion clock's 10 000 s wrap, so a vent is the same for every player and continuous
	/// across the wrap. PRESENTATION ONLY: the weather's plumes and fallout, which the server also works out, never read it; any later
	/// hazard must call these same functions on the server. 0..1, 1 for <see cref="BiomeVentTiming.Steady"/>.
	/// </summary>
	public static class VentTiming
	{
		/// <summary>A jet's flicker slots under the plume tide, seconds (each divides 10 000; the draft's 600–2400 s), by its seed.</summary>
		public static readonly int[] FlickerSlots = { 625, 1000, 1250, 2000, 2500 };

		/// <summary>The share of its slot a jet plays (55–90 %), its ramps, seconds; the Great Plume's floor (decision 53).</summary>
		public const float FlickerLeast = 0.55f, FlickerMost = 0.9f, FlickerRamp = 20f, GreatPlumeFloor = 0.4f;

		/// <summary>The sun's elevations a Solar vent wakes and is full at (decision 66: 8° → 35°), as sines.</summary>
		public static readonly float SolarWake = Mathf.Sin(8f * Mathf.Deg2Rad), SolarFull = Mathf.Sin(35f * Mathf.Deg2Rad);

		/// <summary>A Solar plume's life slots, its shortest and longest life and its ramps, seconds (decision 66: 3–12 days on, then dormant).</summary>
		public const double LifeSlotSeconds = 16.0 * 86400.0, ShortestLife = 3.0 * 86400.0, LongestLife = 12.0 * 86400.0, LifeRamp = 6.0 * 3600.0;

		/// <summary>The quiet degassing between a Strombolian vent's bursts, and between a fountain's episodes, as a share of full.</summary>
		public const float BurstQuiet = 0.1f, EpisodeQuiet = 0.1f;

		/// <summary>The kneading's floor (as the Kneading glow's, SceneGlowModes).</summary>
		public const float KneadingFloor = 0.2f;

		/// <summary>Salts for a jet's flicker and a plume's life draws.</summary>
		public const uint FlickerSalt = 0xF11C4Eu, LifeSalt = 0x11FE5u;

		/// <summary>
		/// A vent's strength now, 0..1. <paramref name="seed"/> is the vent's own (its draws), <paramref name="bearing"/> its crack's
		/// direction (radians, for the kneading), <paramref name="episodes"/> its biome's fountain episodes (empty: the volcanic
		/// default) and <paramref name="great"/> whether it is the scene's Great Plume (decision 53: never under 0.4 with the tide).
		/// </summary>
		public static float Strength(BiomeVentTiming timing, in VentSky sky, uint seed, float bearing, in BiomeFountainEpisodes episodes, bool great = false)
		{
			switch (timing)
			{
				case BiomeVentTiming.PlumeTide:
				{
					float tide = SceneGlowModes.PlumeTideOf(sky.OrbitTurn);
					float s = tide * Flicker(sky.Wrapped, seed);
					return great ? Mathf.Max(GreatPlumeFloor, s) : s;
				}
				case BiomeVentTiming.Solar:
					return Sun(sky.SunHeight) * Life(sky.Seconds, seed);
				case BiomeVentTiming.Strombolian:
					return Mathf.Max(BurstQuiet, VentBursts.At(sky.Wrapped, seed));
				case BiomeVentTiming.Episode:
				{
					BiomeFountainEpisodes schedule = episodes.IsEmpty ? BiomeFountainEpisodes.Volcanic : episodes;
					return EpisodeQuiet + (1f - EpisodeQuiet) * BiomeFountainEpisodes.At(in schedule, sky.Wrapped, seed);
				}
				case BiomeVentTiming.Kneading:
					return Kneading(sky.TidalStress, bearing);
				default:
					return 1f;
			}
		}

		/// <summary>A jet's flicker (decision 53): one "on" span a slot of its own length, covering 55–90 % of it, with 20 s ramps (EpisodeSlots).</summary>
		public static float Flicker(float wrappedSeconds, uint seed)
		{
			uint h = TurbidityEpisodes.Mix(seed, FlickerSalt);
			int length = FlickerSlots[(int)(h % (uint)FlickerSlots.Length)];
			float share = FlickerLeast + (FlickerMost - FlickerLeast) * (float)TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, seed));
			return EpisodeSlots.At(wrappedSeconds, length, share * length, FlickerRamp, h);
		}

		/// <summary>The sun's part of a Solar vent (decision 66): nothing under 8° of elevation, full over 35°, smooth between.</summary>
		public static float Sun(float sunHeight) => SceneGlowModes.Smooth((sunHeight - SolarWake) / (SolarFull - SolarWake));

		/// <summary>
		/// A Solar plume's life (decision 66): the world cut into <see cref="LifeSlotSeconds"/> slots of the shared clock (in double), each
		/// holding one life of 3–12 days at a hashed start that ends inside the slot, ramped over six hours; dormant between.
		/// </summary>
		public static float Life(double seconds, uint seed)
		{
			if (double.IsNaN(seconds) || double.IsInfinity(seconds))
			{
				return 0f;
			}
			long slot = (long)Math.Floor(seconds / LifeSlotSeconds);
			uint h = TurbidityEpisodes.Mix((uint)slot ^ (uint)(slot >> 32) * 0x27D4EB2Fu, seed ^ LifeSalt);
			double life = ShortestLife + (LongestLife - ShortestLife) * TurbidityEpisodes.Unit(h);
			double start = slot * LifeSlotSeconds + TurbidityEpisodes.Unit(TurbidityEpisodes.Mix(h, seed)) * (LifeSlotSeconds - life);
			double into = seconds - start;
			if (into <= 0.0 || into >= life)
			{
				return 0f;
			}
			return SceneGlowModes.Smooth((float)(into / LifeRamp)) * SceneGlowModes.Smooth((float)((life - into) / LifeRamp));
		}

		/// <summary>The tidal kneading (decision 90): the stress across a crack of this bearing, as the Kneading glow reads it.</summary>
		public static float Kneading(Vector2 tidalStress, float bearing)
		{
			float stress = 0.5f + 0.5f * (tidalStress.x * Mathf.Cos(2f * bearing) + tidalStress.y * Mathf.Sin(2f * bearing));
			return KneadingFloor + (1f - KneadingFloor) * stress * stress;
		}

		/// <summary>The crack bearing a vent off any line takes at world (x, z): the Kneading glow's own field (SceneGlowModes), so the two agree.</summary>
		public static float FieldBearing(float x, float z)
			=> Mathf.PI * (0.5f + 0.5f * Mathf.Sin(SceneGlowModes.Turn * SceneGlowModes.Fraction(x * SceneGlowModes.CrackX + z * SceneGlowModes.CrackZ)));
	}
}
