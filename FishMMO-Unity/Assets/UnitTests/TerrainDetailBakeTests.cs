using System.IO;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The scene's baked grass and scatter maps (TerrainDetailBake): the grass cover weighed off the packed maps as the full
	/// build weighs it off the floats, and source pins for the take-on paths that use a bake and the cut that makes one.
	/// </summary>
	public class TerrainDetailBakeTests
	{
		private static string Source(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative));

		[Test]
		public void CoverOf_PackedMaps_IsTheTallestChannelTimesItsDensity()
		{
			const int texels = 3;
			// Two maps (five channels): map 0 holds channels 0..3, map 1 channel 4, RGBA bytes per texel.
			var density = new byte[2 * texels * 4];
			// Texel 0: channel 0 full, channel 1 half. Texel 1: channel 4 full. Texel 2: nothing.
			density[0 * 4 + 0] = 255;
			density[0 * 4 + 1] = 128;
			density[texels * 4 + 1 * 4 + 0] = 255;
			var heights = new[] { 0.5f, 2f, 0f, 0f, 0.9f };
			float[] cover = GrassTerrain.Builder.CoverOf(density, 2, 5, texels, heights);
			LogAssert.AreEqual(texels, cover.Length, "a value per texel");
			LogAssert.IsTrue(Mathf.Abs(128 * 2f / 255f - cover[0]) < 1e-5f, "texel 0: the half-dense 2 m channel stands taller than the full 0.5 m one");
			LogAssert.IsTrue(Mathf.Abs(0.9f - cover[1]) < 1e-5f, "texel 1: channel 4, read from the second map");
			LogAssert.IsTrue(cover[2] == 0f, "texel 2: no grass");
		}

		[Test]
		public void TheTakeOns_TryTheSceneBake_BeforeBuilding()
		{
			string grass = Source("Assets/Scripts/Client/World/Terrain/Grass/GrassBladeSystem.cs");
			string scatter = Source("Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterSystem.cs");
			foreach ((string name, string code) in new[] { ("GrassBladeSystem", grass), ("DetailScatterSystem", scatter) })
			{
				LogAssert.IsTrue(code.Contains("SceneTerrainBake.TileOf(data, out _)"), $"{name} looks for the terrain in the scene's bake");
				LogAssert.IsTrue(code.Contains("building.TakeBaked(tile)"), $"{name} takes it from there when the bake fits");
				LogAssert.IsTrue(code.Contains("BakedBudgetMilliseconds"), $"{name} takes several baked terrains a frame");
			}
		}

		[Test]
		public void TheCut_BakesWhenTheTerrainIsFinished_OnAClientOnlyHolder()
		{
			string generator = Source("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			LogAssert.IsTrue(generator.Contains("SceneTerrainEvents.RaiseFinished(scene, tiles, notes);"), "the end of a cut or repaint raises the event");
			string baker = Source("Assets/Scripts/Client/World/Weather/Editor/TerrainDetailBaker.cs");
			LogAssert.IsTrue(baker.Contains("SceneTerrainEvents.Finished += OnTerrainFinished;"), "the client's baker listens for it");
			LogAssert.IsTrue(baker.Contains("go.AddComponent<ClientOnlyObject>();"), "its holder is client-only, so server builds drop the bake");
			LogAssert.IsTrue(baker.Contains("TerrainCallbacks.heightmapChanged += OnHeightsChanged;"), "hand edits to the heights mark the bake stale");
		}
	}
}
