using System;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The GPU detail scatter's arithmetic (DetailScatterMath, mirrored by FishDetailScatter.compute): a block's count
	/// never exceeds the bound the CPU sizes the slots by, counts keep the painted total, the hashes are the chunk
	/// renderer's, the thinning keeps what it promises; and the chunk renderer's per-owner skip masks, which let the
	/// blade grass and the scatter each hand their prototypes over and back without undoing the other's.
	/// </summary>
	public class DetailScatterMathTests
	{
		[Test]
		public void BlockCount_NeverPassesItsBound_AndKeepsTheExpectedTotal()
		{
			float[] expectations = { 0.004f, 0.3f, 1f, 1.5f, 7.25f, 33.3f, 127.9f, 500f };
			foreach (float e in expectations)
			{
				float stored = DetailScatterMath.Stored(e);
				int bound = DetailScatterMath.Bound(stored);
				long sum = 0;
				const int blocks = 20000;
				for (int i = 0; i < blocks; i++)
				{
					int n = DetailScatterMath.Count(stored, DetailScatterMath.Hash(i, -i * 3, 0xABCDEF01u, 17, 0));
					LogAssert.IsTrue(n <= bound, $"expected {e}: a block holds {n}, over its bound {bound}");
					LogAssert.IsTrue(n <= DetailScatterMath.MaxPerBlock, $"expected {e}: {n} over the cap");
					sum += n;
				}
				float mean = sum / (float)blocks;
				float want = Mathf.Min(e, DetailScatterMath.MaxPerBlock);
				// Half floats keep about three significant digits; the coin's mean is the fraction.
				LogAssert.IsTrue(Math.Abs(mean - want) <= Math.Max(0.01f, want * 0.01f), $"expected {e}: mean {mean}");
			}
			LogAssert.AreEqual(0, DetailScatterMath.Bound(DetailScatterMath.Stored(0f)), "an empty block bounds nothing");
			LogAssert.AreEqual(0, DetailScatterMath.Bound(DetailScatterMath.Stored(float.NaN)), "a bad value bounds nothing");
		}

		[Test]
		public void Hashes_AreTheChunkRenderers_AndSaltsAreStable()
		{
			LogAssert.AreEqual(TerrainDetailMath.Hash(12, -7, unchecked((int)0x9000000Fu), 5, 3), DetailScatterMath.Hash(12, -7, 0x9000000Fu, 5, 3), "the same hash as the chunk renderer's cells");
			uint pebbles = DetailScatterMath.Salt("Detail_Pebbles_Grey");
			LogAssert.AreEqual(pebbles, DetailScatterMath.Salt("Detail_Pebbles_Grey"), "a name's salt is the same every time (every session, every player)");
			LogAssert.AreNotEqual(pebbles, DetailScatterMath.Salt("Detail_Pebbles_Basalt"), "another prefab, another salt");
			Vector2 p = DetailScatterMath.InBlock(DetailScatterMath.Hash(3, 4, pebbles, 1, 1));
			LogAssert.IsTrue(p.x >= 0f && p.x < 1f && p.y >= 0f && p.y < 1f, $"an instance stands inside its block: {p}");
			float yaw = DetailScatterMath.Yaw(DetailScatterMath.Hash(3, 4, pebbles, 1, 1));
			LogAssert.IsTrue(yaw >= 0f && yaw < 2f * Mathf.PI, $"yaw {yaw}");
			float h = DetailScatterMath.PositionHash01(103.25f, -88.5f);
			LogAssert.IsTrue(h >= 0f && h < 1f, $"position hash {h}");
		}

		[Test]
		public void Blocks_TileTheTerrain_AndAnchorToTheWorld()
		{
			// A 0.5 m detail cell makes 4-cell blocks of 2 m; 1056 cells make 264 blocks and 33 work items a side.
			int factor = DetailScatterMath.BlockFactor(0.5f);
			LogAssert.AreEqual(4, factor, "block factor");
			LogAssert.AreEqual(264, DetailScatterMath.Blocks(1056, factor), "blocks");
			LogAssert.AreEqual(265, DetailScatterMath.Blocks(1057, factor), "a partial last block");
			LogAssert.AreEqual(33, DetailScatterMath.Items(264), "items");
			LogAssert.AreEqual(1, DetailScatterMath.BlockFactor(3f), "a coarse layer is never under one cell a block");
			// Two neighbouring terrains' blocks continue one world lattice: block 264 of the first is block 0 of the next.
			LogAssert.AreEqual(528, DetailScatterMath.WorldBlockBase(1056f, 2f), "world block base");
			LogAssert.AreEqual(-528, DetailScatterMath.WorldBlockBase(-1056f, 2f), "negative origins floor");
			LogAssert.AreEqual(DetailScatterMath.WorldBlockBase(0f, 2f) + 264, DetailScatterMath.WorldBlockBase(528f, 2f), "neighbours line up");
		}

		[Test]
		public void Thinning_KeepsItsShare_AndTheEdgeDissolves()
		{
			LogAssert.AreEqual(1f, DetailScatterMath.Keep(40f, 60f, 0.25f, 200f), "whole inside the thin start");
			LogAssert.IsTrue(Math.Abs(DetailScatterMath.Keep(200f, 60f, 0.25f, 200f) - 0.25f) < 1e-5f, "the share kept at the draw distance");
			LogAssert.AreEqual(1f, DetailScatterMath.Keep(150f, 60f, 1f, 200f), "keep 1: no thinning");
			LogAssert.AreEqual(0f, DetailScatterMath.ThinFade(0.5f, 0.1f), "well under the share: whole");
			LogAssert.AreEqual(1f, DetailScatterMath.ThinFade(0.5f, 0.6f), "over the share: gone");
			float band = DetailScatterMath.ThinFade(0.5f, 0.5f - DetailScatterMath.ThinBand * 0.5f);
			LogAssert.IsTrue(band > 0.4f && band < 0.6f, $"inside the band: dithered ({band})");
			// The share of a field kept matches the promise.
			int kept = 0;
			const int n = 40000;
			for (int i = 0; i < n; i++)
			{
				float hash = DetailScatterMath.PositionHash01(i * 0.731f, i * -0.377f + 11f);
				kept += DetailScatterMath.ThinFade(0.3f, hash) < 1f ? 1 : 0;
			}
			LogAssert.IsTrue(Math.Abs(kept / (float)n - 0.3f) < 0.01f, $"kept {kept / (float)n} of a 0.3 share");
			LogAssert.AreEqual(0f, DetailScatterMath.EdgeFade(150f, 200f, 0.1f), "inside the edge band: whole");
			LogAssert.AreEqual(1f, DetailScatterMath.EdgeFade(200f, 200f, 0.1f), "at the distance: gone");
			LogAssert.IsTrue(Math.Abs(DetailScatterMath.EdgeFade(190f, 200f, 0.1f) - 0.5f) < 1e-4f, "half way through the band");
		}

		[Test]
		public void SkipMasks_ArePerOwner_SoOneOwnersReleaseKeepsTheOthers()
		{
			var go = new GameObject("skip mask terrain");
			try
			{
				Terrain terrain = go.AddComponent<Terrain>();
				object grass = new object(), scatter = new object();
				TerrainDetailInstancing.SetSkipped(grass, terrain, new[] { true, false, false });
				TerrainDetailInstancing.SetSkipped(scatter, terrain, new[] { false, false, true });
				LogAssert.IsTrue(TerrainDetailInstancing.IsSkipped(terrain, 0), "the grass's prototype is skipped");
				LogAssert.IsFalse(TerrainDetailInstancing.IsSkipped(terrain, 1), "nobody's prototype is drawn");
				LogAssert.IsTrue(TerrainDetailInstancing.IsSkipped(terrain, 2), "the scatter's prototype is skipped");
				TerrainDetailInstancing.SetSkipped(grass, terrain, null);
				LogAssert.IsFalse(TerrainDetailInstancing.IsSkipped(terrain, 0), "the grass gave its prototype back");
				LogAssert.IsTrue(TerrainDetailInstancing.IsSkipped(terrain, 2), "the scatter's prototype stays skipped after the grass's release");
				TerrainDetailInstancing.SetSkipped(scatter, terrain, null);
				LogAssert.IsFalse(TerrainDetailInstancing.IsSkipped(terrain, 2), "all given back");
				LogAssert.IsFalse(TerrainDetailInstancing.IsSkipped(terrain, 7), "out of range is never skipped");
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
	}
}
