using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.WorldMaps;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.Rendering;

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
	/// photographs each scene in the editor and writes a <see cref="WorldMapDefinition"/> per scene.
	/// </para>
	/// <para>
	/// The bake is build output, not source. A client build runs it, rebuilds the world scene
	/// details cache so the cache references the fresh definitions, builds, and then removes the
	/// bake and rebuilds the cache again; nothing is committed and no scene is written. So the
	/// committed cache carries no definitions, and a definition-less cache is the correct state
	/// outside a build. Without a definition <c>ClientMapSystem.Definition</c> is null and the map
	/// panel draws fog of war over a plain background, which is why the build must bake: that is
	/// the state this project shipped in before the flow existed, diagnosed by reading the asset
	/// tree rather than from any message the client produced.
	/// </para>
	/// <para>
	/// The assertions are made against <c>WorldSceneDetails.asset</c> rather than against the
	/// scene files, because that cache is what the client actually reads.
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
		/// The committed cache must not reference a bake: a definition under the bake folder, or the
		/// folder itself, is a leftover from an interrupted client build and would be committed as
		/// dangling references the moment the folder is cleaned.
		/// </summary>
		[Test]
		public void TheCommittedCache_CarriesNoBakeLeftovers()
		{
			Assert.IsFalse(AssetDatabase.IsValidFolder(WorldMapDefinition.BakedDirectory),
				$"'{WorldMapDefinition.BakedDirectory}' exists outside a client build. Run FishMMO/World Map/Remove Baked Maps, then FishMMO/Rebuild World Scene Details.");

			WorldSceneDetailsCache cache = LoadCache();
			if (cache?.Scenes == null)
			{
				Assert.Ignore("The details cache is missing; TheWorldSceneDetailsCacheExistsAndHasScenes covers that.");
				return;
			}

			foreach (KeyValuePair<string, WorldSceneDetails> pair in cache.Scenes)
			{
				AssertNotBaked(pair.Key, pair.Value?.MapDefinition);
			}
		}

		/// <summary>
		/// The client build's map flow, end to end: bake every world scene, rebuild the cache so it
		/// references the fresh definitions, then remove the bake and rebuild again. This is the
		/// only place the flow runs outside a real build, so it is also what proves every world scene
		/// can be baked at all (a scene without WorldSceneSettings, or without a boundary or terrain,
		/// fails here rather than shipping a grey map).
		/// </summary>
		/// <remarks>
		/// Image assertions need a graphics device; under <c>-nographics</c> the baker writes the
		/// definitions and skips the photographs, and this test checks only the definitions.
		/// </remarks>
		[Test]
		public void ClientBuildBake_GivesEveryWorldSceneAMap_AndCleansUpAfterItself()
		{
			Assume.That(!AssetDatabase.IsValidFolder(WorldMapDefinition.BakedDirectory),
				"A bake is already present; remove it (FishMMO/World Map/Remove Baked Maps) before running this.");

			bool canCapture = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
			try
			{
				WorldMapBaker.BakeAll();
				Assert.IsTrue(WorldSceneDetailsCacheBuilder.Rebuild(), "The cache must rebuild after the bake.");

				WorldSceneDetailsCache cache = LoadCache();
				Assert.IsNotNull(cache?.Scenes, "The rebuilt cache is missing.");

				AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
				List<string> missing = new List<string>();
				List<string> imageless = new List<string>();
				foreach (KeyValuePair<string, WorldSceneDetails> pair in cache.Scenes)
				{
					WorldMapDefinition definition = pair.Value?.MapDefinition;
					if (definition == null)
					{
						missing.Add(pair.Key);
						continue;
					}

					Assert.AreEqual(pair.Key, definition.SceneName, $"{pair.Key}: the cache references another scene's definition.");
					Assert.IsTrue(definition.HasBounds, $"{pair.Key}: the baked definition has no bounds, so the map has nothing to scale against. Add a SceneBoundary or terrain.");

					if (!canCapture)
					{
						continue;
					}

					string imagePath = WorldMapDefinition.BakedImagePath(pair.Key);
					bool imageExists = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath) != null;
					bool referenced = definition.MapImage != null && definition.MapImage.RuntimeKeyIsValid()
						&& definition.MapImage.AssetGUID == AssetDatabase.AssetPathToGUID(imagePath);
					bool addressable = settings != null && settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(imagePath)) != null
						&& settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(imagePath)).parentGroup.Name == WorldMapBaker.AddressableGroupName;
					if (!imageExists || !referenced || !addressable)
					{
						imageless.Add($"{pair.Key} (image {imageExists}, referenced {referenced}, addressable {addressable})");
					}
				}

				Assert.IsEmpty(missing, "These world scenes got no baked definition, so their world map draws fog over a plain background: " + string.Join(", ", missing));
				Assert.IsEmpty(imageless, "These scenes have a definition but no usable, addressable image: " + string.Join(", ", imageless));
			}
			finally
			{
				WorldMapBaker.CleanBakedMaps();
				WorldSceneDetailsCacheBuilder.Rebuild();
			}

			Assert.IsFalse(AssetDatabase.IsValidFolder(WorldMapDefinition.BakedDirectory), "The bake folder must be gone after cleanup.");
			Assert.IsNull(AddressableAssetSettingsDefaultObject.Settings?.FindGroup(WorldMapBaker.AddressableGroupName), "The bake's addressable group must be gone after cleanup.");
			foreach (KeyValuePair<string, WorldSceneDetails> pair in LoadCache().Scenes)
			{
				AssertNotBaked(pair.Key, pair.Value?.MapDefinition);
			}
		}

		/// <summary>A null definition, or a hand-made one outside the bake folder, is fine; a baked one is not.</summary>
		private static void AssertNotBaked(string sceneName, WorldMapDefinition definition)
		{
			if (definition == null)
			{
				return;
			}

			string path = AssetDatabase.GetAssetPath(definition);
			Assert.IsFalse(path.StartsWith(WorldMapDefinition.BakedDirectory),
				$"{sceneName}: the cache references the baked definition '{path}', which only exists during a client build.");
		}
	}
}
