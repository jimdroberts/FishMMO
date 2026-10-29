using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// GPU plant capacity (2026-10-27, when the CPU chunk renderer was removed): every detail kind the catalogue makes routes
	/// to a GPU renderer; the caps hold the measured load with headroom; the scatter's sparse density keeps exactly what the
	/// dense slices held (stored counts, bounds) and its painted-cell masks keep an instance on painted ground, while a
	/// wholly painted block places exactly as before; the blade record carries 64 types without moving a bit of types
	/// 0..31 (BladeCapsTests: 128 types, 64 channels); and the wide grass variant is only asked for by a terrain past sixteen channels.
	/// </summary>
	public class GpuPlantCapacityTests
	{
		// ── Routing ──────────────────────────────────────────────────

		/// <summary>Every detail prefab the art generator writes (details, pebbles, small rocks, bushes) is drawn on the GPU.</summary>
		[Test]
		public void EveryCatalogueDetailKind_RoutesToAGpuRenderer()
		{
			var grass = new GrassBladeSettings();
			int blades = 0, scatter = 0;
			foreach (string prefab in ProceduralArtCatalogue.AllPrefabNames())
			{
				if (!prefab.StartsWith("Detail_", StringComparison.Ordinal))
				{
					continue;
				}
				TerrainDetailRouting.Route route = TerrainDetailRouting.RouteOf(prefab, true, grass);
				LogAssert.AreNotEqual(TerrainDetailRouting.Route.None, route, $"{prefab}: no GPU renderer draws it");
				blades += route == TerrainDetailRouting.Route.Blades ? 1 : 0;
				scatter += route == TerrainDetailRouting.Route.Scatter ? 1 : 0;
				if (DetailScatterSettings.IsBush(prefab))
				{
					LogAssert.AreEqual(TerrainDetailRouting.Route.Scatter, route, $"{prefab}: a bush is cover, the scatter's (fixed reach)");
				}
			}
			LogAssert.IsTrue(blades > 0 && scatter > 0, $"both renderers take kinds ({blades} blade, {scatter} scatter)");
			LogAssert.AreEqual(TerrainDetailRouting.Route.None, TerrainDetailRouting.RouteOf("Detail_FernBracken", false, grass), "a texture detail has no GPU renderer");
			LogAssert.AreEqual(TerrainDetailRouting.Route.Blades, TerrainDetailRouting.RouteOf("Detail_GrassLush", true, grass), "grass is blades");
			LogAssert.AreEqual(TerrainDetailRouting.Route.Scatter, TerrainDetailRouting.RouteOf("Detail_FernBracken", true, grass), "a fern is the scatter's (it was the chunk renderer's)");
		}

		/// <summary>
		/// The generated prefabs themselves (Unity-only: loads them): every scatter kind has the one sub-mesh and the
		/// procedural-instancing twin the scatter draws through, so none is left undrawn at play.
		/// </summary>
		[Test]
		public void EveryGeneratedScatterPrefab_TheScatterCanBuild()
		{
#if UNITY_EDITOR
			var grass = new GrassBladeSettings();
			int checkedCount = 0;
			var refused = new List<string>();
			foreach (string prefab in ProceduralArtCatalogue.AllPrefabNames())
			{
				if (!prefab.StartsWith("Detail_", StringComparison.Ordinal) || TerrainDetailRouting.RouteOf(prefab, true, grass) != TerrainDetailRouting.Route.Scatter)
				{
					continue;
				}
				var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"{ProceduralArtCatalogue.PrefabsFolder}/{prefab}.prefab");
				if (go == null)
				{
					continue;
				}
				var prototype = new DetailPrototype { prototype = go, usePrototypeMesh = true, renderMode = DetailRenderMode.VertexLit };
				if (DetailScatterType.Build(prototype, 0, out string reason) == null)
				{
					refused.Add(reason);
				}
				checkedCount++;
			}
			if (checkedCount == 0)
			{
				Assert.Ignore("No generated detail prefabs on this machine (run Generate biome art).");
			}
			LogAssert.AreEqual(0, refused.Count, $"{refused.Count} of {checkedCount} scatter kinds cannot be drawn: {string.Join("; ", refused)}");
#else
			Assert.Ignore("Editor only.");
#endif
		}

		// ── The caps ─────────────────────────────────────────────────

		/// <summary>
		/// Measured 2026-10-27 over the TerrainData (painted, not listed): Moron 2 scene 38 blade prefabs / 84 mesh kinds, a
		/// tile at most 29 blade prefabs / 63 mesh kinds; Moron 3 27 / 115, tiles 26 / 88. The audit's spec basis for the
		/// busiest scene: 105 blade, 127 scatter + 127 other kinds.
		/// </summary>
		[Test]
		public void Caps_HoldTheMeasuredLoad_WithHeadroom()
		{
			LogAssert.IsTrue(DetailScatterMath.MaxChannels >= 2 * 88, $"scatter channels a terrain {DetailScatterMath.MaxChannels}");
			LogAssert.IsTrue(DetailScatterMath.MaxChannels <= 1 << DetailScatterMath.EntryChannelBits, "an entry can name every channel");
			LogAssert.IsTrue(DetailScatterSystem.MaxTypes >= 2 * (127 + 127), $"scatter types a scene {DetailScatterSystem.MaxTypes}");
			LogAssert.IsTrue(DetailScatterMath.BlocksPerItem * DetailScatterMath.MaxEntriesPerBlock < 65536, "an item's block starts fit sixteen bits");
			LogAssert.AreEqual(1 + 33, DetailScatterMath.ItemHeaderUints, "an item header: its first entry and 65 sixteen-bit starts");
			LogAssert.AreEqual(GrassTerrain.MaxChannels / 4, GrassTerrain.MaxDensityMaps, "four channels a density map");
			LogAssert.AreEqual(82, GrassTerrainAtlas.TerrainUints, "channels 16..63 appended after every older field");
			string compute = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.compute");
			LogAssert.IsTrue(compute.Contains($"#define GRASS_TYPES {GrassMath.MaxTypes} "), "the compute's type cap is the record's");
			LogAssert.IsTrue(compute.Contains($"#define GRASS_TERRAIN_UINTS {GrassTerrainAtlas.TerrainUints}u"), "the compute's table stride is the atlas's");
			LogAssert.IsTrue(compute.Contains("#pragma multi_compile_local __ GRASS_WIDE GRASS_WIDEST"), "the 32- and 64-channel variants exist, one at a time");
			string scatter = File.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishDetailScatter.compute");
			LogAssert.IsTrue(scatter.Contains($"#define SCATTER_TERRAIN_HEADER {DetailScatterAtlas.HeaderUints}u"), "the scatter row header matches");
			LogAssert.IsTrue(scatter.Contains($"#define SCATTER_ITEM_HEADER_UINTS {DetailScatterMath.ItemHeaderUints}u"), "the item header matches");
			LogAssert.IsTrue(scatter.Contains($"#define SCATTER_CELL_STREAM {DetailScatterMath.CellStream}u"), "the cell stream matches");
			LogAssert.IsFalse(scatter.Contains("_ScatterDensity"), "no dense density slices are bound");
		}

		/// <summary>No CPU detail path is left: the chunk renderer's files are gone and the driver builds nothing.</summary>
		[Test]
		public void NoCpuChunkRenderer_IsLeft()
		{
			const string folder = "Assets/Scripts/Client/World/Terrain/";
			LogAssert.IsFalse(File.Exists(folder + "TerrainDetailField.cs"), "TerrainDetailField removed");
			LogAssert.IsFalse(File.Exists(folder + "TerrainDetailModel.cs"), "TerrainDetailModel removed");
			string driver = File.ReadAllText(folder + "TerrainDetailInstancing.cs");
			foreach (string cpu in new[] { "RenderMeshInstanced", "DrawMeshInstanced", "GetDetailLayer", "TerrainGpuRenderer.TryCreate" })
			{
				LogAssert.IsFalse(driver.Contains(cpu), $"the driver does not {cpu}");
			}
			string settings = File.ReadAllText(folder + "Scatter/DetailScatterSettings.cs");
			LogAssert.IsFalse(settings.Contains("PrototypePrefixes"), "the scatter takes every non-blade kind (no prefix list leaves one to a CPU path)");
		}

		// ── The sparse density ───────────────────────────────────────

		/// <summary>A synthetic terrain: per channel per block an expected count (mostly empty) and a painted-cell mask.</summary>
		private static void Synthetic(int blocks, int channels, int seed, out float[][] expected, out byte[][] masks)
		{
			var random = new System.Random(seed);
			expected = new float[channels][];
			masks = new byte[channels][];
			for (int c = 0; c < channels; c++)
			{
				expected[c] = new float[blocks * blocks];
				masks[c] = new byte[blocks * blocks];
				for (int i = 0; i < blocks * blocks; i++)
				{
					if (random.NextDouble() < 0.12)
					{
						expected[c][i] = (float)(random.NextDouble() * (random.NextDouble() < 0.02 ? 200.0 : 6.0));
						masks[c][i] = (byte)random.Next(1, 16);
					}
				}
			}
		}

		[Test]
		public void SparseDensity_KeepsEveryPaintedPair_AndTheDenseBounds()
		{
			const int blocks = 45, channels = 13, factor = 2, stripBlocks = 32;
			Synthetic(blocks, channels, 7, out float[][] expected, out byte[][] masks);
			var density = new DetailScatterDensity(blocks, channels, factor);
			// Emitted a strip at a time, as the build does (strips of whole item rows; the last is partial).
			for (int row0 = 0; row0 < blocks; row0 += stripBlocks)
			{
				int rows = Math.Min(stripBlocks, blocks - row0);
				var window = new float[channels][];
				var cells = new byte[channels][];
				for (int c = 0; c < channels; c++)
				{
					window[c] = new float[stripBlocks * blocks];
					cells[c] = new byte[stripBlocks * blocks];
					Array.Copy(expected[c], row0 * blocks, window[c], 0, rows * blocks);
					Array.Copy(masks[c], row0 * blocks, cells[c], 0, rows * blocks);
				}
				density.EmitRows(window, cells, row0, rows);
			}
			LogAssert.IsTrue(density.Complete, "every item emitted");
			uint[] entries = density.Entries.ToArray();
			int side = DetailScatterMath.Items(blocks);
			var denseBounds = new int[side * side, channels];
			for (int bz = 0; bz < blocks; bz++)
			{
				for (int bx = 0; bx < blocks; bx++)
				{
					int item = bz / DetailScatterMath.ItemSide * side + bx / DetailScatterMath.ItemSide;
					int b = bz % DetailScatterMath.ItemSide * DetailScatterMath.ItemSide + bx % DetailScatterMath.ItemSide;
					int from = density.ItemEntryStart[item] + density.BlockStarts[item * 65 + b];
					int to = density.ItemEntryStart[item] + density.BlockStarts[item * 65 + b + 1];
					int seen = 0, lastChannel = -1;
					for (int c = 0; c < channels; c++)
					{
						float e = expected[c][bz * blocks + bx];
						float stored = DetailScatterMath.Stored(e);
						denseBounds[item, c] += DetailScatterMath.Bound(stored);
						if (!(e > 0f))
						{
							continue;
						}
						LogAssert.IsTrue(from + seen < to, $"block ({bx}, {bz}) channel {c} has its entry");
						DetailScatterMath.UnpackEntry(entries[from + seen], out float got, out int channel, out uint mask);
						LogAssert.AreEqual(c, channel, $"block ({bx}, {bz}): channels ascending");
						LogAssert.IsTrue(channel > lastChannel, "ascending");
						lastChannel = channel;
						LogAssert.AreEqual(BitConverter.SingleToInt32Bits(stored), BitConverter.SingleToInt32Bits(got), $"block ({bx}, {bz}) channel {c}: the stored count the dense slice held");
						LogAssert.AreEqual((uint)masks[c][bz * blocks + bx], mask, $"block ({bx}, {bz}) channel {c}: its painted cells");
						seen++;
					}
					LogAssert.AreEqual(to - from, seen, $"block ({bx}, {bz}): no entry for an unpainted channel");
				}
			}
			DetailScatterDensity.ItemBounds(density.ItemEntryStart, entries, channels, out int[] start, out ushort[] boundChannel, out int[] bound);
			for (int item = 0; item < side * side; item++)
			{
				var sparse = new int[channels];
				for (int k = start[item]; k < start[item + 1]; k++)
				{
					sparse[boundChannel[k]] += bound[k];
				}
				for (int c = 0; c < channels; c++)
				{
					LogAssert.AreEqual(denseBounds[item, c], sparse[c], $"item {item} channel {c}: the upper bound the dense build gave");
				}
			}
			LogAssert.IsTrue(density.ClampedBlocks > 0, "the synthetic terrain has blocks over the per-block cap (counted)");
		}

		[Test]
		public void ItemHeaders_DecodeToTheBlockStarts()
		{
			const int blocks = 20, channels = 5;
			Synthetic(blocks, channels, 11, out float[][] expected, out byte[][] masks);
			var density = new DetailScatterDensity(blocks, channels, 2);
			density.EmitRows(expected, masks, 0, blocks);
			int items = DetailScatterMath.Items(blocks) * DetailScatterMath.Items(blocks);
			var table = new uint[3 + items * DetailScatterMath.ItemHeaderUints];
			DetailScatterDensity.WriteHeaders(density.ItemEntryStart, density.BlockStarts, 1000u, table, 3);
			for (int i = 0; i < items; i++)
			{
				int h = 3 + i * DetailScatterMath.ItemHeaderUints;
				LogAssert.AreEqual(1000u + (uint)density.ItemEntryStart[i], table[h], $"item {i}: its first entry");
				for (int b = 0; b <= DetailScatterMath.BlocksPerItem; b++)
				{
					// FishDetailScatter.compute ScatterHeaderStart.
					uint start = table[h + 1 + (b >> 1)] >> ((b & 1) * 16) & 0xFFFFu;
					LogAssert.AreEqual((uint)density.BlockStarts[i * 65 + b], start, $"item {i} block {b}");
				}
			}
		}

		[Test]
		public void Entries_RoundTrip()
		{
			foreach (float e in new[] { 0.0004f, 0.37f, 1f, 7.25f, 127.9f, 128f })
			{
				foreach (int c in new[] { 0, 1, 95, 255, 4095 })
				{
					foreach (uint m in new[] { 1u, 6u, 0xFu })
					{
						float stored = DetailScatterMath.Stored(e);
						DetailScatterMath.UnpackEntry(DetailScatterMath.PackEntry(stored, c, m), out float got, out int channel, out uint mask);
						LogAssert.AreEqual(BitConverter.SingleToInt32Bits(stored), BitConverter.SingleToInt32Bits(got), $"{e}: stored");
						LogAssert.AreEqual(c, channel, "channel");
						LogAssert.AreEqual(m, mask, "mask");
					}
				}
			}
		}

		// ── Painted cells ────────────────────────────────────────────

		[Test]
		public void PaintedCells_KeepInstancesOnPaint_AndAFullBlockPlacesAsBefore()
		{
			var size = new Vector2(2f, 2f);
			for (int i = 0; i < 4000; i++)
			{
				uint h = DetailScatterMath.Hash(i, -i, 0x1234567u, 3, i + 1);
				// A wholly painted block (or a mask-less one): exactly the old place, InBlock across the extent.
				Vector2 old = Vector2.Scale(DetailScatterMath.InBlock(h), size);
				LogAssert.AreEqual(old, DetailScatterMath.InBlockCells(h, 0xFu, 2, size, size), "full mask: as before");
				LogAssert.AreEqual(old, DetailScatterMath.InBlockCells(h, 0x3u, 4, size, size), "finer cells keep no mask: as before");
				// A block painted on two cells: the instance stands in one of them.
				Vector2 p = DetailScatterMath.InBlockCells(h, 0x9u, 2, size, size);
				int cell = (p.x >= 1f ? 1 : 0) + (p.y >= 1f ? 2 : 0);
				LogAssert.IsTrue(cell == 0 || cell == 3, $"mask 1001: stands in cell 0 or 3, not {cell} ({p})");
			}
			// Each painted cell alike likely.
			var hits = new int[4];
			for (int i = 0; i < 40000; i++)
			{
				hits[DetailScatterMath.PickCell(0xEu, TerrainDetailMath.Unit((uint)i * 2654435761u, DetailScatterMath.CellStream))]++;
			}
			LogAssert.AreEqual(0, hits[0], "an unpainted cell is never picked");
			for (int c = 1; c < 4; c++)
			{
				LogAssert.IsTrue(Math.Abs(hits[c] - 40000 / 3) < 600, $"cell {c}: {hits[c]} of 40000");
			}
			LogAssert.AreEqual(1u, DetailScatterMath.CellBit(1, 0, 2) >> 1, "cell (1, 0) is bit 1");
			LogAssert.AreEqual(8u, DetailScatterMath.CellBit(1, 1, 2), "cell (1, 1) is bit 3");
			LogAssert.AreEqual(1u, DetailScatterMath.FullMask(1), "a 2 m cell's block is one cell");
		}

		[Test]
		public void StripRows_AreWholeItemRows()
		{
			foreach (int factor in new[] { 1, 2, 3, 4, 8 })
			{
				int rows = DetailScatterMath.StripRows(factor);
				LogAssert.AreEqual(0, rows % (DetailScatterMath.ItemSide * factor), $"factor {factor}: {rows} rows are whole item rows");
				LogAssert.IsTrue(rows >= DetailScatterMath.ItemSide * factor, $"factor {factor}: at least one item row");
			}
		}

		// ── The blade record ─────────────────────────────────────────

		[Test]
		public void BladeRecord_CarriesSixtyFourTypes_AndTypesUnder32KeepTheirBits()
		{
			for (int type = 0; type < GrassMath.MaxTypes; type++)
			{
				foreach (float height in new[] { 0.3f, 2.4f, 3.6f })
				{
					uint packed = GrassMath.Pack(type, 1.1f, height, 0.4f, 0.2f, 0.7f);
					uint colour = GrassMath.WithHeadPart(GrassMath.PackColour(0.2f, 0.5f, 0.1f, true), 1) | GrassMath.RecordBits(type, height);
					GrassMath.Unpack(packed, colour, GrassMath.RecordMore(type), out int got, out _, out _, out _, out _, out _);
					LogAssert.AreEqual(type, got, $"type {type} at {height} m");
					GrassMath.UnpackColour(colour, out _, out _, out _, out bool valid);
					LogAssert.IsTrue(valid, $"type {type}: the colour is valid whatever the type's bits");
					// A shadow caster's record: no colour, the shape bits alone.
					GrassMath.UnpackColour(GrassMath.RecordBits(type, height), out _, out _, out _, out bool shadow);
					LogAssert.IsFalse(shadow, $"type {type}: a shadow record's colour is invalid");
					if (type < 32)
					{
						LogAssert.AreEqual(0u, GrassMath.RecordBits(type, height) >> GrassMath.TypeTopShift, $"type {type}: bit 31 untouched (bit for bit as before)");
					}
				}
			}
			GrassMath.UnpackColour(GrassMath.PackColour(0f, 0f, 0f, true), out _, out _, out _, out bool black);
			LogAssert.IsTrue(black, "a black ground's colour still reads as valid");
			LogAssert.AreEqual(0u, GrassMath.PackColour(0.5f, 0.5f, 0.5f, false), "an invalid colour is all zero");
		}
	}
}
