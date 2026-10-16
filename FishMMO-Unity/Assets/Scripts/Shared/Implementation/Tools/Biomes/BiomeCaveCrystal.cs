using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The crystals a biome's caves grow (<see cref="BiomeTemplate.CaveCrystals"/>, 2026-10-16, phase I6c): the mineral deposits
	/// in their chambers and, for the kinds that glow, what lines their ways. Append-only (stored by value); each kind is drawn
	/// in its own habit and material by the biome art generator (ProceduralArtCatalogue.CaveMinerals).
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 every cave and grotto grew selenite (blades of gypsum) wherever it was, each cluster with a cave light of
	/// a hue drawn by chance; that is every biome's default. An ice cave's crystals are its own ice (its shell's, as its
	/// formations are) and a lava tube grows none, whatever this list holds.
	/// </remarks>
	public enum BiomeCaveCrystalKind : byte
	{
		/// <summary>Selenite: milky gypsum blades with chisel tips, a cave light beside them (the old crystal of every cave).</summary>
		Selenite = 0,
		/// <summary>Amethyst: a broken geode lined with violet points.</summary>
		Amethyst = 1,
		/// <summary>Clear quartz: a druse of small water-clear points carpeting the rock.</summary>
		ClearQuartz = 2,
		/// <summary>Smoky quartz: a few stout brown-grey prisms out of a pegmatite pocket.</summary>
		SmokyQuartz = 3,
		/// <summary>Green fluorite: interlocking translucent green cubes.</summary>
		GreenFluorite = 4,
		/// <summary>Frost crystal: six-sided columns of clear ice (an ice cave's own crystal) in a cold land's caves.</summary>
		FrostCrystal = 5,
		/// <summary>Obsidian shards: glassy black blades and conchoidal splinters jutting from the rock.</summary>
		Obsidian = 6,
		/// <summary>Amberlight (fantasy): rounded nodules of glowing resin-amber in a rock bed.</summary>
		Amberlight = 7,
		/// <summary>Wyrdstone (fantasy): a mossy geode whose green-violet points glow from within.</summary>
		Wyrdstone = 8,
		/// <summary>Sunstone (fantasy, volcanic): tabular blades of orange-gold crystal alight like embers, out of scoria.</summary>
		Sunstone = 9,
	}

	/// <summary>One crystal a biome's caves grow and how often among its others (<see cref="BiomeTemplate.CaveCrystals"/>).</summary>
	[Serializable]
	public struct BiomeCaveCrystal
	{
		[Tooltip("The crystal: its habit (prisms, geodes, druses, blades, nodules…), its colour, and whether it glows.")]
		public BiomeCaveCrystalKind Kind;
		[Tooltip("How often a chamber's crystals are this kind among the biome's others (0 never).")]
		[Min(0f)] public float Weight;

		public BiomeCaveCrystal(BiomeCaveCrystalKind kind, float weight)
		{
			Kind = kind;
			Weight = weight;
		}

		/// <summary>The entry as the authoring marks and reports it: "Kind×weight".</summary>
		public override string ToString() => Kind + "×" + Weight.ToString("R", CultureInfo.InvariantCulture);
	}

	/// <summary>The cave-crystal rules that do not depend on a template asset.</summary>
	public static class BiomeCaveCrystals
	{
		/// <summary>What every biome's caves grew until 2026-10-16: selenite alone. A fresh list each call.</summary>
		public static List<BiomeCaveCrystal> Default() => new List<BiomeCaveCrystal> { new BiomeCaveCrystal(BiomeCaveCrystalKind.Selenite, 1f) };

		/// <summary>
		/// The kinds that glow by themselves (their own light, day and night): what lines a cave's ways when a biome grows them
		/// and what a crystal way-light post carries. The others glitter under a lamp; selenite keeps the cave light it always
		/// had beside it.
		/// </summary>
		public static bool Glows(BiomeCaveCrystalKind kind) => kind == BiomeCaveCrystalKind.Amberlight || kind == BiomeCaveCrystalKind.Wyrdstone || kind == BiomeCaveCrystalKind.Sunstone;

		/// <summary>The sum of a list's positive weights.</summary>
		public static float Total(IReadOnlyList<BiomeCaveCrystal> crystals, bool glowingOnly = false)
		{
			float total = 0f;
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Weight > 0f && (!glowingOnly || Glows(c.Kind)))
					{
						total += c.Weight;
					}
				}
			}
			return total;
		}

		/// <summary>
		/// The kind a roll in [0, 1) picks from a list by weight (entries of no weight never; only the glowing ones when
		/// <paramref name="glowingOnly"/>); false when nothing can be picked. Pure and deterministic: a list of one kind needs
		/// no roll at all (<see cref="NeedsRoll"/>).
		/// </summary>
		public static bool TryPick(IReadOnlyList<BiomeCaveCrystal> crystals, float roll, out BiomeCaveCrystalKind kind, bool glowingOnly = false)
		{
			kind = BiomeCaveCrystalKind.Selenite;
			float total = Total(crystals, glowingOnly);
			if (total <= 0f)
			{
				return false;
			}
			float at = Mathf.Clamp01(roll) * total, run = 0f;
			bool found = false;
			foreach (BiomeCaveCrystal c in crystals)
			{
				if (c.Weight <= 0f || glowingOnly && !Glows(c.Kind))
				{
					continue;
				}
				kind = c.Kind;
				found = true;
				run += c.Weight;
				if (at < run)
				{
					return true;
				}
			}
			return found;
		}

		/// <summary>Whether picking from the list needs a roll: more than one entry of weight (else the one kind, or none).</summary>
		public static bool NeedsRoll(IReadOnlyList<BiomeCaveCrystal> crystals, bool glowingOnly = false)
		{
			int count = 0;
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Weight > 0f && (!glowingOnly || Glows(c.Kind)) && ++count > 1)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// The crystal a biome's crystal way-light post carries: its heaviest glowing kind, else its heaviest kind (the first
		/// of equal weights), else selenite.
		/// </summary>
		public static BiomeCaveCrystalKind PostCrystal(IReadOnlyList<BiomeCaveCrystal> crystals)
		{
			BiomeCaveCrystalKind best = BiomeCaveCrystalKind.Selenite;
			float weight = 0f;
			bool glowing = false;
			if (crystals != null)
			{
				foreach (BiomeCaveCrystal c in crystals)
				{
					if (c.Weight <= 0f)
					{
						continue;
					}
					bool g = Glows(c.Kind);
					if (g && !glowing || g == glowing && c.Weight > weight)
					{
						best = c.Kind;
						weight = c.Weight;
						glowing = g;
					}
				}
			}
			return best;
		}
	}
}
