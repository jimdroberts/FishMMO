#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Custom editor for the teleporter cache asset.
	/// Displays cache health, highlights invalid teleporter links, and provides
	/// one-click scene navigation to broken teleporter objects for quick fixes.
	/// </summary>
	[CustomEditor(typeof(TeleporterCache))]
	public class TeleporterCacheEditor : Editor
	{
		/// <summary>
		/// Represents a single invalid teleporter entry shown in the inspector workflow.
		/// Contains enough metadata to locate and select the source object in the editor.
		/// </summary>
		private struct InvalidTeleporterIssue
		{
			/// <summary>
			/// Composite teleporter key, typically SceneName/TeleporterName.
			/// </summary>
			public string CompositeKey;

			/// <summary>
			/// Name of the scene that contains the teleporter.
			/// </summary>
			public string SceneName;

			/// <summary>
			/// Asset path to the scene that contains the teleporter.
			/// </summary>
			public string ScenePath;

			/// <summary>
			/// Name of the teleporter object.
			/// </summary>
			public string TeleporterName;

			/// <summary>
			/// Destination identifier referenced by the teleporter.
			/// </summary>
			public string DestinationId;

			/// <summary>
			/// Serialized global object identifier used for exact object resolution.
			/// </summary>
			public string TeleporterGlobalObjectId;

			/// <summary>
			/// Cached teleporter world position used as fallback disambiguation.
			/// </summary>
			public Vector3 Position;

			/// <summary>
			/// Human-readable validation failure reason.
			/// </summary>
			public string Reason;
		}

		private static bool showInvalidTeleporters = true;
		private static bool showRawCache;
		private static Vector2 invalidScroll;
		private static string searchText = string.Empty;

		/// <summary>
		/// Draws the inspector with cache summary, rebuild actions, invalid-entry list,
		/// and an optional raw cache foldout for low-level inspection.
		/// </summary>
		public override void OnInspectorGUI()
		{
			var script = (TeleporterCache)target;
			serializedObject.Update();

			List<InvalidTeleporterIssue> issues = BuildInvalidTeleporterIssues(script, out int totalTeleporters, out int validTeleporters);
			int invalidTeleporters = issues.Count;

			DrawSummary(script, totalTeleporters, validTeleporters, invalidTeleporters);

			EditorGUILayout.Space();
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Rebuild", GUILayout.Height(36)))
				{
					bool success = script.Rebuild();
					EditorUtility.SetDirty(script);
					AssetDatabase.SaveAssets();

					if (!success)
					{
						EditorUtility.DisplayDialog("Teleporter Cache Rebuild", "Rebuild completed with errors. Review invalid teleporter entries below.", "OK");
					}
				}

				EditorGUI.BeginDisabledGroup(invalidTeleporters == 0);
				if (GUILayout.Button("Open First Invalid", GUILayout.Height(36)))
				{
					NavigateToTeleporter(issues[0]);
				}
				EditorGUI.EndDisabledGroup();
			}

			EditorGUILayout.Space();
			showInvalidTeleporters = EditorGUILayout.Foldout(showInvalidTeleporters, $"Invalid Teleporters ({invalidTeleporters})", true);
			if (showInvalidTeleporters)
			{
				DrawInvalidTeleportersList(issues);
			}

			EditorGUILayout.Space();
			showRawCache = EditorGUILayout.Foldout(showRawCache, "Raw Cache Data", true);
			if (showRawCache)
			{
				SerializedProperty destinationsProperty = serializedObject.FindProperty("Destinations");
				SerializedProperty teleportersProperty = serializedObject.FindProperty("Teleporters");
				EditorGUILayout.PropertyField(destinationsProperty, true);
				EditorGUILayout.PropertyField(teleportersProperty, true);
			}

			serializedObject.ApplyModifiedProperties();
		}

		/// <summary>
		/// Draws high-level cache statistics and a health progress bar.
		/// </summary>
		/// <param name="cache">The teleporter cache being inspected.</param>
		/// <param name="totalTeleporters">Total number of cached teleporters.</param>
		/// <param name="validTeleporters">Number of teleporters with valid destination links.</param>
		/// <param name="invalidTeleporters">Number of teleporters with invalid destination links.</param>
		private static void DrawSummary(TeleporterCache cache, int totalTeleporters, int validTeleporters, int invalidTeleporters)
		{
			EditorGUILayout.LabelField("Cache Summary", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Destinations", cache.Destinations != null ? cache.Destinations.Count.ToString() : "0");
			EditorGUILayout.LabelField("Teleporters", totalTeleporters.ToString());

			Color previous = GUI.contentColor;
			GUI.contentColor = Color.green;
			EditorGUILayout.LabelField("Valid", validTeleporters.ToString());
			GUI.contentColor = previous;

			if (invalidTeleporters > 0)
			{
				GUI.contentColor = new Color(0.9f, 0.3f, 0.25f);
				EditorGUILayout.LabelField("Invalid", invalidTeleporters.ToString());
				GUI.contentColor = previous;
			}

			float health = totalTeleporters <= 0 ? 1f : (float)validTeleporters / totalTeleporters;
			Rect barRect = EditorGUILayout.GetControlRect(false, 18f);
			EditorGUI.ProgressBar(barRect, health, $"Health: {health * 100f:0.0}%");
		}

		/// <summary>
		/// Builds and returns invalid teleporter issues by validating teleporter entries
		/// against destination entries in the cache.
		/// </summary>
		/// <param name="cache">The teleporter cache to validate.</param>
		/// <param name="totalTeleporters">Outputs the total teleporter count found in cache.</param>
		/// <param name="validTeleporters">Outputs the number of valid teleporter links.</param>
		/// <returns>A sorted list of invalid teleporter issues.</returns>
		private static List<InvalidTeleporterIssue> BuildInvalidTeleporterIssues(TeleporterCache cache, out int totalTeleporters, out int validTeleporters)
		{
			totalTeleporters = 0;
			validTeleporters = 0;
			List<InvalidTeleporterIssue> issues = new List<InvalidTeleporterIssue>();

			if (cache == null || cache.Teleporters == null)
			{
				return issues;
			}

			foreach (KeyValuePair<string, SceneTeleporterCacheEntry> kvp in cache.Teleporters)
			{
				totalTeleporters++;
				SceneTeleporterCacheEntry entry = kvp.Value;

				if (entry == null)
				{
					issues.Add(new InvalidTeleporterIssue()
					{
						CompositeKey = kvp.Key,
						Reason = "Missing cache entry object.",
					});
					continue;
				}

				if (string.IsNullOrEmpty(entry.DestinationID))
				{
					issues.Add(CreateIssue(kvp.Key, entry, "No DestinationID assigned."));
					continue;
				}

				if (cache.Destinations == null || !cache.Destinations.TryGetValue(entry.DestinationID, out TeleporterCacheEntry destinationEntry))
				{
					issues.Add(CreateIssue(kvp.Key, entry, "DestinationID not found in destination cache."));
					continue;
				}

				validTeleporters++;
			}

			issues.Sort((a, b) =>
			{
				int sceneCompare = string.Compare(a.SceneName, b.SceneName, System.StringComparison.OrdinalIgnoreCase);
				if (sceneCompare != 0)
				{
					return sceneCompare;
				}

				return string.Compare(a.TeleporterName, b.TeleporterName, System.StringComparison.OrdinalIgnoreCase);
			});

			return issues;
		}

		/// <summary>
		/// Creates an invalid teleporter issue record from a cache entry and reason text.
		/// </summary>
		/// <param name="compositeKey">The composite key that identifies the teleporter entry.</param>
		/// <param name="entry">The source cache entry for the teleporter.</param>
		/// <param name="reason">Validation reason shown to the user.</param>
		/// <returns>A populated invalid issue record.</returns>
		private static InvalidTeleporterIssue CreateIssue(string compositeKey, SceneTeleporterCacheEntry entry, string reason)
		{
			return new InvalidTeleporterIssue()
			{
				CompositeKey = compositeKey,
				SceneName = entry.SceneName,
				ScenePath = entry.ScenePath,
				TeleporterName = entry.TeleporterName,
				DestinationId = entry.DestinationID,
				TeleporterGlobalObjectId = entry.TeleporterGlobalObjectId,
				Position = entry.Position,
				Reason = reason,
			};
		}

		/// <summary>
		/// Draws the invalid teleporter list with filtering and per-entry fix actions.
		/// </summary>
		/// <param name="issues">The invalid teleporter issues to render.</param>
		private static void DrawInvalidTeleportersList(List<InvalidTeleporterIssue> issues)
		{
			if (issues.Count == 0)
			{
				Color previous = GUI.backgroundColor;
				GUI.backgroundColor = new Color(0.80f, 1f, 0.80f);
				EditorGUILayout.HelpBox("No invalid teleporters found.", MessageType.Info);
				GUI.backgroundColor = previous;
				return;
			}

			searchText = EditorGUILayout.TextField("Search", searchText);
			EditorGUILayout.LabelField("Broken entries only. Use Open Scene + Select to jump directly to fix targets.", EditorStyles.wordWrappedMiniLabel);

			invalidScroll = EditorGUILayout.BeginScrollView(invalidScroll, GUILayout.MinHeight(180f), GUILayout.MaxHeight(420f));
			for (int i = 0; i < issues.Count; i++)
			{
				InvalidTeleporterIssue issue = issues[i];

				if (!MatchesSearch(issue, searchText))
				{
					continue;
				}

				using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
				{
					EditorGUILayout.LabelField($"{issue.SceneName}/{issue.TeleporterName}", EditorStyles.boldLabel);
					EditorGUILayout.LabelField($"Reason: {issue.Reason}", EditorStyles.wordWrappedMiniLabel);
					EditorGUILayout.LabelField($"DestinationID: {issue.DestinationId}", EditorStyles.wordWrappedMiniLabel);

					using (new EditorGUILayout.HorizontalScope())
					{
						if (GUILayout.Button("Open Scene + Select", GUILayout.Height(24f)))
						{
							NavigateToTeleporter(issue);
						}

						if (GUILayout.Button("Copy DestinationID", GUILayout.Height(24f)))
						{
							EditorGUIUtility.systemCopyBuffer = issue.DestinationId ?? string.Empty;
						}
					}
				}
			}
			EditorGUILayout.EndScrollView();
		}

		/// <summary>
		/// Returns true when an issue matches the provided search term.
		/// </summary>
		/// <param name="issue">The issue to test.</param>
		/// <param name="search">User-entered search text.</param>
		/// <returns>True if the issue matches; otherwise, false.</returns>
		private static bool MatchesSearch(InvalidTeleporterIssue issue, string search)
		{
			if (string.IsNullOrEmpty(search))
			{
				return true;
			}

			string term = search.Trim();
			if (term.Length == 0)
			{
				return true;
			}

			return ContainsIgnoreCase(issue.SceneName, term)
				|| ContainsIgnoreCase(issue.TeleporterName, term)
				|| ContainsIgnoreCase(issue.DestinationId, term)
				|| ContainsIgnoreCase(issue.Reason, term)
				|| ContainsIgnoreCase(issue.CompositeKey, term);
		}

		/// <summary>
		/// Performs a case-insensitive substring match.
		/// </summary>
		/// <param name="source">The source string.</param>
		/// <param name="value">The value to search for.</param>
		/// <returns>True if <paramref name="value"/> exists within <paramref name="source"/>.</returns>
		private static bool ContainsIgnoreCase(string source, string value)
		{
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
			{
				return false;
			}

			return source.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>
		/// Opens the owning scene and selects the teleporter object for a given invalid issue.
		/// </summary>
		/// <param name="issue">The invalid issue to navigate to.</param>
		private static void NavigateToTeleporter(InvalidTeleporterIssue issue)
		{
			string scenePath = ResolveScenePath(issue);
			if (string.IsNullOrEmpty(scenePath))
			{
				EditorUtility.DisplayDialog("Open Teleporter", $"Could not find scene path for '{issue.SceneName}'.", "OK");
				return;
			}

			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}

			Scene openedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			if (!openedScene.IsValid())
			{
				EditorUtility.DisplayDialog("Open Teleporter", $"Failed to open scene '{scenePath}'.", "OK");
				return;
			}

			GameObject teleporterObject = ResolveTeleporterObject(issue, openedScene);
			if (teleporterObject == null)
			{
				EditorUtility.DisplayDialog("Open Teleporter", $"Scene opened, but teleporter '{issue.TeleporterName}' could not be resolved.", "OK");
				return;
			}

			Selection.activeGameObject = teleporterObject;
			EditorGUIUtility.PingObject(teleporterObject);
		}

		/// <summary>
		/// Resolves the scene asset path for an invalid teleporter issue.
		/// </summary>
		/// <param name="issue">The invalid issue that contains scene metadata.</param>
		/// <returns>A valid Unity scene asset path, or an empty string when not found.</returns>
		private static string ResolveScenePath(InvalidTeleporterIssue issue)
		{
			if (!string.IsNullOrEmpty(issue.ScenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(issue.ScenePath) != null)
			{
				return issue.ScenePath.Replace('\\', '/');
			}

			if (string.IsNullOrEmpty(issue.SceneName))
			{
				return string.Empty;
			}

			string[] sceneGuids = AssetDatabase.FindAssets(issue.SceneName + " t:Scene");
			for (int i = 0; i < sceneGuids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
				string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
				if (string.Equals(fileName, issue.SceneName, System.StringComparison.OrdinalIgnoreCase))
				{
					return path;
				}
			}

			return string.Empty;
		}

		/// <summary>
		/// Resolves a teleporter object in an opened scene using GlobalObjectId first,
		/// then falls back to name and nearest-position matching.
		/// </summary>
		/// <param name="issue">The invalid issue that contains teleporter metadata.</param>
		/// <param name="openedScene">The already opened scene to search.</param>
		/// <returns>The resolved teleporter game object, or null if no match is found.</returns>
		private static GameObject ResolveTeleporterObject(InvalidTeleporterIssue issue, Scene openedScene)
		{
			if (!string.IsNullOrEmpty(issue.TeleporterGlobalObjectId) && GlobalObjectId.TryParse(issue.TeleporterGlobalObjectId, out GlobalObjectId globalObjectId))
			{
				Object resolvedObject = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
				if (resolvedObject is GameObject resolvedGo)
				{
					return resolvedGo;
				}

				if (resolvedObject is Component component)
				{
					return component.gameObject;
				}
			}

			GameObject[] roots = openedScene.GetRootGameObjects();
			SceneTeleporter bestMatch = null;
			float bestDistance = float.MaxValue;

			for (int i = 0; i < roots.Length; i++)
			{
				SceneTeleporter[] teleporters = roots[i].GetComponentsInChildren<SceneTeleporter>(true);
				for (int j = 0; j < teleporters.Length; j++)
				{
					SceneTeleporter teleporter = teleporters[j];
					if (teleporter == null)
					{
						continue;
					}

					if (!string.Equals(teleporter.name.Trim(), issue.TeleporterName, System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					float distance = (teleporter.transform.position - issue.Position).sqrMagnitude;
					if (distance < bestDistance)
					{
						bestDistance = distance;
						bestMatch = teleporter;
					}
				}
			}

			return bestMatch != null ? bestMatch.gameObject : null;
		}
	}
}
#endif