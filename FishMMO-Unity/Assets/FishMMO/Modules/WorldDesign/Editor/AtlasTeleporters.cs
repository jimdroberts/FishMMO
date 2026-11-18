#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One scene-to-scene link, as the teleporter cache knows it.</summary>
	public sealed class AtlasLink
	{
		public SceneTeleporterCacheEntry Teleporter;
		/// <summary>Null when the teleporter has no destination or its destination is gone.</summary>
		public TeleporterCacheEntry Destination;
		public string FromScene => Teleporter.SceneName;
		public string ToScene => Destination != null ? Destination.SceneName : null;
		public bool Unassigned => string.IsNullOrEmpty(Teleporter.DestinationID);
		public bool Missing => !Unassigned && Destination == null;
	}

	/// <summary>
	/// Reads teleporter links from the teleporter cache and connects or disconnects them.
	/// </summary>
	/// <remarks>
	/// The designer only connects teleporters that already exist; it never creates, moves or
	/// renames one. Connecting writes the teleporter's <c>DestinationID</c> — the same write the
	/// inspector dropdown makes — saves only that scene, checks that no destination GUID in the
	/// scene changed on save, and updates the teleporter cache and the scene details cache entry.
	/// </remarks>
	public static class AtlasTeleporters
	{
		public static TeleporterCache Cache() => AssetDatabase.LoadAssetAtPath<TeleporterCache>(TeleporterCache.CACHE_FULL_PATH);

		/// <summary>Every teleporter in the cache with its resolved destination.</summary>
		public static List<AtlasLink> Links(TeleporterCache cache)
		{
			var links = new List<AtlasLink>();
			if (cache == null || cache.Teleporters == null)
			{
				return links;
			}
			foreach (KeyValuePair<string, SceneTeleporterCacheEntry> pair in cache.Teleporters)
			{
				if (pair.Value == null)
				{
					continue;
				}
				TeleporterCacheEntry destination = null;
				if (!string.IsNullOrEmpty(pair.Value.DestinationID) && cache.Destinations != null)
				{
					cache.Destinations.TryGetValue(pair.Value.DestinationID, out destination);
				}
				links.Add(new AtlasLink { Teleporter = pair.Value, Destination = destination });
			}
			links.Sort((a, b) =>
			{
				int c = string.CompareOrdinal(a.FromScene, b.FromScene);
				return c != 0 ? c : string.CompareOrdinal(a.Teleporter.TeleporterName, b.Teleporter.TeleporterName);
			});
			return links;
		}

		/// <summary>Destinations in one scene.</summary>
		public static List<TeleporterCacheEntry> DestinationsIn(TeleporterCache cache, string sceneName)
		{
			var result = new List<TeleporterCacheEntry>();
			if (cache == null || cache.Destinations == null)
			{
				return result;
			}
			foreach (TeleporterCacheEntry entry in cache.Destinations.Values)
			{
				if (entry != null && entry.SceneName == sceneName)
				{
					result.Add(entry);
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
			return result;
		}

		/// <summary>Points a teleporter at a destination (or clears it with null). Returns false and says why on failure.</summary>
		public static bool Connect(SceneTeleporterCacheEntry teleporter, TeleporterCacheEntry destination, out string error)
		{
			error = null;
			if (teleporter == null || string.IsNullOrEmpty(teleporter.ScenePath))
			{
				error = "The teleporter has no scene path. Rebuild the teleporter cache.";
				return false;
			}
			if (!File.Exists(teleporter.ScenePath))
			{
				error = $"Scene {teleporter.ScenePath} does not exist.";
				return false;
			}

			Scene scene = SceneManager.GetSceneByPath(teleporter.ScenePath);
			bool openedHere = false;
			if (!scene.IsValid() || !scene.isLoaded)
			{
				scene = EditorSceneManager.OpenScene(teleporter.ScenePath, OpenSceneMode.Additive);
				openedHere = true;
			}
			else if (scene.isDirty)
			{
				if (!EditorUtility.DisplayDialog("Unsaved scene", $"{scene.name} has unsaved changes. Connecting saves the scene, including those changes.", "Save and connect", "Cancel"))
				{
					error = "Cancelled.";
					return false;
				}
			}

			try
			{
				SceneTeleporter target = FindTeleporter(scene, teleporter);
				if (target == null)
				{
					error = $"No teleporter named '{teleporter.TeleporterName}' in {scene.name}. Rebuild the teleporter cache.";
					return false;
				}

				Dictionary<string, string> before = DestinationIDs(scene);

				var serialized = new SerializedObject(target);
				SerializedProperty property = serialized.FindProperty(nameof(SceneTeleporter.DestinationID));
				property.stringValue = destination != null ? destination.DestinationID : string.Empty;
				serialized.ApplyModifiedProperties();
				EditorUtility.SetDirty(target);
				EditorSceneManager.MarkSceneDirty(scene);
				if (!EditorSceneManager.SaveScene(scene))
				{
					error = $"Saving {scene.name} failed.";
					return false;
				}

				// The scene re-save audit: a destination GUID lost on save silently breaks every
				// teleporter aimed at it.
				Dictionary<string, string> after = DestinationIDs(scene);
				foreach (KeyValuePair<string, string> pair in before)
				{
					if (!after.TryGetValue(pair.Key, out string id) || id != pair.Value)
					{
						Debug.LogError($"[World Atlas] Saving {scene.name} changed the destination ID of '{pair.Key}' from {pair.Value} to {id ?? "(missing)"}. Revert the scene and report this.");
					}
				}

				teleporter.DestinationID = destination != null ? destination.DestinationID : string.Empty;
				TeleporterCache cache = Cache();
				if (cache != null)
				{
					EditorUtility.SetDirty(cache);
				}
				UpdateSceneDetails(teleporter, destination);
				AssetDatabase.SaveAssets();
				return true;
			}
			finally
			{
				if (openedHere)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}
		}

		private static SceneTeleporter FindTeleporter(Scene scene, SceneTeleporterCacheEntry entry)
		{
			if (!string.IsNullOrEmpty(entry.TeleporterGlobalObjectId) &&
				GlobalObjectId.TryParse(entry.TeleporterGlobalObjectId, out GlobalObjectId id))
			{
				if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is GameObject go && go.scene == scene)
				{
					SceneTeleporter found = go.GetComponent<SceneTeleporter>();
					if (found != null)
					{
						return found;
					}
				}
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (SceneTeleporter candidate in root.GetComponentsInChildren<SceneTeleporter>(true))
				{
					if (TeleporterKey.Normalize(candidate.name) == TeleporterKey.Normalize(entry.TeleporterName))
					{
						return candidate;
					}
				}
			}
			return null;
		}

		private static Dictionary<string, string> DestinationIDs(Scene scene)
		{
			var ids = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (TeleporterDestination destination in root.GetComponentsInChildren<TeleporterDestination>(true))
				{
					ids[GlobalObjectId.GetGlobalObjectIdSlow(destination).ToString()] = destination.DestinationID;
				}
			}
			return ids;
		}

		private static void UpdateSceneDetails(SceneTeleporterCacheEntry teleporter, TeleporterCacheEntry destination)
		{
			WorldSceneDetailsCache cache = WorldEditorAssets.SceneDetails();
			if (cache == null || cache.Scenes == null || !cache.Scenes.TryGetValue(teleporter.SceneName, out WorldSceneDetails details) || details == null)
			{
				return;
			}
			string key = TeleporterKey.Normalize(teleporter.TeleporterName);
			if (destination == null)
			{
				details.Teleporters.Remove(key);
			}
			else
			{
				details.Teleporters[key] = new SceneTeleporterDetails
				{
					ToScene = destination.SceneName,
					ToPosition = destination.Position,
					ToRotation = destination.Rotation,
				};
			}
			EditorUtility.SetDirty(cache);
		}

		/// <summary>Opens a scene (asking to save the current ones) and selects the named teleporters.</summary>
		public static void OpenAndSelect(string scenePath, IReadOnlyCollection<string> teleporterNames, bool additive)
		{
			if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
			{
				return;
			}
			if (!additive && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}
			Scene scene = EditorSceneManager.OpenScene(scenePath, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
			if (teleporterNames == null || teleporterNames.Count == 0)
			{
				return;
			}
			var selection = new List<UnityEngine.Object>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (SceneTeleporter teleporter in root.GetComponentsInChildren<SceneTeleporter>(true))
				{
					foreach (string name in teleporterNames)
					{
						if (TeleporterKey.Normalize(teleporter.name) == TeleporterKey.Normalize(name))
						{
							selection.Add(teleporter.gameObject);
						}
					}
				}
			}
			if (selection.Count > 0)
			{
				Selection.objects = selection.ToArray();
				SceneView.FrameLastActiveSceneView();
			}
		}
	}
}
#endif
