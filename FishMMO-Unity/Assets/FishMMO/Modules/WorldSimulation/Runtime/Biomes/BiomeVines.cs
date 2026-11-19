using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>One vine species a biome grows and its share of the vines there (<see cref="BiomeVines.Species"/>).</summary>
	[Serializable]
	public struct BiomeVineSpecies
	{
		[Tooltip("The vine: Ivy, Creeper or Liana (the generated vine art's species).")]
		public string Species;
		[Tooltip("Its weight among the biome's species: the share of its vines that are this one.")]
		[Min(0f)] public float Weight;

		public BiomeVineSpecies(string species, float weight)
		{
			Species = species;
			Weight = weight;
		}

		public override string ToString() => $"{Species} {Weight:0.##}";
	}

	/// <summary>
	/// Which vines grow in a biome and how thickly at each kind of place they grow on (<see cref="BiomeTemplate.Vines"/>): a
	/// cave's mouth, the walls of a ruin or an old building, cliff crags (and the rock walls of a waterfall), and the trees a
	/// vine climbs.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 the scene cut read this from a table keyed by the biome's name (VineBiomes); the biome art authoring
	/// now writes that table's row onto every template, so a re-cut grows the same vines until a template is changed. No
	/// species: the biome grows no vines anywhere (deserts, ice, the sea floor, the airless plains).
	/// </remarks>
	[Serializable]
	public class BiomeVines
	{
		[Tooltip("How often a natural cave's mouth here is overgrown, and how thickly, 0..1.")]
		[Range(0f, 1f)] public float Caves;
		[Tooltip("The chance a wall face of a ruin or an old building here carries a vine, 0..1.")]
		[Range(0f, 1f)] public float Ruins;
		[Tooltip("The share of cliff crags here with a curtain hanging off them, 0..1 (doubled on a waterfall's wet rock).")]
		[Range(0f, 1f)] public float Cliffs;
		[Tooltip("The share of the trees a vine can climb here that carry one, 0..1.")]
		[Range(0f, 1f)] public float Trees;
		[Tooltip("The species that grow here and their weights. Empty: no vines in this biome.")]
		public List<BiomeVineSpecies> Species = new List<BiomeVineSpecies>();

		/// <summary>True when the biome grows a vine at all: a species with a weight.</summary>
		public bool Grows
		{
			get
			{
				if (Species != null)
				{
					foreach (BiomeVineSpecies s in Species)
					{
						if (!string.IsNullOrEmpty(s.Species) && s.Weight > 0f)
						{
							return true;
						}
					}
				}
				return false;
			}
		}

		/// <summary>True when nothing is set: no density and no species (what a template never authored holds).</summary>
		public bool IsEmpty => Caves == 0f && Ruins == 0f && Cliffs == 0f && Trees == 0f && (Species == null || Species.Count == 0);

		/// <summary>A copy (the authoring writes copies, so a template never shares a list with the spec).</summary>
		public BiomeVines Clone()
		{
			return new BiomeVines
			{
				Caves = Caves,
				Ruins = Ruins,
				Cliffs = Cliffs,
				Trees = Trees,
				Species = Species != null ? new List<BiomeVineSpecies>(Species) : new List<BiomeVineSpecies>(),
			};
		}

		/// <summary>Every value as text (densities and weights to 1/10 000), for the authoring's mark and its report.</summary>
		public string Signature()
		{
			var text = new System.Text.StringBuilder(96);
			text.Append(Q(Caves)).Append(',').Append(Q(Ruins)).Append(',').Append(Q(Cliffs)).Append(',').Append(Q(Trees));
			if (Species != null)
			{
				foreach (BiomeVineSpecies s in Species)
				{
					text.Append('|').Append(s.Species ?? "<none>").Append(':').Append(Q(s.Weight));
				}
			}
			return text.ToString();
		}

		private static long Q(float value) => (long)Math.Round(value * 10000.0);
	}
}
