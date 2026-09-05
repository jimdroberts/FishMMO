using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// The loaded <see cref="TitlePoolTemplate"/>s and the per-race merge of a race's own titles
	/// and places with every pool that serves its category. Holds no data of its own: pools
	/// register as they load. Pools are kept in name order and merged race-first, so the merged
	/// list — and therefore every seeded title — is identical on every peer whatever the load order.
	/// </summary>
	public static class TitlePoolRegistry
	{
		private static readonly List<TitlePoolTemplate> pools = new List<TitlePoolTemplate>();
		private static readonly Dictionary<string, RaceTitles> mergedTitles = new Dictionary<string, RaceTitles>(StringComparer.Ordinal);
		private static readonly Dictionary<string, string[]> mergedPlaces = new Dictionary<string, string[]>(StringComparer.Ordinal);

		/// <summary>Raised when a pool registers or unregisters; merged results are rebuilt lazily.</summary>
		public static event Action Changed;

		public static int Count => pools.Count;

		/// <summary>Every registered pool, in name order.</summary>
		public static IReadOnlyList<TitlePoolTemplate> All => pools;

		public static void Register(TitlePoolTemplate pool)
		{
			if (pool == null)
			{
				return;
			}
			pool.InvalidateRuntime();
			if (!pools.Contains(pool))
			{
				pools.Add(pool);
				pools.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			}
			Invalidate();
		}

		public static void Unregister(TitlePoolTemplate pool)
		{
			if (pool != null && pools.Remove(pool))
			{
				Invalidate();
			}
		}

		public static void Clear()
		{
			pools.Clear();
			Invalidate();
		}

		/// <summary>Drops every cached merge. Called whenever a race or pool registers or changes.</summary>
		public static void Invalidate()
		{
			mergedTitles.Clear();
			mergedPlaces.Clear();
			Changed?.Invoke();
		}

		/// <summary>Pools that serve a category, in name order.</summary>
		public static List<TitlePoolTemplate> PoolsFor(string category)
		{
			var result = new List<TitlePoolTemplate>();
			for (int i = 0; i < pools.Count; i++)
			{
				if (pools[i] != null && pools[i].AppliesTo(category))
				{
					result.Add(pools[i]);
				}
			}
			return result;
		}

		/// <summary>
		/// A race's own titles followed by those of every pool serving its category, each list
		/// de-duplicated case-insensitively. The race's own titles come first so its flavour is
		/// never lost to a pool; a race with no pools gets its own titles back unchanged.
		/// </summary>
		public static RaceTitles TitlesFor(RaceTemplate race)
		{
			if (race == null || race.Naming == null)
			{
				return new RaceTitles();
			}
			string key = race.NamingKey ?? race.name ?? "";
			if (mergedTitles.TryGetValue(key, out RaceTitles cached))
			{
				return cached;
			}

			RaceTitles own = race.Naming.RuntimeTitles ?? new RaceTitles();
			List<TitlePoolTemplate> applicable = PoolsFor(race.Category);
			RaceTitles merged;
			if (applicable.Count == 0)
			{
				merged = own;
			}
			else
			{
				merged = new RaceTitles
				{
					Honorific = Merge(own.Honorific, applicable, p => p.Honorific),
					HonorificMasculine = Merge(own.HonorificMasculine, applicable, p => p.HonorificMasculine),
					HonorificFeminine = Merge(own.HonorificFeminine, applicable, p => p.HonorificFeminine),
					Epithet = Merge(own.Epithet, applicable, p => p.Epithet),
					Rank = Merge(own.Rank, applicable, p => p.Rank),
					Legend = Merge(own.Legend, applicable, p => p.Legend),
					Occupational = Merge(own.Occupational, applicable, p => p.Occupational),
				};
			}
			mergedTitles[key] = merged;
			return merged;
		}

		/// <summary>A race's own places followed by every serving pool's, de-duplicated.</summary>
		public static string[] PlacesFor(RaceTemplate race)
		{
			if (race == null || race.Naming == null)
			{
				return Array.Empty<string>();
			}
			string key = race.NamingKey ?? race.name ?? "";
			if (mergedPlaces.TryGetValue(key, out string[] cached))
			{
				return cached;
			}
			List<TitlePoolTemplate> applicable = PoolsFor(race.Category);
			string[] own = race.Naming.RuntimePlaces ?? Array.Empty<string>();
			string[] merged = applicable.Count == 0 ? own : MergeLists(own, applicable, p => p.Places);
			mergedPlaces[key] = merged;
			return merged;
		}

		private static string[] Merge(string[] own, List<TitlePoolTemplate> applicable, Func<RaceTitles, string[]> pick)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var result = new List<string>();
			Append(result, seen, own);
			for (int i = 0; i < applicable.Count; i++)
			{
				Append(result, seen, pick(applicable[i].RuntimeTitles));
			}
			return result.ToArray();
		}

		private static string[] MergeLists(string[] own, List<TitlePoolTemplate> applicable, Func<TitlePoolTemplate, string[]> pick)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var result = new List<string>();
			Append(result, seen, own);
			for (int i = 0; i < applicable.Count; i++)
			{
				Append(result, seen, pick(applicable[i]));
			}
			return result.ToArray();
		}

		private static void Append(List<string> result, HashSet<string> seen, string[] items)
		{
			if (items == null)
			{
				return;
			}
			for (int i = 0; i < items.Length; i++)
			{
				string item = items[i];
				if (!string.IsNullOrWhiteSpace(item) && seen.Add(item.Trim()))
				{
					result.Add(item.Trim());
				}
			}
		}
	}
}
