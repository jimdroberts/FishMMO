using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Stored index maps (decision 24, 2026-10-16): the cut's CPU bake of a tile's alphamaps
	/// (<see cref="TerrainIndexMap.Bake(float[,,])"/>) is the GPU bake's texels exactly, a stored map is drawn only while it
	/// is current, a client build leaves a tile's alphamaps out only when its stored map is theirs texel for texel, and the
	/// CPU readers that used the alphamaps (the grass's surface layers, the ground colour map) read the same layers off a
	/// stored map. Pure: no GPU, no assets, no native terrain.
	/// </summary>
	[TestFixture]
	public class TerrainIndexStoreTests
	{
		private const string Scripts = "Assets/Scripts/";

		/// <summary>Random paint as GetAlphamaps returns it (each weight a byte over 255), with its bytes as control maps.</summary>
		private static float[,,] RandomPaint(Random random, int width, int height, int layers, int busy, out Color32[][] controls)
		{
			var alphas = new float[height, width, layers];
			controls = new Color32[(layers + 3) / 4][];
			for (int k = 0; k < controls.Length; k++)
			{
				controls[k] = new Color32[width * height];
			}
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					for (int n = 1 + random.Next(busy); n > 0; n--)
					{
						int l = random.Next(layers);
						byte b = (byte)random.Next(0, 256);
						alphas[y, x, l] = b / 255f;
						ref Color32 c = ref controls[l >> 2][y * width + x];
						switch (l & 3)
						{
							case 0: c.r = b; break;
							case 1: c.g = b; break;
							case 2: c.b = b; break;
							default: c.a = b; break;
						}
					}
				}
			}
			return alphas;
		}

		// ── The stored bake ──────────────────────────────────────────

		/// <summary>
		/// The bake the cut stores, from GetAlphamaps' floats, is the bake from the alphamap textures' bytes (what the GPU bake
		/// reads, FishTerrainIndexBake.shader = BakeTexel: TerrainIndexMapTests.TheBakeShaderIsTheCSharpBake), word for word,
		/// laid out texel (x, y) at 2 (y × width + x), for any layer count.
		/// </summary>
		[Test]
		public void TheStoredBakeIsTheGpuBakesTexels()
		{
			var random = new Random(24);
			foreach (int layers in new[] { 1, 2, 3, 4, 5, 8, 13, 24, 32, 77, 256 })
			{
				const int width = 9, height = 7;
				float[,,] alphas = RandomPaint(random, width, height, layers, 7, out Color32[][] controls);
				uint[] stored = TerrainIndexMap.Bake(alphas);
				uint[] fromBytes = TerrainIndexMap.Bake(controls, width * height, layers);
				Assert.That(TerrainIndexMap.Same(stored, fromBytes), Is.True, $"{layers} layers: the stored map is the bytes' bake");
				var weights = new byte[layers];
				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						for (int l = 0; l < layers; l++)
						{
							weights[l] = (byte)Mathf.RoundToInt(alphas[y, x, l] * 255f);
						}
						TerrainIndexMap.BakeTexel(weights, 0, layers, out uint ids, out uint packed);
						int at = 2 * (y * width + x);
						Assert.That(stored[at], Is.EqualTo(ids), $"{layers} layers, texel ({x},{y}) ids");
						Assert.That(stored[at + 1], Is.EqualTo(packed), $"{layers} layers, texel ({x},{y}) weights");
					}
				}
			}
		}

		/// <summary>A weight read back from the alphamaps (a byte over 255) becomes that byte again, for every byte.</summary>
		[Test]
		public void EveryAlphamapByteComesBackExactly()
		{
			for (int b = 0; b < 256; b++)
			{
				Assert.That(TerrainIndexMap.ByteOf(b / 255f), Is.EqualTo((byte)b));
			}
			Assert.That(TerrainIndexMap.ByteOf(-0.2f), Is.EqualTo((byte)0));
			Assert.That(TerrainIndexMap.ByteOf(1.3f), Is.EqualTo((byte)255));
		}

		/// <summary>Maps compare by every word; one word apart, or another length, is another map.</summary>
		[Test]
		public void MapsAreTheSameOnlyWordForWord()
		{
			uint[] a = { 1u, 2u, 3u, 4u };
			Assert.That(TerrainIndexMap.Same(a, (uint[])a.Clone()), Is.True);
			Assert.That(TerrainIndexMap.Same(a, new uint[] { 1u, 2u, 3u, 5u }), Is.False);
			Assert.That(TerrainIndexMap.Same(a, new uint[] { 1u, 2u }), Is.False);
			Assert.That(TerrainIndexMap.Same(a, null), Is.False);
		}

		// ── When a stored map is drawn ───────────────────────────────

		[Test]
		public void AStoredMapIsDrawnOnlyWhileItIsCurrent()
		{
			int f = TerrainIndexMap.StoredFormat;
			int words = 16 * 16 * 2;
			Assert.That(TerrainIndexMap.IsUsable(f, false, 16, 24, words, 16, 24), Is.True, "current, on its own alphamaps");
			Assert.That(TerrainIndexMap.IsUsable(f, false, 16, 24, words, 0, 0), Is.True, "a client copy without alphamaps: the build checked it");
			Assert.That(TerrainIndexMap.IsUsable(0, false, 16, 24, words, 16, 24), Is.False, "never stored");
			Assert.That(TerrainIndexMap.IsUsable(f + 1, false, 16, 24, words, 16, 24), Is.False, "another format");
			Assert.That(TerrainIndexMap.IsUsable(f, true, 16, 24, words, 16, 24), Is.False, "stale: the alphamaps were written since");
			Assert.That(TerrainIndexMap.IsUsable(f, true, 16, 24, words, 0, 0), Is.False, "stale, even with no alphamaps to compare");
			Assert.That(TerrainIndexMap.IsUsable(f, false, 16, 24, words - 2, 16, 24), Is.False, "incomplete");
			Assert.That(TerrainIndexMap.IsUsable(f, false, 16, 24, words, 32, 24), Is.False, "the alphamaps were resized");
			Assert.That(TerrainIndexMap.IsUsable(f, false, 16, 24, words, 16, 25), Is.False, "a layer was added");
			Assert.That(TerrainIndexMap.IsUsable(f, false, 16, 0, words, 16, 24), Is.False, "names no layers");
			Assert.That(TerrainIndexMap.IsUsable(f, false, 0, 24, 0, 16, 24), Is.False, "no texels");
		}

		/// <summary>
		/// Writing a terrain's alphamaps marks its stored map stale (the alphamap callback, edit mode and play alike, as both
		/// keep the change), and the cut's bake stores a fresh one; the binder takes a stored map only in play and bakes live in
		/// edit mode, so painting is always live.
		/// </summary>
		[Test]
		public void AlphamapWritesMarkTheStoredMapStale_AndEditModeAlwaysBakesLive()
		{
			string baker = File.ReadAllText(Scripts + "Client/World/Weather/Editor/TerrainDetailBaker.cs");
			Assert.That(baker, Does.Contain("textureName == TerrainData.AlphamapTextureName"), "only the alphamaps stale the map (holes do not)");
			int mark = baker.IndexOf("private static void MarkIndexStale", StringComparison.Ordinal);
			Assert.That(mark, Is.GreaterThan(0));
			string body = baker.Substring(mark, baker.IndexOf("\n\t\t}\n", mark, StringComparison.Ordinal) - mark);
			Assert.That(body, Does.Contain("tile.Index.Stale = true"));
			Assert.That(body, Does.Not.Contain("isPlaying"), "a stroke in play mode writes the asset too");
			Assert.That(baker, Does.Contain("StoreIndex(terrain, tile.Index)"), "every cut and repaint stores the maps");
			Assert.That(baker, Does.Contain("index.Stale = false"));
			Assert.That(baker, Does.Contain("TerrainTileLayerMap.Bake(data.GetAlphamaps(0, 0, res, res), TerrainTileLayers.Of(terrain))"), "stored = the CPU bake of the alphamaps, in scene layers");

			string binder = File.ReadAllText(Scripts + "Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			Assert.That(binder, Does.Contain("rebake: Application.isEditor && !Application.isPlaying"), "edit mode bakes every push");
			Assert.That(binder, Does.Contain("if (!rebake && Application.isPlaying)"), "stored maps only in play, never on a live rebake");
			Assert.That(binder, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(binder, Does.Contain("TerrainIndexMapBaker.MatchesAlphamaps"), "the editor checks a stored map against the alphamaps before drawing it");
			Assert.That(binder, Does.Contain("PushTo(terrain, rebake: true)"), "an alphamap change rebakes live");

			string holder = File.ReadAllText(Scripts + "Shared/Implementation/Entity/WorldSceneDetails/Terrain/SceneTerrainBake.cs");
			Assert.That(holder, Does.Contain("tile.Index.UsableFor(data)"), "the lookup applies the same rule as the build");
		}

		// ── What a client build leaves out ───────────────────────────

		private static TerrainDetailBake.IndexMap Stored(int res, int layers, bool stale = false, int format = TerrainIndexMap.StoredFormat)
		{
			return new TerrainDetailBake.IndexMap { Format = format, Stale = stale, Resolution = res, Layers = layers, Map = new uint[res * res * 2] };
		}

		[Test]
		public void AClientBuildLeavesTheAlphamapsOutOnlyForAMatchingStoredMap()
		{
			int asked = 0;
			Func<bool> same = () => { asked++; return true; };
			Func<bool> differs = () => { asked++; return false; };

			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24), 16, 24, same), Is.Null, "stored, current, the same texels: left out");
			Assert.That(asked, Is.EqualTo(1));

			asked = 0;
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(null, 16, 24, same), Does.Contain("stores no index map"), "a scene never cut since: kept");
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24, format: 0), 16, 24, same), Does.Contain("stores no index map"));
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24, format: TerrainIndexMap.StoredFormat + 1), 16, 24, same), Does.Contain("format"));
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24, stale: true), 16, 24, same), Does.Contain("written after"), "painted since: kept");
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24), 32, 24, same), Does.Contain("16²"));
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24), 16, 25, same), Does.Contain("24 layers"));
			TerrainDetailBake.IndexMap cut = Stored(16, 24);
			cut.Map = new uint[10];
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(cut, 16, 24, same), Does.Contain("incomplete"));
			Assert.That(asked, Is.EqualTo(0), "the whole-tile comparison runs only once everything cheaper passed");

			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24), 16, 24, differs), Does.Contain("differs"), "one texel apart: kept");
			Assert.That(ClientTerrainLayerStripper.KeepAlphamapsReason(Stored(16, 24), 16, 0, same), Is.Not.Null, "no alphamaps: nothing to leave out");
		}

		/// <summary>
		/// The stripper copies a tile without alphamaps only on that decision, checks the stored map against the whole tile,
		/// and points the scene's bake (a clone) at the copies so a player finds its tiles; server builds are untouched.
		/// </summary>
		[Test]
		public void TheStripperDecidesPerTileAndRemapsTheBake()
		{
			string stripper = File.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/ClientTerrainLayerStripper.cs");
			Assert.That(stripper, Does.Contain("KeepAlphamapsReason(stored, original.alphamapResolution, original.alphamapLayers"));
			Assert.That(stripper, Does.Contain("TerrainIndexMap.Same(stored.Map, TerrainTileLayerMap.Bake(original.GetAlphamaps(0, 0, original.alphamapResolution, original.alphamapResolution), sceneLayers))"));
			Assert.That(stripper, Does.Contain("ClientCopy(original, layers, keepAlphamaps: keep)"));
			Assert.That(stripper, Does.Contain("RemapBakes(holders, copies, refused)"));
			Assert.That(stripper, Does.Contain("tile.Index = new TerrainDetailBake.IndexMap()"), "a refused map is not shipped: a player would trust it");
			Assert.That(stripper, Does.Contain("Debug.LogWarning"), "a tile that keeps its alphamaps says why");
			Assert.That(stripper, Does.Contain("ClientOnlySceneStripper.IsServerBuild()"), "server builds keep ServerTerrainStripper's copy");
			string server = File.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/ServerTerrainStripper.cs");
			Assert.That(server, Does.Not.Contain("TerrainIndexMap"), "the server copy is as it was");
		}

		// ── The CPU readers ──────────────────────────────────────────

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
			string grass = File.ReadAllText(Scripts + "Client/World/Terrain/Grass/GrassTerrain.cs");
			Assert.That(grass, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(grass, Does.Contain("GrassMath.PackSurfaceFromIndex"));
			string colour = File.ReadAllText(Scripts + "Client/World/Terrain/GroundColourMap.cs");
			Assert.That(colour, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(colour, Does.Contain("WeighIndexTexel(stored.Map[2 * k], stored.Map[2 * k + 1]"));
			string contact = File.ReadAllText(Scripts + "Client/World/Terrain/GroundMaterialMap.cs");
			Assert.That(contact, Does.Contain("TerrainIndexMapBaker.CopyInto(TerrainArrayBinder.IndexMapOf(t), target, slice)"));
			Assert.That(contact, Does.Contain("TerrainIndexMapBaker.Upload(stored"));
			Assert.That(contact, Does.Not.Contain("if (data.alphamapTextureCount == 0)\n"), "a tile without alphamaps is no longer left out");
			// Footstep surfaces (2026-10-21): the alphamaps where the tile kept them, else the stored map through the same filter
			// the terrain shader runs.
			string footsteps = File.ReadAllText(Scripts + "Client/Audio/World/GroundSurface.cs");
			Assert.That(footsteps, Does.Contain("SceneTerrainBake.IndexOf(data)"));
			Assert.That(footsteps, Does.Contain("TerrainIndexMap.Filter(index.Map, index.Resolution, index.Resolution, u, v, ids, weights)"));

			// No other runtime (non-editor) script reads the alphamaps.
			foreach (string file in Directory.GetFiles(Scripts, "*.cs", SearchOption.AllDirectories))
			{
				string path = file.Replace('\\', '/');
				if (path.Contains("/Editor/") || path.EndsWith("TerrainIndexMapBaker.cs") || path.EndsWith("GroundColourMap.cs") || path.EndsWith("GrassTerrain.cs")
					|| path.EndsWith("Audio/World/GroundSurface.cs"))
				{
					continue;
				}
				string text = File.ReadAllText(file);
				Assert.That(text.Contains("GetAlphamaps(") || text.Contains("alphamapTextures"), Is.False, $"{path} reads the alphamaps at runtime");
			}
		}
	}
}
