using System;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Runtime index of every loaded <see cref="RaceTemplate"/> that carries
	/// usable naming data, keyed by <see cref="RaceTemplate.NamingKey"/>. The
	/// registry holds no data of its own: races register as Addressables load
	/// them (or as the editor loader finds them) and leave when they unload.
	/// </summary>
	public static class RaceRegistry
	{
		private static readonly Dictionary<string, RaceTemplate> templates =
			new Dictionary<string, RaceTemplate>(StringComparer.Ordinal);
		private static List<string> sortedKeys;

		/// <summary>Raised after a race is registered or removed.</summary>
		public static event Action Changed;

		/// <summary>Number of races currently registered.</summary>
		public static int Count => templates.Count;

		/// <summary>Registered race keys in ordinal order, so iteration is deterministic.</summary>
		public static IReadOnlyList<string> SupportedRaces
		{
			get
			{
				if (sortedKeys == null)
				{
					sortedKeys = new List<string>(templates.Keys);
					sortedKeys.Sort(StringComparer.Ordinal);
				}
				return sortedKeys;
			}
		}

		/// <summary>
		/// Registers a race under its naming key. A race without usable naming
		/// data is ignored; a second race resolving to the same key replaces the
		/// first with a warning, since names would otherwise silently come from
		/// whichever loaded last.
		/// </summary>
		public static void Register(RaceTemplate template)
		{
			if (template == null || template.Naming == null || !template.Naming.IsUsable)
			{
				return;
			}
			string key = template.NamingKey;
			if (string.IsNullOrEmpty(key))
			{
				return;
			}
			if (templates.TryGetValue(key, out RaceTemplate existing) && existing != template && existing != null)
			{
				Log.Warning("RaceRegistry",
					$"Race '{template.Name}' and '{existing.Name}' both resolve to naming key '{key}'; '{template.Name}' now supplies its names.");
			}
			template.Naming.BuildRuntime();
			templates[key] = template;
			sortedKeys = null;
			Changed?.Invoke();
		}

		/// <summary>Removes a race, but only if it is still the holder of its key.</summary>
		public static void Unregister(RaceTemplate template)
		{
			if (template == null)
			{
				return;
			}
			string key = template.NamingKey;
			if (string.IsNullOrEmpty(key))
			{
				return;
			}
			if (templates.TryGetValue(key, out RaceTemplate current) && current == template)
			{
				templates.Remove(key);
				sortedKeys = null;
				Changed?.Invoke();
			}
		}

		/// <summary>
		/// Looks a template up by its cached-object ID. Outside play mode nothing has called
		/// <c>AddToCache</c>, so a template's own ID is still zero; the ID is then derived the way
		/// the cache would derive it, which is also what a <c>[TemplateReference]</c> field stores.
		/// </summary>
		public static bool TryGetByID(int id, out RaceTemplate template)
		{
			template = null;
			if (id == 0)
			{
				return false;
			}
			foreach (RaceTemplate candidate in templates.Values)
			{
				if (IDOf(candidate) == id)
				{
					template = candidate;
					return true;
				}
			}
			return false;
		}

		/// <summary>The template's cached-object ID, derived from its type and name when the cache has not assigned one.</summary>
		public static int IDOf(RaceTemplate template)
		{
			if (template == null)
			{
				return 0;
			}
			return template.ID != 0 ? template.ID : (nameof(RaceTemplate) + template.name).GetDeterministicHashCode();
		}

		/// <summary>Removes every race. Tests and the editor loader use this before a fresh registration.</summary>
		public static void Clear()
		{
			if (templates.Count == 0)
			{
				return;
			}
			templates.Clear();
			sortedKeys = null;
			Changed?.Invoke();
		}

		/// <summary>Looks a race up by key; the key is normalised first, so "Wood Elf" finds "woodelf".</summary>
		public static bool TryGet(string raceKey, out RaceTemplate template)
		{
			template = null;
			if (string.IsNullOrEmpty(raceKey))
			{
				return false;
			}
			return templates.TryGetValue(GeneratorUtility.NormalizeRace(raceKey), out template);
		}

		/// <summary>The race for a key, or null when none is registered.</summary>
		public static RaceTemplate Get(string raceKey)
		{
			TryGet(raceKey, out RaceTemplate template);
			return template;
		}

		public static bool Contains(string raceKey)
		{
			return TryGet(raceKey, out _);
		}

		/// <summary>Display name for a race key, or the key itself when the race is unknown.</summary>
		public static string GetDisplayName(string raceKey)
		{
			return TryGet(raceKey, out RaceTemplate template) ? template.Name : raceKey;
		}

		/// <summary>Culture keys available for a race, in ordinal order.</summary>
		public static IReadOnlyList<string> GetCultures(string raceKey)
		{
			if (!TryGet(raceKey, out RaceTemplate template))
			{
				return Array.Empty<string>();
			}
			var cultures = new List<string>(template.Naming.RuntimeCultures.Keys);
			cultures.Sort(StringComparer.Ordinal);
			return cultures;
		}

		/// <summary>
		/// Resolves the phonology for a race and optional culture. A culture the
		/// race does not have falls back to the race's own phonology.
		/// </summary>
		public static RacePhonology ResolvePhonology(string raceKey, string culture)
		{
			return TryGet(raceKey, out RaceTemplate template) ? ResolvePhonology(template, culture) : null;
		}

		/// <summary>Resolves race + culture, then layers a modifier on top when one is named.</summary>
		public static RacePhonology ResolvePhonology(string raceKey, string culture, string modifier)
		{
			RacePhonology phonology = ResolvePhonology(raceKey, culture);
			if (phonology == null || string.IsNullOrEmpty(modifier))
			{
				return phonology;
			}
			return ModifierRegistry.Apply(phonology, modifier);
		}

		/// <summary>Resolves the phonology straight from a race, for callers that already hold one.</summary>
		public static RacePhonology ResolvePhonology(RaceTemplate template, string culture, string modifier = null)
		{
			if (template == null || template.Naming == null)
			{
				return null;
			}
			RacePhonology phonology = template.Naming.RuntimePhonology;
			if (!string.IsNullOrEmpty(culture)
				&& template.Naming.RuntimeCultures.TryGetValue(GeneratorUtility.NormalizeRace(culture), out RacePhonology culturePhonology))
			{
				phonology = culturePhonology;
			}
			return string.IsNullOrEmpty(modifier) ? phonology : ModifierRegistry.Apply(phonology, modifier);
		}

		public static bool TryGetTitles(string raceKey, out RaceTitles titles)
		{
			if (TryGet(raceKey, out RaceTemplate template))
			{
				titles = template.Naming.RuntimeTitles;
				return true;
			}
			titles = null;
			return false;
		}

		public static bool TryGetPlaces(string raceKey, out string[] places)
		{
			if (TryGet(raceKey, out RaceTemplate template))
			{
				places = template.Naming.RuntimePlaces;
				return places != null && places.Length > 0;
			}
			places = null;
			return false;
		}

		public static bool TryGetCitySuffixes(string raceKey, out RaceCitySuffixes suffixes)
		{
			if (TryGet(raceKey, out RaceTemplate template))
			{
				suffixes = template.Naming.RuntimeCitySuffixes;
				return true;
			}
			suffixes = null;
			return false;
		}

		/// <summary>
		/// Registered races with an affinity for a biome, with their weights, heaviest first (ties in
		/// key order). What a spawner asks to decide who lives here.
		/// </summary>
		public static List<(RaceTemplate race, float weight)> RacesForBiome(int biomeID)
		{
			var result = new List<(RaceTemplate race, float weight)>();
			if (biomeID == 0)
			{
				return result;
			}
			foreach (string key in SupportedRaces)
			{
				RaceTemplate race = templates[key];
				float weight = race.AffinityFor(biomeID);
				if (weight > 0f)
				{
					result.Add((race, weight));
				}
			}
			// Stable: equal weights keep key order, so the list is the same on every peer.
			for (int i = 1; i < result.Count; i++)
			{
				(RaceTemplate race, float weight) item = result[i];
				int j = i - 1;
				while (j >= 0 && result[j].weight < item.weight)
				{
					result[j + 1] = result[j];
					j--;
				}
				result[j + 1] = item;
			}
			return result;
		}

		/// <summary>Every tag used by any registered race, in ordinal order.</summary>
		public static IReadOnlyList<string> AllTags()
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (RaceTemplate template in templates.Values)
			{
				string[] tags = template.Naming.RuntimePhonology.Tags;
				if (tags == null)
				{
					continue;
				}
				for (int i = 0; i < tags.Length; i++)
				{
					set.Add(tags[i]);
				}
			}
			var result = new List<string>(set);
			result.Sort(StringComparer.Ordinal);
			return result;
		}

		/// <summary>Races whose phonology carries the given tag.</summary>
		public static IReadOnlyList<string> RacesWithTag(string tag)
		{
			var result = new List<string>();
			foreach (string raceKey in SupportedRaces)
			{
				string[] tags = templates[raceKey].Naming.RuntimePhonology.Tags;
				if (tags == null)
				{
					continue;
				}
				for (int i = 0; i < tags.Length; i++)
				{
					if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
					{
						result.Add(raceKey);
						break;
					}
				}
			}
			return result;
		}

		/// <summary>Races × cultures-per-race × modifiers: how many distinct name profiles are available.</summary>
		public static int PresentationCount()
		{
			int total = 0;
			foreach (RaceTemplate template in templates.Values)
			{
				total += 1 + template.Naming.RuntimeCultures.Count;
			}
			return total * Math.Max(1, ModifierRegistry.Count + 1);
		}
	}
}
