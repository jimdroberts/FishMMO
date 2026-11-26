using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Character creation offers only the starting locations in the world scene details cache, so a cache without one for a
	/// playable race means that race cannot be created at all.
	/// </summary>
	/// <remarks>
	/// It happened: starting locations were added by hand to generated respawn points, and a re-cut, which rebuilds
	/// generated objects, dropped every one of them. The cut now marks each capital's respawn point
	/// (<c>StartingLocations</c>); this guards the shipped cache, which is what the login server and the client read.
	/// </remarks>
	public class StartingLocationTests
	{
		private static WorldSceneDetailsCache LoadCache()
		{
			return AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(
				ProjectFiles.Path(WorldSceneDetailsCache.CACHE_PATH + WorldSceneDetailsCache.CACHE_FILE_NAME));
		}

		[Test]
		public void EveryPlayableRace_HasAStartingLocationInTheCache()
		{
			WorldSceneDetailsCache cache = LoadCache();
			Assert.IsNotNull(cache, "The world scene details cache is missing.");
			Assert.IsNotNull(cache.Scenes, "The cache has no scene dictionary.");

			var offered = new HashSet<RaceTemplate>();
			int locations = 0;
			foreach (KeyValuePair<string, WorldSceneDetails> scene in cache.Scenes)
			{
				foreach (KeyValuePair<string, CharacterInitialSpawnPositionDetails> start in scene.Value.InitialSpawnPositions)
				{
					locations++;
					Assert.AreEqual(scene.Key, start.Value.SceneName,
						$"Starting location '{start.Key}' is listed under '{scene.Key}' but names scene '{start.Value.SceneName}'.");
					if (start.Value.AllowedRaces == null)
					{
						continue;
					}
					foreach (RaceTemplate race in start.Value.AllowedRaces)
					{
						if (race != null)
						{
							offered.Add(race);
						}
					}
				}
			}

			Assert.Greater(locations, 0,
				"No world scene has a starting location, so character creation offers none. Run Maintenance → World → " +
				"\"Place starting locations (all world scenes)\" (it marks every capital and rebuilds the cache).");

			int playable = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(RaceTemplate)))
			{
				var race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (race == null || !race.Playable)
				{
					continue;
				}
				playable++;
				Assert.IsTrue(offered.Contains(race),
					$"The playable race '{race.name}' has no starting location, so it cannot be created. Run Maintenance → World → " +
					"\"Place starting locations (all world scenes)\".");
			}
			Assert.Greater(playable, 0, "No race is playable, so this proves nothing.");
		}
	}
}
