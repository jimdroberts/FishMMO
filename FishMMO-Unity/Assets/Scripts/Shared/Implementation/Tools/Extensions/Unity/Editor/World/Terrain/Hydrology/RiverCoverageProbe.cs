#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Water;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Maps a saved scene's inland water against its terrain from above: the river and lake meshes exactly as the
	/// renderer builds them, rasterised with the shader's own draw rule, against the ground under every cell.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The draw rule (FishInlandWater.hlsl): a fragment is drawn where its vertex alpha is over 0.02 and it claims its
	/// pixel (vertex g, interpolated, over 0.5) or the water over the ground is shallow (column under 0.525 m, where the
	/// shader's smoothstep(0.35, 0.7) passes a half); and the depth test hides it where the ground stands over it.
	/// </para>
	/// <para>
	/// Each cell is one of: drawn water (blue, deeper darker); MISSING (red): ground under the level of the nearest drawn
	/// water, joined to it through ground under that level within a few metres, and no water drawn on it — a pool not
	/// filled, a gap at a bank; CLIPPED (orange): a mesh covers it but the draw rule drops it over ground lower than the
	/// water; by a fall (purple): lower than the water above a fall's lip, which goes over it as the curtain; hidden (dark
	/// green): a mesh under the ground; dry ground (grey, shaded by height).
	/// </para>
	/// <para>
	/// FISHMMO_SCENE: the scene (default Flo Monolith). FISHMMO_COVERAGE_OUT: where the PNGs and report go.
	/// FISHMMO_COVERAGE_CELL: metres per cell (default 0.5). Writes a map per river and per lake, a crop round each
	/// cluster of missing water, and a report of the clusters (where, how big, how deep). Saves nothing into the project.
	/// </para>
	/// </remarks>
	public static class RiverCoverageProbe
	{
		/// <summary>How far under a drawn water's level ground must lie to count as under it, metres.</summary>
		private const float UnderMetres = 0.05f;
		/// <summary>How far from drawn water the missing water is followed, metres.</summary>
		private const float FollowMetres = 8f;
		/// <summary>How far round a fall's drop (lip to landing) the ground is the fall's, not a pool's, metres past its half-width.</summary>
		private const float FallZoneMetres = 8f;
		/// <summary>The shader's shallow-water claim: drawn where the column is under this.</summary>
		private const float ShallowClaimMetres = 0.525f;
		/// <summary>The least cells a cluster of missing water is reported with.</summary>
		private const int MinCluster = 6;

		private enum Cell : byte { Dry, Drawn, Missing, Clipped, Hidden, Fall }

		public static void Run()
		{
			string scenePath = Environment.GetEnvironmentVariable("FISHMMO_SCENE");
			if (string.IsNullOrEmpty(scenePath))
			{
				scenePath = "Assets/Scenes/WorldScene/Arthis/Flo Monolith.unity";
			}
			string outDir = Environment.GetEnvironmentVariable("FISHMMO_COVERAGE_OUT");
			if (string.IsNullOrEmpty(outDir))
			{
				outDir = Path.Combine(Path.GetTempPath(), "river-coverage");
			}
			float cell = float.TryParse(Environment.GetEnvironmentVariable("FISHMMO_COVERAGE_CELL"), System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out float c) && c > 0.05f ? c : 0.5f;
			Directory.CreateDirectory(outDir);
			var report = new List<string>();

			EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			var renderer = UnityEngine.Object.FindAnyObjectByType<InlandWaterRenderer>();
			var bodies = renderer != null ? renderer.GetComponent<SceneWaterBodies>() : null;
			if (renderer == null || bodies == null || bodies.Hydrology == null)
			{
				Debug.LogError($"[River coverage] {scenePath}: no inland water in the scene.");
				return;
			}
			bodies.Build();
			renderer.Rebuild();

			// Every water surface mesh the renderer drew (its own material), in its draw order.
			var surfaces = new List<(string name, Mesh mesh, int order)>();
			foreach (MeshRenderer child in renderer.GetComponentsInChildren<MeshRenderer>(true))
			{
				MeshFilter filter = child.GetComponent<MeshFilter>();
				if (filter == null || filter.sharedMesh == null || child.sharedMaterial != renderer.Material)
				{
					continue;
				}
				surfaces.Add((child.name, filter.sharedMesh, child.sortingOrder));
			}
			surfaces.Sort((a, b) => a.order.CompareTo(b.order));
			report.Add($"scene {scenePath}: {surfaces.Count} water surface mesh(es), {cell} m cells");

			var rivers = new RiverLines(bodies.Hydrology);
			// One map per surface, over its bounds and the distance missing water is followed.
			var allClusters = new List<(string map, int cells, Vector3 centre, float deepest, Rect box)>();
			foreach ((string name, Mesh mesh, int order) in surfaces)
			{
				Bounds b = mesh.bounds;
				float x0 = b.min.x - FollowMetres, z0 = b.min.z - FollowMetres;
				int w = Mathf.CeilToInt((b.size.x + 2f * FollowMetres) / cell), d = Mathf.CeilToInt((b.size.z + 2f * FollowMetres) / cell);
				if ((long)w * d > 60_000_000L)
				{
					report.Add($"{name}: {w}×{d} cells, too large; skipped");
					continue;
				}
				var level = new float[w * d];
				var claim = new float[w * d];
				var alpha = new float[w * d];
				for (int i = 0; i < level.Length; i++)
				{
					level[i] = float.NegativeInfinity;
				}
				// Every surface, in draw order: the first to claim a cell keeps it, as the draw-once stencil does.
				foreach ((string _, Mesh other, int otherOrder) in surfaces)
				{
					Rasterise(other, x0, z0, w, d, cell, level, claim, alpha, true);
				}
				var ground = new float[w * d];
				for (int z = 0; z < d; z++)
				{
					for (int x = 0; x < w; x++)
					{
						ground[z * w + x] = GroundAt(x0 + (x + 0.5f) * cell, z0 + (z + 0.5f) * cell);
					}
				}
				// The sea covers what lies under its level (the ocean is its own surface, not one of these meshes).
				float sea = bodies.Hydrology.SeaLevel;
				if (!float.IsNegativeInfinity(sea) && !float.IsNaN(sea))
				{
					for (int i = 0; i < ground.Length; i++)
					{
						if (ground[i] < sea && (float.IsNegativeInfinity(level[i]) || level[i] < sea))
						{
							level[i] = sea;
							claim[i] = 1f;
							alpha[i] = 1f;
						}
					}
				}
				// Round each fall's drop the water goes over its lip as the curtain: ground there lower than the water above is the fall's.
				var fallZone = new bool[w * d];
				foreach (InlandWaterRenderer.Fall fall in renderer.Falls)
				{
					MarkFall(fallZone, fall.LipPoint, fall.Landing, 0.5f * fall.Width + FallZoneMetres, x0, z0, w, d, cell);
				}
				// The river's own surface beside each cell: what ground under the water means there.
				var local = new float[w * d];
				for (int i = 0; i < local.Length; i++)
				{
					local[i] = float.NaN;
				}
				float x0Local = x0, z0Local = z0;
				float LocalAt(int i)
				{
					if (float.IsNaN(local[i]))
					{
						local[i] = rivers.SurfaceNear(x0Local + (i % w + 0.5f) * cell, z0Local + (i / w + 0.5f) * cell);
					}
					return local[i];
				}
				Cell[] kind = Classify(level, claim, alpha, ground, fallZone, LocalAt, w, d, cell, out float[] wantLevel);
				List<(int cells, Vector3 centre, float deepest, Rect box)> clusters = Clusters(kind, ground, wantLevel, w, d, x0, z0, cell);
				string file = Safe(name) + ".png";
				WriteMap(Path.Combine(outDir, file), kind, level, ground, w, d, 0, 0, w, d);
				int missing = kind.Count(k => k == Cell.Missing), clipped = kind.Count(k => k == Cell.Clipped);
				report.Add($"{file}: {w}×{d} cells from ({x0:0.0}, {z0:0.0}); missing {missing} cells ({missing * cell * cell:0} m²), clipped {clipped} ({clipped * cell * cell:0} m²), {clusters.Count} cluster(s)");
				foreach (var cl in clusters.OrderByDescending(k => k.cells))
				{
					allClusters.Add((name, cl.cells, cl.centre, cl.deepest, cl.box));
				}
				// A crop round each of the largest clusters, at the same cells, with 10 m round it.
				int index = 0;
				foreach (var cl in clusters.OrderByDescending(k => k.cells).Take(12))
				{
					int cx0 = Mathf.Max(0, Mathf.FloorToInt((cl.box.xMin - 10f - x0) / cell)), cz0 = Mathf.Max(0, Mathf.FloorToInt((cl.box.yMin - 10f - z0) / cell));
					int cx1 = Mathf.Min(w, Mathf.CeilToInt((cl.box.xMax + 10f - x0) / cell)), cz1 = Mathf.Min(d, Mathf.CeilToInt((cl.box.yMax + 10f - z0) / cell));
					string crop = $"{Safe(name)}_c{index++:00}.png";
					WriteMap(Path.Combine(outDir, crop), kind, level, ground, w, d, cx0, cz0, cx1, cz1, scale: Mathf.Clamp(600 / Mathf.Max(1, Mathf.Max(cx1 - cx0, cz1 - cz0)), 1, 8));
					report.Add($"  {crop}: {cl.cells} cells ({cl.cells * cell * cell:0} m²) about ({cl.centre.x:0.0}, {cl.centre.y:0.00}, {cl.centre.z:0.0}), up to {cl.deepest:0.00} m under the water");
				}
			}
			report.Add($"total: {allClusters.Sum(k => k.cells)} missing cells in {allClusters.Count} cluster(s) of {MinCluster}+");
			File.WriteAllLines(Path.Combine(outDir, "report.txt"), report);
			Debug.Log("[River coverage] written to " + outDir + "\n" + string.Join("\n", report));
		}

		/// <summary>Rasterises a mesh's triangles onto the cells: the highest surface that claims or shows over each.</summary>
		private static void Rasterise(Mesh mesh, float x0, float z0, int w, int d, float cell, float[] level, float[] claim, float[] alpha, bool counts)
		{
			if (!counts)
			{
				return;
			}
			Vector3[] v = mesh.vertices;
			Color32[] colours = mesh.colors32;
			int[] t = mesh.triangles;
			bool hasColours = colours != null && colours.Length == v.Length;
			for (int k = 0; k + 2 < t.Length; k += 3)
			{
				Vector3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]];
				int ix0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - x0) / cell));
				int ix1 = Mathf.Min(w - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - x0) / cell));
				int iz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - z0) / cell));
				int iz1 = Mathf.Min(d - 1, Mathf.FloorToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - z0) / cell));
				if (ix0 > ix1 || iz0 > iz1)
				{
					continue;
				}
				float det = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
				if (Mathf.Abs(det) < 1e-9f)
				{
					continue;
				}
				float ga = hasColours ? colours[t[k]].g / 255f : 1f, gb = hasColours ? colours[t[k + 1]].g / 255f : 1f, gc = hasColours ? colours[t[k + 2]].g / 255f : 1f;
				float aa = hasColours ? colours[t[k]].a / 255f : 1f, ab = hasColours ? colours[t[k + 1]].a / 255f : 1f, ac = hasColours ? colours[t[k + 2]].a / 255f : 1f;
				for (int z = iz0; z <= iz1; z++)
				{
					float pz = z0 + (z + 0.5f) * cell;
					for (int x = ix0; x <= ix1; x++)
					{
						float px = x0 + (x + 0.5f) * cell;
						float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / det;
						float l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / det;
						float l3 = 1f - l1 - l2;
						if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f)
						{
							continue;
						}
						int i = z * w + x;
						float y = l1 * a.y + l2 * b.y + l3 * c.y;
						float g = l1 * ga + l2 * gb + l3 * gc;
						float al = l1 * aa + l2 * ab + l3 * ac;
						// The draw-once stencil: the first claiming surface keeps the pixel; otherwise the highest shows.
						if (claim[i] > 0.5f)
						{
							continue;
						}
						if (g > 0.5f || y > level[i])
						{
							level[i] = y;
							claim[i] = g;
							alpha[i] = al;
						}
					}
				}
			}
		}

		/// <summary>The shader's draw rule over each cell, then the water missing where drawn water would run on into lower ground.</summary>
		/// <summary>Marks the cells within <paramref name="radius"/> of the segment a…b (a fall's drop, from above).</summary>
		private static void MarkFall(bool[] zone, Vector3 a, Vector3 b, float radius, float x0, float z0, int w, int d, float cell)
		{
			int ix0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - radius - x0) / cell)), ix1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + radius - x0) / cell));
			int iz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.z, b.z) - radius - z0) / cell)), iz1 = Mathf.Min(d - 1, Mathf.CeilToInt((Mathf.Max(a.z, b.z) + radius - z0) / cell));
			var ab = new Vector2(b.x - a.x, b.z - a.z);
			float lengthSq = Mathf.Max(1e-6f, ab.sqrMagnitude);
			for (int z = iz0; z <= iz1; z++)
			{
				for (int x = ix0; x <= ix1; x++)
				{
					var p = new Vector2(x0 + (x + 0.5f) * cell - a.x, z0 + (z + 0.5f) * cell - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(p, ab) / lengthSq);
					if ((p - ab * t).sqrMagnitude <= radius * radius)
					{
						zone[z * w + x] = true;
					}
				}
			}
		}

		private static Cell[] Classify(float[] level, float[] claim, float[] alpha, float[] ground, bool[] fallZone, Func<int, float> localLevel, int w, int d, float cell, out float[] want)
		{
			var kind = new Cell[w * d];
			want = new float[w * d];
			var queue = new Queue<int>();
			var reach = new float[w * d];
			for (int i = 0; i < kind.Length; i++)
			{
				want[i] = float.NegativeInfinity;
				reach[i] = float.PositiveInfinity;
				if (float.IsNegativeInfinity(level[i]) || float.IsNaN(ground[i]))
				{
					continue;
				}
				float column = level[i] - ground[i];
				bool drawn = alpha[i] > 0.02f && (claim[i] > 0.5f || column < ShallowClaimMetres);
				if (column <= 0f)
				{
					kind[i] = Cell.Hidden;
				}
				else if (drawn)
				{
					kind[i] = Cell.Drawn;
					want[i] = level[i];
					reach[i] = 0f;
					queue.Enqueue(i);
				}
				else if (column > UnderMetres)
				{
					kind[i] = Cell.Clipped;
				}
			}
			/* From the drawn water on into every cell under the river's own surface beside it (the nearest point of its line),
			 * joined to the drawn water through such cells, that nothing draws. Taking the level of the drawn water the search
			 * came from instead ran a sloping river's upstream water down along its banks, over its falls into the gorges, and
			 * round a bank that holds into the hollow behind it. */
			var visited = new bool[w * d];
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				int x = i % w, z = i / w;
				for (int k = 0; k < 4; k++)
				{
					int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= w || nz >= d)
					{
						continue;
					}
					int j = nz * w + nx;
					if (kind[j] == Cell.Drawn || visited[j] || reach[i] + cell > FollowMetres || float.IsNaN(ground[j]))
					{
						continue;
					}
					if (fallZone[j])
					{
						visited[j] = true;
						if (kind[j] == Cell.Dry && ground[j] < localLevel(j) - UnderMetres)
						{
							kind[j] = Cell.Fall;
						}
						continue;
					}
					visited[j] = true;
					float there = localLevel(j);
					if (!float.IsNaN(there) && ground[j] < there - UnderMetres)
					{
						want[j] = there;
						reach[j] = reach[i] + cell;
						if (kind[j] == Cell.Dry || kind[j] == Cell.Hidden)
						{
							kind[j] = Cell.Missing;
						}
						queue.Enqueue(j);
					}
				}
			}
			return kind;
		}

		/// <summary>The joined clusters of missing (and clipped) water: how many cells, where, how deep, and their box.</summary>
		private static List<(int cells, Vector3 centre, float deepest, Rect box)> Clusters(Cell[] kind, float[] ground, float[] want, int w, int d, float x0, float z0, float cell)
		{
			var result = new List<(int, Vector3, float, Rect)>();
			var seen = new bool[kind.Length];
			var stack = new Stack<int>();
			for (int s = 0; s < kind.Length; s++)
			{
				if (seen[s] || (kind[s] != Cell.Missing && kind[s] != Cell.Clipped))
				{
					continue;
				}
				int count = 0;
				double sx = 0, sy = 0, sz = 0;
				float deepest = 0f, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
				seen[s] = true;
				stack.Push(s);
				while (stack.Count > 0)
				{
					int i = stack.Pop();
					int x = i % w, z = i / w;
					float px = x0 + (x + 0.5f) * cell, pz = z0 + (z + 0.5f) * cell;
					count++;
					sx += px; sz += pz; sy += ground[i];
					if (!float.IsNegativeInfinity(want[i]))
					{
						deepest = Mathf.Max(deepest, want[i] - ground[i]);
					}
					minX = Mathf.Min(minX, px); maxX = Mathf.Max(maxX, px); minZ = Mathf.Min(minZ, pz); maxZ = Mathf.Max(maxZ, pz);
					for (int k = 0; k < 4; k++)
					{
						int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
						if (nx < 0 || nz < 0 || nx >= w || nz >= d)
						{
							continue;
						}
						int j = nz * w + nx;
						if (!seen[j] && (kind[j] == Cell.Missing || kind[j] == Cell.Clipped))
						{
							seen[j] = true;
							stack.Push(j);
						}
					}
				}
				if (count >= MinCluster)
				{
					result.Add((count, new Vector3((float)(sx / count), (float)(sy / count), (float)(sz / count)), deepest, Rect.MinMaxRect(minX, minZ, maxX, maxZ)));
				}
			}
			return result;
		}

		/// <summary>Writes the cells x0…x1, z0…z1 as a PNG, north up, each cell <paramref name="scale"/> pixels.</summary>
		private static void WriteMap(string path, Cell[] kind, float[] level, float[] ground, int w, int d, int x0, int z0, int x1, int z1, int scale = 1)
		{
			int pw = (x1 - x0) * scale, ph = (z1 - z0) * scale;
			if (pw <= 0 || ph <= 0)
			{
				return;
			}
			float low = float.PositiveInfinity, high = float.NegativeInfinity;
			for (int z = z0; z < z1; z++)
			{
				for (int x = x0; x < x1; x++)
				{
					float g = ground[z * w + x];
					if (!float.IsNaN(g)) { low = Mathf.Min(low, g); high = Mathf.Max(high, g); }
				}
			}
			var texture = new Texture2D(pw, ph, TextureFormat.RGB24, false);
			var pixels = new Color32[pw * ph];
			for (int z = z0; z < z1; z++)
			{
				for (int x = x0; x < x1; x++)
				{
					int i = z * w + x;
					float g = ground[i];
					// Hill shade from the slope to the west and the south, over a height ramp.
					float west = x > 0 ? ground[i - 1] : g, south = z > 0 ? ground[i - w] : g;
					float shade = Mathf.Clamp01(0.6f + 0.25f * ((g - west) + (g - south)));
					float t = high > low ? (g - low) / (high - low) : 0.5f;
					Color32 colour;
					switch (kind[i])
					{
						case Cell.Drawn:
							float column = Mathf.Clamp01((level[i] - g) / 3f);
							colour = new Color32((byte)(40 - 30 * column), (byte)(140 - 80 * column), (byte)(230 - 60 * column), 255);
							break;
						case Cell.Missing: colour = new Color32(235, 30, 30, 255); break;
						case Cell.Clipped: colour = new Color32(255, 150, 0, 255); break;
						case Cell.Fall: colour = new Color32(170, 80, 200, 255); break;
						case Cell.Hidden: colour = new Color32((byte)(50 * shade), (byte)(110 * shade), (byte)(50 * shade), 255); break;
						default:
							byte grey = (byte)(255 * Mathf.Clamp01(shade * (0.45f + 0.4f * t)));
							colour = new Color32(grey, grey, grey, 255);
							break;
					}
					for (int sy = 0; sy < scale; sy++)
					{
						for (int sx = 0; sx < scale; sx++)
						{
							pixels[((z - z0) * scale + sy) * pw + (x - x0) * scale + sx] = colour;
						}
					}
				}
			}
			texture.SetPixels32(pixels);
			texture.Apply();
			File.WriteAllBytes(path, texture.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(texture);
		}

		/// <summary>The rivers' lines, for the surface at the nearest point of any of them.</summary>
		private sealed class RiverLines
		{
			private const float Cell = 16f;
			private const float Reach = 40f;
			private readonly SceneHydrology hydrology;
			private readonly Dictionary<long, List<(int river, int point)>> index = new Dictionary<long, List<(int, int)>>();

			public RiverLines(SceneHydrology hydrology)
			{
				this.hydrology = hydrology;
				for (int r = 0; r < hydrology.Rivers.Count; r++)
				{
					SceneHydrology.River river = hydrology.Rivers[r];
					if (!river.Perennial || river.Points == null)
					{
						continue;
					}
					for (int i = 0; i + 1 < river.Points.Length; i++)
					{
						Vector3 a = river.Points[i], b = river.Points[i + 1];
						int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - Reach) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + Reach) / Cell);
						int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - Reach) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + Reach) / Cell);
						for (int z = z0; z <= z1; z++)
						{
							for (int x = x0; x <= x1; x++)
							{
								long key = ((long)x << 32) ^ (uint)z;
								if (!index.TryGetValue(key, out var list))
								{
									index[key] = list = new List<(int, int)>();
								}
								list.Add((r, i));
							}
						}
					}
				}
			}

			/// <summary>The surface at the nearest point of the nearest river line within reach; NaN for none.</summary>
			public float SurfaceNear(float x, float z)
			{
				long key = ((long)Mathf.FloorToInt(x / Cell) << 32) ^ (uint)Mathf.FloorToInt(z / Cell);
				if (!index.TryGetValue(key, out var list))
				{
					return float.NaN;
				}
				float best = float.MaxValue, level = float.NaN;
				foreach ((int r, int i) in list)
				{
					Vector3 a = hydrology.Rivers[r].Points[i], b = hydrology.Rivers[r].Points[i + 1];
					var ab = new Vector2(b.x - a.x, b.z - a.z);
					var ap = new Vector2(x - a.x, z - a.z);
					float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
					float distance = (ap - ab * t).sqrMagnitude;
					if (distance < best)
					{
						best = distance;
						level = Mathf.Lerp(a.y, b.y, t);
					}
				}
				return level;
			}
		}

		private static string Safe(string name) => new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

		/// <summary>The terrain's height at a scene position; NaN off every tile.</summary>
		private static float GroundAt(float x, float z)
		{
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = terrain.terrainData.size;
				if (x < origin.x || z < origin.z || x > origin.x + size.x || z > origin.z + size.z)
				{
					continue;
				}
				return terrain.SampleHeight(new Vector3(x, 0f, z)) + origin.y;
			}
			return float.NaN;
		}
	}
}
#endif
