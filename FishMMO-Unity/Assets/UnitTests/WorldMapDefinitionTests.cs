using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every world scene actually has a map to draw.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The world map is BAKED, unlike the minimap: the minimap is a live overhead camera, but a
	/// client only ever holds the streamed part of a zone, so a live capture of a whole zone would
	/// have holes in it wherever nothing had spawned. <c>FishMMO/World Map/Bake Maps</c>
	/// photographs each scene in the editor, writes a <see cref="WorldMapDefinition"/>, and assigns
	/// it back onto that scene's <c>WorldSceneSettings</c>.
	/// </para>
	/// <para>
	/// Until that has been run for a scene, <c>ClientMapSystem.Definition</c> is null and
	/// <c>BeginLoadMapImage</c> returns at its first line without logging anything. The map panel
	/// then draws fog of war over a plain grey background — bounds still resolve from the scene
	/// boundary, so everything else about the map works and nothing reports a problem. That is the
	/// state this project shipped in, and it was diagnosed by reading the asset tree rather than
	/// from any message the client produced.
	/// </para>
	/// <para>
	/// The assertion is made against <c>WorldSceneDetails.asset</c> rather than against the scene
	/// files, because that cache is what the client actually reads. A scene can carry a definition
	/// that never reached the cache — the cache is rebuilt on demand, and one that predates the
	/// bake is exactly as broken for a player as a scene that was never baked.
	/// </para>
	/// </remarks>
	public class WorldMapDefinitionTests
	{
		/// <summary>Path the cache asset is written to; see <see cref="WorldSceneDetailsCache"/>.</summary>
		private const string CachePath =
			WorldSceneDetailsCache.CACHE_PATH + WorldSceneDetailsCache.CACHE_FILE_NAME;

		private static WorldSceneDetailsCache LoadCache()
		{
			return AssetDatabase.LoadAssetAtPath<WorldSceneDetailsCache>(CachePath);
		}

		/// <summary>Without the cache there is nothing for the client to read, map or otherwise.</summary>
		[Test]
		public void TheWorldSceneDetailsCacheExistsAndHasScenes()
		{
			WorldSceneDetailsCache cache = LoadCache();

			Assert.IsNotNull(cache,
				$"'{CachePath}' is missing. The client reads every scene's bounds, spawn points and " +
				"map definition from it; rebuild it from the FishMMO menu.");

			Assert.IsNotNull(cache.Scenes, "The cache has no scene dictionary at all.");
			Assert.Greater(cache.Scenes.Count, 0,
				"The cache lists no scenes, so the assertions below would prove nothing.");
		}

		/// <summary>
		/// The regression: a scene with no map definition renders as fog over grey, silently.
		/// </summary>
		[Test]
		public void EveryWorldSceneHasAMapDefinition()
		{
			WorldSceneDetailsCache cache = LoadCache();
			if (cache?.Scenes == null)
			{
				Assert.Ignore("The details cache is missing; TheWorldSceneDetailsCacheExistsAndHasScenes covers that.");
				return;
			}

			List<string> missing = new List<string>();

			foreach (KeyValuePair<string, WorldSceneDetails> pair in cache.Scenes)
			{
				if (pair.Value == null || pair.Value.MapDefinition == null)
				{
					missing.Add(pair.Key);
				}
			}

			Assert.IsEmpty(missing,
				"These world scenes have no WorldMapDefinition, so their world map draws fog over a " +
				"plain background and nothing logs a reason: " + string.Join(", ", missing) +
				". Run FishMMO/World Map/Bake Maps, then rebuild the world scene details cache.");
		}

		/// <summary>
		/// A definition with no image is the same grey map, one step further along.
		/// </summary>
		/// <remarks>
		/// The baker writes the definition's data even when it cannot photograph the scene — it
		/// needs a graphics device to read pixels back, and under <c>-nographics</c> it skips only
		/// the capture. So a definition can exist, carry correct bounds and labels, and still have
		/// no map image, which looks from the client exactly like not being baked at all.
		/// </remarks>
		[Test]
		public void EveryMapDefinitionCarriesAnImage()
		{
			WorldSceneDetailsCache cache = LoadCache();
			if (cache?.Scenes == null)
			{
				Assert.Ignore("The details cache is missing; TheWorldSceneDetailsCacheExistsAndHasScenes covers that.");
				return;
			}

			List<string> imageless = new List<string>();

			foreach (KeyValuePair<string, WorldSceneDetails> pair in cache.Scenes)
			{
				WorldMapDefinition definition = pair.Value?.MapDefinition;
				if (definition == null)
				{
					// Covered by EveryWorldSceneHasAMapDefinition; not worth failing twice for.
					continue;
				}

				if (definition.MapImage == null || !definition.MapImage.RuntimeKeyIsValid())
				{
					imageless.Add(pair.Key);
				}
			}

			Assert.IsEmpty(imageless,
				"These scenes have a map definition but no usable map image reference, which draws " +
				"the same blank map: " + string.Join(", ", imageless) +
				". Re-run the bake with a graphics device available (xvfb-run in a headless shell), " +
				"and check the image landed in an addressable group.");
		}
	}
}
