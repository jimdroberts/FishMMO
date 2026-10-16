#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How a template's pieces are laid out inside its footprint.</summary>
	public enum PointOfInterestLayout : byte
	{
		/// <summary>The first piece in the middle; the rest scattered round it.</summary>
		Single,
		/// <summary>Evenly round a ring, facing in (a stone circle, a camp round its fire).</summary>
		Ring,
		/// <summary>Rows and columns square to the site's heading (a farm, a graveyard).</summary>
		Grid,
		/// <summary>Two rows facing each other across a street along the heading.</summary>
		Street,
		/// <summary>Loosely spread, no two too close (ruins, a battlefield).</summary>
		Scattered,
		/// <summary>The first slot round the edge as a wall, facing out; the rest in rows inside.</summary>
		Walled,
		/// <summary>In a line along the heading (a road, a bridge's spans).</summary>
		Linear,
	}

	/// <summary>One kind of piece a template lays: a structure-kit tag, how many, and how worn.</summary>
	[Serializable]
	public class PointOfInterestPieceSlot
	{
		/// <summary>The structure kit's tag ("hut", "wall", "grave"); resolved by <see cref="PointOfInterestPieces"/>.</summary>
		public string Tag;
		[Min(0)] public int MinCount = 1;
		[Min(0)] public int MaxCount = 1;
		/// <summary>How ruined, 0 whole … 1 rubble: a worn piece may be left out, sunk or tilted.</summary>
		[Range(0f, 1f)] public float MinDecay;
		[Range(0f, 1f)] public float MaxDecay;
		[Min(0.01f)] public float MinScale = 1f;
		[Min(0.01f)] public float MaxScale = 1f;
		/// <summary>Where the layout puts this slot's pieces; Auto takes the template layout's default for the slot (<see cref="PointOfInterestLayouts.RoleFor"/>).</summary>
		public PointOfInterestSlotRole Role;
		/// <summary>The style this slot's pieces are resolved in ("stone" walls round a timber town); empty takes the site's.</summary>
		public string Style;
	}

	/// <summary>
	/// What a placed point of interest is built as: its pieces, their layout, and the features (spawners, a waypoint, a
	/// portal) it carries. Editor-only data; assets live in <c>Assets/Templates/World/PointsOfInterest</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A kind with no template is still a point of interest.</b> The site is planned, named and drawn on the map; it
	/// simply builds nothing (a marker-only record). That is what keeps the generator working before any templates or
	/// art exist, and why a kind gets a pad and a keep-out only once something will be built there.
	/// </para>
	/// <para>
	/// <b>Choosing.</b> Each site of a kind draws one of the kind's templates, seeded by the site, weighted by
	/// <see cref="WeightFor"/>: the biome's weight (the biome template's for the template's variant and style, 2026-10-16),
	/// the race's fit, and whether its size class is allowed.
	/// </para>
	/// <para>
	/// <b>Features</b> are <see cref="PointOfInterestFeature"/> subclasses held by reference, so another assembly's
	/// features (waypoints, spawners, portals) slot in without this type knowing them.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New Point of Interest Template", menuName = "FishMMO/World/Point of Interest Template")]
	public class PointOfInterestTemplate : ScriptableObject
	{
		/// <summary>Where the project's templates are kept.</summary>
		public const string Folder = "Assets/Templates/World/PointsOfInterest";

		public POIType Kind = POIType.Landmark;
		/// <summary>
		/// The variant this template is of its kind ("Stone" of "Village - Stone"): what a biome template weighs it by
		/// (BiomeTemplate.SiteVariants). Empty: read from the asset's name (<see cref="VariantOf"/>).
		/// </summary>
		public string Variant;
		/// <summary>How often this template is chosen against the kind's others, before biome and race fit.</summary>
		[Min(0f)] public float Weight = 1f;

		/// <summary>
		/// The template's weight in a biome that does not weigh its variant (BiomeTemplate.SiteVariants), and on ground with no
		/// biome.
		/// </summary>
		[Header("Fit")]
		[Min(0f)] public float DefaultBiomeWeight = 1f;
		/// <summary>
		/// The biomes this variant was written for, by name: the default the biome art authoring writes onto the biome templates
		/// (2026-10-16), which the cut reads instead. Only a biome template never authored with its variant weights still
		/// reads these, by its name, with a warning.
		/// </summary>
		public List<PointOfInterestBiomeWeight> BiomeWeights = new List<PointOfInterestBiomeWeight>();
		/// <summary>RaceTemplate.Category values this template suits; empty for any.</summary>
		public List<string> RaceCategories = new List<string>();
		/// <summary>Race naming keys this template suits; empty for any.</summary>
		public List<string> Races = new List<string>();
		public bool Small = true;
		public bool Medium = true;
		public bool Large = true;

		/// <summary>How fully the site's pad is flattened, 0 left as it is … 1 level.</summary>
		[Header("Ground")]
		[Range(0f, 1f)] public float PadFlatness = 1f;

		/// <summary>The structure kit's style ("timber", "stone", "orcish"); empty lets the site's race choose.</summary>
		[Header("Build")]
		public string Style;
		public PointOfInterestLayout Layout = PointOfInterestLayout.Scattered;
		public List<PointOfInterestPieceSlot> Pieces = new List<PointOfInterestPieceSlot>();
		[SerializeReference] public List<PointOfInterestFeature> Features = new List<PointOfInterestFeature>();

		/// <summary>The variant a biome weighs this template by: <see cref="Variant"/>, else read from the asset's name.</summary>
		public string VariantKey => !string.IsNullOrWhiteSpace(Variant) ? Variant.Trim() : VariantOf(name, Kind);

		/// <summary>
		/// The variant an authored template's asset name carries: "&lt;Kind&gt; - &lt;Variant&gt;", a run of sizes after it in
		/// brackets ("Camp - Hide Tents (Medium-Large)" → "Hide Tents"). A name not of that form is its own variant. Pure.
		/// </summary>
		public static string VariantOf(string assetName, POIType kind)
		{
			if (string.IsNullOrEmpty(assetName))
			{
				return string.Empty;
			}
			string variant = assetName.Trim();
			string prefix = kind + " - ";
			if (variant.StartsWith(prefix, StringComparison.Ordinal))
			{
				variant = variant.Substring(prefix.Length);
			}
			int open = variant.LastIndexOf(" (", StringComparison.Ordinal);
			if (open > 0 && variant.EndsWith(")", StringComparison.Ordinal) && IsSizeRun(variant.Substring(open + 2, variant.Length - open - 3)))
			{
				variant = variant.Substring(0, open);
			}
			return variant;
		}

		// "Small", "Medium", "Large", or two of them joined by a dash (PointOfInterestTemplateAuthoring.Plan's labels).
		private static bool IsSizeRun(string run)
		{
			string[] parts = run.Split('-');
			if (parts.Length < 1 || parts.Length > 2)
			{
				return false;
			}
			foreach (string part in parts)
			{
				if (part != "Small" && part != "Medium" && part != "Large")
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The weight the template's own biome list gives a biome by its asset name (<see cref="BiomeWeights"/>, a later entry
		/// winning; <see cref="DefaultBiomeWeight"/> for a biome it does not name, or none): what the pick used until
		/// 2026-10-16, read now only for a biome template never authored with its variant weights (BiomeTemplateArt).
		/// </summary>
		public float BiomeWeightByName(string biomeName)
		{
			float biome = DefaultBiomeWeight;
			if (!string.IsNullOrEmpty(biomeName))
			{
				foreach (PointOfInterestBiomeWeight entry in BiomeWeights)
				{
					if (string.Equals(entry.Biome, biomeName, StringComparison.OrdinalIgnoreCase))
					{
						biome = entry.Weight;
					}
				}
			}
			return biome;
		}

		/// <summary>The template's weight for a site: 0 where it does not fit at all.</summary>
		/// <param name="biomeWeight">
		/// The weight the site's biome gives this template (BiomeTemplateArt.SiteVariantWeight: the biome template's weight for
		/// its variant and style, else <see cref="DefaultBiomeWeight"/>).
		/// </param>
		/// <param name="race">The site's race naming key (empty for none).</param>
		/// <param name="raceCategory">That race's category (empty for none).</param>
		public float WeightFor(PointOfInterestRecord record, float biomeWeight, string race, string raceCategory)
		{
			if (record == null || record.Kind != Kind)
			{
				return 0f;
			}
			return Fit(Weight, biomeWeight, Small, Medium, Large, Races, RaceCategories, record.SizeClass, race, raceCategory);
		}

		/// <summary>
		/// A template's weight from its parts: its own weight × the biome's × whether the site's size class and race fit (0 or 1).
		/// Pure, for the pick and its tests.
		/// </summary>
		public static float Fit(float weight, float biomeWeight, bool small, bool medium, bool large, IReadOnlyList<string> races,
			IReadOnlyList<string> raceCategories, int sizeClass, string race, string raceCategory)
		{
			bool size = sizeClass == 0 ? small : sizeClass == 1 ? medium : large;
			if (!size)
			{
				return 0f;
			}
			float fit = 1f;
			if (races != null && races.Count > 0 && !Contains(races, race))
			{
				fit = 0f;
			}
			if (raceCategories != null && raceCategories.Count > 0 && !Contains(raceCategories, raceCategory))
			{
				fit = 0f;
			}
			return Mathf.Max(0f, weight) * Mathf.Max(0f, biomeWeight) * fit;
		}

		private static bool Contains(IReadOnlyList<string> list, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}
			foreach (string entry in list)
			{
				if (string.Equals(entry, value, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}
}
#endif
