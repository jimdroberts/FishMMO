using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How one source of a biome's cold glows moves (<see cref="BiomeTemplate.GlowModes"/>, 2026-10-25, I6i): its way glows
	/// (glowjars, rune stones) or its glowing crystals (cave crystals, a crystal post's crystal), each by a <see cref="SceneGlowMode"/>,
	/// warm or cold. Read at runtime by the client from the template under each glow (the GPU's glow table, FishGlowModes.hlsl, and
	/// the site lights), so a mode named on a template needs no re-cut. A source with no entry glows exactly as before.
	/// </summary>
	[Serializable]
	public struct BiomeGlowMode
	{
		[Tooltip("Which glows: Way (glowjars, rune stones and their lights) or Crystal (glowing cave crystals, a crystal post's crystal and their lights).")]
		public SceneGlowSource Source;
		[Tooltip("How their brightness moves, a pure function of the shared clock (and the sky's or the weather's state where the mode names it). Default: as before (a way glow pulses, a stormglass flickers, a crystal shines steady).")]
		public SceneGlowMode Mode;
		[Tooltip("A warm tint (firelight's colour at the glow's own brightness) instead of the cold one.")]
		public bool Warm;
		[Tooltip("Step and DayCount only: the count of days (1–16); 0 the mode's own (Step 13, DayCount 8).")]
		[Range(0, SceneGlowModes.MaxEvery)] public int Every;
		[Tooltip("Episode only: the fountain-episode schedule its brightness follows; empty, the biome's own fountain episodes (else the volcanic default).")]
		public BiomeFountainEpisodes Episodes;

		public BiomeGlowMode(SceneGlowSource source, SceneGlowMode mode, bool warm = false, int every = 0)
		{
			Source = source;
			Mode = mode;
			Warm = warm;
			Every = every;
			Episodes = default;
		}

		/// <summary>Whether the entry changes anything: a mode other than Default, or the warm tint.</summary>
		public bool Moves => Mode != SceneGlowMode.Default || Warm;

		/// <summary>The entry as the authoring marks and reports it: "Source=Mode[+warm][/every][@episodes]".</summary>
		public override string ToString() => Source + "=" + Mode + (Warm ? "+warm" : string.Empty)
			+ (Every > 0 ? "/" + Every.ToString(CultureInfo.InvariantCulture) : string.Empty)
			+ (Episodes.IsEmpty ? string.Empty : "@" + Episodes);
	}

	/// <summary>The glow-mode lists' rules (pure).</summary>
	public static class BiomeGlowModes
	{
		/// <summary>No entries: every glow as before. What every template and spec entry had until a round names one.</summary>
		public static List<BiomeGlowMode> None() => new List<BiomeGlowMode>();

		/// <summary>A list's entry for a source (the first naming it, if it moves anything); false when none does.</summary>
		public static bool TryFor(IReadOnlyList<BiomeGlowMode> list, SceneGlowSource source, out BiomeGlowMode entry)
		{
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					if (list[i].Source == source && list[i].Moves)
					{
						entry = list[i];
						return true;
					}
				}
			}
			entry = default;
			return false;
		}

		/// <summary>
		/// The entry a source of a biome resolves to: the biome's own; else the world-level list (a cloud world's body, 2026-10-25,
		/// <see cref="Celestial.CloudWorldStructures.GlowModes"/>); an Episode entry with no schedule takes the biome's fountain
		/// episodes (<paramref name="fountains"/>), else the volcanic default. False: as before.
		/// </summary>
		public static bool Resolve(IReadOnlyList<BiomeGlowMode> own, IReadOnlyList<BiomeGlowMode> world, in BiomeFountainEpisodes fountains,
			SceneGlowSource source, out BiomeGlowMode entry)
		{
			if (!TryFor(own, source, out entry) && !TryFor(world, source, out entry))
			{
				return false;
			}
			if (entry.Mode == SceneGlowMode.Episode && entry.Episodes.IsEmpty)
			{
				entry.Episodes = fountains.IsEmpty ? BiomeFountainEpisodes.Volcanic : fountains;
			}
			return true;
		}
	}

	/// <summary>
	/// A biome's lava-fountain episodes (2026-10-25, I6i; decision 93, the Volcanic round): its fissure fountains play in episodes, each
	/// fountain on one of up to three slot lengths (2000, 2500, 5000 s by default: all divide the motion clock's 10 000 s wrap), on for
	/// a share of its slot with ramps either side, at a hashed moment of each slot. ONE slot function (<see cref="At"/>) shared by the
	/// glows that follow it (Glow EPISODE) and the later fountain effect (I2r d), a pure function of the shared motion clock and the
	/// fountain's (or glow's) seed, so every player sees the same fountain rise at the same instant. Empty (all zero) = no episodes:
	/// every template's default.
	/// </summary>
	[Serializable]
	public struct BiomeFountainEpisodes
	{
		[Tooltip("The slot lengths, seconds (each should divide 10 000: 2000, 2500, 5000…); 0 = unused. A fountain takes one by its seed.")]
		public int SlotA, SlotB, SlotC;
		[Tooltip("The share of its slot a fountain plays (0.1–0.25 for Hawaiian rift fountains).")]
		[Range(0f, 1f)] public float OnShare;
		[Tooltip("How long a fountain takes to rise and to die back, seconds.")]
		[Min(0f)] public float RampSeconds;
		[Tooltip("The height its jets reach at the height of an episode, metres (min..max by the fountain's seed): for the fountain effect.")]
		public Vector2 Heights;

		/// <summary>The Volcanic draft's schedule: slots 2000/2500/5000 s, on 18 %, 30 s ramps, jets 20–300 m.</summary>
		public static BiomeFountainEpisodes Volcanic => new BiomeFountainEpisodes { SlotA = 2000, SlotB = 2500, SlotC = 5000, OnShare = 0.18f, RampSeconds = 30f, Heights = new Vector2(20f, 300f) };

		/// <summary>Whether it names no slot (no episodes).</summary>
		public bool IsEmpty => SlotA <= 0 && SlotB <= 0 && SlotC <= 0;

		/// <summary>How many slot lengths it names (the non-zero ones, in order A, B, C).</summary>
		public int Count => (SlotA > 0 ? 1 : 0) + (SlotB > 0 ? 1 : 0) + (SlotC > 0 ? 1 : 0);

		/// <summary>The i-th named slot length (the non-zero ones, in order).</summary>
		public int Slot(int i)
		{
			if (SlotA > 0 && i-- == 0)
			{
				return SlotA;
			}
			if (SlotB > 0 && i-- == 0)
			{
				return SlotB;
			}
			return SlotC > 0 && i == 0 ? SlotC : 0;
		}

		/// <summary>Whether every named slot divides the motion clock's wrap (so an episode never straddles it).</summary>
		public bool DividesWrap
		{
			get
			{
				int wrap = (int)SceneGlow.WrapSeconds;
				return (SlotA <= 0 || wrap % SlotA == 0) && (SlotB <= 0 || wrap % SlotB == 0) && (SlotC <= 0 || wrap % SlotC == 0);
			}
		}

		/// <summary>
		/// THE slot function: a fountain's (or glow's) episode strength at a moment of the shared motion clock (seconds, wrapped at
		/// <see cref="SceneGlow.WrapSeconds"/>), 0..1. The seed picks the slot length (seed mod count) and, slot by slot, the moment its
		/// episode starts (a hash of the slot's index and the seed), early enough that the episode ends in its slot: every slot holds
		/// one, and is read alone, so the strength is continuous in time and across the wrap.
		/// </summary>
		public static float At(in BiomeFountainEpisodes schedule, float seconds, uint seed)
		{
			int count = schedule.Count;
			if (count == 0)
			{
				return 0f;
			}
			float length = schedule.Slot((int)(seed % (uint)count));
			return Weather.EpisodeSlots.At(seconds, length, Mathf.Clamp01(schedule.OnShare) * length, schedule.RampSeconds, seed);
		}

		/// <summary>The cell a glow takes its fountain seed from, metres: glows within one stir with the same fountain.</summary>
		public const float GlowCellMetres = 500f;

		/// <summary>A glow's seed by its place: its <see cref="GlowCellMetres"/> cell's hash (a stand-in for the nearest fountain's, until fountains are stored).</summary>
		public static uint GlowSeed(float x, float z)
		{
			int cx = (int)Math.Floor(x / GlowCellMetres), cz = (int)Math.Floor(z / GlowCellMetres);
			return Weather.TurbidityEpisodes.Mix((uint)cx * 0x8DA6B343u ^ (uint)cz * 0xD8163841u, 0xE9150DEu);
		}

		/// <summary>A glow's episode strength at world (x, z): <see cref="At"/> by its cell's seed.</summary>
		public static float GlowAt(in BiomeFountainEpisodes schedule, float seconds, float x, float z) => At(in schedule, seconds, GlowSeed(x, z));

		/// <summary>"A/B/C×share~ramp".</summary>
		public override string ToString() => SlotA.ToString(CultureInfo.InvariantCulture) + "/" + SlotB.ToString(CultureInfo.InvariantCulture) + "/"
			+ SlotC.ToString(CultureInfo.InvariantCulture) + "×" + OnShare.ToString("R", CultureInfo.InvariantCulture) + "~" + RampSeconds.ToString("R", CultureInfo.InvariantCulture)
			+ "^" + Heights.x.ToString("R", CultureInfo.InvariantCulture) + "-" + Heights.y.ToString("R", CultureInfo.InvariantCulture);
	}
}
