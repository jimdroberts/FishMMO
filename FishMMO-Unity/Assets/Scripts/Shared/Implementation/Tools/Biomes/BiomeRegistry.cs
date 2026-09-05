using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.NameGeneration;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// Runtime index of every loaded <see cref="BiomeTemplate"/>, by key and by cached-object
	/// ID. Holds no data of its own: templates register as Addressables load them (or as the
	/// editor loader finds them) and leave when they unload.
	/// </summary>
	public static class BiomeRegistry
	{
		private static readonly Dictionary<string, BiomeTemplate> byKey = new Dictionary<string, BiomeTemplate>(StringComparer.Ordinal);
		private static readonly Dictionary<int, BiomeTemplate> byID = new Dictionary<int, BiomeTemplate>();
		private static List<string> sortedKeys;
		private static List<BiomeTemplate> selectable;

		/// <summary>Raised after a biome is registered or removed.</summary>
		public static event Action Changed;

		/// <summary>Number of biomes currently registered.</summary>
		public static int Count => byKey.Count;

		/// <summary>Registered biome keys in ordinal order, so iteration is deterministic.</summary>
		public static IReadOnlyList<string> SupportedBiomes
		{
			get
			{
				if (sortedKeys == null)
				{
					sortedKeys = new List<string>(byKey.Keys);
					sortedKeys.Sort(StringComparer.Ordinal);
				}
				return sortedKeys;
			}
		}

		/// <summary>
		/// Keys of the registered biomes that carry usable naming data, in key order — what the name
		/// generator can name from. A biome without naming (a designer's new terrain-only biome) is
		/// still registered for terrain and maps, but is not offered here.
		/// </summary>
		public static IReadOnlyList<string> NameableBiomes
		{
			get
			{
				var keys = new List<string>();
				foreach (string key in SupportedBiomes)
				{
					BiomeTemplate biome = byKey[key];
					if (biome.Naming != null && biome.Naming.IsUsable)
					{
						keys.Add(key);
					}
				}
				return keys;
			}
		}

		/// <summary>Biomes climate-driven generation may choose (SelectionWeight &gt; 0), in key order.</summary>
		public static IReadOnlyList<BiomeTemplate> Selectable
		{
			get
			{
				if (selectable == null)
				{
					selectable = new List<BiomeTemplate>();
					foreach (string key in SupportedBiomes)
					{
						BiomeTemplate biome = byKey[key];
						if (biome.IsSelectable)
						{
							selectable.Add(biome);
						}
					}
				}
				return selectable;
			}
		}

		/// <summary>
		/// Registers a biome under its key and ID. A second biome resolving to the same key
		/// replaces the first with a warning, since names would otherwise silently come from
		/// whichever loaded last.
		/// </summary>
		public static void Register(BiomeTemplate biome)
		{
			if (biome == null || string.IsNullOrEmpty(biome.Key))
			{
				return;
			}
			if (byKey.TryGetValue(biome.Key, out BiomeTemplate existing) && existing != biome && existing != null)
			{
				Log.Warning("BiomeRegistry", $"Biomes '{biome.name}' and '{existing.name}' both resolve to key '{biome.Key}'; '{biome.name}' now stands.");
				byID.Remove(IDOf(existing));
			}
			biome.Naming?.BuildRuntime();
			byKey[biome.Key] = biome;
			byID[IDOf(biome)] = biome;
			Invalidate();
		}

		/// <summary>Removes a biome, but only if it is still the holder of its key.</summary>
		public static void Unregister(BiomeTemplate biome)
		{
			if (biome == null || string.IsNullOrEmpty(biome.Key))
			{
				return;
			}
			if (byKey.TryGetValue(biome.Key, out BiomeTemplate current) && current == biome)
			{
				byKey.Remove(biome.Key);
				byID.Remove(IDOf(biome));
				Invalidate();
			}
		}

		/// <summary>Removes every biome. Tests and the editor loader use this before a fresh registration.</summary>
		public static void Clear()
		{
			if (byKey.Count == 0)
			{
				return;
			}
			byKey.Clear();
			byID.Clear();
			Invalidate();
		}

		private static void Invalidate()
		{
			sortedKeys = null;
			selectable = null;
			Changed?.Invoke();
		}

		/// <summary>Looks a biome up by key; the key is normalised first, so "Alpine Meadow" finds "alpinemeadow".</summary>
		public static bool TryGet(string biomeKey, out BiomeTemplate biome)
		{
			biome = null;
			if (string.IsNullOrEmpty(biomeKey))
			{
				return false;
			}
			return byKey.TryGetValue(BiomeClimateVariant.Normalize(biomeKey), out biome);
		}

		/// <summary>The biome for a key, or null when none is registered.</summary>
		public static BiomeTemplate Get(string biomeKey)
		{
			TryGet(biomeKey, out BiomeTemplate biome);
			return biome;
		}

		public static bool Contains(string biomeKey) => TryGet(biomeKey, out _);

		/// <summary>Looks a biome up by cached-object ID — what biome maps, race affinities and [TemplateReference] fields store.</summary>
		public static bool TryGetByID(int id, out BiomeTemplate biome)
		{
			biome = null;
			return id != 0 && byID.TryGetValue(id, out biome);
		}

		/// <summary>The biome's cached-object ID, derived from type and name when the cache has not assigned one (edit mode).</summary>
		public static int IDOf(BiomeTemplate biome)
		{
			if (biome == null)
			{
				return 0;
			}
			return biome.ID != 0 ? biome.ID : (nameof(BiomeTemplate) + biome.name).GetDeterministicHashCode();
		}

		/// <summary>Display name for a biome key, or the key itself when the biome is unknown.</summary>
		public static string GetDisplayName(string biomeKey)
		{
			return TryGet(biomeKey, out BiomeTemplate biome) ? biome.ResolvedDisplayName : biomeKey;
		}

		/// <summary>The runtime naming phonology for a biome, or null when the biome is unknown or has none.</summary>
		public static BiomePhonology ResolvePhonology(string biomeKey)
		{
			return TryGet(biomeKey, out BiomeTemplate biome) && biome.Naming != null ? biome.Naming.RuntimePhonology : null;
		}

	}
}
