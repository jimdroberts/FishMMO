using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// One sea creature kind at home in a biome's water and how much (<see cref="BiomeTemplate.SeaLife"/>): groups of the kind
	/// are <see cref="Weight"/> times as common there as its density says, whatever the climate (a reef is warm by being a
	/// reef). A kind a biome does not list lives there as in any other water: by its own elsewhere weight and the climate.
	/// </summary>
	/// <remarks>
	/// Until 2026-10-16 (phase I4) each kind named its favourite biomes and one weight for all of them (the client's
	/// SeaCreatureKind.Prefers and PreferWeight); I4 moved the names onto the templates, and I4b the weight with them, so each
	/// biome says how much each of its creatures belongs. The biome art authoring writes, for each kind a biome hosts, the
	/// weight the kind had for its favourites, so nothing changes until a template is edited.
	/// </remarks>
	[Serializable]
	public struct BiomeSeaLife
	{
		[Tooltip("The sea life kind's name, as the weather profile's sea life names it (Reef fish, Sea turtle, Crab, Whale…).")]
		public string Kind;
		[Tooltip("How many times as common the kind's groups are in this biome's water, whatever the climate there. 0: none here at all.")]
		[Min(0f)] public float Weight;

		public BiomeSeaLife(string kind, float weight)
		{
			Kind = kind;
			Weight = weight;
		}

		/// <summary>
		/// The weight a list gives a kind (by name, ignoring case): the first entry naming it, never below 0; false when none
		/// does or the list or name is empty.
		/// </summary>
		public static bool TryWeight(IReadOnlyList<BiomeSeaLife> entries, string kind, out float weight)
		{
			weight = 0f;
			if (entries == null || string.IsNullOrEmpty(kind))
			{
				return false;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				if (string.Equals(entries[i].Kind, kind, StringComparison.OrdinalIgnoreCase))
				{
					weight = Mathf.Max(0f, entries[i].Weight);
					return true;
				}
			}
			return false;
		}

		/// <summary>The entry as the authoring marks and reports it: "Kind×weight" (the weight round-tripped exactly).</summary>
		public override string ToString() => (Kind ?? "<none>") + "×" + Weight.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
	}
}
