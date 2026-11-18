#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Ridge and furrow carved into the terrain (2026-10-18, phase I2h, decision 26): each ridge-and-furrow field's broad
	/// ridges raised into the heightmap where its crop is sown (<see cref="PointOfInterestFields.CarveRaise"/>), the same on every
	/// cut, repaint and Regenerate POIs.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Carved from the ground as it was, never on top of the last carve.</b> Fields are laid by their site's builder in the
	/// POI place stage, which runs again on every repaint and Regenerate POIs over ground the last run already ridged. A raise
	/// applied there again would pile ridge on ridge; read from the ridged ground, the field's slope test (7°) would refuse
	/// fields the cut laid, and every draw after them would move. So what a carve raised is kept, sample by sample, as the
	/// terrain data took it (<see cref="GeneratedFieldCarve"/>, as the cliff placer keeps its talus cones), and taken out again
	/// (<see cref="Restore"/>) before anything reads the ground: first thing in a repaint and in Regenerate POIs, and in the
	/// place stage before it replaces the scene's POI root. Every stage then reads the ground exactly as the cut's did before the
	/// first field was laid, lays the same fields, and carves the same ridges from the same ground. The ways' carve keeps the
	/// same promise another way (PathCarver: its level read from ground past its shoulders, which it never touches); a field
	/// cannot, since its ridges stand on the ground beneath them rather than at a level of their own.
	/// </para>
	/// <para>
	/// <b>Everything that stands on the ground follows it.</b> The carve runs before the field's crop, furrows and fence are laid
	/// (on its own ground: the furrows and the fence line are not raised) and before the props, collision and NavMesh are baked
	/// (SceneGenerator.BakePropsAndNavMesh, after the place stage), and the terrain collider is the heightmap itself. A tree
	/// standing in a field keeps its height over the ground, as the talus cones' trees do. The client's grass and detail bake is
	/// made after the cut and the repaint (SceneTerrainEvents).
	/// </para>
	/// </remarks>
	public static class PointOfInterestFieldCarve
	{
		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>
		/// The samples of one tile's heightmap a field raises, and by how much, metres (pure): every sample, row by row, whose
		/// <see cref="PointOfInterestFields.CarveRaise"/> is above nothing, the profile averaged over one sample's spacing. The
		/// tile's samples stand at <paramref name="origin"/> + (column, row) × size ÷ (resolution − 1), world xz. The answer
		/// depends on nothing but the field and the grid, so two tiles sharing an edge raise it alike.
		/// </summary>
		public static int Plan(Vector2 centre, float yaw, Vector2 size, BiomeFieldSurface surface, Vector2 origin, Vector2 tileSize, int resolution,
			List<int> samples, List<float> metres)
		{
			samples.Clear();
			metres.Clear();
			if (!BiomeFieldSurfaces.Carved(surface) || resolution < 2 || !(tileSize.x > 0f) || !(tileSize.y > 0f))
			{
				return 0;
			}
			float stepX = tileSize.x / (resolution - 1), stepZ = tileSize.y / (resolution - 1);
			float reach = 0.5f * size.magnitude;
			int i0 = Mathf.Clamp(Mathf.FloorToInt((centre.x - reach - origin.x) / stepX), 0, resolution - 1);
			int i1 = Mathf.Clamp(Mathf.CeilToInt((centre.x + reach - origin.x) / stepX), 0, resolution - 1);
			int j0 = Mathf.Clamp(Mathf.FloorToInt((centre.y - reach - origin.y) / stepZ), 0, resolution - 1);
			int j1 = Mathf.Clamp(Mathf.CeilToInt((centre.y + reach - origin.y) / stepZ), 0, resolution - 1);
			if (centre.x + reach < origin.x || centre.x - reach > origin.x + tileSize.x || centre.y + reach < origin.y || centre.y - reach > origin.y + tileSize.y)
			{
				return 0;
			}
			for (int j = j0; j <= j1; j++)
			{
				for (int i = i0; i <= i1; i++)
				{
					// By the share across the tile, so its last column stands exactly at origin + size: where the next tile's first does.
					float x = origin.x + tileSize.x * (i / (float)(resolution - 1)), z = origin.y + tileSize.y * (j / (float)(resolution - 1));
					float raise = PointOfInterestFields.CarveRaise(centre, yaw, size, surface, x, z, stepX);
					if (raise > 0f)
					{
						samples.Add(j * resolution + i);
						metres.Add(raise);
					}
				}
			}
			return samples.Count;
		}

		/// <summary>The bounds of a list of sample indices on a grid <paramref name="resolution"/> wide: first column and row, and how many of each.</summary>
		public static void Bounds(List<int> samples, int resolution, out int x0, out int z0, out int width, out int depth)
		{
			int x1 = -1, z1 = -1;
			x0 = z0 = int.MaxValue;
			foreach (int s in samples)
			{
				int x = s % resolution, z = s / resolution;
				x0 = Mathf.Min(x0, x);
				z0 = Mathf.Min(z0, z);
				x1 = Mathf.Max(x1, x);
				z1 = Mathf.Max(z1, z);
			}
			if (x1 < 0)
			{
				x0 = z0 = width = depth = 0;
				return;
			}
			width = x1 - x0 + 1;
			depth = z1 - z0 + 1;
		}

		/// <summary>
		/// Raises the samples of a block of normalised heights (rows × columns, its first sample at column <paramref name="x0"/>,
		/// row <paramref name="z0"/> of a grid <paramref name="resolution"/> wide) by <paramref name="metres"/> over a height range
		/// of <paramref name="heightScale"/> metres, held within 0 … 1 (pure).
		/// </summary>
		public static void Raise(float[,] heights, int x0, int z0, int resolution, List<int> samples, List<float> metres, float heightScale)
		{
			float scale = heightScale > 0f ? 1f / heightScale : 0f;
			for (int k = 0; k < samples.Count; k++)
			{
				int row = samples[k] / resolution - z0, column = samples[k] % resolution - x0;
				heights[row, column] = Mathf.Clamp01(heights[row, column] + metres[k] * scale);
			}
		}

		/// <summary>
		/// What a raise left in a block of heights, sample by sample (pure): each sample's height after less its height before,
		/// normalised, as the terrain data took it, so taking it out (<see cref="Lower"/>) gives back exactly what was there.
		/// </summary>
		public static List<float> Deltas(float[,] before, float[,] after, int x0, int z0, int resolution, List<int> samples)
		{
			var deltas = new List<float>(samples.Count);
			foreach (int s in samples)
			{
				int row = s / resolution - z0, column = s % resolution - x0;
				deltas.Add(after[row, column] - before[row, column]);
			}
			return deltas;
		}

		/// <summary>Takes a carve's <paramref name="deltas"/> back out of a block of normalised heights, held within 0 … 1 (pure).</summary>
		public static void Lower(float[,] heights, int x0, int z0, int resolution, List<int> samples, List<float> deltas)
		{
			for (int k = 0; k < samples.Count && k < deltas.Count; k++)
			{
				int row = samples[k] / resolution - z0, column = samples[k] % resolution - x0;
				heights[row, column] = Mathf.Clamp01(heights[row, column] - deltas[k]);
			}
		}

		// ── The terrain ───────────────────────────────────────────────

		/// <summary>
		/// Carves one field into every tile it crosses and records what it raised for <see cref="Restore"/>; returns the samples
		/// raised. Nothing for a surface not carved or a site with no terrain (a harness).
		/// </summary>
		public static int Carve(PointOfInterestSiteContext context, Vector2 centre, float yaw, Vector2 size, BiomeFieldSurface surface)
		{
			Terrain[,] terrains = context != null ? context.Terrains : null;
			if (terrains == null || !BiomeFieldSurfaces.Carved(surface))
			{
				return 0;
			}
			GeneratedFieldCarve record = null;
			var samples = new List<int>();
			var metres = new List<float>();
			int raised = 0;
			for (int tz = 0; tz < terrains.GetLength(1); tz++)
			{
				for (int tx = 0; tx < terrains.GetLength(0); tx++)
				{
					Terrain terrain = terrains[tx, tz];
					TerrainData data = terrain != null ? terrain.terrainData : null;
					if (data == null)
					{
						continue;
					}
					int res = data.heightmapResolution;
					Vector3 origin = terrain.transform.position, tileSize = data.size;
					if (Plan(centre, yaw, size, surface, new Vector2(origin.x, origin.z), new Vector2(tileSize.x, tileSize.z), res, samples, metres) == 0)
					{
						continue;
					}
					Bounds(samples, res, out int x0, out int z0, out int w, out int d);
					float[,] before = data.GetHeights(x0, z0, w, d);
					float[,] heights = (float[,])before.Clone();
					Raise(heights, x0, z0, res, samples, metres, tileSize.y);
					Rect area = SampleArea(origin, tileSize, res, x0, z0, w, d);
					TreeInstance[] trees = data.treeInstances;
					float[] under = TreesUnder(data, trees, area, origin, tileSize);
					data.SetHeights(x0, z0, heights);
					float[,] after = data.GetHeights(x0, z0, w, d);
					ReseatTrees(data, trees, under);
					EditorUtility.SetDirty(data);

					record ??= RecordOf(context);
					if (record != null)
					{
						record.Edits.Add(new GeneratedFieldCarve.TileEdit
						{
							TileX = tx,
							TileZ = tz,
							Resolution = res,
							Samples = new List<int>(samples),
							Deltas = Deltas(before, after, x0, z0, res, samples),
						});
						EditorUtility.SetDirty(record);
					}
					raised += samples.Count;
				}
			}
			return raised;
		}

		/// <summary>
		/// The samples of one tile's heightmap a carve to a target ground moves, and by how much, metres, signed (pure; 2026-10-18, I2i,
		/// a paddy's floor and bund): every sample in <paramref name="area"/> (world xz) whose <paramref name="offset"/> (x, z, the
		/// ground there in metres) is not nothing. <paramref name="groundAt"/> gives a sample's ground (column, row) in metres. As
		/// <see cref="Plan"/>, the samples stand by their share across the tile, so two tiles sharing an edge move it alike.
		/// </summary>
		public static int PlanTo(Rect area, Vector2 origin, Vector2 tileSize, int resolution, Func<int, int, float> groundAt,
			Func<float, float, float, float> offset, List<int> samples, List<float> metres)
		{
			samples.Clear();
			metres.Clear();
			if (resolution < 2 || !(tileSize.x > 0f) || !(tileSize.y > 0f) || offset == null || groundAt == null)
			{
				return 0;
			}
			if (area.xMax < origin.x || area.xMin > origin.x + tileSize.x || area.yMax < origin.y || area.yMin > origin.y + tileSize.y)
			{
				return 0;
			}
			float stepX = tileSize.x / (resolution - 1), stepZ = tileSize.y / (resolution - 1);
			int i0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - origin.x) / stepX), 0, resolution - 1);
			int i1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - origin.x) / stepX), 0, resolution - 1);
			int j0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - origin.y) / stepZ), 0, resolution - 1);
			int j1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - origin.y) / stepZ), 0, resolution - 1);
			for (int j = j0; j <= j1; j++)
			{
				for (int i = i0; i <= i1; i++)
				{
					float x = origin.x + tileSize.x * (i / (float)(resolution - 1)), z = origin.y + tileSize.y * (j / (float)(resolution - 1));
					float move = offset(x, z, groundAt(i, j));
					if (Mathf.Abs(move) > 1e-5f && !float.IsNaN(move))
					{
						samples.Add(j * resolution + i);
						metres.Add(move);
					}
				}
			}
			return samples.Count;
		}

		/// <summary>
		/// Carves the ground in <paramref name="area"/> to a target (2026-10-18, I2i: a paddy's level floor and its bund) on every tile
		/// it crosses, <paramref name="offset"/> giving the move at (x, z, the ground there), and records the moves for
		/// <see cref="Restore"/> exactly as a ridge-and-furrow carve's (raised or lowered alike). Returns the samples moved.
		/// </summary>
		public static int CarveTo(PointOfInterestSiteContext context, Rect area, Func<float, float, float, float> offset)
		{
			Terrain[,] terrains = context != null ? context.Terrains : null;
			if (terrains == null || offset == null)
			{
				return 0;
			}
			GeneratedFieldCarve record = null;
			var samples = new List<int>();
			var metres = new List<float>();
			int moved = 0;
			for (int tz = 0; tz < terrains.GetLength(1); tz++)
			{
				for (int tx = 0; tx < terrains.GetLength(0); tx++)
				{
					Terrain terrain = terrains[tx, tz];
					TerrainData data = terrain != null ? terrain.terrainData : null;
					if (data == null)
					{
						continue;
					}
					int res = data.heightmapResolution;
					Vector3 origin = terrain.transform.position, tileSize = data.size;
					var flatOrigin = new Vector2(origin.x, origin.z);
					var flatSize = new Vector2(tileSize.x, tileSize.z);
					// The ground at the block's samples, read once: the whole tile's rows the area spans.
					float stepX = tileSize.x / (res - 1), stepZ = tileSize.z / (res - 1);
					int bx0 = Mathf.Clamp(Mathf.FloorToInt((area.xMin - origin.x) / stepX), 0, res - 1), bx1 = Mathf.Clamp(Mathf.CeilToInt((area.xMax - origin.x) / stepX), 0, res - 1);
					int bz0 = Mathf.Clamp(Mathf.FloorToInt((area.yMin - origin.z) / stepZ), 0, res - 1), bz1 = Mathf.Clamp(Mathf.CeilToInt((area.yMax - origin.z) / stepZ), 0, res - 1);
					if (area.xMax < origin.x || area.xMin > origin.x + tileSize.x || area.yMax < origin.z || area.yMin > origin.z + tileSize.z || bx1 < bx0 || bz1 < bz0)
					{
						continue;
					}
					float[,] block = data.GetHeights(bx0, bz0, bx1 - bx0 + 1, bz1 - bz0 + 1);
					if (PlanTo(area, flatOrigin, flatSize, res, (i, j) => origin.y + block[j - bz0, i - bx0] * tileSize.y, offset, samples, metres) == 0)
					{
						continue;
					}
					Bounds(samples, res, out int x0, out int z0, out int w, out int d);
					float[,] before = data.GetHeights(x0, z0, w, d);
					float[,] heights = (float[,])before.Clone();
					Raise(heights, x0, z0, res, samples, metres, tileSize.y);
					Rect touched = SampleArea(origin, tileSize, res, x0, z0, w, d);
					TreeInstance[] trees = data.treeInstances;
					float[] under = TreesUnder(data, trees, touched, origin, tileSize);
					data.SetHeights(x0, z0, heights);
					float[,] after = data.GetHeights(x0, z0, w, d);
					ReseatTrees(data, trees, under);
					EditorUtility.SetDirty(data);

					record ??= RecordOf(context);
					if (record != null)
					{
						record.Edits.Add(new GeneratedFieldCarve.TileEdit
						{
							TileX = tx,
							TileZ = tz,
							Resolution = res,
							Samples = new List<int>(samples),
							Deltas = Deltas(before, after, x0, z0, res, samples),
						});
						EditorUtility.SetDirty(record);
					}
					moved += samples.Count;
				}
			}
			return moved;
		}

		/// <summary>
		/// Takes every field carve recorded in <paramref name="scene"/> back out of its tiles (the scene's tile grid,
		/// <paramref name="terrains"/>), newest first, and drops the records: the ground is then as the first field found it.
		/// Returns the samples lowered. A record naming a tile that is missing or of another resolution is dropped with a note
		/// (its ridges stay in that tile's ground until it is cut again).
		/// </summary>
		public static int Restore(Scene scene, Terrain[,] terrains, List<string> notes)
		{
			if (!scene.IsValid() || terrains == null)
			{
				return 0;
			}
			var records = new List<GeneratedFieldCarve>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				records.AddRange(root.GetComponentsInChildren<GeneratedFieldCarve>(true));
			}
			int lowered = 0, skipped = 0;
			for (int r = records.Count - 1; r >= 0; r--)
			{
				GeneratedFieldCarve record = records[r];
				for (int e = record.Edits.Count - 1; e >= 0; e--)
				{
					GeneratedFieldCarve.TileEdit edit = record.Edits[e];
					Terrain terrain = edit.TileX >= 0 && edit.TileZ >= 0 && edit.TileX < terrains.GetLength(0) && edit.TileZ < terrains.GetLength(1)
						? terrains[edit.TileX, edit.TileZ] : null;
					TerrainData data = terrain != null ? terrain.terrainData : null;
					if (data == null || data.heightmapResolution != edit.Resolution || edit.Samples.Count != edit.Deltas.Count || edit.Samples.Count == 0)
					{
						skipped += edit.Samples.Count;
						continue;
					}
					int res = data.heightmapResolution;
					Bounds(edit.Samples, res, out int x0, out int z0, out int w, out int d);
					if (x0 + w > res || z0 + d > res)
					{
						skipped += edit.Samples.Count;
						continue;
					}
					Vector3 origin = terrain.transform.position, tileSize = data.size;
					float[,] heights = data.GetHeights(x0, z0, w, d);
					Lower(heights, x0, z0, res, edit.Samples, edit.Deltas);
					TreeInstance[] trees = data.treeInstances;
					float[] under = TreesUnder(data, trees, SampleArea(origin, tileSize, res, x0, z0, w, d), origin, tileSize);
					data.SetHeights(x0, z0, heights);
					ReseatTrees(data, trees, under);
					EditorUtility.SetDirty(data);
					lowered += edit.Samples.Count;
				}
				UnityEngine.Object.DestroyImmediate(record.gameObject);
			}
			if (lowered > 0)
			{
				notes?.Add($"Fields: the last run's ridge and furrow taken out of {lowered:N0} heightmap sample(s) before anything read the ground (carved again as the fields are laid).");
			}
			if (skipped > 0)
			{
				notes?.Add($"Fields: {skipped:N0} recorded ridge-and-furrow sample(s) name a tile that is gone or of another resolution and were left as they are; re-cut the scene to clear them.");
			}
			return lowered;
		}

		/// <summary>The scene's carve record under its POI root (the site's parent), made on first use (editor-only, left out of builds).</summary>
		private static GeneratedFieldCarve RecordOf(PointOfInterestSiteContext context)
		{
			Transform host = context.Root != null ? context.Root.parent != null ? context.Root.parent : context.Root : null;
			if (host == null)
			{
				return null;
			}
			Transform existing = host.Find(GeneratedFieldCarve.ObjectName);
			if (existing != null && existing.TryGetComponent(out GeneratedFieldCarve found))
			{
				return found;
			}
			var child = new GameObject(GeneratedFieldCarve.ObjectName) { tag = "EditorOnly" };
			child.transform.SetParent(host, false);
			return child.AddComponent<GeneratedFieldCarve>();
		}

		/// <summary>The world xz rectangle a block of samples covers.</summary>
		private static Rect SampleArea(Vector3 origin, Vector3 tileSize, int res, int x0, int z0, int w, int d)
		{
			float stepX = tileSize.x / (res - 1), stepZ = tileSize.z / (res - 1);
			return new Rect(origin.x + (x0 - 1) * stepX, origin.z + (z0 - 1) * stepZ, (w + 1) * stepX, (d + 1) * stepZ);
		}

		/// <summary>The ground under each tree standing in <paramref name="area"/> (NaN for the rest), normalised, before an edit.</summary>
		private static float[] TreesUnder(TerrainData data, TreeInstance[] trees, Rect area, Vector3 origin, Vector3 tileSize)
		{
			var under = new float[trees.Length];
			for (int t = 0; t < trees.Length; t++)
			{
				float x = origin.x + trees[t].position.x * tileSize.x, z = origin.z + trees[t].position.z * tileSize.z;
				under[t] = area.Contains(new Vector2(x, z)) ? data.GetInterpolatedHeight(trees[t].position.x, trees[t].position.z) : float.NaN;
			}
			return under;
		}

		/// <summary>
		/// Each tree that stood in the edit keeps its height over the ground, as the talus cones' do (CliffPlacer.LowerCones): the
		/// scatter sinks every tree by design, and a snap would stand it on the surface instead.
		/// </summary>
		private static void ReseatTrees(TerrainData data, TreeInstance[] trees, float[] under)
		{
			bool moved = false;
			for (int t = 0; t < trees.Length; t++)
			{
				if (float.IsNaN(under[t]))
				{
					continue;
				}
				float by = data.GetInterpolatedHeight(trees[t].position.x, trees[t].position.z) - under[t];
				if (by != 0f)
				{
					trees[t].position.y = Mathf.Clamp01(trees[t].position.y + by / data.size.y);
					moved = true;
				}
			}
			if (moved)
			{
				data.SetTreeInstances(trees, false);
			}
		}
	}
}
#endif
