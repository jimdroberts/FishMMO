using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Runtime index of every loaded <see cref="BiomeNamingTemplate"/>, keyed by
	/// its normalised biome key. Templates register as they load and leave as
	/// they unload; the registry holds no data of its own.
	/// </summary>
	public static class BiomeRegistry
	{
		private static readonly Dictionary<string, BiomeNamingTemplate> templates =
			new Dictionary<string, BiomeNamingTemplate>(StringComparer.Ordinal);
		private static List<string> sortedKeys;

		/// <summary>Raised after a template is registered or removed.</summary>
		public static event Action Changed;

		/// <summary>Number of biomes currently registered.</summary>
		public static int Count => templates.Count;

		/// <summary>Registered biome keys in ordinal order, so iteration is deterministic.</summary>
		public static IReadOnlyList<string> SupportedBiomes
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

		/// <summary>Registers a template under its key, replacing any previous holder of that key.</summary>
		public static void Register(BiomeNamingTemplate template)
		{
			if (template == null || string.IsNullOrEmpty(template.Key))
			{
				return;
			}
			template.BuildRuntime();
			templates[template.Key] = template;
			sortedKeys = null;
			Changed?.Invoke();
		}

		/// <summary>Removes a template, but only if it is still the holder of its key.</summary>
		public static void Unregister(BiomeNamingTemplate template)
		{
			if (template == null || string.IsNullOrEmpty(template.Key))
			{
				return;
			}
			if (templates.TryGetValue(template.Key, out BiomeNamingTemplate current) && current == template)
			{
				templates.Remove(template.Key);
				sortedKeys = null;
				Changed?.Invoke();
			}
		}

		/// <summary>
		/// Looks a template up by its cached-object ID. Outside play mode nothing has called
		/// <c>AddToCache</c>, so a template's own ID is still zero; the ID is then derived the way
		/// the cache would derive it, which is also what a <c>[TemplateReference]</c> field stores.
		/// </summary>
		public static bool TryGetByID(int id, out BiomeNamingTemplate template)
		{
			template = null;
			if (id == 0)
			{
				return false;
			}
			foreach (BiomeNamingTemplate candidate in templates.Values)
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
		public static int IDOf(BiomeNamingTemplate template)
		{
			if (template == null)
			{
				return 0;
			}
			return template.ID != 0 ? template.ID : (nameof(BiomeNamingTemplate) + template.name).GetDeterministicHashCode();
		}

		/// <summary>Removes every template.</summary>
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

		/// <summary>Looks a biome up by key; the key is normalised first.</summary>
		public static bool TryGet(string biomeKey, out BiomeNamingTemplate template)
		{
			template = null;
			if (string.IsNullOrEmpty(biomeKey))
			{
				return false;
			}
			return templates.TryGetValue(GeneratorUtility.NormalizeRace(biomeKey), out template);
		}

		/// <summary>The template for a biome, or null when none is registered.</summary>
		public static BiomeNamingTemplate Get(string biomeKey)
		{
			TryGet(biomeKey, out BiomeNamingTemplate template);
			return template;
		}

		public static bool Contains(string biomeKey)
		{
			return TryGet(biomeKey, out _);
		}

		/// <summary>Display name for a biome key, or the key itself when the biome is unknown.</summary>
		public static string GetDisplayName(string biomeKey)
		{
			return TryGet(biomeKey, out BiomeNamingTemplate template) ? template.ResolvedDisplayName : biomeKey;
		}

		/// <summary>The runtime phonology for a biome, or null when the biome is unknown.</summary>
		public static BiomePhonology ResolvePhonology(string biomeKey)
		{
			return TryGet(biomeKey, out BiomeNamingTemplate template) ? template.RuntimePhonology : null;
		}
	}
}
