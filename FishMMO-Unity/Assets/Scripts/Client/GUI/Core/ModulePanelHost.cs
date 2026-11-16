using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// Puts modules' UI panels into the GUI scenes: when a scene loads, every prefab carrying the Addressables label
	/// <c>&lt;scene name&gt;_Panels</c> (for example <c>ClientWorldGUI_Panels</c>) is instantiated into it.
	/// </summary>
	/// <remarks>
	/// <para>A module's panel lives in the module's folder as a prefab, not in the GUI scene, so removing the module
	/// leaves the scene without a missing script, and nothing outside the module names the panel.</para>
	/// <para>Each panel keeps its prefab's name, which is its <see cref="UIManager"/> key: it is instantiated under an
	/// inactive holder, renamed, then moved into the scene, so its Awake (where it registers) sees the right name.
	/// A panel instantiated after the client is set still receives it: registration injects it.</para>
	/// <para>A scene with no such label costs one location lookup.</para>
	/// </remarks>
	public static class ModulePanelHost
	{
		/// <summary>The label suffix: <c>&lt;scene name&gt;</c> + this.</summary>
		public const string LabelSuffix = "_Panels";

		private static readonly Dictionary<string, List<AsyncOperationHandle<GameObject>>> loaded =
			new Dictionary<string, List<AsyncOperationHandle<GameObject>>>();
		private static GameObject holder;

		/// <summary>The label a scene's module panels carry.</summary>
		public static string LabelFor(string sceneName) => sceneName + LabelSuffix;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Install()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
			loaded.Clear();
			holder = null;
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (string.IsNullOrEmpty(scene.name) || loaded.ContainsKey(scene.path ?? scene.name))
			{
				return;
			}
			string key = scene.path ?? scene.name;
			string label = LabelFor(scene.name);
			var handles = new List<AsyncOperationHandle<GameObject>>();
			loaded[key] = handles;
			Addressables.LoadResourceLocationsAsync(label, typeof(GameObject)).Completed += locations =>
			{
				try
				{
					if (locations.Status != AsyncOperationStatus.Succeeded || locations.Result == null || locations.Result.Count == 0)
					{
						return;
					}
					foreach (IResourceLocation location in locations.Result)
					{
						AsyncOperationHandle<GameObject> panel = Addressables.LoadAssetAsync<GameObject>(location);
						handles.Add(panel);
						panel.Completed += done => Place(scene, done);
					}
				}
				finally
				{
					Addressables.Release(locations);
				}
			};
		}

		private static void Place(Scene scene, AsyncOperationHandle<GameObject> done)
		{
			if (done.Status != AsyncOperationStatus.Succeeded || done.Result == null)
			{
				Log.Warning("ModulePanelHost", $"A module panel for '{scene.name}' failed to load: {done.OperationException?.Message}");
				return;
			}
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return;
			}
			if (holder == null)
			{
				holder = new GameObject("ModulePanelHost.Holder");
				holder.SetActive(false);
				Object.DontDestroyOnLoad(holder);
			}
			GameObject prefab = done.Result;
			GameObject instance = Object.Instantiate(prefab, holder.transform);
			instance.name = prefab.name;
			instance.transform.SetParent(null, false);
			SceneManager.MoveGameObjectToScene(instance, scene);
		}

		private static void OnSceneUnloaded(Scene scene)
		{
			string key = scene.path ?? scene.name;
			if (!loaded.TryGetValue(key, out List<AsyncOperationHandle<GameObject>> handles))
			{
				return;
			}
			loaded.Remove(key);
			// The instances went with the scene; the prefabs are released here.
			foreach (AsyncOperationHandle<GameObject> handle in handles)
			{
				if (handle.IsValid())
				{
					Addressables.Release(handle);
				}
			}
		}
	}
}
