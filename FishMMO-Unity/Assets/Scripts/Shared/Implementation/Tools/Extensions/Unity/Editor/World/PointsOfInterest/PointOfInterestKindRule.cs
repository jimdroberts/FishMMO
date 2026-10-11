#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Whether a kind is found in the generated ground or budgeted and placed.</summary>
	public enum PointOfInterestPlacement : byte
	{
		/// <summary>Found in the data (a fall, a lake, a peak, a biome's heart); never budgeted.</summary>
		Detected,
		/// <summary>Sited on a scored grid against a budget per square kilometre.</summary>
		Placed,
	}

	/// <summary>How a detected kind is found.</summary>
	public enum PointOfInterestDetector : byte
	{
		None,
		Falls,
		Rapids,
		Rivers,
		Lakes,
		Springs,
		HotSprings,
		RiverMouths,
		Deltas,
		Peaks,
		Passes,
		Gorges,
		Mesas,
		Buttes,
		Islands,
		Bays,
		Headlands,
		Sinkholes,
		/// <summary>Connected ground of the rule's biomes, marked at the cell nearest its centroid.</summary>
		BiomeCluster,
		/// <summary>As <see cref="BiomeCluster"/>, marked at its highest cell (a volcano's summit).</summary>
		BiomeClusterHighest,
		/// <summary>As <see cref="BiomeCluster"/>, marked at its lowest cell (a lake of something other than water).</summary>
		BiomeClusterLowest,
	}

	/// <summary>One biome's weight for a kind, matched by the biome asset's name (case ignored).</summary>
	[Serializable]
	public struct PointOfInterestBiomeWeight
	{
		public string Biome;
		public float Weight;

		public PointOfInterestBiomeWeight(string biome, float weight)
		{
			Biome = biome;
			Weight = weight;
		}
	}

	/// <summary>
	/// What one point-of-interest kind needs and how many a scene gets: the catalogue's row for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Plain data so the planner stays pure and testable: the defaults are code
	/// (<see cref="PointOfInterestRules.Defaults"/>), a <see cref="PointOfInterestCatalogue"/> asset may replace any row,
	/// and a scene's <see cref="PointOfInterestSettings"/> may change a row's budget for that scene alone.
	/// </para>
	/// <para>
	/// <b>Budgets are per square kilometre at Normal density</b>, multiplied by the density preset and the scene's
	/// multiplier, then clamped to [<see cref="Min"/>, <see cref="Max"/>]. A Min is a wish, never a promise: a kind
	/// whose ground the scene does not have gets none.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class PointOfInterestKindRule
	{
		/// <summary>No altitude limit.</summary>
		public const float Unbounded = 100000f;

		public POIType Kind;
		public PointOfInterestPlacement Placement = PointOfInterestPlacement.Placed;
		public bool Enabled = true;

		/// <summary>Sites per km² at Normal density.</summary>
		[Header("Budget")]
		public float PerKm2;
		public int Min;
		public int Max = 4;
		/// <summary>Least distance between two sites of this kind, metres.</summary>
		public float SpacingMetres = 400f;

		/// <summary>Smallest and largest footprint radius, metres; each site draws its own between them.</summary>
		[Header("Footprint")]
		public float FootprintMin = 10f;
		public float FootprintMax = 20f;
		/// <summary>Steepest ground anywhere in the footprint, degrees.</summary>
		public float MaxSlopeDegrees = 22f;
		/// <summary>Most height difference across the footprint, metres; 0 derives it from the radius and slope.</summary>
		public float MaxReliefMetres;

		/// <summary>Altitude band of the site's anchor, metres above sea level.</summary>
		[Header("Ground")]
		public float MinAltitude = -Unbounded;
		public float MaxAltitude = Unbounded;
		/// <summary>On the sea floor: <see cref="MinDepth"/> … <see cref="MaxDepth"/> metres under sea level.</summary>
		public bool Underwater;
		public float MinDepth = 4f;
		public float MaxDepth = 40f;
		/// <summary>Hardest-rock requirement, 0 … 1 (geology hardness); 0 for none.</summary>
		public float MinHardness;
		/// <summary>Needs a steep, hard rock face with standable ground at its foot (caves, overhangs, mines).</summary>
		public bool Face;
		/// <summary>Stands where a river crosses the way between two settlements (a bridge).</summary>
		public bool RiverCrossing;

		/// <summary>Within this of a settlement, metres; 0 for no such rule. The site's parent is that settlement.</summary>
		[Header("Neighbours")]
		public float NearSettlementMetres;
		/// <summary>At least this far from every settlement, metres; 0 for no such rule.</summary>
		public float AwayFromSettlementsMetres;
		/// <summary>Within this of the sea's edge, metres; 0 for no such rule.</summary>
		public float CoastMetres;
		/// <summary>Within this of a river, a lake or the sea, metres; 0 for no such rule.</summary>
		public float NearWaterMetres;

		/// <summary>Weight of a biome the list does not name: 1 for a kind found anywhere, 0 for a biome-specific kind.</summary>
		[Header("Biomes")]
		public float DefaultBiomeWeight = 1f;
		public List<PointOfInterestBiomeWeight> Biomes = new List<PointOfInterestBiomeWeight>();

		/// <summary>RaceTemplate.Category values a site's race is drawn from, in preference; empty for any.</summary>
		[Header("Race")]
		public List<string> RaceCategories = new List<string>();

		[Header("Detection")]
		public PointOfInterestDetector Detector;
		/// <summary>Smallest biome cluster (or plateau, island) a detected kind marks, m².</summary>
		public float MinAreaM2;

		/// <summary>The weight this rule gives a biome, by its asset name; a later entry for the same biome wins.</summary>
		public float BiomeWeight(string biomeName)
		{
			float weight = DefaultBiomeWeight;
			if (!string.IsNullOrEmpty(biomeName))
			{
				foreach (PointOfInterestBiomeWeight entry in Biomes)
				{
					if (string.Equals(entry.Biome, biomeName, StringComparison.OrdinalIgnoreCase))
					{
						weight = entry.Weight;
					}
				}
			}
			return weight;
		}

		/// <summary>The relief a footprint may span: the stated one, else what its slope allows over half its radius.</summary>
		public float ReliefFor(float radius)
			=> MaxReliefMetres > 0f ? MaxReliefMetres : Mathf.Max(3f, radius * 0.5f * Mathf.Tan(MaxSlopeDegrees * Mathf.Deg2Rad));

		public PointOfInterestKindRule Clone()
		{
			var copy = (PointOfInterestKindRule)MemberwiseClone();
			copy.Biomes = new List<PointOfInterestBiomeWeight>(Biomes);
			copy.RaceCategories = new List<string>(RaceCategories);
			return copy;
		}

		// ── Fluent setup, for the defaults table ─────────────────────

		public PointOfInterestKindRule Footprint(float min, float max, float slope)
		{
			FootprintMin = min;
			FootprintMax = max;
			MaxSlopeDegrees = slope;
			return this;
		}

		/// <summary>Only in these biomes (weight 1 each, 0 elsewhere), replacing any weights set before.</summary>
		public PointOfInterestKindRule Only(params string[] biomes)
		{
			DefaultBiomeWeight = 0f;
			Biomes.Clear();
			foreach (string biome in biomes)
			{
				Biomes.Add(new PointOfInterestBiomeWeight(biome, 1f));
			}
			return this;
		}

		/// <summary>These biomes at <paramref name="weight"/>; the default weight unchanged.</summary>
		public PointOfInterestKindRule Weigh(float weight, params string[] biomes)
		{
			foreach (string biome in biomes)
			{
				Biomes.Add(new PointOfInterestBiomeWeight(biome, weight));
			}
			return this;
		}

		public PointOfInterestKindRule Races(params string[] categories)
		{
			RaceCategories.AddRange(categories);
			return this;
		}

		public PointOfInterestKindRule Altitude(float min, float max)
		{
			MinAltitude = min;
			MaxAltitude = max;
			return this;
		}

		public PointOfInterestKindRule Sea(float minDepth, float maxDepth)
		{
			Underwater = true;
			MinDepth = minDepth;
			MaxDepth = maxDepth;
			return this;
		}

		public PointOfInterestKindRule Hard(float hardness, bool face = false)
		{
			MinHardness = hardness;
			Face = face;
			return this;
		}

		public PointOfInterestKindRule NearSettlement(float metres)
		{
			NearSettlementMetres = metres;
			return this;
		}

		public PointOfInterestKindRule AwayFromSettlements(float metres)
		{
			AwayFromSettlementsMetres = metres;
			return this;
		}

		public PointOfInterestKindRule Coast(float metres)
		{
			CoastMetres = metres;
			return this;
		}

		public PointOfInterestKindRule NearWater(float metres)
		{
			NearWaterMetres = metres;
			return this;
		}

		public PointOfInterestKindRule Area(float m2)
		{
			MinAreaM2 = m2;
			return this;
		}
	}
}
#endif
