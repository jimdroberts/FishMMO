using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Stored index maps: the cut's CPU bake of a tile's alphamaps
	/// (<see cref="TerrainIndexMap.Bake(float[,,])"/>) is the GPU bake's texels exactly, a stored map is drawn only while it
	/// is current, a client build leaves a tile's alphamaps out only when its stored map is theirs texel for texel, and the
	/// CPU readers that used the alphamaps (the grass's surface layers, the ground colour map) read the same layers off a
	/// stored map. Pure: no GPU, no assets, no native terrain.
	/// </summary>
	[TestFixture]
	public class TerrainIndexStoreTests
	{
		private const string Scripts = "Assets/FishMMO/";

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
			string stripper = ProjectFiles.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/ClientTerrainLayerStripper.cs");
			Assert.That(stripper, Does.Contain("KeepAlphamapsReason(stored, original.alphamapResolution, original.alphamapLayers"));
			Assert.That(stripper, Does.Contain("TerrainIndexMap.Same(stored.Map, TerrainTileLayerMap.Bake(original.GetAlphamaps(0, 0, original.alphamapResolution, original.alphamapResolution), sceneLayers))"));
			Assert.That(stripper, Does.Contain("ClientCopy(original, layers, keepAlphamaps: keep)"));
			Assert.That(stripper, Does.Contain("RemapBakes(holders, copies, refused)"));
			Assert.That(stripper, Does.Contain("tile.Index = new TerrainDetailBake.IndexMap()"), "a refused map is not shipped: a player would trust it");
			Assert.That(stripper, Does.Contain("Debug.LogWarning"), "a tile that keeps its alphamaps says why");
			Assert.That(stripper, Does.Contain("ClientOnlySceneStripper.IsServerBuild()"), "server builds keep ServerTerrainStripper's copy");
			string server = ProjectFiles.ReadAllText(Scripts + "Shared/Implementation/Tools/Extensions/Unity/Editor/ServerTerrainStripper.cs");
			Assert.That(server, Does.Not.Contain("TerrainIndexMap"), "the server copy is as it was");
		}

		// ── The CPU readers ──────────────────────────────────────────

	}
}
