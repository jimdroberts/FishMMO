using System;
using System.Collections.Generic;
using System.Numerics;
using FishMMO.Client;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The rock seam fillets' arithmetic (RockFilletMath, mirrored by FishRockFillets.compute): the smooth minimum's bound,
	/// how far a fillet can stand outside the rocks it joins (the colliders), the cell test that picks seam cells, and the
	/// kept surface's edge staying tucked under the rocks. Analytic rocks (spheres, boxes) through the CPU reference
	/// mesher. Pure math: no GPU, no scene.
	/// </summary>
	public class RockFilletMathTests
	{
		private const float Width = 1f;
		private const float Inset = 0.06f;
		private const float Tuck = 0.03f;

		private static float Sphere(Vector3 p, Vector3 centre, float radius) => Vector3.Distance(p, centre) - radius;

		private static float Box(Vector3 p, Vector3 centre, Vector3 half)
		{
			Vector3 q = Vector3.Abs(p - centre) - half;
			return Vector3.Max(q, Vector3.Zero).Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f);
		}

		private static Func<Vector3, RockFilletMath.Sample> FieldOf(Func<Vector3, float>[] rocks, float width)
		{
			var d = new float[rocks.Length];
			return p =>
			{
				for (int i = 0; i < rocks.Length; i++)
				{
					d[i] = rocks[i](p);
				}
				RockFilletMath.TwoSmallest(d, rocks.Length, out float d1, out float d2, out int first, out _);
				return new RockFilletMath.Sample { F = RockFilletMath.Field(d1, d2, width, Inset), H = d1, Nearest = first };
			};
		}

		/// <summary>Every cell of the box [lo, hi) (whole cells) that the cell test passes, meshed.</summary>
		private static List<RockFilletMath.Triangle> MeshRegion(Func<Vector3, float>[] rocks, Vector3 lo, Vector3 hi, float tuck, out int cellsTested, out int cellsPassed, out int cellsWithTriangles, bool useCellTest = true)
		{
			Func<Vector3, RockFilletMath.Sample> field = FieldOf(rocks, Width);
			var output = new List<RockFilletMath.Triangle>();
			var centre = new float[rocks.Length];
			cellsTested = cellsPassed = cellsWithTriangles = 0;
			float c = RockFilletMath.CellSize;
			for (float z = lo.Z; z < hi.Z - 1e-3f; z += c)
			{
				for (float y = lo.Y; y < hi.Y - 1e-3f; y += c)
				{
					for (float x = lo.X; x < hi.X - 1e-3f; x += c)
					{
						var min = new Vector3(x, y, z);
						for (int i = 0; i < rocks.Length; i++)
						{
							centre[i] = rocks[i](min + new Vector3(c * 0.5f));
						}
						cellsTested++;
						bool pass = RockFilletMath.CellMayHoldFillet(centre, rocks.Length, Width, Inset);
						cellsPassed += pass ? 1 : 0;
						if (useCellTest && !pass)
						{
							continue;
						}
						int before = output.Count;
						RockFilletMath.MeshCell(field, min, tuck, output);
						cellsWithTriangles += output.Count > before ? 1 : 0;
					}
				}
			}
			return output;
		}

		[Test]
		public void SmoothMin_StaysWithinAQuarterWidthUnderTheMinimum()
		{
			var random = new Random(7);
			for (int i = 0; i < 20000; i++)
			{
				float a = (float)(random.NextDouble() * 4.0 - 2.0);
				float b = (float)(random.NextDouble() * 4.0 - 2.0);
				float k = (float)(random.NextDouble() * 1.5);
				float s = RockFilletMath.SmoothMin(a, b, k);
				float m = MathF.Min(a, b);
				LogAssert.IsTrue(s <= m + 1e-6f && s >= m - k * 0.25f - 1e-6f, $"smin({a}, {b}, {k}) = {s} outside [{m - k * 0.25f}, {m}]");
				if (MathF.Abs(a - b) >= k)
				{
					LogAssert.IsTrue(MathF.Abs(s - m) < 1e-6f, $"apart by the width or more, smin is the minimum ({a}, {b}, {k})");
				}
				LogAssert.IsTrue(MathF.Abs(s - RockFilletMath.SmoothMin(b, a, k)) < 1e-6f, "symmetric in its rocks");
			}
			LogAssert.IsTrue(MathF.Abs(RockFilletMath.SmoothMin(0.3f, 0.3f, Width) - (0.3f - Width * 0.25f)) < 1e-6f, "the full quarter width where they meet");
		}

		[Test]
		public void PairWidth_IsCappedBySizeAndByTheGridsReach()
		{
			LogAssert.AreEqual(1f, RockFilletMath.PairWidth(1f, 30f, 12f, 0.5f), "big rocks take the profile's width");
			LogAssert.AreEqual(0.4f, RockFilletMath.PairWidth(1f, 30f, 0.8f, 0.5f), "a small stone never more than half its size");
			LogAssert.AreEqual(RockFilletMath.MaxWidth, RockFilletMath.PairWidth(4f, 30f, 30f, 0.5f), "never wider than the gathered rocks reach");
		}

		[Test]
		public void TwoSmallest_IsOrderFree()
		{
			float[] a = { 3f, -0.2f, 0.5f, 7f };
			float[] b = { 7f, 0.5f, 3f, -0.2f };
			RockFilletMath.TwoSmallest(a, 4, out float a1, out float a2, out int af, out int asecond);
			RockFilletMath.TwoSmallest(b, 4, out float b1, out float b2, out int bf, out _);
			LogAssert.AreEqual(a1, b1, "the nearest is the same whatever the order");
			LogAssert.AreEqual(a2, b2, "and the second");
			LogAssert.AreEqual(1, af, "first index");
			LogAssert.AreEqual(2, asecond, "second index");
			LogAssert.AreEqual(3, bf, "first index in the other order");
		}

		[Test]
		public void LoneRock_HasNoFillet()
		{
			var rocks = new Func<Vector3, float>[] { p => Sphere(p, new Vector3(4f, 4f, 4f), 2.6f) };
			List<RockFilletMath.Triangle> tris = MeshRegion(rocks, Vector3.Zero, new Vector3(8f, 8f, 8f), Tuck, out _, out int passed, out _, useCellTest: false);
			LogAssert.AreEqual(0, tris.Count, "a rock with no neighbour keeps its own surface: the inset surface is all under it");
			LogAssert.AreEqual(0, passed, "and the cell test passes no cell");
		}

		[Test]
		public void TwoBoulders_FilletStaysWithinTheBound_AndItsEdgeIsTucked()
		{
			// Two boulders overlapping a little, as the scatter lays them.
			var rocks = new Func<Vector3, float>[]
			{
				p => Sphere(p, new Vector3(3.2f, 4f, 4f), 2f),
				p => Sphere(p, new Vector3(6.6f, 4.3f, 4.1f), 1.8f),
			};
			CheckFillet(rocks, Vector3.Zero, new Vector3(10f, 8f, 8f), "boulders");
		}

		[Test]
		public void WallOnLedge_FilletStaysWithinTheBound_AndItsEdgeIsTucked()
		{
			// A cliff section standing on another at right angles, a little tilted: the rock walls' usual seam.
			var rocks = new Func<Vector3, float>[]
			{
				p => Box(p, new Vector3(6f, 2f, 6f), new Vector3(5f, 1.5f, 5f)),
				p =>
				{
					Vector3 q = p - new Vector3(6f, 6f, 6f);
					float c = MathF.Cos(0.12f), s = MathF.Sin(0.12f);
					q = new Vector3(c * q.X - s * q.Y, s * q.X + c * q.Y, q.Z);
					return Box(q, Vector3.Zero, new Vector3(1.2f, 3f, 4f));
				},
			};
			CheckFillet(rocks, Vector3.Zero, new Vector3(12f, 10f, 12f), "wall on ledge");
		}

		private static void CheckFillet(Func<Vector3, float>[] rocks, Vector3 lo, Vector3 hi, string what)
		{
			List<RockFilletMath.Triangle> tris = MeshRegion(rocks, lo, hi, Tuck, out int tested, out int passed, out int withTriangles);
			LogAssert.IsTrue(tris.Count > 0, $"{what}: the seam is filled");
			float bound = RockFilletMath.MaxOffset(Width, Inset);
			float most = float.MinValue;
			foreach (RockFilletMath.Triangle t in tris)
			{
				most = MathF.Max(most, MathF.Max(t.HA, MathF.Max(t.HB, t.HC)));
			}
			Console.WriteLine($"{what}: {tris.Count} triangles in {withTriangles}/{passed}/{tested} cells (meshed/passed/tested); fillet stands at most {most:F3} m outside the rocks (bound k/4 − inset = {bound:F3} m)");
			// Linear interpolation along a lattice edge of a field that bends: a few centimetres over the exact bound.
			LogAssert.IsTrue(most <= bound + 0.03f, $"{what}: the fillet stays within k/4 − inset of the rocks ({most} > {bound})");
			LogAssert.IsTrue(most >= 0.5f * bound, $"{what}: the fillet shows in the crease ({most} of {bound})");

			// The cell test is conservative: no cell it rejects would have drawn anything.
			List<RockFilletMath.Triangle> all = MeshRegion(rocks, lo, hi, Tuck, out _, out _, out int everyCell, useCellTest: false);
			LogAssert.AreEqual(tris.Count, all.Count, $"{what}: the cell test skips no triangle ({everyCell} cells meshed without it)");

			// The kept surface's open edge is under the rocks by at least the tuck: an edge used once borders a dropped
			// triangle, all of whose corners are deeper than that.
			var uses = new Dictionary<(long, long), int>();
			var depth = new Dictionary<long, float>();
			long Key(Vector3 p, float h)
			{
				long k = ((long)MathF.Round(p.X * 4096f) * 73856093L) ^ ((long)MathF.Round(p.Y * 4096f) * 19349663L) ^ ((long)MathF.Round(p.Z * 4096f) * 83492791L);
				depth[k] = h;
				return k;
			}
			void Edge(long a, long b)
			{
				var e = a < b ? (a, b) : (b, a);
				uses[e] = uses.TryGetValue(e, out int n) ? n + 1 : 1;
			}
			foreach (RockFilletMath.Triangle t in tris)
			{
				long a = Key(t.A, t.HA), b = Key(t.B, t.HB), c = Key(t.C, t.HC);
				Edge(a, b);
				Edge(b, c);
				Edge(c, a);
			}
			int open = 0;
			float shallowest = float.MinValue;
			foreach (var pair in uses)
			{
				if (pair.Value != 1)
				{
					continue;
				}
				open++;
				shallowest = MathF.Max(shallowest, MathF.Max(depth[pair.Key.Item1], depth[pair.Key.Item2]));
			}
			Console.WriteLine($"{what}: {open} open edges, the shallowest {shallowest:F3} m (tuck {Tuck})");
			LogAssert.IsTrue(open > 0, $"{what}: the fillet is a strip with an edge, not the whole surface");
			LogAssert.IsTrue(shallowest <= -Tuck + 1e-4f, $"{what}: every open edge is under the rocks by the tuck ({shallowest})");

			// Front faces outward: the winding agrees with the blended field's gradient.
			Func<Vector3, RockFilletMath.Sample> field = FieldOf(rocks, Width);
			int agree = 0;
			foreach (RockFilletMath.Triangle t in tris)
			{
				Vector3 c = (t.A + t.B + t.C) / 3f;
				const float e = 0.01f;
				var g = new Vector3(
					field(c + new Vector3(e, 0f, 0f)).F - field(c - new Vector3(e, 0f, 0f)).F,
					field(c + new Vector3(0f, e, 0f)).F - field(c - new Vector3(0f, e, 0f)).F,
					field(c + new Vector3(0f, 0f, e)).F - field(c - new Vector3(0f, 0f, e)).F);
				agree += Vector3.Dot(Vector3.Cross(t.B - t.A, t.C - t.A), g) > 0f ? 1 : 0;
			}
			LogAssert.IsTrue(agree >= tris.Count * 0.99f, $"{what}: triangles face out of the rocks ({agree}/{tris.Count})");
		}

		[Test]
		public void Surface_HasNoCracks_AcrossCubesAndCells()
		{
			// Everything kept (no tuck), the blended surface of two overlapping boulders inside the region is closed:
			// every edge is shared by exactly two triangles.
			var rocks = new Func<Vector3, float>[]
			{
				p => Sphere(p, new Vector3(3.7f, 3.9f, 4.1f), 1.7f),
				p => Sphere(p, new Vector3(6.1f, 4.2f, 3.9f), 1.5f),
			};
			List<RockFilletMath.Triangle> tris = MeshRegion(rocks, Vector3.Zero, new Vector3(10f, 8f, 8f), 1e3f, out _, out _, out _, useCellTest: false);
			var uses = new Dictionary<(long, long), int>();
			long Key(Vector3 p) => ((long)MathF.Round(p.X * 4096f) * 73856093L) ^ ((long)MathF.Round(p.Y * 4096f) * 19349663L) ^ ((long)MathF.Round(p.Z * 4096f) * 83492791L);
			foreach (RockFilletMath.Triangle t in tris)
			{
				long a = Key(t.A), b = Key(t.B), c = Key(t.C);
				foreach (var e in new[] { (a, b), (b, c), (c, a) })
				{
					var k = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
					if (k.Item1 == k.Item2)
					{
						continue;   // a crossing on a lattice corner: a degenerate sliver
					}
					uses[k] = uses.TryGetValue(k, out int n) ? n + 1 : 1;
				}
			}
			int bad = 0;
			foreach (int n in uses.Values)
			{
				bad += n != 2 ? 1 : 0;
			}
			LogAssert.IsTrue(tris.Count > 1000, $"a surface was made ({tris.Count})");
			LogAssert.AreEqual(0, bad, "every edge is shared by two triangles: no cracks between tetrahedra, cubes or cells");
		}

		[Test]
		public void ComputeAndShader_MirrorTheMath()
		{
			// Source scan: the compute's constants and tables are RockFilletMath's and RockFillets', and every pass and
			// kernel compiles with DXC for Vulkan.
			string compute = System.IO.File.ReadAllText(RockFillets.ComputeAssetPath);
			string shader = System.IO.File.ReadAllText(RockFillets.ShaderAssetPath);
			void Define(string name, string value) =>
				LogAssert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(compute, $@"#define {name} {System.Text.RegularExpressions.Regex.Escape(value)}\b"), $"{name} is {value}");
			Define("FISH_FILLET_SUB", RockFilletMath.Sub.ToString());
			Define("FISH_FILLET_SAMPLES", RockFilletMath.Samples.ToString());
			Define("FISH_FILLET_SAMPLE_COUNT", (RockFilletMath.Samples * RockFilletMath.Samples * RockFilletMath.Samples).ToString());
			Define("FISH_FILLET_ROCKS", RockFilletMath.MaxCellRocks.ToString());
			Define("FISH_FILLET_LOOKS", RockFillets.MaxLooks.ToString());
			Define("FISH_FILLET_MAX_WIDTH", RockFilletMath.MaxWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
			var tets = new System.Text.StringBuilder();
			for (int i = 0; i < RockFilletMath.Tets.Length; i++)
			{
				tets.Append(RockFilletMath.Tets[i]).Append('u');
			}
			string table = compute.Substring(compute.IndexOf("kFilletTets[24]", StringComparison.Ordinal));
			table = table.Substring(0, table.IndexOf("};", StringComparison.Ordinal));
			LogAssert.AreEqual(tets.ToString(), System.Text.RegularExpressions.Regex.Replace(table.Substring(table.IndexOf('{')), @"[^0-9u]", ""), "the tetrahedra are RockFilletMath.Tets");
			foreach (string kernel in new[] { "FishFilletClear", "FishFilletSnapshot", "FishFilletClassify", "FishFilletMesh", "FishFilletFinalize", "FishFilletScatter" })
			{
				LogAssert.IsTrue(compute.Contains($"#pragma kernel {kernel}"), $"kernel {kernel} declared");
			}
			LogAssert.IsTrue(compute.Contains("#pragma use_dxc vulkan"), "the compute compiles with DXC");
			LogAssert.AreEqual(System.Text.RegularExpressions.Regex.Matches(shader, "HLSLPROGRAM").Count,
				System.Text.RegularExpressions.Regex.Matches(shader, @"HLSLPROGRAM\s*\n\s*#pragma use_dxc vulkan").Count, "every pass compiles with DXC");
		}

		[Test]
		public void Tetrahedra_TileTheCube()
		{
			// Six tetrahedra about the main diagonal, each 1/6 of the cube, all with corners 0 and 7.
			float total = 0f;
			for (int t = 0; t < 6; t++)
			{
				var v = new Vector3[4];
				for (int c = 0; c < 4; c++)
				{
					RockFilletMath.Corner(RockFilletMath.Tets[t * 4 + c], out int x, out int y, out int z);
					v[c] = new Vector3(x, y, z);
				}
				LogAssert.AreEqual(0, RockFilletMath.Tets[t * 4], "every tetrahedron starts at corner 0");
				LogAssert.AreEqual(7, RockFilletMath.Tets[t * 4 + 3], "and ends at corner 7");
				float volume = MathF.Abs(Vector3.Dot(v[1] - v[0], Vector3.Cross(v[2] - v[0], v[3] - v[0]))) / 6f;
				LogAssert.IsTrue(MathF.Abs(volume - 1f / 6f) < 1e-6f, $"tetrahedron {t} is a sixth of the cube ({volume})");
				total += volume;
			}
			LogAssert.IsTrue(MathF.Abs(total - 1f) < 1e-5f, "together they fill it");
		}
	}
}
