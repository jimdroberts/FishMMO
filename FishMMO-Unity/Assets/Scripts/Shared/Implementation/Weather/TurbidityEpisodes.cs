using System;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// A scene's turbidity episodes (2026-10-25, I6i; decision 92: a canyon's turbidity currents, the sea floor's slides): now and
	/// then, for a fixed while, the floor stirs. A pure function of the world clock and the scene, in double precision, so every
	/// player sees the same episode at the same moment, a rejoin lands in it, and the later turbidity effect (silt clouds down the
	/// canyon) reads exactly the same schedule as the glows that tremble with it (Glow TREMOR, <see cref="SceneGlowMode.Tremor"/>).
	/// </summary>
	/// <remarks>
	/// The world clock is cut into slots of <see cref="SlotSeconds"/>. A slot holds an episode with chance <see cref="Chance"/>
	/// (a hash of the slot and the scene's seed); its episode starts at a hashed moment within the slot, early enough that it ends in
	/// the same slot, so no episode ever straddles two slots and a slot is read alone. Each runs <see cref="DurationSeconds"/>:
	/// rising over <see cref="RiseSeconds"/>, then settling as the silt does (a slow quadratic fall to nothing at its end).
	/// </remarks>
	public static class TurbidityEpisodes
	{
		/// <summary>The schedule's slots, seconds of world time (four hours).</summary>
		public const double SlotSeconds = 14400.0;

		/// <summary>The chance a slot holds an episode.</summary>
		public const double Chance = 0.25;

		/// <summary>How long an episode runs, and how long it takes to rise, seconds.</summary>
		public const double DurationSeconds = 1800.0, RiseSeconds = 90.0;

		/// <summary>A scene's seed for the schedule: a stable hash of its name (the same on every machine).</summary>
		public static uint SeedOf(string sceneName)
		{
			uint h = 2166136261u;
			if (sceneName != null)
			{
				foreach (char c in sceneName)
				{
					h = (h ^ c) * 16777619u;
				}
			}
			return h ^ 0x7A2B1Du;
		}

		/// <summary>The slot holding a moment of world time.</summary>
		public static long SlotOf(double worldSeconds) => (long)Math.Floor(worldSeconds / SlotSeconds);

		/// <summary>Whether a slot holds an episode, and when it starts (world seconds) if it does.</summary>
		public static bool TryEpisode(uint seed, long slot, out double startSeconds)
		{
			uint a = Mix((uint)slot ^ (uint)(slot >> 32) * 0x27D4EB2Fu, seed);
			uint b = Mix(a, seed ^ 0x9E3779B9u);
			startSeconds = slot * SlotSeconds + Unit(b) * (SlotSeconds - DurationSeconds);
			return Unit(a) < Chance;
		}

		/// <summary>
		/// The episode's strength at a moment of world time: 0 between episodes, rising to 1 over <see cref="RiseSeconds"/>, then
		/// settling to 0 at its end. Continuous in time.
		/// </summary>
		public static float Strength(uint seed, double worldSeconds)
		{
			if (double.IsNaN(worldSeconds) || double.IsInfinity(worldSeconds))
			{
				return 0f;
			}
			if (!TryEpisode(seed, SlotOf(worldSeconds), out double start))
			{
				return 0f;
			}
			double into = worldSeconds - start;
			if (into <= 0.0 || into >= DurationSeconds)
			{
				return 0f;
			}
			double rise = Smooth(into / RiseSeconds);
			double settle = 1.0 - (into - RiseSeconds) / (DurationSeconds - RiseSeconds);
			settle = into <= RiseSeconds ? 1.0 : settle * settle;
			return (float)(rise * settle);
		}

		private static double Smooth(double t)
		{
			t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
			return t * t * (3.0 - 2.0 * t);
		}

		/// <summary>A 32-bit mix (lowbias32) of a value and a seed.</summary>
		public static uint Mix(uint v, uint seed)
		{
			uint h = v * 0x9E3779B1u ^ seed;
			h ^= h >> 16;
			h *= 0x7FEB352Du;
			h ^= h >> 15;
			h *= 0x846CA68Bu;
			h ^= h >> 16;
			return h;
		}

		/// <summary>A hash's top 24 bits as [0, 1).</summary>
		public static double Unit(uint h) => (h >> 8) * (1.0 / 16777216.0);
	}
}
