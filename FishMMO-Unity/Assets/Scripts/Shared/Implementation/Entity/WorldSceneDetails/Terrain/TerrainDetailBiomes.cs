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
	/// <para>
	/// <b>A site paints its own biome's prototype</b> (2026-10-16, I4b): it reuses a tile's prototype of the plant only when
	/// that prototype is its own biome's (a rule's or an earlier site's, <see cref="SiteLayer"/>); where the tile's prototype of
	/// the plant is another biome's, the site adds one of its own (a copy of that one, with a noise seed of its own,
	/// <see cref="SiteSeed"/>), so the plant takes the look of the biome the site stands in rather than whichever biome's
	/// prototype came first. Its cover goes on as before, measured against the plant's prototype it used to paint into
	/// (<see cref="SiteCover"/>), so a plant is no thicker for being split between two prototypes.
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
		public bool TryGet(string prefabName, int noiseSeed, out Entry found) => TryGet(Prototypes, prefabName, noiseSeed, out found);

		/// <summary>The entry a list records for a prototype of this prefab and noise seed (the first); false when none is.</summary>
		public static bool TryGet(IReadOnlyList<Entry> entries, string prefabName, int noiseSeed, out Entry found)
		{
			found = default;
			if (entries == null || string.IsNullOrEmpty(prefabName))
			{
				return false;
			}
			for (int i = 0; i < entries.Count; i++)
			{
				Entry entry = entries[i];
				if (entry.NoiseSeed == noiseSeed && string.Equals(entry.Prefab, prefabName, StringComparison.Ordinal))
				{
					found = entry;
					return true;
				}
			}
			return false;
		}

		// ── A point of interest's own prototypes (2026-10-16) ─────────

		/// <summary>
		/// The prototype a point of interest of biome <paramref name="biomeID"/> paints a plant (<paramref name="prefabName"/>)
		/// into on a tile whose prototypes are <paramref name="prefabs"/> (their prefabs' names, null for none) and
		/// <paramref name="seeds"/> (their noise seeds), recorded in <paramref name="entries"/>: the first prototype of the plant
		/// recorded for the same biome (a rule's or an earlier site's), or -1 when there is none and the site adds its own.
		/// <paramref name="first"/> is the tile's first prototype of the plant whatever its biome (-1 when it has none): the one
		/// a site painted into before, which an added prototype copies. A site with no biome (0) takes the first, as before.
		/// </summary>
		public static int SiteLayer(string prefabName, int biomeID, IReadOnlyList<string> prefabs, IReadOnlyList<int> seeds, IReadOnlyList<Entry> entries,
			out int first)
		{
			first = -1;
			if (string.IsNullOrEmpty(prefabName) || prefabs == null || seeds == null)
			{
				return -1;
			}
			for (int i = 0; i < prefabs.Count && i < seeds.Count; i++)
			{
				if (!string.Equals(prefabs[i], prefabName, StringComparison.Ordinal))
				{
					continue;
				}
				if (first < 0)
				{
					first = i;
				}
				if (biomeID == 0)
				{
					return i;
				}
				if (TryGet(entries, prefabName, seeds[i], out Entry entry) && entry.BiomeID == biomeID)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>
		/// The noise seed of a prototype a site adds for its biome: <paramref name="seed"/> (the one it copied), unless a
		/// prototype of the same plant on the tile has it or the record names the plant with it, then hashed with the biome until
		/// neither does, so every prototype of a plant is told apart by its seed (<see cref="Entry"/>'s key). Deterministic: a
		/// re-cut adds the same.
		/// </summary>
		public static int SiteSeed(int seed, int biomeID, string prefabName, IReadOnlyList<string> prefabs, IReadOnlyList<int> seeds, IReadOnlyList<Entry> entries)
		{
			int candidate = seed;
			for (int attempt = 0; attempt < 64 && SeedTaken(candidate, prefabName, prefabs, seeds, entries); attempt++)
			{
				unchecked
				{
					uint h = (uint)candidate ^ (uint)biomeID * 0x9E3779B1u ^ (uint)attempt * 0x85EBCA6Bu;
					h ^= h >> 16;
					h *= 0x7feb352du;
					h ^= h >> 15;
					h *= 0x846ca68bu;
					h ^= h >> 16;
					candidate = (int)(h & 0x7FFFFFFF);
				}
			}
			return candidate;
		}

		private static bool SeedTaken(int seed, string prefabName, IReadOnlyList<string> prefabs, IReadOnlyList<int> seeds, IReadOnlyList<Entry> entries)
		{
			if (prefabs != null && seeds != null)
			{
				for (int i = 0; i < prefabs.Count && i < seeds.Count; i++)
				{
					if (seeds[i] == seed && string.Equals(prefabs[i], prefabName, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}
			return TryGet(entries, prefabName, seed, out _);
		}

		/// <summary>
		/// The cover a site leaves in its own prototype of a plant at one detail cell, where it wants <paramref name="want"/>
		/// and grows only ever more: measured, as it always was, against the plant's prototype it used to paint into (the tile's
		/// first of the plant, <paramref name="baseCover"/>) together with its own (<paramref name="ownCover"/>), and only the
		/// difference added to its own. <paramref name="sameLayer"/>: its own is that first one (the tile had no other biome's
		/// prototype of the plant first), so the cell is simply raised to <paramref name="want"/>. Either way the plant's cover
		/// in the two is what one prototype held before.
		/// </summary>
		public static int SiteCover(int baseCover, int ownCover, bool sameLayer, int want)
		{
			if (sameLayer)
			{
				return Math.Max(ownCover, want);
			}
			int have = baseCover + ownCover;
			return want > have ? ownCover + (want - have) : ownCover;
		}

		/// <summary>
		/// Records a prototype a point of interest added for the biome it stands in (<see cref="Entry.Site"/>), unless one is
		/// recorded already under the same prefab and seed (a rule's, or an earlier site's: a site adds its own prototype, with
		/// a seed of its own, wherever the tile's is another biome's, <see cref="SiteLayer"/>). True when it added the entry.
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
