using UnityEngine;
using UnityEditor;

namespace FishMMO.Shared
{
	public class WorldSceneDetailsCacheBuilder
	{
		[MenuItem("FishMMO/Build/World Scene Details", priority = -9)]
		public static void Rebuild()
		{
			// rebuild world details cache, this includes teleporters, teleporter destinations, spawn points, and other constant scene data
			WorldSceneDetailsCache worldDetailsCache = AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(WorldSceneDetailsCache.CACHE_FULL_PATH);
			if (worldDetailsCache != null)
			{
				worldDetailsCache.Rebuild();
				EditorUtility.SetDirty(worldDetailsCache);
			}
			else
			{
				worldDetailsCache = ScriptableObject.CreateInstance<WorldSceneDetailsCache>();
				worldDetailsCache.Rebuild();
				EditorUtility.SetDirty(worldDetailsCache);
				AssetDatabase.CreateAsset(worldDetailsCache, WorldSceneDetailsCache.CACHE_FULL_PATH);
			}
			AssetDatabase.SaveAssets();
		}
	}
}