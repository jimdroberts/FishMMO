using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Client;
using FishMMO.Shared;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Per-tile terrain layers (2026-10-27): a tile keeps only the layers it paints, every weight byte unchanged, and every reader
	/// that turns a channel into a scene layer renames it (TerrainTileLayerMap). The index map a compacted tile bakes is the one
	/// the whole-palette tile baked, slot for slot wherever a slot has weight; two tiles meeting at a seam with different layer
	/// sets still agree on it; the grass's surface layers and the blend the terrain draws are unchanged. Pure: no Unity.
	/// </summary>
	[TestFixture]
	public class TerrainTileLayersTests
	{
		private const string Scripts = "Assets/Scripts/";

		/// <summary>
		/// A tile painting <paramref name="painted"/> of a scene's <paramref name="sceneLayers"/>, as GetAlphamaps returns it (bytes
		/// over 255), up to <paramref name="busy"/> layers a texel; a share of texels takes weights from a few values so ties occur.
		/// </summary>
		private static float[,,] Paint(Random random, int width, int height, int sceneLayers, int[] painted, int busy)
		{
			var alphas = new float[height, width, sceneLayers];
			byte[] tied = { 32, 64, 64, 128 };
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					bool ties = random.Next(4) == 0;
					for (int n = 1 + random.Next(busy); n > 0; n--)
					{
						int l = painted[random.Next(painted.Length)];
						byte b = ties ? tied[random.Next(tied.Length)] : (byte)random.Next(1, 256);
						alphas[y, x, l] = b / 255f;
					}
				}
			}
			return alphas;
		}

		/// <summary>The cut's compaction of one tile: the channels it paints, in scene order, and their weights moved down.</summary>
		private static float[,,] Compact(float[,,] full, out int[] sceneLayers, int[] sceneOfChannel = null)
		{
			int height = full.GetLength(0), width = full.GetLength(1), layers = full.GetLength(2);
			var painted = new bool[layers];
			TerrainTileLayerMap.MarkPainted(full, painted);
			int[] keep = TerrainTileLayerMap.Keep(painted, sceneOfChannel);
			sceneLayers = TerrainTileLayerMap.SceneLayersOf(keep, sceneOfChannel, layers, out int[] outputOf);
			var compact = new float[height, width, sceneLayers.Length];
			TerrainTileLayerMap.Compact(full, 0, keep, outputOf, compact);
			return compact;
		}

		private static int[] Pick(Random random, int sceneLayers, int count)
		{
			var all = new List<int>();
			for (int l = 0; l < sceneLayers; l++)
			{
				all.Add(l);
			}
			var picked = new int[count];
			for (int i = 0; i < count; i++)
			{
				int at = random.Next(all.Count);
				picked[i] = all[at];
				all.RemoveAt(at);
			}
			return picked;
		}

		/// <summary>Two texels' words are the same ground: the same weights, and the same id wherever a slot has weight.</summary>
		private static void AssertSameGround(uint idsA, uint wA, uint idsB, uint wB, string what)
		{
			Assert.That(wA, Is.EqualTo(wB), $"{what}: weights");
			for (int s = 0; s < TerrainIndexMap.Slots; s++)
			{
				if (TerrainIndexMap.WeightOf(wA, s) > 0)
				{
					Assert.That(TerrainIndexMap.IdOf(idsA, s), Is.EqualTo(TerrainIndexMap.IdOf(idsB, s)), $"{what}: slot {s}");
				}
			}
		}

		private static void AssertDistinct(uint ids, string what)
		{
			var seen = new HashSet<int>();
			for (int s = 0; s < TerrainIndexMap.Slots; s++)
			{
				Assert.That(seen.Add(TerrainIndexMap.IdOf(ids, s)), Is.True, $"{what}: ids distinct");
			}
		}

		// ── The tile's channels ──────────────────────────────────────

		[Test]
		public void ATileKeepsEveryLayerItPaints_InSceneOrder_AndNothingElse()
		{
			// Channels 1, 3, 4 painted; their scene layers 0, 2, 1 (a tool appended channel 4 out of order).
			int[] keep = TerrainTileLayerMap.Keep(new[] { false, true, false, true, true }, new[] { 4, 0, 3, 2, 1 });
			Assert.That(keep, Is.EqualTo(new[] { 1, 4, 3 }), "by scene layer");
			Assert.That(TerrainTileLayerMap.Keep(new[] { true, false, true }, new[] { -1, 5, 2 }), Is.EqualTo(new[] { 2, 0 }), "unknown last");
			Assert.That(TerrainTileLayerMap.Keep(new bool[3], null), Is.EqualTo(new[] { 0 }), "a tile painted nowhere keeps one layer");

			int[] map = TerrainTileLayerMap.SceneLayersOf(new[] { 0, 2, 3 }, new[] { 7, 9, 7, 12 }, 20, out int[] outputOf);
			Assert.That(map, Is.EqualTo(new[] { 7, 12 }), "a scene layer named twice is one channel");
			Assert.That(outputOf, Is.EqualTo(new[] { 0, 0, 1 }));
			map = TerrainTileLayerMap.SceneLayersOf(new[] { 0, 1 }, new[] { 3, -1 }, 20, out _);
			Assert.That(map, Is.EqualTo(new[] { 3, 20 }), "an unknown layer reads past the scene's count");
		}

		[Test]
		public void CompactionMovesEveryWeightExactly_AndSumsARepeatedLayer()
		{
			var random = new Random(5);
			float[,,] full = Paint(random, 9, 7, 30, new[] { 2, 5, 11, 29 }, 4);
			float[,,] compact = Compact(full, out int[] map);
			Assert.That(map, Is.EqualTo(new[] { 2, 5, 11, 29 }));
			for (int y = 0; y < 7; y++)
			{
				for (int x = 0; x < 9; x++)
				{
					for (int c = 0; c < map.Length; c++)
					{
						Assert.That(compact[y, x, c], Is.EqualTo(full[y, x, map[c]]), "the same float, so the same byte");
					}
				}
			}

			var twice = new float[1, 1, 3];
			twice[0, 0, 0] = 40 / 255f;
			twice[0, 0, 2] = 60 / 255f;
			float[,,] merged = Compact(twice, out int[] one, new[] { 4, 1, 4 });
			Assert.That(one, Is.EqualTo(new[] { 4 }));
			Assert.That(TerrainIndexMap.ByteOf(merged[0, 0, 0]), Is.EqualTo(100));
		}

		// ── The index map ────────────────────────────────────────────

		[Test]
		public void TheBakeTableRenamesChannels_AndKeepsATexelsIdsDistinct()
		{
			Assert.That(TerrainTileLayerMap.BakeTable(null), Is.Null);
			Assert.That(TerrainTileLayerMap.BakeTable(new[] { 0, 1, 2, 3, 4 }), Is.Null, "identity: nothing to rename");
			Assert.That(TerrainTileLayerMap.BakeTable(new[] { 3, 7 }), Is.EqualTo(new[] { 3, 7, 0, 1 }), "phantoms: the lowest layers the tile lacks");
			Assert.That(TerrainTileLayerMap.BakeTable(new[] { 0, 2 }), Is.EqualTo(new[] { 0, 2, 1, 3 }));
			Assert.That(TerrainTileLayerMap.BakeTable(new[] { 9, 40, 41, 200, 255 }), Is.EqualTo(new[] { 9, 40, 41, 200, 255 }));
			uint ids = 0u | 1u << 8 | 2u << 16 | 3u << 24;
			Assert.That(TerrainTileLayerMap.RemapIds(ids, new[] { 9, 40, 41, 200 }), Is.EqualTo(9u | 40u << 8 | 41u << 16 | 200u << 24));
			Assert.That(TerrainTileLayerMap.RemapIds(ids, null), Is.EqualTo(ids));
			Assert.That(TerrainTileLayerMap.SceneLayerOf(null, 17), Is.EqualTo(17));
			Assert.That(TerrainTileLayerMap.SceneLayerOf(new[] { 4, 8 }, 1), Is.EqualTo(8));
		}

		/// <summary>
		/// A compacted tile's index map, renamed to scene layers, is the whole-palette tile's: the same weight word every texel,
		/// the same id in every slot with weight (only the zero-weight fillers may name other layers), ids distinct.
		/// </summary>
		[Test]
		public void ACompactedTileBakesTheWholePaletteTilesIndexMap()
		{
			var random = new Random(27);
			foreach ((int scene, int paints, int busy) in new[] { (40, 12, 4), (40, 3, 3), (210, 62, 7), (256, 65, 6), (12, 1, 1), (8, 8, 8) })
			{
				int[] painted = Pick(random, scene, paints);
				float[,,] full = Paint(random, 23, 19, scene, painted, busy);
				float[,,] compact = Compact(full, out int[] map);
				Assert.That(map.Length, Is.LessThanOrEqualTo(paints));
				uint[] whole = TerrainIndexMap.Bake(full);
				uint[] own = TerrainTileLayerMap.Bake(compact, map);
				Assert.That(own.Length, Is.EqualTo(whole.Length));
				for (int i = 0; i < whole.Length; i += 2)
				{
					AssertSameGround(whole[i], whole[i + 1], own[i], own[i + 1], $"{scene}/{paints} texel {i / 2}");
					AssertDistinct(own[i], $"{scene}/{paints} texel {i / 2}");
				}
			}
		}

		/// <summary>The blend the terrain draws (FishIndexFilter) is unchanged: every weighted layer, bit for bit.</summary>
		[Test]
		public void TheDrawnBlendIsUnchanged()
		{
			var random = new Random(3);
			int[] painted = Pick(random, 64, 20);
			const int Width = 17, Height = 13;
			float[,,] full = Paint(random, Width, Height, 64, painted, 6);
			float[,,] compact = Compact(full, out int[] map);
			uint[] whole = TerrainIndexMap.Bake(full);
			uint[] own = TerrainTileLayerMap.Bake(compact, map);
			var idsA = new int[4]; var wA = new float[4]; var idsB = new int[4]; var wB = new float[4];
			for (int trial = 0; trial < 4000; trial++)
			{
				float u = (float)random.NextDouble(), v = (float)random.NextDouble();
				TerrainIndexMap.Filter(whole, Width, Height, u, v, idsA, wA);
				TerrainIndexMap.Filter(own, Width, Height, u, v, idsB, wB);
				for (int s = 0; s < 4; s++)
				{
					Assert.That(wB[s], Is.EqualTo(wA[s]), $"trial {trial} slot {s}");
					if (wA[s] > 0f)
					{
						Assert.That(idsB[s], Is.EqualTo(idsA[s]), $"trial {trial} slot {s}");
					}
				}
			}
		}

		/// <summary>
		/// Two tiles meeting at a seam, each painting layers the other does not: their shared edge texels (the same bytes on both
		/// sides, as the terrain addresses its control maps edge to edge) bake to the same ground on both, so the blend at the
		/// seam is one blend whichever tile draws it.
		/// </summary>
		[Test]
		public void NeighbouringTilesAgreeOnTheirSharedEdge()
		{
			var random = new Random(11);
			const int Scene = 120, Size = 16;
			int[] shared = Pick(random, Scene, 10);
			var onlyA = new List<int>(shared);
			var onlyB = new List<int>(shared);
			foreach (int l in Pick(random, Scene, 30))
			{
				if (Array.IndexOf(shared, l) < 0)
				{
					(onlyA.Count <= onlyB.Count ? onlyA : onlyB).Add(l);
				}
			}
			float[,,] a = Paint(random, Size, Size, Scene, onlyA.ToArray(), 6);
			float[,,] b = Paint(random, Size, Size, Scene, onlyB.ToArray(), 6);
			// A's east column is B's west column: the same texels.
			for (int y = 0; y < Size; y++)
			{
				for (int l = 0; l < Scene; l++)
				{
					a[y, Size - 1, l] = 0f;
				}
				for (int n = 0; n < 6; n++)
				{
					int l = shared[random.Next(shared.Length)];
					a[y, Size - 1, l] = random.Next(1, 256) / 255f;
				}
				for (int l = 0; l < Scene; l++)
				{
					b[y, 0, l] = a[y, Size - 1, l];
				}
			}
			uint[] ownA = TerrainTileLayerMap.Bake(Compact(a, out int[] mapA), mapA);
			uint[] ownB = TerrainTileLayerMap.Bake(Compact(b, out int[] mapB), mapB);
			Assert.That(mapA, Is.Not.EqualTo(mapB), "the tiles keep different layers");
			uint[] wholeA = TerrainIndexMap.Bake(a);
			for (int y = 0; y < Size; y++)
			{
				int ea = 2 * (y * Size + Size - 1), wb = 2 * (y * Size);
				AssertSameGround(ownA[ea], ownA[ea + 1], ownB[wb], ownB[wb + 1], $"seam row {y}");
				AssertSameGround(wholeA[ea], wholeA[ea + 1], ownB[wb], ownB[wb + 1], $"seam row {y} against the whole palette");
			}
			var idsA = new int[4]; var wA = new float[4]; var idsB = new int[4]; var wB = new float[4];
			for (int s = 0; s <= 40; s++)
			{
				float v = s / 40f;
				TerrainIndexMap.Filter(ownA, Size, Size, 1f, v, idsA, wA);
				TerrainIndexMap.Filter(ownB, Size, Size, 0f, v, idsB, wB);
				for (int k = 0; k < 4; k++)
				{
					Assert.That(wB[k], Is.EqualTo(wA[k]), $"seam at {v} slot {k}");
					if (wA[k] > 0f)
					{
						Assert.That(idsB[k], Is.EqualTo(idsA[k]), $"seam at {v} slot {k}");
					}
				}
			}
		}

		// ── The other readers ────────────────────────────────────────

		/// <summary>The grass's surface layers (top two less the third) of a compacted tile, renamed: the whole-palette tile's.</summary>
		[Test]
		public void TheGrassSurfaceLayersAreUnchanged()
		{
			var random = new Random(8);
			int[] painted = Pick(random, 90, 25);
			float[,,] full = Paint(random, 21, 21, 90, painted, 5);
			float[,,] compact = Compact(full, out int[] map);
			var wf = new float[90];
			var wc = new float[map.Length];
			for (int y = 0; y < 21; y++)
			{
				for (int x = 0; x < 21; x++)
				{
					for (int l = 0; l < 90; l++)
					{
						wf[l] = full[y, x, l];
					}
					for (int c = 0; c < map.Length; c++)
					{
						wc[c] = compact[y, x, c];
					}
					uint whole = GrassMath.PackSurfaceLayers(wf, 90);
					uint own = GrassMath.RenameSurfaceLayers(GrassMath.PackSurfaceLayers(wc, map.Length), map);
					Assert.That(own >> 16, Is.EqualTo(whole >> 16), "share and presence");
					if (whole != 0u)
					{
						Assert.That(own & 255u, Is.EqualTo(whole & 255u), "strongest");
						if ((whole >> 16 & 255u) < 255u)
						{
							Assert.That(own >> 8 & 255u, Is.EqualTo(whole >> 8 & 255u), "second, where it has a share");
						}
					}
				}
			}
			Assert.That(GrassMath.RenameSurfaceLayers(0u, map), Is.EqualTo(0u), "unpainted stays unpainted");
			Assert.That(GrassMath.RenameSurfaceLayers(0x7F000102u, null), Is.EqualTo(0x7F000102u));
		}

		/// <summary>The scene's numbering is never shrunk from one tile: kept in order, a layer only a tile carries appended.</summary>
		[Test]
		public void TheScenesLayersAreKept_AndATilesNewLayerIsAppended()
		{
			var scene = new List<string> { "grass", "sand", "rock", "snow" };
			var tiles = new List<IList<string>> { new[] { "sand", "rock" }, new[] { "grass", "moss" }, new string[0] };
			Assert.That(TerrainTileLayerMap.Union<string>(scene, tiles), Is.EqualTo(new[] { "grass", "sand", "rock", "snow", "moss" }));
			Assert.That(TerrainTileLayerMap.Union<string>(null, tiles), Is.EqualTo(new[] { "sand", "rock", "grass", "moss" }));
			List<string> grown = TerrainTileLayerMap.Union<string>(scene, tiles);
			Assert.That(TerrainTileLayerMap.Resolve<string>(new[] { "rock", "moss", "grass" }, grown), Is.EqualTo(new[] { 2, 4, 0 }));
			Assert.That(TerrainTileLayerMap.Resolve<string>(new[] { "rock", "lava", null }, scene), Is.EqualTo(new[] { 2, 4, 5 }), "unknown: distinct, past the scene");
			Assert.That(TerrainTileLayerMap.Resolve<string>(new[] { "sand", "sand" }, new[] { "sand", "x", "sand" }), Is.EqualTo(new[] { 0, 0 }), "the first listing");
		}

		/// <summary>
		/// The cap is above every tile measured (2026-10-27, saved tiles: Moron 3 1_1 paints 65, Moron 2 3_3 62, Moron 1 2_4 43,
		/// Belron 1 17), with room; and the sizes the notes report are Unity's (512² RGBA32 with every mip, four layers a map).
		/// </summary>
		[Test]
		public void TheCapAndTheSizes()
		{
			foreach (int measured in new[] { 65, 62, 43, 17 })
			{
				Assert.That(measured * 1.25, Is.LessThanOrEqualTo(TerrainTileLayerMap.MaximumTileLayers));
			}
			Assert.That(TerrainTileLayerMap.MapsFor(210), Is.EqualTo(53));
			Assert.That(TerrainTileLayerMap.MapsFor(62), Is.EqualTo(16));
			Assert.That(TerrainTileLayerMap.MapsFor(1), Is.EqualTo(1));
			Assert.That(TerrainTileLayerMap.ControlBytes(4, 512), Is.EqualTo(1398100L));
			Assert.That(TerrainTileLayerMap.ControlBytes(210, 512), Is.EqualTo(53L * 1398100L));
		}

		/// <summary>Every reader that turns a channel into a scene layer goes through the tile's map; the cut compacts last.</summary>
		[Test]
		public void EveryChannelReaderRenamesThroughTheTilesMap()
		{
			string binder = File.ReadAllText(Scripts + "Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			Assert.That(binder, Does.Contain("TerrainIndexMapBaker.Bake(data, TerrainIndexMapBaker.ShaderOf(set), baked, 0, TerrainTileLayers.Of(terrain))"));
			Assert.That(binder, Does.Contain("StoredStillGood(terrain, data, stored, TerrainTileLayers.Of(terrain))"));
			Assert.That(binder, Does.Contain("MatchesAlphamaps(data, stored, 32, out string bad, sceneLayers)"));

			string baker = File.ReadAllText(Scripts + "Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainIndexMapBaker.cs");
			Assert.That(baker, Does.Contain("properties.SetTexture(RemapId, RemapTexture(TerrainTileLayerMap.BakeTable(sceneLayers)));"));
			Assert.That(baker.Split(new[] { "ids = TerrainTileLayerMap.RemapIds(ids, table);" }, StringSplitOptions.None).Length - 1, Is.EqualTo(2),
				"both CPU checks of a GPU bake rename as it does");
			string shader = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishTerrainIndexBake.shader");
			Assert.That(shader, Does.Contain("Texture2D<float4> _FishIndexRemap;"));
			Assert.That(shader, Does.Contain("uint4 n = uint4(SceneLayer((uint)ids.x), SceneLayer((uint)ids.y), SceneLayer((uint)ids.z), SceneLayer((uint)ids.w));"),
				"renamed after the four are chosen, as the CPU renames the chosen ids");

			string contact = File.ReadAllText(Scripts + "Client/World/Terrain/GroundMaterialMap.cs");
			Assert.That(contact, Does.Contain("TerrainIndexMapBaker.Bake(data, bake, target, slice, TerrainTileLayers.Of(t))"));
			string colour = File.ReadAllText(Scripts + "Client/World/Terrain/GroundColourMap.cs");
			Assert.That(colour, Does.Contain("int slice = TerrainTileLayerMap.SceneLayerOf(sceneLayers, l);"));
			Assert.That(colour, Does.Contain("LayerColour(set, ls, i, TerrainTileLayerMap.SceneLayerOf(sceneLayers, i), out bool fromArray)"));
			Assert.That(colour, Does.Contain("count = set.LayerCount;"), "a stored map names the set's layers, past a compacted tile's count");
			string grass = File.ReadAllText(Scripts + "Client/World/Terrain/Grass/GrassTerrain.cs");
			Assert.That(grass, Does.Contain("GrassMath.RenameSurfaceLayers(GrassMath.PackSurfaceLayers(weights, alayers), sceneLayers)"));
			string footsteps = File.ReadAllText(Scripts + "Client/Audio/World/GroundSurface.cs");
			Assert.That(footsteps, Does.Contain("TerrainLayer[] painted = data.terrainLayers;"), "footsteps read a tile's own layers by name: per tile already");

			string generator = File.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			int bake = generator.IndexOf("internal static void BakePropsAndNavMesh(", StringComparison.Ordinal);
			int compact = generator.IndexOf("TerrainTileLayerSync.Compact(scene, tiles, notes);", bake, StringComparison.Ordinal);
			int finished = generator.IndexOf("SceneTerrainEvents.RaiseFinished(scene, tiles, notes);", bake, StringComparison.Ordinal);
			Assert.That(compact, Is.GreaterThan(bake));
			Assert.That(finished, Is.GreaterThan(compact), "compacted before the index maps and details are baked from it");
			Assert.That(generator, Does.Contain("TerrainTileLayerSync.ScenePalette(tiles[0].gameObject.scene, tiles)"));
			string setup = File.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/TerrainArraySetup.cs");
			Assert.That(setup, Does.Contain("bool perTile = TerrainTileLayerSync.IsCompacted(binder) || !AllSame(tileLayers);"),
				"the provenance is never shrunk to one compacted tile's layers");
			string shading = File.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/World/BackdropShadingTool.cs");
			Assert.That(shading, Does.Contain("TerrainTileLayerSync.ScenePalette(scene, tiles)"));
			string stripper = File.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/ClientTerrainLayerStripper.cs");
			Assert.That(stripper, Does.Contain("WorldDesign.TerrainTileLayerSync.Record(terrain, sceneLayers);"), "a kept tile's map reaches the player");
			string store = File.ReadAllText(Scripts + "Client/World/Weather/Editor/TerrainDetailBaker.cs");
			Assert.That(store, Does.Contain("TerrainTileLayerMap.Bake(data.GetAlphamaps(0, 0, res, res), TerrainTileLayers.Of(terrain))"));
		}
	}
}
