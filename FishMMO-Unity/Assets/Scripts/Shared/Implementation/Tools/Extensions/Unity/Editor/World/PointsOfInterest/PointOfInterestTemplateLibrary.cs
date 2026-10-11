#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEditor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The project's point-of-interest templates by kind, in path order so every pick is stable.</summary>
	public sealed class PointOfInterestTemplateLibrary
	{
		private readonly Dictionary<POIType, List<PointOfInterestTemplate>> byKind = new Dictionary<POIType, List<PointOfInterestTemplate>>();

		public PointOfInterestTemplateLibrary(IEnumerable<PointOfInterestTemplate> templates)
		{
			if (templates == null)
			{
				return;
			}
			foreach (PointOfInterestTemplate template in templates)
			{
				if (template == null)
				{
					continue;
				}
				if (!byKind.TryGetValue(template.Kind, out List<PointOfInterestTemplate> list))
				{
					list = new List<PointOfInterestTemplate>();
					byKind[template.Kind] = list;
				}
				list.Add(template);
			}
		}

		/// <summary>Every template asset in the project (LOCAL ones included), sorted by path.</summary>
		public static PointOfInterestTemplateLibrary Load()
		{
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(PointOfInterestTemplate)}"))
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(StringComparer.Ordinal);
			var templates = new List<PointOfInterestTemplate>();
			foreach (string path in paths)
			{
				var template = AssetDatabase.LoadAssetAtPath<PointOfInterestTemplate>(path);
				if (template != null)
				{
					templates.Add(template);
				}
			}
			return new PointOfInterestTemplateLibrary(templates);
		}

		/// <summary>Whether any template builds a kind.</summary>
		public bool Has(POIType kind) => byKind.TryGetValue(kind, out List<PointOfInterestTemplate> list) && list.Count > 0;

		/// <summary>A kind's template by asset name, or null.</summary>
		public PointOfInterestTemplate Named(POIType kind, string name)
		{
			if (string.IsNullOrEmpty(name) || !byKind.TryGetValue(kind, out List<PointOfInterestTemplate> list))
			{
				return null;
			}
			foreach (PointOfInterestTemplate template in list)
			{
				if (template.name == name)
				{
					return template;
				}
			}
			return null;
		}

		/// <summary>
		/// A template for a site, drawn by its site seed and weighted by <see cref="PointOfInterestTemplate.WeightFor"/>;
		/// null (a marker-only site) when the kind has none or none fits.
		/// </summary>
		public PointOfInterestTemplate Pick(PointOfInterestRecord record, string biomeName, string raceCategory)
		{
			if (record == null || !byKind.TryGetValue(record.Kind, out List<PointOfInterestTemplate> list))
			{
				return null;
			}
			var weights = new float[list.Count];
			float total = 0f;
			for (int i = 0; i < list.Count; i++)
			{
				weights[i] = list[i].WeightFor(record, biomeName, record.Race, raceCategory);
				total += weights[i];
			}
			return Pick(list, weights, total, record.SiteSeed);
		}

		/// <summary>A seeded weighted draw; null when nothing weighs anything. Pure.</summary>
		public static T Pick<T>(IReadOnlyList<T> items, IReadOnlyList<float> weights, float total, int seed) where T : class
		{
			if (items == null || items.Count == 0 || !(total > 0f))
			{
				return null;
			}
			float roll = PointOfInterestPlanner.Unit(PointOfInterestPlanner.Hash(seed, 0x7E3A, 0, 0)) * total;
			for (int i = 0; i < items.Count; i++)
			{
				if (weights[i] <= 0f)
				{
					continue;
				}
				roll -= weights[i];
				if (roll < 0f)
				{
					return items[i];
				}
			}
			for (int i = items.Count - 1; i >= 0; i--)
			{
				if (weights[i] > 0f)
				{
					return items[i];
				}
			}
			return null;
		}
	}
}
#endif
