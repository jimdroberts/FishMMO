using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "WorldSceneDetails", menuName = "FishMMO/World Scene Details")]
	public class WorldSceneDetailsCache : ScriptableObject
	{
		public const string CACHE_PATH = "Assets/Prefabs/Shared/";
		public const string CACHE_FILE_NAME = "WorldSceneDetails.asset";
		public const string CACHE_FULL_PATH = CACHE_PATH + CACHE_FILE_NAME;

		public List<WorldSceneDetailsCacheReader> WorldSceneDetailsReaders;

		public WorldSceneDetailsDictionary Scenes = new WorldSceneDetailsDictionary();

		public bool Rebuild()
		{
			if (WorldSceneDetailsReaders == null || WorldSceneDetailsReaders.Count < 1)
			{
				return false;
			}
			bool result = false;
			foreach (WorldSceneDetailsCacheReader worldReader in WorldSceneDetailsReaders)
			{
				bool currentResult = worldReader.Rebuild(ref Scenes);
				if (currentResult)
				{
					result = currentResult;
				}
			}
			return result;
		}
	}
}