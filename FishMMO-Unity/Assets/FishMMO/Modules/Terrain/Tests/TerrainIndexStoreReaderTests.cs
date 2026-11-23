using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>Split from TerrainIndexStoreTests: the Terrain module's half.</summary>
	public class TerrainIndexStoreReaderTests
	{
		/// <summary>
		/// Writing a terrain's alphamaps marks its stored map stale (the alphamap callback, edit mode and play alike, as both
		/// keep the change), and the cut's bake stores a fresh one; the binder takes a stored map only in play and bakes live in
		/// edit mode, so painting is always live.
		/// </summary>
		[Test]
		public void AlphamapWritesMarkTheStoredMapStale_AndEditModeAlwaysBakesLive()
		{
			string baker = ProjectFiles.ReadAllText(Scripts + "Modules/Terrain/Editor/TerrainDetailBaker.cs");
			Assert.That(baker, Does.Contain("textureName == TerrainData.AlphamapTextureName"), "only the alphamaps stale the map (holes do not)");
			int mark = baker.IndexOf("private static void MarkIndexStale", StringComparison.Ordinal);
			Assert.That(mark, Is.GreaterThan(0));
			string body = baker.Substring(mark, baker.IndexOf("\n\t\t}\n", mark, StringComparison.Ordinal) - mark);
			Assert.That(body, Does.Contain("tile.Index.Stale = true"));
			Assert.That(body, Does.Not.Contain("isPlaying"), "a stroke in play mode writes the asset too");
			Assert.That(baker, Does.Contain("StoreIndex(terrain, tile.Index)"), "every cut and repaint stores the maps");
			Assert.That(baker, Does.Contain("index.Stale = false"));
			Assert.That(baker, Does.Contain("TerrainTileLayerMap.Bake(data.GetAlphamaps(0, 0, res, res), TerrainTileLayers.Of(terrain))"), "stored = the CPU bake of the alphamaps, in scene layers");

			string binder = ProjectFiles.ReadAllText(Scripts + "Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			Assert.That(binder, Does.Contain("rebake: Application.isEditor && !Application.isPlaying"), "edit mode bakes every push");
			Assert.That(binder, Does.Contain("if (!rebake && Application.isPlaying)"), "stored maps only in play, never on a live rebake");
			Assert.That(binder, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(binder, Does.Contain("TerrainIndexMapBaker.MatchesAlphamaps"), "the editor checks a stored map against the alphamaps before drawing it");
			Assert.That(binder, Does.Contain("PushTo(terrain, rebake: true)"), "an alphamap change rebakes live");

			string holder = ProjectFiles.ReadAllText(Scripts + "Shared/Implementation/Entity/WorldSceneDetails/Terrain/SceneTerrainBake.cs");
			Assert.That(holder, Does.Contain("tile.Index.UsableFor(data)"), "the lookup applies the same rule as the build");
		}

		/// <summary>
		/// The grass's surface layers from a stored index texel are the ones from the alphamaps (GrassMath.PackSurfaceLayers):
		/// the same ids, the share within one rounding step, unpainted ground 0.
		/// </summary>
		[Test]
		public void TheGrassReadsTheSameSurfaceLayersOffAStoredMap()
		{
			var random = new Random(31);
			int compared = 0;
			for (int trial = 0; trial < 4000; trial++)
			{
				int layers = 1 + random.Next(256);
				var bytes = new byte[layers];
				for (int n = random.Next(8); n > 0; n--)
				{
					bytes[random.Next(layers)] = (byte)random.Next(1, 256);
				}
				var floats = new float[layers];
				for (int l = 0; l < layers; l++)
				{
					floats[l] = bytes[l] / 255f;
				}
				uint fromAlphamaps = FishMMO.Client.GrassMath.PackSurfaceLayers(floats, layers);
				TerrainIndexMap.BakeTexel(bytes, 0, layers, out uint ids, out uint packed);
				uint fromIndex = FishMMO.Client.GrassMath.PackSurfaceFromIndex(ids, packed);
				if (TerrainIndexMap.WeightOf(packed, 0) == 0 && fromAlphamaps != 0u)
				{
					// The strongest tied with a fifth layer: the one case the stored texel reads as unpainted.
					continue;
				}
				compared++;
				if (fromAlphamaps == 0u)
				{
					Assert.That(fromIndex, Is.EqualTo(0u), $"trial {trial}: unpainted");
					continue;
				}
				Assert.That(fromIndex & 255u, Is.EqualTo(fromAlphamaps & 255u), $"trial {trial}: strongest");
				Assert.That(fromIndex >> 24, Is.EqualTo(255u), $"trial {trial}: painted");
				int share = (int)(fromAlphamaps >> 16 & 255u);
				if (share < 255)
				{
					Assert.That(fromIndex >> 8 & 255u, Is.EqualTo(fromAlphamaps >> 8 & 255u), $"trial {trial}: second");
				}
				Assert.That((int)(fromIndex >> 16 & 255u), Is.EqualTo(share).Within(1), $"trial {trial}: the first's share");
			}
			Assert.That(compared, Is.GreaterThan(3900));
		}

		/// <summary>
		/// The ground colour map from a stored index texel (GroundColourMap.WeighIndexTexel) is the alphamap path's weighted
		/// mean wherever a texel has four layers or fewer; past four it leaves out only what the terrain itself leaves out.
		/// </summary>
		[Test]
		public void TheGroundColourIsTheSameOffAStoredMapUpToFourLayers()
		{
			var random = new Random(41);
			for (int trial = 0; trial < 2000; trial++)
			{
				int layers = 1 + random.Next(40);
				var colours = new Color[layers];
				var flora = new Vector4[layers];
				for (int l = 0; l < layers; l++)
				{
					colours[l] = new Color((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1f);
					flora[l] = new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 0f);
				}
				var bytes = new byte[layers];
				int painted = 1 + random.Next(Math.Min(4, layers));
				for (int n = painted; n > 0; n--)
				{
					bytes[random.Next(layers)] = (byte)random.Next(1, 256);
				}
				// The alphamap path (GroundColourMap.Paint): every layer by its byte.
				float r = 0f, g = 0f, b = 0f, total = 0f;
				Vector4 share = Vector4.zero;
				for (int l = 0; l < layers; l++)
				{
					if (bytes[l] == 0)
					{
						continue;
					}
					float w = bytes[l] / 255f;
					r += colours[l].r * w; g += colours[l].g * w; b += colours[l].b * w;
					share += flora[l] * w;
					total += w;
				}
				TerrainIndexMap.BakeTexel(bytes, 0, layers, out uint ids, out uint packed);
				float ir = 0f, ig = 0f, ib = 0f, itotal = 0f;
				Vector4 ishare = Vector4.zero;
				FishMMO.Client.GroundColourMap.WeighIndexTexel(ids, packed, layers, colours, flora, ref ir, ref ig, ref ib, ref ishare, ref itotal);
				Assert.That(itotal, Is.EqualTo(total).Within(1e-5f), $"trial {trial}: total");
				if (total > 1e-3f)
				{
					Assert.That(ir / itotal, Is.EqualTo(r / total).Within(1e-5f), $"trial {trial}: red");
					Assert.That(ig / itotal, Is.EqualTo(g / total).Within(1e-5f), $"trial {trial}: green");
					Assert.That(ib / itotal, Is.EqualTo(b / total).Within(1e-5f), $"trial {trial}: blue");
					Assert.That((ishare / itotal - share / total).magnitude, Is.LessThan(1e-4f), $"trial {trial}: make-up");
				}
			}
		}

		/// <summary>
		/// Every runtime CPU reader of the alphamaps has a stored-map path for a tile without them, and the contact blend takes
		/// the binder's map (stored or baked) before baking its own.
		/// </summary>
		[Test]
		public void EveryRuntimeAlphamapReaderHasAStoredMapPath()
		{
			string grass = ProjectFiles.ReadAllText(Scripts + "Modules/Terrain/Client/Grass/GrassTerrain.cs");
			Assert.That(grass, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(grass, Does.Contain("GrassMath.PackSurfaceFromIndex"));
			string colour = ProjectFiles.ReadAllText(Scripts + "Modules/Terrain/Client/GroundColourMap.cs");
			Assert.That(colour, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(colour, Does.Contain("WeighIndexTexel(stored.Map[2 * k], stored.Map[2 * k + 1]"));
			string contact = ProjectFiles.ReadAllText(Scripts + "Modules/Terrain/Client/GroundMaterialMap.cs");
			Assert.That(contact, Does.Contain("TerrainIndexMapBaker.CopyInto(TerrainArrayBinder.IndexMapOf(t), target, slice)"));
			Assert.That(contact, Does.Contain("TerrainIndexMapBaker.Upload(stored"));
			Assert.That(contact, Does.Not.Contain("if (data.alphamapTextureCount == 0)\n"), "a tile without alphamaps is no longer left out");
			// Footstep surfaces: the alphamaps where the tile kept them, else the stored map through the same filter
			// the terrain shader runs.
			string footsteps = ProjectFiles.ReadAllText(Scripts + "Modules/WorldSimulation/Client/Audio/World/GroundSurface.cs");
			Assert.That(footsteps, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(footsteps, Does.Contain("TerrainIndexMap.Filter(index.Map, index.Resolution, index.Resolution, u, v, ids, weights)"));

			// No other runtime (non-editor) script reads the alphamaps.
			foreach (string file in ProjectFiles.CoreSources())
			{
				string path = file.Replace('\\', '/');
				if (path.Contains("/Editor/") || path.EndsWith("TerrainIndexMapBaker.cs") || path.EndsWith("GroundColourMap.cs") || path.EndsWith("GrassTerrain.cs")
					|| path.EndsWith("Audio/World/GroundSurface.cs"))
				{
					continue;
				}
				string text = ProjectFiles.ReadAllText(file);
				Assert.That(text.Contains("GetAlphamaps(") || text.Contains("alphamapTextures"), Is.False, $"{path} reads the alphamaps at runtime");
			}
		}

		private const string Scripts = "Assets/FishMMO/";
	}
}
