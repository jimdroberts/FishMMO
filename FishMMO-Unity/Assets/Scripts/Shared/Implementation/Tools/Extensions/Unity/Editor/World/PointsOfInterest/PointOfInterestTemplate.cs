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
	/// <see cref="WeightFor"/>: the biome's weight, the race's fit, and whether its size class is allowed.
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
		/// <summary>How often this template is chosen against the kind's others, before biome and race fit.</summary>
		[Min(0f)] public float Weight = 1f;

		/// <summary>Weight of a biome <see cref="BiomeWeights"/> does not name.</summary>
		[Header("Fit")]
		[Min(0f)] public float DefaultBiomeWeight = 1f;
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

		/// <summary>The template's weight for a site: 0 where it does not fit at all.</summary>
		/// <param name="biomeName">The site's biome asset name.</param>
		/// <param name="race">The site's race naming key (empty for none).</param>
		/// <param name="raceCategory">That race's category (empty for none).</param>
		public float WeightFor(PointOfInterestRecord record, string biomeName, string race, string raceCategory)
		{
			if (record == null || record.Kind != Kind)
			{
				return 0f;
			}
			bool size = record.SizeClass == 0 ? Small : record.SizeClass == 1 ? Medium : Large;
			if (!size)
			{
				return 0f;
			}
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
			float fit = 1f;
			if (Races.Count > 0 && !Contains(Races, race))
			{
				fit = 0f;
			}
			if (RaceCategories.Count > 0 && !Contains(RaceCategories, raceCategory))
			{
				fit = 0f;
			}
			return Mathf.Max(0f, Weight) * Mathf.Max(0f, biome) * fit;
		}

		private static bool Contains(List<string> list, string value)
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
