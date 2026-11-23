using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The world map's scene dropdown, as pure rules (issue #241): which entered scenes it lists and what each
	/// choice reads.
	/// </summary>
	public static class MapSceneChoices
	{
		/// <summary>What the scene the character stands in is marked with.</summary>
		public const string CurrentSuffix = " (you are here)";

		/// <summary>The label of one choice.</summary>
		public static string Label(string displayName, bool isCurrent)
		{
			string name = string.IsNullOrEmpty(displayName) ? "Unknown" : displayName;
			return isCurrent ? name + CurrentSuffix : name;
		}

		/// <summary>
		/// Whether an entered scene is offered: only scenes this build knows (a scene since removed from the world has a row
		/// on the server and nothing to draw). Without a details cache every scene is offered.
		/// </summary>
		public static bool IsListable(string sceneName, WorldSceneDetailsCache cache)
		{
			if (string.IsNullOrEmpty(sceneName))
			{
				return false;
			}
			return cache?.Scenes == null || cache.Scenes.ContainsKey(sceneName);
		}

		/// <summary>
		/// Makes every label unique: two scenes with the same display name get their scene names appended, so a choice always
		/// names one scene (the dropdown selects by label).
		/// </summary>
		public static void Disambiguate(List<string> labels, IReadOnlyList<string> scenes)
		{
			if (labels == null || scenes == null)
			{
				return;
			}
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach (string label in labels)
			{
				counts.TryGetValue(label, out int n);
				counts[label] = n + 1;
			}
			for (int i = 0; i < labels.Count && i < scenes.Count; ++i)
			{
				if (counts[labels[i]] > 1)
				{
					labels[i] = $"{labels[i]} — {scenes[i]}";
				}
			}
		}
	}
}
