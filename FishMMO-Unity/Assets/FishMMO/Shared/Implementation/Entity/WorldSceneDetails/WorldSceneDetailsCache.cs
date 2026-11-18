using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject cache for storing and managing all world scene details in the game.
	/// Provides centralized access and rebuild functionality for scene configuration data.
	/// </summary>
	[CreateAssetMenu(fileName = "WorldSceneDetails", menuName = "FishMMO/World Scene Details")]
	public class WorldSceneDetailsCache : ScriptableObject
	{
		/// <summary>
		/// Path to the folder where the cache asset is stored.
		/// </summary>
		public const string CACHE_PATH = "Assets/Prefabs/Shared/";

		/// <summary>
		/// File name of the cache asset.
		/// </summary>
		public const string CACHE_FILE_NAME = "WorldSceneDetails.asset";

		/// <summary>
		/// Full path to the cache asset file.
		/// </summary>
		public const string CACHE_FULL_PATH = CACHE_PATH + CACHE_FILE_NAME;

		/// <summary>
		/// List of readers responsible for loading and rebuilding scene details from various sources.
		/// </summary>
		public List<WorldSceneDetailsCacheReader> WorldSceneDetailsReaders;

		/// <summary>
		/// Dictionary containing all scene details, keyed by scene identifier.
		/// </summary>
		public WorldSceneDetailsDictionary Scenes = new WorldSceneDetailsDictionary();

		/// <summary>
		/// Rebuilds the scene details cache by invoking all registered readers.
		/// Returns true if any reader successfully rebuilt the cache.
		/// </summary>
		/// <returns>True if the cache was rebuilt by any reader; otherwise, false.</returns>
		public bool Rebuild()
		{
			if (WorldSceneDetailsReaders == null || WorldSceneDetailsReaders.Count < 1)
			{
				return false;
			}
			bool result = false;
			// Iterate through all readers and attempt to rebuild the cache.
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