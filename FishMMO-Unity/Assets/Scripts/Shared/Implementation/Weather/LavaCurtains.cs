using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// Curtains of fire (2026-10-25, phase I2r "molten ground", decision 65 (d) and 93): lava fountains standing along a fissure, a
	/// stretch of each stored vent line of a biome whose vents lie on Fissures and whose template names fountain episodes
	/// (<see cref="BiomeTemplate.FountainEpisodes"/>), playing on THE slot function (<see cref="BiomeFountainEpisodes.At"/>, I6i's
	/// <see cref="EpisodeSlots"/>): every stretch its own seed, so a fissure erupts in sections, each rising, holding and dying away on
	/// the shared clock. Presentation only (the client's plume presenter draws them; nothing on the wire, no damage), and a pure
	/// function of the clock: every player sees the same section in the same fountain at the same moment.
	/// </summary>
	public static class LavaCurtains
	{
		/// <summary>The longest stretch one curtain stands along, m (a longer segment is cut into stretches, each its own seed).</summary>
		public const float StretchMetres = 150f;
		/// <summary>A curtain weaker than this is not drawn.</summary>
		public const float Faintest = 0.01f;
		/// <summary>The salt a stretch's seed is mixed with.</summary>
		public const uint Salt = 0x6C0F7A11u;

		/// <summary>One curtain now: its stretch's ends (world xz), its strength (0..1) and height (m), its seed and biome.</summary>
		public struct Curtain
		{
			public Vector2 A, B;
			public float Strength;
			public float Height;
			public uint Seed;
			/// <summary>The template id of the biome whose fissure it stands on.</summary>
			public int BiomeID;

			/// <summary>Its middle, world xz.</summary>
			public Vector2 Middle => 0.5f * (A + B);
		}

		/// <summary>Whether a stored vent line can hold curtains: a fissure's segment (not a pad) of a biome with fountain episodes.</summary>
		public static bool Holds(in VentLine line, in BiomeFountainEpisodes schedule)
		{
			return line.Layout == BiomeVentLayout.Fissures && !line.IsPad && !schedule.IsEmpty;
		}

		/// <summary>A stretch's seed: its line's, mixed with how far along the line it starts (whole metres).</summary>
		public static uint StretchSeed(uint lineSeed, float alongMetres)
		{
			return TurbidityEpisodes.Mix(lineSeed ^ Salt, unchecked((uint)Mathf.RoundToInt(alongMetres)));
		}

		/// <summary>A curtain's height at an episode strength, m: the schedule's low to its high fountain.</summary>
		public static float HeightOf(in BiomeFountainEpisodes schedule, float strength)
		{
			Vector2 h = schedule.Heights;
			float low = Mathf.Max(1f, Mathf.Min(h.x, h.y)), high = Mathf.Max(low, Mathf.Max(h.x, h.y));
			return low + (high - low) * Mathf.Clamp01(strength);
		}

		/// <summary>The launch speed that throws a clot <paramref name="height"/> metres up under <paramref name="gravity"/>, m/s.</summary>
		public static float LaunchSpeed(float height, float gravity) => Mathf.Sqrt(2f * Mathf.Max(0.01f, gravity) * Mathf.Max(0f, height));

		/// <summary>
		/// The clots' fixed flight period, s: a full-height clot's time aloft, snapped to a whole divisor of the shared clock's 10 000 s
		/// wrap, so the clots meet themselves across it. Fixed per schedule: the arcs scale with the episode, their pace never changes.
		/// </summary>
		public static float FlightPeriod(in BiomeFountainEpisodes schedule, float gravity)
		{
			float g = Mathf.Max(0.01f, gravity);
			float flight = 2f * LaunchSpeed(HeightOf(in schedule, 1f), g) / g;
			int cycles = Mathf.Max(1, Mathf.RoundToInt(BiomeGroundGlow.WrapSeconds / Mathf.Max(0.5f, flight)));
			return BiomeGroundGlow.WrapSeconds / (float)cycles;
		}

		/// <summary>
		/// Every curtain of one vent line now (<paramref name="seconds"/> the motion clock, wrapped at 10 000 s), weaker than
		/// <see cref="Faintest"/> left out, into <paramref name="into"/>; nothing for a line that holds none.
		/// </summary>
		public static void Of(in VentLine line, in BiomeFountainEpisodes schedule, float seconds, List<Curtain> into)
		{
			if (!Holds(in line, in schedule))
			{
				return;
			}
			Vector2 d = line.B - line.A;
			float length = d.magnitude;
			if (length < 1f)
			{
				return;
			}
			int stretches = Mathf.Max(1, Mathf.CeilToInt(length / StretchMetres));
			for (int k = 0; k < stretches; k++)
			{
				float t0 = k / (float)stretches, t1 = (k + 1) / (float)stretches;
				uint seed = StretchSeed(line.Seed, line.AlongA + length * t0);
				float strength = BiomeFountainEpisodes.At(in schedule, seconds, seed);
				if (strength < Faintest)
				{
					continue;
				}
				into.Add(new Curtain
				{
					A = line.A + d * t0,
					B = line.A + d * t1,
					Strength = strength,
					Height = HeightOf(in schedule, strength),
					Seed = seed,
					BiomeID = line.BiomeID,
				});
			}
		}

		/// <summary>
		/// Every curtain of a scene's stored vent lines now, each line's schedule its biome template's (<paramref name="scheduleOf"/> by
		/// template id; an empty one for none): into <paramref name="into"/>, cleared first.
		/// </summary>
		public static void Of(IReadOnlyList<VentLine> lines, System.Func<int, BiomeFountainEpisodes> scheduleOf, float seconds, List<Curtain> into)
		{
			into.Clear();
			if (lines == null || scheduleOf == null)
			{
				return;
			}
			foreach (VentLine line in lines)
			{
				if (line.Layout != BiomeVentLayout.Fissures || line.IsPad)
				{
					continue;
				}
				BiomeFountainEpisodes schedule = scheduleOf(line.BiomeID);
				Of(in line, in schedule, seconds, into);
			}
		}
	}
}
