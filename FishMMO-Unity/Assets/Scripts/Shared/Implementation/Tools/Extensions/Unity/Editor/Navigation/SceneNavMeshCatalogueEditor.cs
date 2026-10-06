#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Server.Implementation.World.SceneServer.Navigation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Keeps <see cref="SceneNavMeshCatalogue"/> in step with the scenes' baked NavMesh surfaces, and every NavMesh
	/// in the server-only addressables group.
	/// </summary>
	/// <remarks>
	/// A scene's entry is written whenever the scene is saved, from its baked <see cref="NavMeshSurface"/>: the
	/// surface's data, and its pose, since a surface adds its data at its own transform. A scene saved with no baked
	/// surface loses its entry. The data asset is registered in <see cref="ServerAddressables.GroupName"/>
	/// explicitly. Builds and play mode strip the surfaces themselves (<see cref="NavMeshSurfaceStripper"/>), so
	/// this is the only way a NavMesh reaches the scene server.
	/// </remarks>
	[InitializeOnLoad]
	public static class SceneNavMeshCatalogueEditor
	{
		static SceneNavMeshCatalogueEditor()
		{
			EditorSceneManager.sceneSaved -= OnSceneSaved;
			EditorSceneManager.sceneSaved += OnSceneSaved;
		}

		private static void OnSceneSaved(Scene scene) => Sync(scene);

		/// <summary>The project's catalogue, or null when there is none.</summary>
		public static SceneNavMeshCatalogue Find()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SceneNavMeshCatalogue)))
			{
				var catalogue = AssetDatabase.LoadAssetAtPath<SceneNavMeshCatalogue>(AssetDatabase.GUIDToAssetPath(guid));
				if (catalogue != null)
				{
					return catalogue;
				}
			}
			return null;
		}

		/// <summary>
		/// Writes <paramref name="scene"/>'s entry from its baked surface, or removes it when the scene has none.
		/// Scenes not saved to disk are left alone. Returns true when the scene has an entry afterwards.
		/// </summary>
		public static bool Sync(Scene scene)
		{
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
			{
				return false;
			}
			NavMeshSurface baked = null;
			int surfaces = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (NavMeshSurface surface in root.GetComponentsInChildren<NavMeshSurface>(true))
				{
					if (surface.navMeshData != null && AssetDatabase.Contains(surface.navMeshData))
					{
						baked ??= surface;
						surfaces++;
					}
				}
			}
			if (baked == null)
			{
				Remove(scene.name);
				return false;
			}
			if (surfaces > 1)
			{
				Debug.LogWarning($"[NavMesh catalogue] '{scene.name}' has {surfaces} baked NavMesh surfaces; the scene server loads one NavMesh per scene, so only '{baked.name}' is used.");
			}
			Transform pose = baked.transform;
			return Put(scene.name, baked.navMeshData, pose.position, pose.rotation);
		}

		/// <summary>Puts <paramref name="data"/> in the catalogue as <paramref name="sceneName"/>'s NavMesh, and its asset in the server group.</summary>
		public static bool Put(string sceneName, NavMeshData data, Vector3 position, Quaternion rotation)
		{
			SceneNavMeshCatalogue catalogue = Find();
			if (catalogue == null)
			{
				Debug.LogWarning($"[NavMesh catalogue] No {nameof(SceneNavMeshCatalogue)} asset; '{sceneName}' has no NavMesh on the scene server.");
				return false;
			}
			string path = AssetDatabase.GetAssetPath(data);
			if (string.IsNullOrEmpty(path))
			{
				return false;
			}
			ServerAddressables.Register(path);

			SceneNavMeshCatalogue.Entry entry = catalogue.Entries.Find(e => e != null && e.SceneName == sceneName);
			if (entry != null && entry.Data == data && entry.Position == position && entry.Rotation == rotation)
			{
				return true;
			}
			Undo.RecordObject(catalogue, "NavMesh catalogue");
			if (entry == null)
			{
				entry = new SceneNavMeshCatalogue.Entry { SceneName = sceneName };
				catalogue.Entries.Add(entry);
				catalogue.Entries.Sort((a, b) => string.CompareOrdinal(a?.SceneName, b?.SceneName));
			}
			entry.Data = data;
			entry.Position = position;
			entry.Rotation = rotation;
			catalogue.Entries.RemoveAll(e => e == null || e.Data == null);
			catalogue.Invalidate();
			EditorUtility.SetDirty(catalogue);
			AssetDatabase.SaveAssetIfDirty(catalogue);
			return true;
		}

		/// <summary>Takes <paramref name="sceneName"/> out of the catalogue, and its NavMesh asset out of the server group.</summary>
		public static void Remove(string sceneName)
		{
			SceneNavMeshCatalogue catalogue = Find();
			if (catalogue == null)
			{
				return;
			}
			var gone = new List<SceneNavMeshCatalogue.Entry>(catalogue.Entries.FindAll(e => e != null && e.SceneName == sceneName));
			if (gone.Count == 0)
			{
				return;
			}
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			foreach (SceneNavMeshCatalogue.Entry entry in gone)
			{
				string path = entry.Data != null ? AssetDatabase.GetAssetPath(entry.Data) : null;
				if (settings != null && !string.IsNullOrEmpty(path))
				{
					settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(path));
				}
				catalogue.Entries.Remove(entry);
			}
			catalogue.Invalidate();
			EditorUtility.SetDirty(catalogue);
			AssetDatabase.SaveAssetIfDirty(catalogue);
		}
	}
}
#endif
