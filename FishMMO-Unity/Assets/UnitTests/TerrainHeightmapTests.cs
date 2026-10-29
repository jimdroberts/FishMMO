using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client's own terrain heightmap draw (TerrainHeightmapSystem, 2026-10-28): the quadtree's layout, the patch mesh, and
	/// — over a whole synthetic scene of four tiles with a cliff, rolling hills and flat ground, built as the GPU builds it
	/// (roughness, dilated level field, node bounds) — that the selection covers every point exactly once, that the geomorph
	/// leaves no crack between any two drawn patches (same level or not, across tile seams too) at any camera, that no patch
	/// folds over itself, and that every drawn vertex is at a level its field allows. Then the sources: the shader is the
	/// tiles' own passes with FISH_TERRAIN_GPU, compiled with DXC, and the switch, the profile and the restore are in place.
	/// </summary>
	[TestFixture]
	public class TerrainHeightmapTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Heightmap = "Assets/Scripts/Client/World/Terrain/Heightmap/";

		// ── Layout ──────────────────────────────────────────────────────────

		[Test]
		public void TheQuadtreeLayout_RoundTrips()
		{
			Assert.That(TerrainHeightmapMath.LevelCount(512), Is.EqualTo(6), "Moron 2's 513 heightmaps: 32 patches a side, six levels");
			Assert.That(TerrainHeightmapMath.LevelCount(1024), Is.EqualTo(7));
			Assert.That(TerrainHeightmapMath.LevelCount(16), Is.EqualTo(1));
			Assert.That(TerrainHeightmapMath.LevelCount(500), Is.EqualTo(0), "not 16·2^k: refused (Unity draws it)");
			Assert.That(TerrainHeightmapMath.LevelCount(48), Is.EqualTo(0));
			Assert.That(TerrainHeightmapMath.NodesPerTile(512), Is.EqualTo(1365), "the engine's own patch count (m_PrecomputedError)");
			foreach (int quads in new[] { 16, 128, 512 })
			{
				int levels = TerrainHeightmapMath.LevelCount(quads);
				int local = 0;
				for (int level = 0; level < levels; level++)
				{
					int across = TerrainHeightmapMath.NodesAcross(quads, level);
					for (int z = 0; z < across; z++)
					{
						for (int x = 0; x < across; x++)
						{
							Assert.That(TerrainHeightmapMath.NodeIndex(0, quads, level, x, z), Is.EqualTo(local));
							TerrainHeightmapMath.DecodeNode(local, quads, out int l, out int dx, out int dz);
							Assert.That((l, dx, dz), Is.EqualTo((level, x, z)), $"quads {quads} node {local}");
							local++;
						}
					}
				}
				Assert.That(local, Is.EqualTo(TerrainHeightmapMath.NodesPerTile(quads)));
				Assert.That(TerrainHeightmapMath.NodesAcross(quads, levels - 1), Is.EqualTo(1), "one root a tile");
			}
		}

		[Test]
		public void ThePatch_FacesUp_AndFullySlidIsTheCoarseGrid()
		{
			int[] indices = TerrainHeightmapMath.PatchIndices();
			int n = TerrainHeightmapMath.PatchVertices;
			Assert.That(indices.Length, Is.EqualTo(16 * 16 * 6));
			Vector3 V(int i) => new Vector3(i % n, 0f, i / n);
			for (int t = 0; t < indices.Length; t += 3)
			{
				Vector3 normal = Vector3.Cross(V(indices[t + 1]) - V(indices[t]), V(indices[t + 2]) - V(indices[t]));
				Assert.That(normal.y, Is.GreaterThan(0f), "clockwise from above: Unity's front face points up");
			}
			// Every vertex slid all the way onto level 1: each triangle is degenerate or a triangle of the coarse grid with the
			// same diagonal, and the coarse grid's triangles are all there once.
			var coarse = new HashSet<string>();
			for (int t = 0; t < indices.Length; t += 3)
			{
				var tri = new Vector2[3];
				for (int k = 0; k < 3; k++)
				{
					Vector3 v = V(indices[t + k]);
					tri[k] = TerrainHeightmapMath.Morph(new Vector2(v.x, v.z), 0, 2, TerrainHeightmapMath.MorphStart, (p, l) => 1.5f);
				}
				float area = (tri[1].x - tri[0].x) * (tri[2].y - tri[0].y) - (tri[2].x - tri[0].x) * (tri[1].y - tri[0].y);
				if (Mathf.Abs(area) < 1e-6f)
				{
					continue;
				}
				Assert.That(Mathf.Abs(area), Is.EqualTo(4f).Within(1e-4f), "a coarse half-quad");
				Assert.That(coarse.Add($"{tri[0]}{tri[1]}{tri[2]}"), Is.True, "no coarse triangle twice");
			}
			Assert.That(coarse.Count, Is.EqualTo(8 * 8 * 2));
		}

		[Test]
		public void TheLodBias_IsThePixelErrorInLevels()
		{
			// 1440 px, 60°: K = 720 / tan 30° = 1247 px a metre at a metre; at 4 px: log2(311.8).
			float c = TerrainHeightmapMath.LodBias(4f, 1440f, 60f, false, 0f);
			Assert.That(c, Is.EqualTo(Mathf.Log(1247.08f / 4f, 2f)).Within(1e-3f));
			Assert.That(TerrainHeightmapMath.LodBias(2f, 1440f, 60f, false, 0f), Is.EqualTo(c + 1f).Within(1e-4f), "half the error: a level finer");
			Assert.That(TerrainHeightmapMath.LodBias(4f, 720f, 60f, false, 0f), Is.EqualTo(c - 1f).Within(1e-4f), "half the pixels: a level coarser");
			// f_l >= l + 1 exactly where level l+1's error is within the pixel error: 1 m of error at 311.8 m, 4 px.
			float g0 = TerrainHeightmapMath.FieldValue(1f, 0);
			Assert.That(TerrainHeightmapMath.Lod(311.8f, g0, c, false), Is.EqualTo(1f).Within(1e-3f));
			Assert.That(TerrainHeightmapMath.Lod(311.8f * 2f, g0, c, false), Is.EqualTo(2f).Within(1e-3f));
			// An orthographic camera measures no distance: the field is the roughness against its pixels a metre alone.
			Assert.That(TerrainHeightmapMath.Lod(1000f, 0f, 2f, true), Is.EqualTo(-2f));
		}

		// ── A whole scene ───────────────────────────────────────────────────

		/// <summary>Four tiles of 128 quads (2 m texels), built as FishTerrainHeightmap.compute builds them.</summary>
		private sealed class Scene
		{
			public const int Quads = 128;
			public const float Texel = 2f;
			public const int TilesAcross = 2;
			public readonly int Levels = TerrainHeightmapMath.LevelCount(Quads);
			public readonly int NodesPerTile = TerrainHeightmapMath.NodesPerTile(Quads);
			public readonly float[][] Heights = new float[TilesAcross * TilesAcross][];
			public readonly Vector3[] Origins = new Vector3[TilesAcross * TilesAcross];
			public readonly int CellsX = TilesAcross * Quads / TerrainHeightmapMath.CellQuads;
			public readonly int CellsZ = TilesAcross * Quads / TerrainHeightmapMath.CellQuads;
			public readonly float CellMetres = Texel * TerrainHeightmapMath.CellQuads;
			public readonly Vector2 GridOrigin = new Vector2(-100f, 40f);
			public float[][] Fields;
			public Vector4[] Nodes;

			public static float World(float x, float z)
			{
				// Rolling hills, a cliff running across the scene, flat ground in one corner, fine noise on the hills.
				float hills = 30f * Mathf.Sin(x * 0.011f) * Mathf.Cos(z * 0.017f) + 12f * Mathf.Sin(x * 0.043f + z * 0.031f);
				float cliff = 45f / (1f + Mathf.Exp(-(x - 0.6f * z - 120f) * 0.35f));
				float noise = 1.5f * Mathf.Sin(x * 0.9f + 3f * Mathf.Sin(z * 0.37f)) * Mathf.Cos(z * 0.77f);
				float flat = Mathf.Clamp01((x - GridX0 - 60f) / 80f) * Mathf.Clamp01((z - GridZ0 - 60f) / 80f);
				return 50f + (hills + cliff + noise) * flat;
			}

			private const float GridX0 = -100f, GridZ0 = 40f;

			public Scene()
			{
				int res = Quads + 1;
				for (int tz = 0; tz < TilesAcross; tz++)
				{
					for (int tx = 0; tx < TilesAcross; tx++)
					{
						int t = tz * TilesAcross + tx;
						Origins[t] = new Vector3(GridX0 + tx * Quads * Texel, 0f, GridZ0 + tz * Quads * Texel);
						var h = new float[res * res];
						for (int z = 0; z < res; z++)
						{
							for (int x = 0; x < res; x++)
							{
								h[z * res + x] = World(Origins[t].x + x * Texel, Origins[t].z + z * Texel);
							}
						}
						Heights[t] = h;
					}
				}
				BuildField();
				BuildNodes();
			}

			private void BuildField()
			{
				int res = Quads + 1;
				Fields = new float[Levels - 1][];
				for (int field = 0; field < Levels - 1; field++)
				{
					var cells = new float[CellsX * CellsZ];
					for (int t = 0; t < Heights.Length; t++)
					{
						for (int z = 0; z < res; z++)
						{
							for (int x = 0; x < res; x++)
							{
								float deviation = TerrainHeightmapMath.TexelDeviation(Heights[t], Quads, x, z, field + 1);
								int cx = Mathf.Clamp(Mathf.FloorToInt((Origins[t].x + x * Texel - GridOrigin.x) / CellMetres), 0, CellsX - 1);
								int cz = Mathf.Clamp(Mathf.FloorToInt((Origins[t].z + z * Texel - GridOrigin.y) / CellMetres), 0, CellsZ - 1);
								cells[cz * CellsX + cx] = Mathf.Max(cells[cz * CellsX + cx], deviation);
							}
						}
					}
					for (int i = 0; i < cells.Length; i++)
					{
						cells[i] = TerrainHeightmapMath.FieldValue(cells[i], field);
					}
					float[] x1 = TerrainHeightmapMath.Dilate(cells, CellsX, CellsZ, true, TerrainHeightmapMath.FieldSlope, TerrainHeightmapMath.DilationReach);
					Fields[field] = TerrainHeightmapMath.Dilate(x1, CellsX, CellsZ, false, TerrainHeightmapMath.FieldSlope, TerrainHeightmapMath.DilationReach);
				}
				TerrainHeightmapMath.Raise(Fields);
			}

			private void BuildNodes()
			{
				Nodes = new Vector4[Heights.Length * NodesPerTile];
				int res = Quads + 1;
				for (int t = 0; t < Heights.Length; t++)
				{
					int across = TerrainHeightmapMath.NodesAcross(Quads, 0);
					for (int nz = 0; nz < across; nz++)
					{
						for (int nx = 0; nx < across; nx++)
						{
							float lo = float.MaxValue, hi = float.MinValue;
							for (int z = 0; z <= 16; z++)
							{
								for (int x = 0; x <= 16; x++)
								{
									float h = Heights[t][(nz * 16 + z) * res + nx * 16 + x];
									lo = Mathf.Min(lo, h);
									hi = Mathf.Max(hi, h);
								}
							}
							Nodes[TerrainHeightmapMath.NodeIndex(t, Quads, 0, nx, nz)] = new Vector4(Origins[t].y + lo, Origins[t].y + hi, 0f, 0f);
						}
					}
					for (int level = 1; level < Levels; level++)
					{
						int a = TerrainHeightmapMath.NodesAcross(Quads, level);
						for (int nz = 0; nz < a; nz++)
						{
							for (int nx = 0; nx < a; nx++)
							{
								Vector4 r = new Vector4(float.MaxValue, float.MinValue, float.MinValue, 0f);
								for (int k = 0; k < 4; k++)
								{
									Vector4 c = Nodes[TerrainHeightmapMath.NodeIndex(t, Quads, level - 1, nx * 2 + (k & 1), nz * 2 + (k >> 1))];
									r = new Vector4(Mathf.Min(r.x, c.x), Mathf.Max(r.y, c.y), 0f, 0f);
								}
								// FishTerrainNodeParent: the largest g_(level-1) over the cells under the node and one round it.
								int span = 1 << level;
								Vector2 corner = new Vector2(Origins[t].x + nx * 16 * span * Texel, Origins[t].z + nz * 16 * span * Texel);
								int cx = Mathf.FloorToInt((corner.x - GridOrigin.x) / CellMetres + 0.5f);
								int cz = Mathf.FloorToInt((corner.y - GridOrigin.y) / CellMetres + 0.5f);
								float[] field = Fields[level - 1];
								for (int dz = -1; dz <= span; dz++)
								{
									for (int dx = -1; dx <= span; dx++)
									{
										int x = Mathf.Clamp(cx + dx, 0, CellsX - 1), z = Mathf.Clamp(cz + dz, 0, CellsZ - 1);
										r.z = Mathf.Max(r.z, field[z * CellsX + x]);
									}
								}
								Nodes[TerrainHeightmapMath.NodeIndex(t, Quads, level, nx, nz)] = r;
							}
						}
					}
				}
			}

			/// <summary>FishTerrainHeightAt: bilinear between texels, exact at one.</summary>
			public float HeightAt(int t, Vector2 g)
			{
				int res = Quads + 1;
				float bx = Mathf.Floor(g.x), bz = Mathf.Floor(g.y);
				float fx = g.x - bx, fz = g.y - bz;
				float H(int x, int z) => Heights[t][Mathf.Clamp(z, 0, Quads) * res + Mathf.Clamp(x, 0, Quads)];
				int ix = (int)bx, iz = (int)bz;
				if (fx == 0f && fz == 0f)
				{
					return H(ix, iz);
				}
				return Mathf.Lerp(Mathf.Lerp(H(ix, iz), H(ix + 1, iz), fx), Mathf.Lerp(H(ix, iz + 1), H(ix + 1, iz + 1), fx), fz);
			}

			public Vector3 WorldAt(int t, Vector2 p) => new Vector3(Origins[t].x + p.x * Texel, Origins[t].y + HeightAt(t, p), Origins[t].z + p.y * Texel);

			public float LodAt(Vector3 world, Vector3 eye, float bias, int field)
			{
				float logRough = TerrainHeightmapMath.FieldAt(Fields[field], CellsX, CellsZ, (world.x - GridOrigin.x) / CellMetres, (world.z - GridOrigin.y) / CellMetres);
				return TerrainHeightmapMath.Lod(Vector3.Distance(world, eye), logRough, bias, false);
			}

			public void Box(int t, int level, int nx, int nz, out Vector3 lo, out Vector3 hi, out float logRough)
			{
				Vector4 b = Nodes[TerrainHeightmapMath.NodeIndex(t, Quads, level, nx, nz)];
				float side = 16 * (1 << level) * Texel;
				lo = new Vector3(Origins[t].x + nx * side, b.x, Origins[t].z + nz * side);
				hi = new Vector3(lo.x + side, b.y + TerrainHeightmapMath.LiftPadMetres, lo.z + side);
				logRough = b.z;
			}

			/// <summary>FishTerrainSelect for one node.</summary>
			public bool Drawn(int t, int level, int nx, int nz, Vector3 eye, float bias)
			{
				Box(t, level, nx, nz, out Vector3 lo, out Vector3 hi, out float rough);
				float self = TerrainHeightmapMath.MinLod(eye, lo, hi, rough, bias, false);
				float parent = 0f;
				if (level + 1 < Levels)
				{
					Box(t, level + 1, nx >> 1, nz >> 1, out Vector3 plo, out Vector3 phi, out float prough);
					parent = TerrainHeightmapMath.MinLod(eye, plo, phi, prough, bias, false);
				}
				return TerrainHeightmapMath.Drawn(level, Levels, self, parent);
			}

			/// <summary>A drawn vertex (FishTerrainGpuInstancing): its texel place slid by the field, and where that is.</summary>
			public Vector3 Vertex(int t, int level, int nx, int nz, int i, int j, Vector3 eye, float bias, out Vector2 g, out Vector2 p)
			{
				int stride = 1 << level;
				g = new Vector2((nx * 16 + i) * stride, (nz * 16 + j) * stride);
				p = TerrainHeightmapMath.Morph(g, level, Levels, TerrainHeightmapMath.MorphStart, (q, l) => LodAt(WorldAt(t, q), eye, bias, l));
				return WorldAt(t, p);
			}
		}

		private struct DrawnNode
		{
			public int Tile, Level, X, Z;
			public Vector3[] Vertices;  // 17 x 17, row by row
			public Vector2 Min, Max;    // world x/z
		}

		private static readonly Vector3[] Eyes =
		{
			new Vector3(60f, 0f, 220f), new Vector3(150f, 0f, 300f), new Vector3(-90f, 0f, 60f), new Vector3(300f, 0f, 500f),
			new Vector3(120f, 400f, 250f), new Vector3(-300f, 30f, -200f), new Vector3(10f, 0f, 400f), new Vector3(250f, 0f, 120f),
			new Vector3(2500f, 600f, -1800f),
		};

		private static List<DrawnNode> Select(Scene scene, Vector3 eye, float bias)
		{
			var drawn = new List<DrawnNode>();
			for (int t = 0; t < scene.Heights.Length; t++)
			{
				for (int level = 0; level < scene.Levels; level++)
				{
					int across = TerrainHeightmapMath.NodesAcross(Scene.Quads, level);
					for (int nz = 0; nz < across; nz++)
					{
						for (int nx = 0; nx < across; nx++)
						{
							if (!scene.Drawn(t, level, nx, nz, eye, bias))
							{
								continue;
							}
							var node = new DrawnNode { Tile = t, Level = level, X = nx, Z = nz, Vertices = new Vector3[17 * 17] };
							for (int j = 0; j <= 16; j++)
							{
								for (int i = 0; i <= 16; i++)
								{
									node.Vertices[j * 17 + i] = scene.Vertex(t, level, nx, nz, i, j, eye, bias, out _, out _);
								}
							}
							float side = 16 * (1 << level) * Scene.Texel;
							node.Min = new Vector2(scene.Origins[t].x + nx * side, scene.Origins[t].z + nz * side);
							node.Max = node.Min + new Vector2(side, side);
							drawn.Add(node);
						}
					}
				}
			}
			return drawn;
		}

		private static float Bias(float pixelError) => TerrainHeightmapMath.LodBias(pixelError, 1440f, 60f, false, 0f);

		/// <summary>Eyes 2 m over the ground where they stand on it (y 0 in the list), as given otherwise.</summary>
		private static Vector3 EyeOver(Vector3 e) => e.y == 0f ? new Vector3(e.x, Scene.World(e.x, e.z) + 2f, e.z) : e;

		[Test]
		public void TheSelection_CoversEveryPointOnce_AndUsesSeveralLevels()
		{
			var scene = new Scene();
			var levelsSeen = new HashSet<int>();
			foreach (Vector3 e in Eyes)
			{
				Vector3 eye = EyeOver(e);
				foreach (float pixelError in new[] { 1f, 4f, 16f })
				{
					List<DrawnNode> drawn = Select(scene, eye, Bias(pixelError));
					var cover = new int[scene.Heights.Length, 8, 8];
					foreach (DrawnNode n in drawn)
					{
						levelsSeen.Add(n.Level);
						int span = 1 << n.Level;
						for (int z = 0; z < span; z++)
						{
							for (int x = 0; x < span; x++)
							{
								cover[n.Tile, n.X * span + x, n.Z * span + z]++;
							}
						}
					}
					foreach (int c in cover)
					{
						Assert.That(c, Is.EqualTo(1), $"eye {eye}, {pixelError} px: every level-0 cell drawn exactly once");
					}
				}
			}
			Assert.That(levelsSeen.Count, Is.EqualTo(scene.Levels), "the cameras between them draw every level");
		}

		[Test]
		public void EveryDrawnVertex_IsAtALevelItsFieldAllows_AndNoPatchFolds()
		{
			var scene = new Scene();
			int partial = 0;
			float worstFold = 0f;
			foreach (Vector3 e in Eyes)
			{
				Vector3 eye = EyeOver(e);
				float bias = Bias(4f);
				foreach (DrawnNode n in Select(scene, eye, bias))
				{
					for (int j = 0; j <= 16; j++)
					{
						for (int i = 0; i <= 16; i++)
						{
							scene.Vertex(n.Tile, n.Level, n.X, n.Z, i, j, eye, bias, out Vector2 g, out Vector2 p);
							if (n.Level > 0)
							{
								float allowed = scene.LodAt(scene.WorldAt(n.Tile, g), eye, bias, n.Level - 1);
								Assert.That(allowed, Is.GreaterThanOrEqualTo(n.Level - 1e-4f), "the node's bound is conservative: its level is allowed at every vertex");
							}
							if (n.Level + 1 >= scene.Levels)
							{
								continue;
							}
							float f = scene.LodAt(scene.WorldAt(n.Tile, g), eye, bias, n.Level);
							float t = TerrainHeightmapMath.MorphShare(f, n.Level, TerrainHeightmapMath.MorphStart);
							if (t > 0f && t < 1f && g != p)
							{
								partial++;
							}
						}
					}
					// Slid toward lower texels only, every row and column keeps its order: no triangle turns over (beyond a sliver: two
					// steps of the geomorph at once, where a field turns fast, can cross by a hair).
					for (int j = 0; j <= 16; j++)
					{
						for (int i = 1; i <= 16; i++)
						{
							worstFold = Mathf.Max(worstFold, n.Vertices[j * 17 + i - 1].x - n.Vertices[j * 17 + i].x);
							worstFold = Mathf.Max(worstFold, n.Vertices[(i - 1) * 17 + j].z - n.Vertices[i * 17 + j].z);
						}
					}
				}
			}
			Assert.That(partial, Is.GreaterThan(0), "some vertices are caught mid-morph (the test sees the geomorph at work)");
			Console.WriteLine($"worst fold {worstFold * 1000f:0.###} mm");
			Assert.That(worstFold, Is.LessThan(0.01f), "no row or column folds back by a centimetre");
		}

		[Test]
		public void NoCrack_BetweenAnyTwoDrawnPatches()
		{
			var scene = new Scene();
			int sharedEdges = 0, mixedEdges = 0;
			foreach (Vector3 e in Eyes)
			{
				Vector3 eye = EyeOver(e);
				foreach (float pixelError in new[] { 2f, 4f })
				{
					List<DrawnNode> drawn = Select(scene, eye, Bias(pixelError));
					for (int a = 0; a < drawn.Count; a++)
					{
						for (int b = 0; b < drawn.Count; b++)
						{
							if (a == b)
							{
								continue;
							}
							foreach ((Vector3[] Line, bool AlongX, float Fixed) edgeA in Edges(drawn[a]))
							{
								foreach ((Vector3[] Line, bool AlongX, float Fixed) edgeB in Edges(drawn[b]))
								{
									if (edgeA.AlongX != edgeB.AlongX || Mathf.Abs(edgeA.Fixed - edgeB.Fixed) > 1e-3f)
									{
										continue;
									}
									int checkedHere = CheckOnLine(edgeA.Line, edgeB.Line, edgeA.AlongX, $"eye {eye} {pixelError} px: tile {drawn[a].Tile} L{drawn[a].Level} ({drawn[a].X},{drawn[a].Z}) against tile {drawn[b].Tile} L{drawn[b].Level} ({drawn[b].X},{drawn[b].Z})");
									if (checkedHere > 0)
									{
										sharedEdges++;
										if (drawn[a].Level != drawn[b].Level || drawn[a].Tile != drawn[b].Tile)
										{
											mixedEdges++;
										}
									}
								}
							}
						}
					}
				}
			}
			Assert.That(sharedEdges, Is.GreaterThan(100));
			Assert.That(mixedEdges, Is.GreaterThan(10), "edges between levels and across tile seams were checked");
		}

		/// <summary>A drawn node's four edges as world polylines: along x (z fixed) or along z (x fixed).</summary>
		private static IEnumerable<(Vector3[] Line, bool AlongX, float Fixed)> Edges(DrawnNode n)
		{
			var bottom = new Vector3[17];
			var top = new Vector3[17];
			var left = new Vector3[17];
			var right = new Vector3[17];
			for (int k = 0; k <= 16; k++)
			{
				bottom[k] = n.Vertices[k];
				top[k] = n.Vertices[16 * 17 + k];
				left[k] = n.Vertices[k * 17];
				right[k] = n.Vertices[k * 17 + 16];
			}
			yield return (bottom, true, n.Min.y);
			yield return (top, true, n.Max.y);
			yield return (left, false, n.Min.x);
			yield return (right, false, n.Max.x);
		}

		/// <summary>Every vertex of <paramref name="line"/> within <paramref name="other"/>'s span lies on it. Returns how many were checked.</summary>
		private static int CheckOnLine(Vector3[] line, Vector3[] other, bool alongX, string what)
		{
			float S(Vector3 v) => alongX ? v.x : v.z;
			float lo = S(other[0]), hi = S(other[other.Length - 1]);
			int count = 0;
			foreach (Vector3 v in line)
			{
				float s = S(v);
				if (s < lo - 1e-4f || s > hi + 1e-4f)
				{
					continue;
				}
				Assert.That(alongX ? v.z : v.x, Is.EqualTo(alongX ? other[0].z : other[0].x).Within(1e-3f), what + ": the vertex stays on the edge's line");
				float height = float.NaN;
				for (int k = 0; k + 1 < other.Length; k++)
				{
					float s0 = S(other[k]), s1 = S(other[k + 1]);
					if (Mathf.Abs(s - s0) < 1e-4f)
					{
						height = other[k].y;
						break;
					}
					if (s > s0 && s < s1)
					{
						height = Mathf.Lerp(other[k].y, other[k + 1].y, (s - s0) / (s1 - s0));
						break;
					}
				}
				if (float.IsNaN(height) && Mathf.Abs(s - hi) < 1e-4f)
				{
					height = other[other.Length - 1].y;
				}
				Assert.That(float.IsNaN(height), Is.False, what + $": vertex at {s} found on the other edge");
				Assert.That(v.y, Is.EqualTo(height).Within(2e-3f), what + $": no crack at {s}");
				count++;
			}
			return count;
		}

		[Test]
		public void TheFields_AreWhatEachCoarserLevelMisses_AndRaisedLevelOnLevel()
		{
			// A plane is drawn exactly at every level; a single bump is missed by every level that skips it.
			int quads = 64, res = quads + 1;
			var plane = new float[res * res];
			var bump = new float[res * res];
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					plane[z * res + x] = 0.3f * x - 0.2f * z + 7f;
					bump[z * res + x] = x == 5 && z == 9 ? 8f : 0f;
				}
			}
			for (int level = 1; level < TerrainHeightmapMath.LevelCount(quads); level++)
			{
				Assert.That(TerrainHeightmapMath.TexelDeviation(plane, quads, 5, 9, level), Is.EqualTo(0f).Within(1e-4f));
				Assert.That(TerrainHeightmapMath.TexelDeviation(plane, quads, 64, 64, level), Is.EqualTo(0f).Within(1e-4f));
				Assert.That(TerrainHeightmapMath.TexelDeviation(bump, quads, 5, 9, level), Is.EqualTo(8f).Within(1e-4f), "every coarser level misses it all");
			}
			Assert.That(TerrainHeightmapMath.TexelDeviation(bump, quads, 4, 8, 2), Is.EqualTo(0f).Within(1e-4f), "a texel the level keeps");
			// g_l: log2 of the error less l + 1; the floor keeps flat ground finite.
			Assert.That(TerrainHeightmapMath.FieldValue(8f, 0), Is.EqualTo(3f - 1f).Within(1e-5f));
			Assert.That(TerrainHeightmapMath.FieldValue(8f, 2), Is.EqualTo(3f - 3f).Within(1e-5f));
			Assert.That(TerrainHeightmapMath.FieldValue(0f, 0), Is.EqualTo(TerrainHeightmapMath.Log2(TerrainHeightmapMath.FieldFloorMetres) - 1f).Within(1e-4f));
			// Raised: f_(l-1) >= f_l - 1, i.e. g_l >= g_(l-1) - 1, cell by cell.
			var fields = new[] { new[] { 5f, -3f }, new[] { 0f, -2f }, new[] { -9f, 0f } };
			TerrainHeightmapMath.Raise(fields);
			Assert.That(fields[1], Is.EqualTo(new[] { 4f, -2f }));
			Assert.That(fields[2], Is.EqualTo(new[] { 3f, 0f }));
		}

		[Test]
		public void TheFieldsDilation_LimitsItsSlope()
		{
			var field = new float[32 * 32];
			for (int i = 0; i < field.Length; i++)
			{
				field[i] = -7f;
			}
			field[16 * 32 + 16] = 3f;
			float[] x = TerrainHeightmapMath.Dilate(field, 32, 32, true, 0.125f, 64);
			float[] d = TerrainHeightmapMath.Dilate(x, 32, 32, false, 0.125f, 64);
			Assert.That(d[16 * 32 + 16], Is.EqualTo(3f));
			Assert.That(d[16 * 32 + 20], Is.EqualTo(3f - 0.5f).Within(1e-5f));
			Assert.That(d[20 * 32 + 20], Is.EqualTo(3f - 1f).Within(1e-5f), "the cone falls by the slope a cell along each axis");
			for (int z = 0; z < 32; z++)
			{
				for (int i = 1; i < 32; i++)
				{
					Assert.That(Mathf.Abs(d[z * 32 + i] - d[z * 32 + i - 1]), Is.LessThanOrEqualTo(0.125f + 1e-5f));
					Assert.That(Mathf.Abs(d[i * 32 + z] - d[(i - 1) * 32 + z]), Is.LessThanOrEqualTo(0.125f + 1e-5f));
				}
			}
		}

		// ── Sources ─────────────────────────────────────────────────────────

		[Test]
		public void TheGpuShader_IsTheTilesOwnPasses_CompiledWithDxc()
		{
			string gpu = File.ReadAllText(Shaders + "FishWeatherTerrainArrayGpu.shader");
			Assert.That(gpu, Does.Contain("Shader \"" + TerrainHeightmapSystem.ShaderName + "\""));
			foreach (string pass in new[] { "ForwardLit", "ShadowCaster", "GBuffer", "DepthOnly", "DepthNormals" })
			{
				Assert.That(gpu, Does.Contain($"Name \"{pass}\""), pass);
			}
			Assert.That(Regex.Matches(gpu, @"#define FISH_TERRAIN_GPU 1").Count, Is.EqualTo(5), "every pass");
			Assert.That(Regex.Matches(gpu, @"HLSLPROGRAM\s+#pragma use_dxc vulkan").Count, Is.EqualTo(5), "DXC everywhere");
			Assert.That(Regex.Matches(gpu, "#include \"FishWeatherTerrainArrayPasses.hlsl\"").Count, Is.EqualTo(5), "the tiles' own passes");
			Assert.That(gpu, Does.Not.Contain("multi_compile_instancing"));
			Assert.That(gpu, Does.Not.Contain("#pragma shader_feature"), "a runtime material's shader_feature variants are stripped from builds");
			Assert.That(gpu, Does.Contain("#pragma multi_compile_local_fragment _ _ALPHATEST_ON"));
			Assert.That(gpu, Does.Contain("Cull [_FishGpuShadowCull]"));

			string input = File.ReadAllText(Shaders + "FishWeatherTerrainArrayInput.hlsl");
			Assert.That(input, Does.Contain("FISH_TERRAIN_ARRAY_NUMBERS_AS(static)"), "the drawn tile's numbers are statics in the GPU draw");
			Assert.That(input, Does.Contain("#define FISH_INDEX_LOAD(x, y) _FishIndexMapArray.Load(int3((x), (y), 0))"));
			Assert.That(input, Does.Not.Contain("_FishIndexMap.Load(int3(i"), "every index read goes through FISH_INDEX_LOAD");
			string passes = File.ReadAllText(Shaders + "FishWeatherTerrainArrayPasses.hlsl");
			Assert.That(Regex.Matches(passes, @"FishTerrainGpuSelectTile\(IN\.fishTile\);").Count, Is.EqualTo(4), "every fragment picks its tile first");
			Assert.That(Regex.Matches(passes, @"FishTerrainGpuInstancing\(").Count, Is.EqualTo(3), "every vertex stage takes the patch");
			Assert.That(passes, Does.Contain("o.fishTile = FishTerrainGpuBeginPatch(v.fishInstance);\n    if (FishCasterCulledAt(fishTerrainPatchSphere.xyz, fishTerrainPatchSphere.w))")
				.Or.Contain("o.fishTile = FishTerrainGpuBeginPatch(v.fishInstance);\r\n    if (FishCasterCulledAt(fishTerrainPatchSphere.xyz, fishTerrainPatchSphere.w))"),
				"the caster is culled per cascade before any of its vertex work");
			Assert.That(passes, Does.Not.Contain("= GetVertexPositionInputs("), "the object transform goes through the wrappers");
		}

		[Test]
		public void TheComputeAndTheHlsl_FollowTheMath()
		{
			string hlsl = File.ReadAllText(Shaders + "FishTerrainHeightmap.hlsl");
			Assert.That(hlsl, Does.Contain($"#define FISH_TERRAIN_PATCH_QUADS {TerrainHeightmapMath.PatchQuads}u"));
			Assert.That(hlsl, Does.Contain($"#define FISH_TERRAIN_LIST_SOLID {TerrainHeightmapMath.ListSolid}u"));
			Assert.That(hlsl, Does.Contain($"#define FISH_TERRAIN_LIST_HOLED {TerrainHeightmapMath.ListHoled}u"));
			Assert.That(hlsl, Does.Contain($"#define FISH_TERRAIN_LIST_SHADOW_SOLID {TerrainHeightmapMath.ListShadowSolid}u"));
			Assert.That(hlsl, Does.Contain($"#define FISH_TERRAIN_LIST_SHADOW_HOLED {TerrainHeightmapMath.ListShadowHoled}u"));
			Assert.That(hlsl, Does.Contain("float t = saturate((FishTerrainLodAt(FishTerrainWorldAt(tile, p, height), l) - (float)l - a) / (1.0 - a));"));
			Assert.That(hlsl, Does.Contain("float stride = (float)(2u << l);"));
			string compute = File.ReadAllText(Shaders + "FishTerrainHeightmap.compute");
			foreach (string kernel in new[] { "FishTerrainRoughClear", "FishTerrainRough", "FishTerrainRoughLog", "FishTerrainDilateX", "FishTerrainDilateZ",
				"FishTerrainNodeLeaf", "FishTerrainNodeParent", "FishTerrainClear", "FishTerrainSelect", "FishTerrainArgsFence" })
			{
				Assert.That(compute, Does.Contain("#pragma kernel " + kernel + "\n").Or.Contain("#pragma kernel " + kernel + "\r\n"), kernel);
			}
			Assert.That(compute, Does.Contain("#pragma use_dxc vulkan"));
			Assert.That(compute, Does.Contain("return u >= v ? h00 + u * (h10 - h00) + v * (h11 - h10) : h00 + v * (h01 - h00) + u * (h11 - h01);"),
				"the roughness measures the patch's own triangulation (TerrainHeightmapMath.QuadHeight)");
			Assert.That(compute, Does.Contain("if (level > 0u && FishTerrainMinLod(lo, hi, bounds.z) < (float)level)"), "splits: TerrainHeightmapMath.Splits");
			Assert.That(compute, Does.Contain("if (!(FishTerrainMinLod(parentLo, parentHi, parentBounds.z) < (float)(level + 1u)))"), "drawn only under a parent that splits");
		}

		[Test]
		public void TheSystem_IsClientPlayOnly_GivesTheTilesBack_AndHasItsSwitch()
		{
			string system = File.ReadAllText(Heightmap + "TerrainHeightmapSystem.cs");
			Assert.That(system, Does.Contain("if (hooked || !Application.isPlaying)"), "never in edit mode");
			Assert.That(system, Does.Contain("if (!Enabled || !Supported())"), "the switch (and an unsupported device) give the ground back to Unity");
			Assert.That(system, Does.Contain("profile.TerrainHeightmap.Enabled"));
			Assert.That(system, Does.Contain("public static bool? Override;"));
			string field = File.ReadAllText(Heightmap + "TerrainHeightmapField.cs");
			Assert.That(field, Does.Contain("Tiles[i].drawHeightmap = false;"));
			Assert.That(field, Does.Contain("Tiles[i].drawHeightmap = drewHeightmap[i];"), "released tiles draw their own heightmaps again");
			Assert.That(field, Does.Contain("lightProbeUsage = LightProbeUsage.CustomProvided"), "lit by the ambient probe, as the tiles were");
			string profile = File.ReadAllText("Assets/Scripts/Client/World/Weather/Presentation/WeatherRenderProfile.cs");
			Assert.That(profile, Does.Contain("public TerrainHeightmapSettings TerrainHeightmap = new TerrainHeightmapSettings();"));
			Assert.That(new TerrainHeightmapSettings().Enabled, Is.True);
			string assets = File.ReadAllText("Assets/Scripts/Client/World/Weather/Editor/WeatherRenderAssets.cs");
			Assert.That(assets, Does.Contain("TerrainHeightmapSystem.ComputeAssetPath").And.Contain("TerrainHeightmapSystem.ShaderAssetPath"));
			string binder = File.ReadAllText("Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/Terrain/TerrainArrayBinder.cs");
			Assert.That(Regex.Matches(binder, @"TilePushes\+\+;").Count, Is.EqualTo(2), "a push and an unbind are both seen");
			Assert.That(File.Exists(TerrainHeightmapSystem.ComputeAssetPath), Is.True);
			Assert.That(File.Exists(TerrainHeightmapSystem.ShaderAssetPath), Is.True);
		}
	}
}
