using UnityEngine;
using UnityEditor;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using System;

namespace FishMMO.Shared
{
	public class WorldSceneDetailsCacheBuilder
	{
		[MenuItem("FishMMO/Build/Rebuild World Scene Details", priority = -10)]
		public static void Rebuild()
		{
			// Try loading the addressable asset asynchronously
			AsyncOperationHandle handle = Addressables.LoadAssetAsync<WorldSceneDetailsCache>(WorldSceneDetailsCache.CACHE_FULL_PATH);

			// Callback when the asset is loaded
			handle.Completed += (op) =>
			{
				if (op.Status == AsyncOperationStatus.Succeeded)
				{
					// Successfully loaded the asset
					WorldSceneDetailsCache worldDetailsCache = op.Result as WorldSceneDetailsCache;
					if (worldDetailsCache != null)
					{
						UnityEngine.Debug.Log($"Addressable asset loaded: {worldDetailsCache.name}");
						worldDetailsCache.Rebuild();
						EditorUtility.SetDirty(worldDetailsCache);
						AssetDatabase.SaveAssets();
					}
					else
					{
						UnityEngine.Debug.LogError("Failed to cast the loaded asset to WorldSceneDetailsCache.");
					}
				}
				else
				{
					// If the asset failed to load, log the error and handle accordingly
					UnityEngine.Debug.LogError($"Failed to load Addressable asset: {WorldSceneDetailsCache.CACHE_FULL_PATH}");
					HandleLoadFailure();
				}
			};
		}

		// Handle the failure by creating a new instance of the asset if it couldn't be loaded
		private static void HandleLoadFailure()
		{
			try
			{
				// Create a new instance of WorldSceneDetailsCache if loading fails
				WorldSceneDetailsCache worldDetailsCache = ScriptableObject.CreateInstance<WorldSceneDetailsCache>();
				worldDetailsCache.Rebuild();
				EditorUtility.SetDirty(worldDetailsCache);

				// Create the asset and save it
				AssetDatabase.CreateAsset(worldDetailsCache, WorldSceneDetailsCache.CACHE_FULL_PATH);

				AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
				if (settings == null)
				{
					UnityEngine.Debug.LogError("Addressable Asset Settings not found.");
					return;
				}

				AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(WorldSceneDetailsCache.CACHE_FULL_PATH));
				if (entry == null)
				{
					entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(WorldSceneDetailsCache.CACHE_FULL_PATH), settings.DefaultGroup);
					UnityEngine.Debug.Log($"Asset '{WorldSceneDetailsCache.CACHE_FULL_PATH}' added to Addressables.");
				}

				EditorUtility.SetDirty(settings);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}
			catch (Exception ex)
			{
				// Log any error that occurs during asset creation
				UnityEngine.Debug.LogError($"Error during asset creation: {ex.Message}");
			}
		}
	}
}