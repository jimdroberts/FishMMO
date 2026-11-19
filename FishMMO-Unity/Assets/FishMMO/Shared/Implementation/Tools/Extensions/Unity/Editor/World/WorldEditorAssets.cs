#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Asset plumbing for the Solar System and World Atlas pages: where world assets live, how a
	/// script-made asset becomes loadable at runtime, the example system, and one atlas entry per
	/// world scene.
	/// </summary>
	public static class WorldEditorAssets
	{
		public const string Root = "Assets/FishMMO/Modules/WorldSimulation/Templates/World";
		public const string BodiesFolder = Root + "/Bodies";
		public const string AtlasFolder = Root + "/Atlas";
		public const string LayersFolder = AtlasFolder + "/Layers";
		public const string ScenesFolder = AtlasFolder + "/Scenes";

		/// <summary>Every asset of a type in the project.</summary>
		public static List<T> FindAll<T>() where T : UnityEngine.Object
		{
			var result = new List<T>();
			foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
			{
				T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null)
				{
					result.Add(asset);
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
			return result;
		}

		public static T FindFirst<T>() where T : UnityEngine.Object
		{
			List<T> all = FindAll<T>();
			return all.Count > 0 ? all[0] : null;
		}

		/// <summary>Creates folders on the way to a path.</summary>
		public static void EnsureFolder(string folder)
		{
			folder = folder.Replace('\\', '/').TrimEnd('/');
			if (AssetDatabase.IsValidFolder(folder))
			{
				return;
			}
			string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
			EnsureFolder(parent);
			AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
		}

		/// <summary>Creates, saves and registers a new asset with a unique file name.</summary>
		public static T Create<T>(string folder, string name, Action<T> setup = null) where T : ScriptableObject
		{
			EnsureFolder(folder);
			T asset = ScriptableObject.CreateInstance<T>();
			string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Sanitize(name)}.asset");
			asset.name = Path.GetFileNameWithoutExtension(path);
			setup?.Invoke(asset);
			AssetDatabase.CreateAsset(asset, path);
			RegisterAddressable(asset);
			Undo.RegisterCreatedObjectUndo(asset, "Create " + typeof(T).Name);
			return asset;
		}

		/// <summary>Deletes an asset after asking.</summary>
		public static bool DeleteWithConfirm(UnityEngine.Object asset)
		{
			if (asset == null)
			{
				return false;
			}
			string path = AssetDatabase.GetAssetPath(asset);
			if (!EditorUtility.DisplayDialog("Delete asset", $"Delete {path}?\n\nThis cannot be undone.", "Delete", "Cancel"))
			{
				return false;
			}
			return AssetDatabase.DeleteAsset(path);
		}

		public static string Sanitize(string name)
		{
			foreach (char c in Path.GetInvalidFileNameChars())
			{
				name = name.Replace(c, '_');
			}
			return string.IsNullOrWhiteSpace(name) ? "Unnamed" : name.Trim();
		}

		/// <summary>
		/// Puts an asset in the shared static group, labelled and addressed by name, so the client
		/// and the server load and cache it at boot. An asset made by a script is never inspected,
		/// so the inspector's own registration never runs for it.
		/// </summary>
		public static void RegisterAddressable(UnityEngine.Object asset)
		{
			RegisterAddressable(asset, Constants.SharedStaticLabel);
		}

		/// <summary>The client's own static group and label: render assets the server never loads.</summary>
		public const string ClientStaticGroup = "Client_Static_Permanent";

		/// <summary>Registers an asset in a named static group, labelled with the group's name.</summary>
		public static void RegisterAddressable(UnityEngine.Object asset, string groupName)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null || asset == null)
			{
				return;
			}
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
			if (string.IsNullOrEmpty(guid))
			{
				return;
			}
			AddressableAssetGroup group = settings.FindGroup(groupName) ?? settings.DefaultGroup;
			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			if (entry == null || entry.parentGroup != group)
			{
				entry = settings.CreateOrMoveEntry(guid, group);
			}
			if (entry == null)
			{
				Debug.LogError($"[WorldDesign] Could not make {asset.name} addressable.");
				return;
			}
			entry.address = asset.name;
			foreach (string other in new[] { Constants.SharedStaticLabel, ClientStaticGroup })
			{
				if (other != groupName)
				{
					entry.labels.Remove(other);
				}
			}
			if (!entry.labels.Contains(groupName))
			{
				settings.AddLabel(groupName);
				entry.labels.Add(groupName);
			}
			settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
		}

		// ── Scenes ────────────────────────────────────────────────────

		/// <summary>Asset paths of every world scene (and local scenes when enabled).</summary>
		public static List<string> WorldScenePaths()
		{
			var paths = new List<string>();
			AddScenes(Constants.Configuration.WorldScenePath, paths);
			if (EditorPrefs.GetBool("FishMMOEnableLocalDirectory"))
			{
				AddScenes(Constants.Configuration.LocalScenePath, paths);
			}
			paths.Sort(StringComparer.Ordinal);
			return paths;
		}

		private static void AddScenes(string folder, List<string> into)
		{
			folder = folder.Replace('\\', '/').TrimEnd('/');
			if (!Directory.Exists(folder))
			{
				return;
			}
			foreach (string file in Directory.GetFiles(folder, "*.unity", SearchOption.AllDirectories))
			{
				string path = file.Replace('\\', '/');
				if (!into.Contains(path))
				{
					into.Add(path);
				}
			}
		}

		/// <summary>Scene names that are dungeon instances.</summary>
		public static HashSet<string> DungeonSceneNames()
		{
			var names = new HashSet<string>(StringComparer.Ordinal);
			foreach (DungeonTemplate template in FindAll<DungeonTemplate>())
			{
				if (!string.IsNullOrEmpty(template.DungeonSceneName))
				{
					names.Add(template.DungeonSceneName);
				}
			}
			return names;
		}

		/// <summary>The scene details cache, or null.</summary>
		public static WorldSceneDetailsCache SceneDetails()
		{
			return AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(WorldSceneDetailsCache.CACHE_FULL_PATH);
		}

		/// <summary>A scene's size in km from its baked boundaries, or null when it has none.</summary>
		public static Vector2? SceneSizeKm(WorldSceneDetailsCache cache, string sceneName)
		{
			if (cache == null || cache.Scenes == null || !cache.Scenes.TryGetValue(sceneName, out WorldSceneDetails details))
			{
				return null;
			}
			Rect rect = MapBoundsResolver.FromSceneBoundaries(details);
			if (rect.width <= 0f || rect.height <= 0f)
			{
				return null;
			}
			return new Vector2(rect.width / 1000f, rect.height / 1000f);
		}
	
	}
}
#endif
