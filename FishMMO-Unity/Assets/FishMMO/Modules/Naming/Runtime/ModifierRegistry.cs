using System;
using System.Collections.Generic;

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// Runtime index of every loaded <see cref="NameModifierTemplate"/>, keyed by
	/// its normalised modifier key. A modifier is an orthogonal flavour applied
	/// on top of any race and culture; with 30 races, 3 cultures each and 10
	/// modifiers that is 900 name profiles from roughly the authoring cost of 30.
	/// </summary>
	public static class ModifierRegistry
	{
		private static readonly Dictionary<string, NameModifierTemplate> templates =
			new Dictionary<string, NameModifierTemplate>(StringComparer.Ordinal);
		private static List<string> sortedKeys;

		/// <summary>Raised after a template is registered or removed.</summary>
		public static event Action Changed;

		/// <summary>Number of modifiers currently registered.</summary>
		public static int Count => templates.Count;

		/// <summary>Registered modifier keys in ordinal order.</summary>
		public static IReadOnlyList<string> SupportedModifiers
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
		public static void Register(NameModifierTemplate template)
		{
			if (template == null || string.IsNullOrEmpty(template.Key))
			{
				return;
			}
			templates[template.Key] = template;
			sortedKeys = null;
			Changed?.Invoke();
		}

		/// <summary>Removes a template, but only if it is still the holder of its key.</summary>
		public static void Unregister(NameModifierTemplate template)
		{
			if (template == null || string.IsNullOrEmpty(template.Key))
			{
				return;
			}
			if (templates.TryGetValue(template.Key, out NameModifierTemplate current) && current == template)
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
		public static bool TryGetByID(int id, out NameModifierTemplate template)
		{
			template = null;
			if (id == 0)
			{
				return false;
			}
			foreach (NameModifierTemplate candidate in templates.Values)
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
		public static int IDOf(NameModifierTemplate template)
		{
			if (template == null)
			{
				return 0;
			}
			return template.ID != 0 ? template.ID : (nameof(NameModifierTemplate) + template.name).GetDeterministicHashCode();
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

		/// <summary>Looks a modifier up by key; the key is normalised first.</summary>
		public static bool TryGet(string modifierKey, out NameModifierTemplate template)
		{
			template = null;
			if (string.IsNullOrEmpty(modifierKey))
			{
				return false;
			}
			return templates.TryGetValue(GeneratorUtility.NormalizeRace(modifierKey), out template);
		}

		public static bool Contains(string modifierKey)
		{
			return TryGet(modifierKey, out _);
		}

		/// <summary>Display name for a modifier key, or the key itself when unknown.</summary>
		public static string GetDisplayName(string modifierKey)
		{
			return TryGet(modifierKey, out NameModifierTemplate template) ? template.ResolvedDisplayName : modifierKey;
		}

		/// <summary>
		/// Applies a modifier to a phonology, returning a new phonology. An
		/// unknown modifier key returns the base phonology unchanged.
		/// </summary>
		public static RacePhonology Apply(RacePhonology basePhonology, string modifierKey)
		{
			if (basePhonology == null || !TryGet(modifierKey, out NameModifierTemplate template))
			{
				return basePhonology;
			}
			return template.Apply(basePhonology);
		}
	}
}
