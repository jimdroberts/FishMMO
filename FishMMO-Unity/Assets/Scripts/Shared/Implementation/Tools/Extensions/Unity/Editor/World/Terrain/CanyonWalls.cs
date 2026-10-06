#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Tuning for <see cref="CanyonWalls.Build"/>; the defaults are the shipped behaviour.</summary>
	public sealed class CanyonWallOptions
	{
		/// <summary>The least steepness a cell must have to become wall, in degrees.</summary>
		public float MinDegrees = 48f;

		/// <summary>The least plateau weight a cell must stand in: walls are canyon country's.</summary>
		public float MinPlateauWeight = 0.3f;

		/// <summary>Wall faces are built this many quads to a heightmap cell each way, so beds and lips can be finer than the heightmap.</summary>
		public int Subdivision = 2;

		/// <summary>How far a hard cap juts out over the soft beds under it, and they are cut back under it, in metres at full contrast.</summary>
		public float StrataReliefMetres = 2.5f;

		/// <summary>The rock face's roughness, in metres.</summary>
		public float RoughnessMetres = 0.35f;

		/// <summary>Bands smaller than this many cells stay terrain.</summary>
		public int MinCells = 8;

		/// <summary>The most wall cells a scene takes; past it the walls are left out and reported, rather than burying the scene in triangles.</summary>
		public int MaxCells = 150_000;

		/// <summary>Steepness, in degrees, below which a wall's top or foot has been left, when finding how tall a wall stands.</summary>
		public float ExitDegrees = 35f;

		/// <summary>The furthest a wall's face is moved from where the terrain had it, in metres.</summary>
		public float MaxShiftMetres = 25f;

		/// <summary>Size of the chunks the walls are split into, in metres, for culling.</summary>
		public float ChunkMetres = 128f;

		/// <summary>Metres of rock one repeat of the wall's texture covers.</summary>
		public float TextureMetres = 4f;

		/// <summary>Samples over which a wall's shape eases into the terrain at its edges.</summary>
		public int TaperSamples = 2;
	}

	/// <summary>What <see cref="CanyonWalls.Build"/> did.</summary>
	public sealed class CanyonWallReport
	{
		public readonly List<string> Notes = new List<string>();
		public readonly List<string> Wrote = new List<string>();
		/// <summary>Heightmap cells turned into wall.</summary>
		public int Cells;
		public long Triangles;
		public int Chunks;

		public override string ToString() => $"[Canyon walls] {Cells:N0} cells of wall in {Chunks} chunk(s), {Triangles:N0} triangles.";
	}

	/// <summary>
	/// Vertical and overhanging rock walls where canyon country's benches break off: the steep bands
	/// rebuilt as meshes with their faces stood up and their beds jutting and recessed, the terrain
	/// holed under them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why meshes.</b> A heightmap has one height per sample, so the plateau's risers can only
	/// slope — about 60° at two metres a sample — and can never overhang. A canyon wall is a cliff
	/// with its hard caps jutting over the soft beds the weather has cut back under them. So the steep
	/// band of each riser is rebuilt as a mesh and the terrain under it holed
	/// (<see cref="TerrainData.SetHoles"/>), which drops both its surface and its collider there.
	/// </para>
	/// <para>
	/// <b>The band's own grid, moved sideways.</b> The mesh is the terrain's own cells in the band,
	/// subdivided, with every vertex moved horizontally down the slope: towards the riser's middle,
	/// which stands the face up; out where the rock at its height is a hard cap and in where it is
	/// soft (<see cref="SceneGeologyGrid.HardnessAt"/>), which makes the lips and the alcoves under
	/// them; and a little roughness. The move eases to nothing over the band's last cells, so the
	/// mesh's edge sits exactly on the terrain's vertices and meets it without a seam.
	/// </para>
	/// <para>
	/// <b>Like the cliffs:</b> each chunk carries its collider on the Ground layer, which the server
	/// keeps, and its renderer on a <see cref="ClientOnlyObject"/> child, which server builds strip.
	/// The holes are part of the terrain data, which the server keeps too, so both sides collide with
	/// the same wall. It wears the rock type of the geology under it. Built before the cliff rocks
	/// are placed, so none stand on a hole.
	/// </para>
	/// </remarks>
	public static class CanyonWalls
	{
		/// <summary>How far from the scene's water no wall stands, metres.</summary>
		public const float WaterClearMetres = 6f;

		/// <summary>The one root every wall chunk lives under.</summary>
		public const string RootName = "Canyon Walls";

		/// <summary>Builds the walls into <paramref name="scene"/>, holing <paramref name="terrains"/> under them.</summary>
		/// <param name="field">The scene's ground, as written to the tiles.</param>
		/// <param name="plateauWeight">Per sample, how much of a plateau the ground became.</param>
		/// <param name="geology">The rock under every sample.</param>
		/// <param name="assetPath">Where the wall meshes are written: one asset holds them all. Null keeps them in memory (tests).</param>
		/// <param name="water">Per sample, true where the scene's water or the ground it shaped stands (a river's channel, banks and bars, a lake): no wall is stood there or within a few metres of it, or a river crossing a riser ran through a wall. Null for none.</param>
		public static CanyonWallReport Build(Scene scene, Terrain[,] terrains, TerrainTilePlan plan, SceneHeightField field,
			float[] plateauWeight, SceneGeologyGrid geology, string assetPath, CanyonWallOptions options = null, Func<int, bool> water = null)
		{
			options ??= new CanyonWallOptions();
			var report = new CanyonWallReport();
			if (plateauWeight == null || geology == null)
			{
				return report;
			}
			int width = field.Width, depth = field.Depth;
			float cell = field.Spacing;
			float[] height = field.Metres;

			// ── Which cells are wall ──
			var slope = new float[width * depth];
			for (int z = 0; z < depth; z++)
			{
				int za = Math.Max(0, z - 1), zb = Math.Min(depth - 1, z + 1);
				for (int x = 0; x < width; x++)
				{
					int xa = Math.Max(0, x - 1), xb = Math.Min(width - 1, x + 1);
					float gx = (height[z * width + xb] - height[z * width + xa]) / ((xb - xa) * cell);
					float gz = (height[zb * width + x] - height[za * width + x]) / ((zb - za) * cell);
					slope[z * width + x] = Mathf.Sqrt(gx * gx + gz * gz);
				}
			}
			int cellsX = width - 1, cellsZ = depth - 1;
			var wall = new bool[cellsX * cellsZ];
			float steep = Mathf.Tan(options.MinDegrees * Mathf.Deg2Rad);
			int margin = Mathf.CeilToInt((SceneErosion.EdgeFadeMetres + 8f) / cell);
			for (int z = margin; z < cellsZ - margin; z++)
			{
				for (int x = margin; x < cellsX - margin; x++)
				{
					int a = z * width + x, b = a + 1, c = a + width, d = c + 1;
					float s = 0.25f * (slope[a] + slope[b] + slope[c] + slope[d]);
					float w = 0.25f * (plateauWeight[a] + plateauWeight[b] + plateauWeight[c] + plateauWeight[d]);
					wall[z * cellsX + x] = s >= steep && w >= options.MinPlateauWeight;
				}
			}
			if (water != null)
			{
				int clear = Mathf.Max(1, Mathf.CeilToInt(WaterClearMetres / cell));
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						if (!water(z * width + x))
						{
							continue;
						}
						for (int dz = -clear; dz <= clear; dz++)
						{
							for (int dx = -clear; dx <= clear; dx++)
							{
								int cx = x + dx, cz = z + dz;
								if (cx >= 0 && cz >= 0 && cx < cellsX && cz < cellsZ)
								{
									wall[cz * cellsX + cx] = false;
								}
							}
						}
					}
				}
			}
			Close(wall, cellsX, cellsZ);
			DropSmall(wall, cellsX, cellsZ, options.MinCells);
			// A cell's margin round every band, so the eased edge lies in the gentler ground at a riser's
			// lip and foot and the steep core stands fully upright.
			Grow(wall, cellsX, cellsZ, margin);
			int count = 0;
			foreach (bool w in wall)
			{
				if (w)
				{
					count++;
				}
			}
			if (count == 0)
			{
				return report;
			}
			if (count > options.MaxCells)
			{
				report.Notes.Add($"Canyon walls: {count:N0} cells of wall is over the {options.MaxCells:N0} a scene takes; they are left as terrain.");
				return report;
			}
			report.Cells = count;

			// ── How each sample of a wall moves ──
			float[] taper = Taper(wall, width, depth, cellsX, cellsZ, options.TaperSamples);
			Vector2[] downhill = Downhill(height, width, depth, cell);
			float[] shifts = Shifts(field, wall, cellsX, cellsZ, downhill, options);

			// ── The meshes, by chunk ──
			Material material = MaterialFor(geology, wall, width, cellsX, cellsZ, report);
			int k = Mathf.Max(1, options.Subdivision);
			int chunkCells = Mathf.Max(8, Mathf.RoundToInt(options.ChunkMetres / cell));
			int layer = CliffPlacer.ColliderLayer;
			Clear(scene);
			var root = new GameObject(RootName) { layer = layer };
			SceneManager.MoveGameObjectToScene(root, scene);
			GameObjectUtility.SetStaticEditorFlags(root, CliffPlacer.Flags);
			var meshes = new List<Mesh>();

			for (int cz0 = 0; cz0 < cellsZ; cz0 += chunkCells)
			{
				for (int cx0 = 0; cx0 < cellsX; cx0 += chunkCells)
				{
					Mesh mesh = BuildChunk(field, geology, wall, cellsX, cellsZ, cx0, cz0, chunkCells, k, taper, shifts, downhill, options);
					if (mesh == null)
					{
						continue;
					}
					mesh.name = $"{scene.name} Canyon Wall {cx0 / chunkCells} {cz0 / chunkCells}";
					meshes.Add(mesh);
					report.Triangles += mesh.triangles.Length / 3;

					var chunk = new GameObject($"Chunk {cx0 / chunkCells} {cz0 / chunkCells}") { layer = layer };
					chunk.transform.SetParent(root.transform, false);
					GameObjectUtility.SetStaticEditorFlags(chunk, CliffPlacer.Flags);
					chunk.AddComponent<MeshCollider>().sharedMesh = mesh;

					var visual = new GameObject(CliffPlacer.VisualName) { layer = layer };
					visual.transform.SetParent(chunk.transform, false);
					GameObjectUtility.SetStaticEditorFlags(visual, CliffPlacer.Flags);
					visual.AddComponent<ClientOnlyObject>();
					visual.AddComponent<MeshFilter>().sharedMesh = mesh;
					var renderer = visual.AddComponent<MeshRenderer>();
					renderer.sharedMaterial = material;
					renderer.shadowCastingMode = ShadowCastingMode.On;
					report.Chunks++;
				}
			}

			// One asset for every chunk's mesh, beside the terrain.
			for (int i = 0; assetPath != null && i < meshes.Count; i++)
			{
				if (i == 0)
				{
					AssetDatabase.CreateAsset(meshes[i], assetPath);
				}
				else
				{
					AssetDatabase.AddObjectToAsset(meshes[i], assetPath);
				}
			}
			if (meshes.Count > 0 && assetPath != null)
			{
				report.Wrote.Add(assetPath);
			}

			Hole(terrains, plan, wall, cellsX, cellsZ);
			report.Notes.Add(report.ToString());
			return report;
		}

		/// <summary>Removes every wall root from the scene.</summary>
		public static int Clear(Scene scene)
		{
			int removed = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == RootName)
				{
					UnityEngine.Object.DestroyImmediate(root);
					removed++;
				}
			}
			return removed;
		}

		// ── Masks ─────────────────────────────────────────────────────

		/// <summary>A closing (grow then shrink by a cell), so a wall is not a sieve of single cells.</summary>
		private static void Close(bool[] mask, int width, int depth)
		{
			var grown = new bool[mask.Length];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					bool any = false;
					for (int dz = -1; dz <= 1 && !any; dz++)
					{
						for (int dx = -1; dx <= 1 && !any; dx++)
						{
							int xx = x + dx, zz = z + dz;
							any = xx >= 0 && zz >= 0 && xx < width && zz < depth && mask[zz * width + xx];
						}
					}
					grown[z * width + x] = any;
				}
			}
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					bool all = grown[z * width + x];
					for (int dz = -1; dz <= 1 && all; dz++)
					{
						for (int dx = -1; dx <= 1 && all; dx++)
						{
							int xx = x + dx, zz = z + dz;
							all = xx < 0 || zz < 0 || xx >= width || zz >= depth || grown[zz * width + xx];
						}
					}
					// Shrinking never takes away a cell that was steep to begin with.
					mask[z * width + x] = mask[z * width + x] || all;
				}
			}
		}

		/// <summary>Grows every band by a cell, inside the scene's margin.</summary>
		private static void Grow(bool[] mask, int width, int depth, int margin)
		{
			var grown = (bool[])mask.Clone();
			for (int z = margin; z < depth - margin; z++)
			{
				for (int x = margin; x < width - margin; x++)
				{
					if (mask[z * width + x])
					{
						continue;
					}
					for (int n = 0; n < 4; n++)
					{
						int xx = x + (n == 0 ? -1 : n == 1 ? 1 : 0), zz = z + (n == 2 ? -1 : n == 3 ? 1 : 0);
						if (mask[zz * width + xx])
						{
							grown[z * width + x] = true;
							break;
						}
					}
				}
			}
			Array.Copy(grown, mask, mask.Length);
		}

		/// <summary>Drops bands smaller than <paramref name="minimum"/> cells. Returns the cells left.</summary>
		private static int DropSmall(bool[] mask, int width, int depth, int minimum)
		{
			var seen = new bool[mask.Length];
			var stack = new Stack<int>();
			var band = new List<int>();
			int kept = 0;
			for (int start = 0; start < mask.Length; start++)
			{
				if (!mask[start] || seen[start])
				{
					continue;
				}
				band.Clear();
				stack.Push(start);
				seen[start] = true;
				while (stack.Count > 0)
				{
					int c = stack.Pop();
					band.Add(c);
					int x = c % width, z = c / width;
					for (int n = 0; n < 4; n++)
					{
						int xx = x + (n == 0 ? -1 : n == 1 ? 1 : 0), zz = z + (n == 2 ? -1 : n == 3 ? 1 : 0);
						if (xx < 0 || zz < 0 || xx >= width || zz >= depth)
						{
							continue;
						}
						int m = zz * width + xx;
						if (mask[m] && !seen[m])
						{
							seen[m] = true;
							stack.Push(m);
						}
					}
				}
				if (band.Count < minimum)
				{
					foreach (int c in band)
					{
						mask[c] = false;
					}
				}
				else
				{
					kept += band.Count;
				}
			}
			return kept;
		}

		/// <summary>
		/// Per sample, 0 on a wall's edge (any neighbouring cell not wall) easing to 1 a few samples in:
		/// how much of its move a sample makes.
		/// </summary>
		private static float[] Taper(bool[] wall, int width, int depth, int cellsX, int cellsZ, int samples)
		{
			var distance = new int[width * depth];
			var queue = new Queue<int>();
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int touching = 0, walls = 0;
					for (int dz = -1; dz <= 0; dz++)
					{
						for (int dx = -1; dx <= 0; dx++)
						{
							int cx = x + dx, cz = z + dz;
							if (cx < 0 || cz < 0 || cx >= cellsX || cz >= cellsZ)
							{
								continue;
							}
							touching++;
							if (wall[cz * cellsX + cx])
							{
								walls++;
							}
						}
					}
					int i = z * width + x;
					if (walls == 0)
					{
						distance[i] = -1; // not part of any wall
					}
					else if (walls < touching || touching < 4)
					{
						distance[i] = 0; // a wall's edge: pinned to the terrain
						queue.Enqueue(i);
					}
					else
					{
						distance[i] = int.MaxValue;
					}
				}
			}
			while (queue.Count > 0)
			{
				int i = queue.Dequeue();
				int x = i % width, z = i / width;
				for (int n = 0; n < 4; n++)
				{
					int xx = x + (n == 0 ? -1 : n == 1 ? 1 : 0), zz = z + (n == 2 ? -1 : n == 3 ? 1 : 0);
					if (xx < 0 || zz < 0 || xx >= width || zz >= depth)
					{
						continue;
					}
					int m = zz * width + xx;
					if (distance[m] == int.MaxValue)
					{
						distance[m] = distance[i] + 1;
						queue.Enqueue(m);
					}
				}
			}
			var taper = new float[width * depth];
			for (int i = 0; i < taper.Length; i++)
			{
				taper[i] = distance[i] <= 0 ? 0f : Mathf.SmoothStep(0f, 1f, distance[i] / (float)Mathf.Max(1, samples));
			}
			return taper;
		}

		// ── Shape ─────────────────────────────────────────────────────

		/// <summary>
		/// Per wall sample, how far to move it down the slope to stand its wall up: half the difference
		/// between how far the ground stays steep below it and above it. That carries every sample of a
		/// riser onto the line halfway across it, whatever the riser's profile, and leaves a sample on a
		/// bench — steep neither way — where it is.
		/// </summary>
		private static float[] Shifts(SceneHeightField field, bool[] wall, int cellsX, int cellsZ, Vector2[] downhill, CanyonWallOptions options)
		{
			int width = field.Width, depth = field.Depth;
			float exit = Mathf.Tan(options.ExitDegrees * Mathf.Deg2Rad);
			var shift = new float[width * depth];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int i = z * width + x;
					if (!TouchesWall(wall, cellsX, cellsZ, x, z))
					{
						continue;
					}
					Vector2 n = downhill[i];
					float east = field.EastOf(x), north = field.NorthOf(z);
					float up = SteepFor(field, east, north, -n, exit, options.MaxShiftMetres);
					float down = SteepFor(field, east, north, n, exit, options.MaxShiftMetres);
					shift[i] = 0.5f * (down - up);
				}
			}
			return shift;
		}

		/// <summary>How far, in metres, the ground stays steep walking from a point along <paramref name="direction"/>.</summary>
		private static float SteepFor(SceneHeightField field, float east, float north, Vector2 direction, float exit, float maxMetres)
		{
			float step = field.Spacing * 0.5f;
			float previous = field.MetresAt(east, north);
			float travelled = 0f;
			while (travelled + step <= maxMetres)
			{
				float h = field.MetresAt(east + direction.x * (travelled + step), north + direction.y * (travelled + step));
				if (Mathf.Abs(h - previous) / step < exit)
				{
					/* The steep ground ends inside this step: found to an eighth of it. Whole steps put every sample of
					 * a riser up to a step off the line halfway across it, and the face stood up leaning by that much. */
					float fine = step / 8f;
					float h0 = previous;
					for (int k = 1; k <= 8; k++)
					{
						float hk = field.MetresAt(east + direction.x * (travelled + k * fine), north + direction.y * (travelled + k * fine));
						if (Mathf.Abs(hk - h0) / fine < exit)
						{
							break;
						}
						h0 = hk;
						travelled += fine;
					}
					return travelled;
				}
				previous = h;
				travelled += step;
			}
			return travelled;
		}

		private static bool TouchesWall(bool[] wall, int cellsX, int cellsZ, int x, int z)
		{
			for (int dz = -1; dz <= 0; dz++)
			{
				for (int dx = -1; dx <= 0; dx++)
				{
					int cx = x + dx, cz = z + dz;
					if (cx >= 0 && cz >= 0 && cx < cellsX && cz < cellsZ && wall[cz * cellsX + cx])
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>Per sample, the unit direction down the broad slope, from heights smoothed over a few samples.</summary>
		private static Vector2[] Downhill(float[] height, int width, int depth, float cell)
		{
			var smooth = new float[height.Length];
			LandscapeEvolution.SmoothInto(height, smooth, width, depth, 3);
			var downhill = new Vector2[height.Length];
			for (int z = 0; z < depth; z++)
			{
				int za = Math.Max(0, z - 1), zb = Math.Min(depth - 1, z + 1);
				for (int x = 0; x < width; x++)
				{
					int xa = Math.Max(0, x - 1), xb = Math.Min(width - 1, x + 1);
					var g = new Vector2(smooth[z * width + xb] - smooth[z * width + xa], smooth[zb * width + x] - smooth[za * width + x]);
					downhill[z * width + x] = g.sqrMagnitude > 1e-10f ? -g.normalized : Vector2.zero;
				}
			}
			return downhill;
		}

		/// <summary>
		/// One chunk's mesh: every wall cell in it, subdivided, each vertex moved down the slope towards
		/// its wall's middle and in or out with its bed. Null when the chunk has no wall.
		/// </summary>
		private static Mesh BuildChunk(SceneHeightField field, SceneGeologyGrid geology, bool[] wall, int cellsX, int cellsZ,
			int cx0, int cz0, int chunkCells, int k, float[] taper, float[] shifts, Vector2[] downhill, CanyonWallOptions options)
		{
			int width = field.Width;
			float cell = field.Spacing;
			var index = new Dictionary<long, int>();
			var vertices = new List<Vector3>();
			var uvs = new List<Vector2>();
			var triangles = new List<int>();
			int cx1 = Math.Min(cellsX, cx0 + chunkCells), cz1 = Math.Min(cellsZ, cz0 + chunkCells);
			long stride = (long)cellsX * k + 1;

			int Vertex(int sx, int sz)
			{
				long key = (long)sz * stride + sx;
				if (index.TryGetValue(key, out int existing))
				{
					return existing;
				}
				// Where it is in samples, and the four samples around it.
				float gx = sx / (float)k, gz = sz / (float)k;
				int x0 = Math.Min((int)gx, width - 2), z0 = Math.Min((int)gz, field.Depth - 2);
				float fx = gx - x0, fz = gz - z0;
				int a = z0 * width + x0, b = a + 1, c = a + width, d = c + 1;
				float Lerp(float[] v) => Mathf.Lerp(Mathf.Lerp(v[a], v[b], fx), Mathf.Lerp(v[c], v[d], fx), fz);
				float h = Lerp(field.Metres);
				float t = Lerp(taper);
				float east = Mathf.Lerp(field.EastOf(x0), field.EastOf(x0 + 1), fx);
				float north = Mathf.Lerp(field.NorthOf(z0), field.NorthOf(z0 + 1), fz);
				var position = new Vector3(east, h, north);
				if (t > 0f)
				{
					Vector2 n = Vector2.Lerp(Vector2.Lerp(downhill[a], downhill[b], fx), Vector2.Lerp(downhill[c], downhill[d], fx), fz);
					n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector2.zero;
					// Onto the line halfway across the riser: the upper half out over the lower, which stands it up.
					float shift = Lerp(shifts);
					// Out for a hard cap, in for a soft bed: the lips and the alcoves under them.
					int nearest = (fz < 0.5f ? z0 : z0 + 1) * width + (fx < 0.5f ? x0 : x0 + 1);
					GeologyColumn rock = geology.ColumnOf(nearest);
					shift += (geology.HardnessAt(nearest, h) - rock.Lithology.Hardness) * options.StrataReliefMetres;
					shift += (Noise(east, h, north) - 0.5f) * 2f * options.RoughnessMetres;
					position.x += n.x * shift * t;
					position.z += n.y * shift * t;
				}
				int i = vertices.Count;
				vertices.Add(position);
				uvs.Add(new Vector2((position.x + position.z) / options.TextureMetres, position.y / options.TextureMetres));
				index[key] = i;
				return i;
			}

			for (int cz = cz0; cz < cz1; cz++)
			{
				for (int cx = cx0; cx < cx1; cx++)
				{
					if (!wall[cz * cellsX + cx])
					{
						continue;
					}
					for (int j = 0; j < k; j++)
					{
						for (int i = 0; i < k; i++)
						{
							int sx = cx * k + i, sz = cz * k + j;
							int v00 = Vertex(sx, sz), v10 = Vertex(sx + 1, sz), v01 = Vertex(sx, sz + 1), v11 = Vertex(sx + 1, sz + 1);
							// Clockwise seen from above, as the terrain's own: the face turns outward when it stands up.
							triangles.Add(v00); triangles.Add(v01); triangles.Add(v11);
							triangles.Add(v00); triangles.Add(v11); triangles.Add(v10);
						}
					}
				}
			}
			if (triangles.Count == 0)
			{
				return null;
			}
			var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(triangles, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>Smooth 3D value noise in [0, 1] about two metres across: the rock face's roughness.</summary>
		private static float Noise(float x, float y, float z)
		{
			const float Scale = 0.5f;
			x *= Scale; y *= Scale; z *= Scale;
			int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
			float fx = x - ix, fy = y - iy, fz = z - iz;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			fz = fz * fz * (3f - 2f * fz);
			float Corner(int ox, int oy, int oz) => ProceduralNoise.ToUnit(ProceduralNoise.Hash(ix + ox, iy + oy, iz + oz, 0x5A11));
			float x00 = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), fx), x10 = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), fx);
			float x01 = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), fx), x11 = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), fx);
			return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
		}

		/// <summary>The walls' material: the cliff rock of the rock type most of the wall cells stand in.</summary>
		private static Material MaterialFor(SceneGeologyGrid geology, bool[] wall, int width, int cellsX, int cellsZ, CanyonWallReport report)
		{
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int cz = 0; cz < cellsZ; cz++)
			{
				for (int cx = 0; cx < cellsX; cx++)
				{
					if (!wall[cz * cellsX + cx])
					{
						continue;
					}
					string type = geology.ColumnOf(cz * width + cx).Lithology.RockTypeName ?? "Sandstone";
					counts[type] = (counts.TryGetValue(type, out int n) ? n : 0) + 1;
				}
			}
			string best = "Sandstone";
			int most = -1;
			foreach (KeyValuePair<string, int> kv in counts)
			{
				if (kv.Value > most || (kv.Value == most && string.CompareOrdinal(kv.Key, best) < 0))
				{
					best = kv.Key;
					most = kv.Value;
				}
			}
			string name = CliffRocks.MaterialName(best);
			string path = name != null ? ProceduralArtCatalogue.MaterialPath(name) : null;
			Material material = path != null ? AssetDatabase.LoadAssetAtPath<Material>(path) : null;
			if (material == null)
			{
				report.Notes.Add($"Canyon walls: no material for {best} rock at '{path}'; run Generate Biome Art, then re-cut. The walls render with the default material until then.");
			}
			return material;
		}

		// ── Holes ─────────────────────────────────────────────────────

		/// <summary>Holes every tile's terrain under the wall cells, surface and collider both.</summary>
		private static void Hole(Terrain[,] terrains, TerrainTilePlan plan, bool[] wall, int cellsX, int cellsZ)
		{
			int perTile = plan.Resolution - 1;
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					TerrainData data = terrains[tx, tz] != null ? terrains[tx, tz].terrainData : null;
					if (data == null)
					{
						continue;
					}
					int holes = data.holesResolution;
					bool[,] solid = null;
					for (int z = 0; z < perTile; z++)
					{
						for (int x = 0; x < perTile; x++)
						{
							int cx = tx * perTile + x, cz = tz * perTile + z;
							if (cx >= cellsX || cz >= cellsZ || !wall[cz * cellsX + cx])
							{
								continue;
							}
							if (solid == null)
							{
								solid = data.GetHoles(0, 0, holes, holes);
							}
							// The hole map is the heightmap's cells, [z, x], true where the surface is.
							if (x < holes && z < holes)
							{
								solid[z, x] = false;
							}
						}
					}
					if (solid != null)
					{
						data.SetHoles(0, 0, solid);
						EditorUtility.SetDirty(data);
					}
				}
			}
		}
	}
}
#endif
