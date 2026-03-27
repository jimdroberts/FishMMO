#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Shared utility for drawing a TeleporterCache destination dropdown in custom editors.
	/// Used by SceneTeleporterEditor and TeleporterEditor to avoid duplicating dropdown logic.
	/// </summary>
	public static class TeleporterDestinationDropdown
	{
		/// <summary>
		/// Draws a destination selection dropdown populated from the TeleporterCache.
		/// Shows appropriate warnings if the cache is missing, empty, or the current ID is orphaned.
		/// </summary>
		/// <param name="serializedObject">The SerializedObject being edited.</param>
		/// <param name="destinationIDProperty">The SerializedProperty for the DestinationID field.</param>
		public static void Draw(SerializedObject serializedObject, SerializedProperty destinationIDProperty)
		{
			TeleporterCache cache = AssetDatabase.LoadAssetAtPath<TeleporterCache>(TeleporterCache.CACHE_FULL_PATH);
			if (cache == null)
			{
				EditorGUILayout.HelpBox("TeleporterCache asset not found at " + TeleporterCache.CACHE_FULL_PATH + ". Create and rebuild it first.", MessageType.Warning);
				return;
			}

			if (cache.Destinations == null || cache.Destinations.Count == 0)
			{
				EditorGUILayout.HelpBox("TeleporterCache is empty. Rebuild it to populate destinations.", MessageType.Info);
				return;
			}

			// Build dropdown entries grouped by scene.
			List<string> displayOptions = new List<string>();
			List<string> idOptions = new List<string>();

			displayOptions.Add("(None)");
			idOptions.Add("");

			foreach (KeyValuePair<string, TeleporterCacheEntry> kvp in cache.Destinations)
			{
				TeleporterCacheEntry entry = kvp.Value;
				displayOptions.Add($"{entry.SceneName}/{entry.DisplayName} ({entry.DestinationID.Substring(0, Mathf.Min(8, entry.DestinationID.Length))})");
				idOptions.Add(entry.DestinationID);
			}

			// Find the current selection index.
			int currentIndex = 0;
			string currentID = destinationIDProperty.stringValue;
			if (!string.IsNullOrEmpty(currentID))
			{
				currentIndex = idOptions.IndexOf(currentID);
				if (currentIndex < 0)
				{
					EditorGUILayout.HelpBox($"DestinationID '{currentID}' not found in TeleporterCache. The destination may have been removed. Rebuild the cache or select a new destination.", MessageType.Error);
					currentIndex = 0;
				}
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Destination Selection", EditorStyles.boldLabel);

			int newIndex = EditorGUILayout.Popup("Destination", currentIndex, displayOptions.ToArray());
			if (newIndex != currentIndex)
			{
				serializedObject.Update();
				destinationIDProperty.stringValue = idOptions[newIndex];
				serializedObject.ApplyModifiedProperties();
			}
		}
	}
}
#endif