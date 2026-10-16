using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Which biome's spawn rule made each of a terrain's detail prototypes, beside the terrain (written by the scene cut's
	/// scatter, TerrainScatter). The scatter makes one prototype per rule and prefab, so the grass on a terrain is several
	/// prototypes of one prefab, each painted where its own biome grows it; with this the client knows which biome's look a
	/// prototype's blades take (<see cref="Biomes.BiomeTemplate.GrassTypes"/>), and that its healthy and dry tints are its
	/// rule's.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Keyed by the prototype's prefab name and noise seed</b>, not its index: the scatter seeds every prototype from its
	/// rule (a distinct seed per rule and prefab), both survive any copy of the prototype, and a prototype added afterwards
	/// (a point of interest's wheat, a hand-painted one) simply has no entry. By name rather than by reference so the record
	/// pulls no prefab into a build the terrain's own prototypes do not (the server strips those).
	/// </para>
	/// <para>
	/// A biome is its cached-object id (<see cref="Biomes.BiomeRegistry.IDOf"/>), as the scene's biome map stores it, never a
	/// reference: a scene reference to a template would pull its art into the scene's bundle.
	/// </para>
	/// <para>
	/// <b>A point of interest's prototypes</b> (a herb grove's herbs and flowers, a cave mouth's growth, a field's wheat:
	/// prototypes a site adds to a tile after the scatter) are recorded too since 2026-10-16, for the biome the site stands
	/// in, marked <see cref="Entry.Site"/>: they take that biome's look of the prefab (or the old look where it has none), and
	/// keep their material's tints, as they had before, since no rule of the biome chose them.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public sealed class TerrainDetailBiomes : MonoBehaviour
	{
		/// <summary>One detail prototype's biome.</summary>
		[Serializable]
		public struct Entry
		{
			public string Prefab;
			public int NoiseSeed;
			public int BiomeID;
			/// <summary>Added by a point of interest for the biome it stands in, not made by a rule: its tints are its material's.</summary>
			public bool Site;
		}

		[Tooltip("Each detail prototype the scatter made from a biome's rule: its prefab's name, its noise seed and the biome's id. Written by the cut; not edited by hand.")]
		public List<Entry> Prototypes = new List<Entry>();

		/// <summary>The biome id whose rule made a prototype of this prefab and noise seed; 0 when none is recorded.</summary>
		public int BiomeOf(string prefabName, int noiseSeed) => TryGet(prefabName, noiseSeed, out Entry entry) ? entry.BiomeID : 0;

		/// <summary>The entry recorded for a prototype of this prefab and noise seed; false when none is.</summary>
		public bool TryGet(string prefabName, int noiseSeed, out Entry found)
		{
			found = default;
			if (Prototypes == null || string.IsNullOrEmpty(prefabName))
			{
				return false;
			}
			for (int i = 0; i < Prototypes.Count; i++)
			{
				Entry entry = Prototypes[i];
				if (entry.NoiseSeed == noiseSeed && string.Equals(entry.Prefab, prefabName, StringComparison.Ordinal))
				{
					found = entry;
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Records a prototype a point of interest added for the biome it stands in (<see cref="Entry.Site"/>), unless one is
		/// recorded already (a rule's, or an earlier site's). True when it added the entry.
		/// </summary>
		public bool RecordSite(string prefabName, int noiseSeed, int biomeID)
		{
			if (string.IsNullOrEmpty(prefabName) || biomeID == 0 || TryGet(prefabName, noiseSeed, out _))
			{
				return false;
			}
			Prototypes ??= new List<Entry>();
			Prototypes.Add(new Entry { Prefab = prefabName, NoiseSeed = noiseSeed, BiomeID = biomeID, Site = true });
			return true;
		}

		/// <summary>The biome id recorded for a terrain's prototype; 0 when the terrain has no record or none for it.</summary>
		public static int BiomeOf(Terrain terrain, DetailPrototype prototype) => TryGet(terrain, prototype, out Entry entry) ? entry.BiomeID : 0;

		/// <summary>The entry recorded for a terrain's prototype; false when the terrain has no record or none for it.</summary>
		public static bool TryGet(Terrain terrain, DetailPrototype prototype, out Entry entry)
		{
			entry = default;
			if (terrain == null || prototype == null || prototype.prototype == null)
			{
				return false;
			}
			TerrainDetailBiomes record = terrain.GetComponent<TerrainDetailBiomes>();
			return record != null && record.TryGet(prototype.prototype.name, prototype.noiseSeed, out entry);
		}
	}
}
