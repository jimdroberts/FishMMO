using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A cave piece's collider mesh (2026-10-20, <see cref="CaveMesher.ColliderOf"/>): the piece's own triangles on welded
	/// positions only, so the surface a player walks on, bumps into and is stopped by is exactly the drawn rock, closed where
	/// it was closed, at a fraction of the render layout's memory.
	/// </summary>
	[TestFixture]
	public class CaveColliderMeshTests
	{
		private const float HoleStep = 1200f / 512f, GridX = -2400f, GridZ = -2400f, CaveLift = 60f;

		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		private static Func<float, float, float> SeaCliff()
			=> (x, z) =>
			{
				float t = Mathf.Clamp01(z / 25f);
				return -1.5f + 2.5f * Mathf.Clamp01((z + 12f) / 10f) + 39f * (t * t * (3f - 2f * t)) + 0.8f * Mathf.Sin(x * 0.07f) * Mathf.Clamp01(z / 10f);
			};

		/// <summary>A cave planned on the cave tests' escarpment (or sea cliff), its mouth's terrain holes cut.</summary>
		private static CaveSystem Plan(CaveForm form, int size, int seed, out Func<float, float, float> g)
		{
			float tilt = seed * 13f % 40f - 20f;
			bool sea = form == CaveForm.SeaCave || form == CaveForm.Grotto;
			Func<float, float, float> face = Escarpment(tilt, seed);
			g = sea ? SeaCliff() : (px, pz) => face(px, pz) + CaveLift;
			Func<float, float, float> ground = g;
			CaveSystem system = CaveSystem.Plan(form, size, new Vector3(0f, 0f, sea ? -4f : -1f), sea ? 180f : Mathf.Repeat(180f - tilt, 360f), seed * 7919, seed,
				CaveGround.Sample(ground, -560f, -560f, 560f, 560f, 2f), 0f, out string problem, form == CaveForm.Grotto ? 0f : float.NaN);
			Assert.That(system, Is.Not.Null, $"{form} size {size} seed {seed} was refused: {problem}");
			system.HoleCells(0, g, HoleStep, GridX, GridZ);
			return system;
		}

		private static List<CaveMesher.Piece> Mesh(CaveSystem system, Func<float, float, float> g, bool keepHidden = false)
		{
			Bounds e = system.Extent;
			CaveGround ground = CaveGround.Sample(g, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f,
				system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f, HoleStep, GridX, GridZ);
			return CaveMesher.Build(system, ground, out _, keepHidden);
		}

		private static readonly (CaveForm Form, int Size, int Seed)[] Caves =
		{
			(CaveForm.Cave, 0, 1), (CaveForm.Cave, 1, 2), (CaveForm.Grotto, 1, 1), (CaveForm.Mine, 0, 1), (CaveForm.LavaTube, 0, 2),
		};

		[Test]
		public void AColliderIsItsPiecesOwnTrianglesOnWeldedPositions()
		{
			foreach ((CaveForm form, int size, int seed) in Caves)
			{
				CaveSystem system = Plan(form, size, seed, out Func<float, float, float> g);
				long renderBytes = 0, colliderBytes = 0;
				var welded = new List<Vector3>();
				var tris = new List<int>();
				foreach (CaveMesher.Piece piece in Mesh(system, g))
				{
					List<Vector3> p = piece.Mesh.Positions;
					List<int> t = piece.Mesh.Submeshes[0];
					CaveMesher.ColliderOf(p, t, welded, tris);
					string what = $"{form} {size} piece {piece.Key}";
					Assert.That(new HashSet<Vector3>(welded).Count, Is.EqualTo(welded.Count), $"{what}: every position once");
					Assert.That(welded.Count, Is.LessThanOrEqualTo(p.Count), $"{what}: no more vertices than the piece");
					// The same triangles, in order and wound as they were, bar those with no area.
					int k = 0;
					for (int i = 0; i < t.Count; i += 3)
					{
						Vector3 a = p[t[i]], b = p[t[i + 1]], c = p[t[i + 2]];
						if (a == b || b == c || c == a)
						{
							continue;
						}
						Assert.That(k + 2, Is.LessThan(tris.Count), $"{what}: a triangle went missing");
						Assert.That(welded[tris[k]] == a && welded[tris[k + 1]] == b && welded[tris[k + 2]] == c, Is.True, $"{what}: triangle {i / 3} is not the piece's own");
						k += 3;
					}
					Assert.That(k, Is.EqualTo(tris.Count), $"{what}: no triangle added");
					// As the cut writes them: 68 bytes a render vertex (CaveShaper.WriteMeshes), 12 a collider's; 16-bit indices both.
					renderBytes += 68L * p.Count + 2L * t.Count;
					colliderBytes += 12L * welded.Count + 2L * tris.Count;
				}
				Assert.That(colliderBytes, Is.LessThan(0.35 * renderBytes), $"{form} {size}: collider {colliderBytes / 1024} KB against render {renderBytes / 1024} KB");
			}
		}

		[Test]
		public void TheCollidersAreAsClosedAsTheRock()
		{
			foreach ((CaveForm form, int size, int seed) in new[] { (CaveForm.Cave, 0, 1), (CaveForm.Grotto, 1, 1), (CaveForm.Mine, 0, 1) })
			{
				CaveSystem system = Plan(form, size, seed, out Func<float, float, float> g);
				List<CaveMesher.Piece> pieces = Mesh(system, g, keepHidden: true);
				var render = new List<(Vector3, Vector3, Vector3)>();
				var solid = new List<(Vector3, Vector3, Vector3)>();
				var welded = new List<Vector3>();
				var tris = new List<int>();
				foreach (CaveMesher.Piece piece in pieces)
				{
					List<Vector3> p = piece.Mesh.Positions;
					List<int> t = piece.Mesh.Submeshes[0];
					for (int i = 0; i < t.Count; i += 3)
					{
						render.Add((p[t[i]], p[t[i + 1]], p[t[i + 2]]));
					}
					CaveMesher.ColliderOf(p, t, welded, tris);
					for (int i = 0; i < tris.Count; i += 3)
					{
						solid.Add((welded[tris[i]], welded[tris[i + 1]], welded[tris[i + 2]]));
					}
				}
				Assert.That(OddEdges(solid), Is.Zero, $"{form} {size}: the whole collider is closed (no edge on an odd number of faces)");
				Assert.That(OddEdges(solid), Is.EqualTo(OddEdges(render)), $"{form} {size}: as closed as the drawn rock");
			}
		}

		/// <summary>Edges on an odd number of faces, vertices welded by position across all the triangles.</summary>
		private static int OddEdges(List<(Vector3 A, Vector3 B, Vector3 C)> triangles)
		{
			var id = new Dictionary<Vector3, int>();
			int Id(Vector3 v)
			{
				if (!id.TryGetValue(v, out int i))
				{
					id[v] = i = id.Count;
				}
				return i;
			}
			var use = new Dictionary<(int, int), int>();
			void Edge(int x, int y)
			{
				var key = x < y ? (x, y) : (y, x);
				use[key] = use.TryGetValue(key, out int n) ? n + 1 : 1;
			}
			foreach (var t in triangles)
			{
				int a = Id(t.A), b = Id(t.B), c = Id(t.C);
				if (a == b || b == c || c == a)
				{
					continue;
				}
				Edge(a, b);
				Edge(b, c);
				Edge(c, a);
			}
			int odd = 0;
			foreach (int n in use.Values)
			{
				if ((n & 1) == 1)
				{
					odd++;
				}
			}
			return odd;
		}

		[Test]
		public void EveryPassageWalksOnTheColliderAsOnTheDrawnRock()
		{
			foreach ((CaveForm form, int size, int seed) in Caves)
			{
				CaveSystem system = Plan(form, size, seed, out Func<float, float, float> g);
				var render = new TriangleGrid();
				var solid = new TriangleGrid();
				var welded = new List<Vector3>();
				var tris = new List<int>();
				foreach (CaveMesher.Piece piece in Mesh(system, g))
				{
					List<Vector3> p = piece.Mesh.Positions;
					List<int> t = piece.Mesh.Submeshes[0];
					for (int i = 0; i < t.Count; i += 3)
					{
						render.Add(p[t[i]], p[t[i + 1]], p[t[i + 2]]);
					}
					CaveMesher.ColliderOf(p, t, welded, tris);
					for (int i = 0; i < tris.Count; i += 3)
					{
						solid.Add(welded[tris[i]], welded[tris[i + 1]], welded[tris[i + 2]]);
					}
				}
				int samples = 0, floored = 0;
				foreach (CavePassage passage in system.Passages)
				{
					for (int n = 1; n < passage.Nodes.Count; n++)
					{
						CaveNode a = passage.Nodes[n - 1], b = passage.Nodes[n];
						int steps = Mathf.CeilToInt(Vector3.Distance(a.Centre, b.Centre) / 0.5f);
						for (int s = 0; s < steps; s++)
						{
							float f = s / (float)steps;
							Vector3 at = Vector3.Lerp(a.Centre, b.Centre, f);
							float floor = Mathf.Lerp(a.Floor, b.Floor, f);
							var from = new Vector3(at.x, floor + 1.2f, at.z);
							string what = $"{form} {size} {passage.Kind} at {from}";
							samples++;
							// Down onto the floor and up to the roof: the same hits, to the float, on both.
							bool down = render.Cast(from, -1f, 4f, out float renderFloor);
							Assert.That(solid.Cast(from, -1f, 4f, out float solidFloor), Is.EqualTo(down), $"{what}: the collider has a floor where the rock is drawn with one");
							if (down)
							{
								floored++;
								Assert.That(solidFloor, Is.EqualTo(renderFloor).Within(1e-4f), $"{what}: the collider's floor is the drawn floor");
							}
							bool up = render.Cast(from, 1f, 6f, out float renderRoof);
							Assert.That(solid.Cast(from, 1f, 6f, out float solidRoof), Is.EqualTo(up), $"{what}: the collider has a roof where the rock is drawn with one");
							if (up)
							{
								Assert.That(solidRoof, Is.EqualTo(renderRoof).Within(1e-4f), $"{what}: the collider's roof is the drawn roof");
							}
						}
					}
				}
				Assert.That(floored, Is.GreaterThan(samples * 9 / 10), $"{form} {size}: the drawn rock has a floor under nine in ten of the way's samples");
			}
		}

		/// <summary>Triangles bucketed by the 1 m columns their boxes cover, cast against straight up or down.</summary>
		private sealed class TriangleGrid
		{
			private readonly Dictionary<Vector2Int, List<(Vector3 A, Vector3 B, Vector3 C)>> cells = new Dictionary<Vector2Int, List<(Vector3, Vector3, Vector3)>>();

			public void Add(Vector3 a, Vector3 b, Vector3 c)
			{
				int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
				int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z))), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
				for (int x = x0; x <= x1; x++)
				{
					for (int z = z0; z <= z1; z++)
					{
						var key = new Vector2Int(x, z);
						if (!cells.TryGetValue(key, out var list))
						{
							cells[key] = list = new List<(Vector3, Vector3, Vector3)>();
						}
						list.Add((a, b, c));
					}
				}
			}

			/// <summary>The nearest height a vertical ray from <paramref name="from"/> (up when <paramref name="sign"/> is 1) meets within <paramref name="reach"/>.</summary>
			public bool Cast(Vector3 from, float sign, float reach, out float height)
			{
				height = 0f;
				if (!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(from.x), Mathf.FloorToInt(from.z)), out var list))
				{
					return false;
				}
				float best = float.MaxValue;
				foreach (var t in list)
				{
					// The point in the triangle's xz projection, by barycentric weights; its height there.
					float d = (t.B.z - t.C.z) * (t.A.x - t.C.x) + (t.C.x - t.B.x) * (t.A.z - t.C.z);
					if (Mathf.Abs(d) < 1e-9f)
					{
						continue;
					}
					float u = ((t.B.z - t.C.z) * (from.x - t.C.x) + (t.C.x - t.B.x) * (from.z - t.C.z)) / d;
					float v = ((t.C.z - t.A.z) * (from.x - t.C.x) + (t.A.x - t.C.x) * (from.z - t.C.z)) / d;
					float w = 1f - u - v;
					if (u < 0f || v < 0f || w < 0f)
					{
						continue;
					}
					float y = u * t.A.y + v * t.B.y + w * t.C.y;
					float along = (y - from.y) * sign;
					if (along >= 0f && along <= reach && along < best)
					{
						best = along;
						height = y;
					}
				}
				return best < float.MaxValue;
			}
		}
	}
}
