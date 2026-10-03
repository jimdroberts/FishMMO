using System;
using System.Numerics;
using FishMMO.Client;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The procedural blade grass's arithmetic (GrassMath, mirrored by FishGrassBlades.compute/.hlsl):
	/// determinism, the nested lattice that keeps the far field a subset of the near one, and the blade
	/// record and curve. Pure math: no GPU, no scene.
	/// </summary>
	public class GrassMathTests
	{
		[Test]
		public void Hashes_AreAFunctionOfCellAndSalt()
		{
			uint a = GrassMath.Hash(1203, -88, GrassMath.SaltPriority);
			LogAssert.AreEqual(a, GrassMath.Hash(1203, -88, GrassMath.SaltPriority), "same cell, same hash");
			LogAssert.AreNotEqual(a, GrassMath.Hash(1204, -88, GrassMath.SaltPriority), "the next cell differs");
			LogAssert.AreNotEqual(a, GrassMath.Hash(1203, -88, GrassMath.SaltSpawn), "another salt differs");
			Vector2 r = GrassMath.CellRoot(1203, -88, 0.062f);
			LogAssert.AreEqual(r, GrassMath.CellRoot(1203, -88, 0.062f), "a cell's root is the same every time");
			LogAssert.IsTrue(r.X >= 1203 * 0.062f && r.X < 1204 * 0.062f && r.Y >= -88 * 0.062f && r.Y < -87 * 0.062f, $"the root stays in its cell: {r}");
		}

		[Test]
		public void Thinning_IsNested_AndKeepsTheShareItPromises()
		{
			// Every cell kept at a share has a level at least LevelForShare(share): a tile on that coarser
			// lattice misses none of them.
			float[] shares = { 1f, 0.6f, 0.25f, 0.1f, 0.03f, 0.004f };
			const int side = 256;
			foreach (float share in shares)
			{
				int level = GrassMath.LevelForShare(share);
				int kept = 0;
				for (int j = -side / 2; j < side / 2; j++)
				{
					for (int i = -side / 2; i < side / 2; i++)
					{
						if (GrassMath.CellPriority(i, j) < share)
						{
							kept++;
							LogAssert.IsTrue(GrassMath.CellLevel(i, j) >= level, $"cell ({i}, {j}) kept at share {share} is below lattice level {level}");
						}
					}
				}
				// Priorities are uniform: the kept fraction is the share (the field's density follows the rings exactly).
				float fraction = kept / (float)(side * side);
				LogAssert.IsTrue(Math.Abs(fraction - share) <= Math.Max(0.004f, share * 0.08f), $"share {share}: kept {fraction}");
			}
			// A smaller share keeps a subset: blades thin out and come back without reshuffling.
			for (int j = 0; j < 64; j++)
			{
				for (int i = 0; i < 64; i++)
				{
					float p = GrassMath.CellPriority(i, j);
					LogAssert.IsTrue(!(p < 0.05f) || p < 0.2f, "kept at 0.05 implies kept at 0.2");
				}
			}
		}

		[Test]
		public void Rings_FallFromOne_AndWidenCompensates()
		{
			float[] d = { 6f, 15f, 40f, 100f, 300f };
			float[] n = { 260f, 80f, 20f, 5.5f, 1.5f };
			LogAssert.AreEqual(1f, GrassMath.Share(3f, d, n, 5), "full density inside the first ring");
			float last = 1f;
			for (float x = 6f; x <= 300f; x += 3f)
			{
				float s = GrassMath.Share(x, d, n, 5);
				LogAssert.IsTrue(s <= last + 1e-6f && s > 0f, $"share falls with distance: {x} m → {s}");
				last = s;
			}
			LogAssert.IsTrue(Math.Abs(GrassMath.Share(40f, d, n, 5) - 20f / 260f) < 1e-4f, "a ring point's own density");
			LogAssert.IsTrue(Math.Abs(GrassMath.Widen(0.25f, 0.5f, 6f) - 2f) < 1e-4f, "a quarter of the blades, twice as wide");
			LogAssert.AreEqual(6f, GrassMath.Widen(1e-4f, 0.5f, 6f), "widening is capped");
		}

		[Test]
		public void BladeRecord_RoundTrips()
		{
			uint packed = GrassMath.Pack(7, 2.5f, 0.45f, 0.6f, 0.5f, 0.3f);
			GrassMath.Unpack(packed, out int type, out float facing, out float height, out float lean, out float fade, out float colour);
			LogAssert.AreEqual(7, type, "type");
			LogAssert.IsTrue(Math.Abs(facing - 2.5f) <= 6.2832f / 256f + 1e-4f, $"facing {facing}");
			LogAssert.IsTrue(Math.Abs(height - 0.45f) <= 0.012f, $"height {height}");
			LogAssert.IsTrue(Math.Abs(lean - 0.6f) <= 1f / 30f + 1e-4f, $"lean {lean}");
			LogAssert.IsTrue(Math.Abs(fade - 0.5f) <= 1f / 62f + 1e-4f, $"fade {fade}");
			LogAssert.IsTrue(Math.Abs(colour - 0.3f) <= 1f / 62f + 1e-4f, $"colour {colour}");
		}

		[Test]
		public void TerrainColour_RoundTrips_KeepingDarkSoilSteps()
		{
			// A dark soil (linear ~0.02) must not collapse to black: square-root coding keeps its steps.
			uint packed = GrassMath.PackColour(0.02f, 0.35f, 0.9f, true);
			GrassMath.UnpackColour(packed, out float r, out float g, out float b, out bool valid);
			LogAssert.IsTrue(valid, "valid");
			LogAssert.IsTrue(Math.Abs(r - 0.02f) <= 0.002f, $"r {r}");
			LogAssert.IsTrue(Math.Abs(g - 0.35f) <= 0.005f, $"g {g}");
			LogAssert.IsTrue(Math.Abs(b - 0.9f) <= 0.008f, $"b {b}");
			GrassMath.UnpackColour(GrassMath.PackColour(0.5f, 0.5f, 0.5f, false), out _, out _, out _, out bool none);
			LogAssert.IsFalse(none, "no colour without arrays");
		}

		[Test]
		public void PackSurfaceLayers_KeepsTheTwoStrongest_LessTheThird()
		{
			// Layers 5 (0.5), 2 (0.3), 7 (0.2): the third's 0.2 comes off both, so 0.3 vs 0.1 = a 0.75 share.
			var weights = new float[10];
			weights[5] = 0.5f;
			weights[2] = 0.3f;
			weights[7] = 0.2f;
			uint packed = GrassMath.PackSurfaceLayers(weights, weights.Length);
			LogAssert.AreEqual(5u, packed & 255u, "first layer");
			LogAssert.AreEqual(2u, packed >> 8 & 255u, "second layer");
			LogAssert.AreEqual((uint)Math.Round(0.75f * 255f), packed >> 16 & 255u, "first layer's share");
			LogAssert.AreEqual(255u, packed >> 24, "valid");

			// A single layer is the whole colour; no weight at all is invalid.
			var one = new float[4];
			one[3] = 1f;
			uint single = GrassMath.PackSurfaceLayers(one, one.Length);
			LogAssert.AreEqual(3u, single & 255u, "the only layer");
			LogAssert.AreEqual(255u, single >> 16 & 255u, "all of it");
			LogAssert.AreEqual(0u, GrassMath.PackSurfaceLayers(new float[4], 4), "no layers, no colour");
		}

		[Test]
		public void Curve_KeepsTheBladesLength_UnderLeanBendAndWind()
		{
			var facing = Vector2.Normalize(new Vector2(0.6f, -0.8f));
			foreach (float lean in new[] { 0f, 0.3f, 0.7f })
			{
				foreach (float bend in new[] { 0f, 0.5f, 1f })
				{
					foreach (float push in new[] { 0f, 0.2f, 0.45f })
					{
						GrassMath.Curve(0.5f, lean, bend, facing, new Vector2(push, 0.1f * push), out Vector3 control, out Vector3 tip);
						float length = GrassMath.MeasuredLength(control, tip);
						LogAssert.IsTrue(Math.Abs(length - 0.5f) <= 0.5f * 0.03f, $"lean {lean} bend {bend} push {push}: length {length}");
						LogAssert.IsTrue(tip.Y > 0f || push > 0.3f, "an unpushed blade's tip stays above its root");
					}
				}
			}
		}
	}
}
