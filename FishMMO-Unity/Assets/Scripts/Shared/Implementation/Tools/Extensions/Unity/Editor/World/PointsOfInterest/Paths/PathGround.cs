#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>A scene's way segments in 16 m buckets, for the questions asked of every texel or heightmap sample.</summary>
	/// <remarks>Read only once built, so it is safe from the scatter's worker threads.</remarks>
	public sealed class PathSegmentIndex
	{
		private const float Bucket = 16f;
		public readonly IReadOnlyList<ScenePath> Paths;
		private readonly Dictionary<long, List<(int path, int point)>> buckets = new Dictionary<long, List<(int, int)>>();

		/// <param name="reach">How far past a segment's points it is filed, metres (its widest question).</param>
		public PathSegmentIndex(IReadOnlyList<ScenePath> paths, Func<ScenePath, float> reach)
		{
			Paths = paths ?? Array.Empty<ScenePath>();
			for (int p = 0; p < Paths.Count; p++)
			{
				ScenePath path = Paths[p];
				if (path == null || path.Count < 2)
				{
					continue;
				}
				float r = reach(path);
				for (int i = 1; i < path.Count; i++)
				{
					Vector3 a = path.Points[i - 1], b = path.Points[i];
					int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r) / Bucket), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + r) / Bucket);
					int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - r) / Bucket), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + r) / Bucket);
					for (int z = z0; z <= z1; z++)
					{
						for (int x = x0; x <= x1; x++)
						{
							long key = ((long)x << 32) ^ (uint)z;
							if (!buckets.TryGetValue(key, out List<(int, int)> list))
							{
								list = new List<(int, int)>();
								buckets[key] = list;
							}
							list.Add((p, i));
						}
					}
				}
			}
		}

		/// <summary>The segments filed where (x, z) lies: (path index, index of the segment's second point).</summary>
		public List<(int path, int point)> Near(float x, float z)
		{
			long key = ((long)Mathf.FloorToInt(x / Bucket) << 32) ^ (uint)Mathf.FloorToInt(z / Bucket);
			return buckets.TryGetValue(key, out List<(int, int)> list) ? list : null;
		}

		/// <summary>Distance from (x, z) to a segment in the ground plane, and how far along it (0 … 1) the nearest point is.</summary>
		public float Distance(int path, int point, float x, float z, out float t)
		{
			Vector3 a = Paths[path].Points[point - 1], b = Paths[path].Points[point];
			float abx = b.x - a.x, abz = b.z - a.z;
			float length2 = abx * abx + abz * abz;
			t = length2 > 1e-8f ? Mathf.Clamp01(((x - a.x) * abx + (z - a.z) * abz) / length2) : 0f;
			float dx = a.x + abx * t - x, dz = a.z + abz * t - z;
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		public float HalfWidthAt(int path, int point, float t) => Mathf.Lerp(Paths[path].HalfWidth[point - 1], Paths[path].HalfWidth[point], t);
	}

	/// <summary>
	/// Where the scatter, the cliffs and the river rocks keep off the ways: trees and rocks off a way and its clearance,
	/// details (grass, flowers) off the trodden width of a road; a trail's own grass is thinned by the shaders instead.
	/// </summary>
	public sealed class PathExclusion
	{
		private readonly PathSegmentIndex index;

		public PathExclusion(IReadOnlyList<ScenePath> paths)
		{
			index = new PathSegmentIndex(paths, p => p.MaxHalfWidth + ScenePathStyle.For(p.Class).Clearance);
		}

		/// <summary>True where nothing that stands (trees, rocks, cliffs) may be placed.</summary>
		public bool Trees(float x, float z)
		{
			List<(int path, int point)> near = index.Near(x, z);
			if (near == null)
			{
				return false;
			}
			foreach ((int path, int point) in near)
			{
				float d = index.Distance(path, point, x, z, out float t);
				if (d < index.HalfWidthAt(path, point, t) + ScenePathStyle.For(index.Paths[path].Class).Clearance)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True where details may not grow: a kept road's trodden width.</summary>
		public bool Details(float x, float z)
		{
			List<(int path, int point)> near = index.Near(x, z);
			if (near == null)
			{
				return false;
			}
			foreach ((int path, int point) in near)
			{
				if (!ScenePathStyle.For(index.Paths[path].Class).ClearsDetails)
				{
					continue;
				}
				float d = index.Distance(path, point, x, z, out float t);
				if (d < index.HalfWidthAt(path, point, t))
				{
					return true;
				}
			}
			return false;
		}
	}

	/// <summary>
	/// Lays the ways into the ground: gently. Each is evened along its length and benched across a hillside, sunk a few
	/// centimetres where it is earth, and blended back into the ground over its shoulders; never cut or built more than its
	/// class allows (a trail follows the ground, a highway levels it).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Measured from the ground beside it.</b> The level a way is laid at is read from the ground past its shoulders on
	/// both sides (the middle of the two, on a hillside the ground under the centre line), which the carve never touches —
	/// so carving again (Regenerate POIs on carved ground) lays it at the same level instead of sinking it deeper.
	/// </para>
	/// <para>
	/// Water, pads, the stretches inside sites and on bridges are left alone: a river's bed is the water's, a pad already
	/// levels its site, a bridge stands on its own.
	/// </para>
	/// </remarks>
	public static class PathCarver
	{
		/// <summary>The level each point of each way is laid at, from the ground beside it.</summary>
		public static float[][] Targets(IReadOnlyList<ScenePath> paths, Func<float, float, float> ground, Func<float, float, bool> wet)
		{
			var targets = new float[paths.Count][];
			for (int p = 0; p < paths.Count; p++)
			{
				ScenePath path = paths[p];
				ScenePathStyle style = ScenePathStyle.For(path.Class);
				int n = path.Count;
				var natural = new float[n];
				for (int i = 0; i < n; i++)
				{
					Vector3 a = path.Points[Mathf.Max(0, i - 1)], b = path.Points[Mathf.Min(n - 1, i + 1)];
					var dir = new Vector2(b.x - a.x, b.z - a.z);
					dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
					var normal = new Vector2(-dir.y, dir.x);
					float off = path.HalfWidth[i] + style.Shoulder + 1f;
					Vector3 c = path.Points[i];
					float lx = c.x + normal.x * off, lz = c.z + normal.y * off, rx = c.x - normal.x * off, rz = c.z - normal.y * off;
					bool lw = wet != null && wet(lx, lz), rw = wet != null && wet(rx, rz);
					natural[i] = lw && rw ? ground(c.x, c.z) : lw ? ground(rx, rz) : rw ? ground(lx, lz) : 0.5f * (ground(lx, lz) + ground(rx, rz));
				}
				// Evened along the way (a gaussian over its points), then held within the class's cut and fill.
				float sigma = Mathf.Max(0.5f, style.SmoothMetres / PathNetworkPlanner.PointSpacing);
				int reach = Mathf.CeilToInt(sigma * 2.5f);
				var target = new float[n];
				for (int i = 0; i < n; i++)
				{
					float sum = 0f, weight = 0f;
					for (int k = Mathf.Max(0, i - reach); k <= Mathf.Min(n - 1, i + reach); k++)
					{
						float w = Mathf.Exp(-0.5f * (k - i) * (k - i) / (sigma * sigma));
						sum += natural[k] * w;
						weight += w;
					}
					float even = weight > 0f ? sum / weight : natural[i];
					target[i] = Mathf.Clamp(even, natural[i] - style.MaxCut, natural[i] + style.MaxCut) - style.Dip;
				}
				targets[p] = target;
			}
			return targets;
		}

		/// <summary>
		/// The height a ground sample takes from the ways near it: each way's level weighted by how far inside its blend the
		/// sample lies, the strongest way's weight deciding how much of the ground gives way. NaN where no way reaches.
		/// </summary>
		public static float Blend(PathSegmentIndex index, float[][] targets, float x, float z, out float weight)
		{
			weight = 0f;
			List<(int path, int point)> near = index.Near(x, z);
			if (near == null)
			{
				return float.NaN;
			}
			float sum = 0f, sumW = 0f;
			foreach ((int path, int point) in near)
			{
				ScenePath way = index.Paths[path];
				ScenePathPointFlags fa = way.FlagsAt(point - 1), fb = way.FlagsAt(point);
				if (((fa | fb) & ScenePathPointFlags.InSite) != 0 || (fa & fb & ScenePathPointFlags.Bridge) != 0)
				{
					continue;
				}
				ScenePathStyle style = ScenePathStyle.For(way.Class);
				float d = index.Distance(path, point, x, z, out float t);
				float half = index.HalfWidthAt(path, point, t);
				if (d >= half + style.Shoulder)
				{
					continue;
				}
				float w = d <= half ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (d - half) / Mathf.Max(0.1f, style.Shoulder));
				float level = Mathf.Lerp(targets[path][point - 1], targets[path][point], t);
				sum += w * level;
				sumW += w;
				weight = Mathf.Max(weight, w);
			}
			return sumW > 1e-5f ? sum / sumW : float.NaN;
		}

		/// <summary>Carves every way into the tiles; returns the heightmap samples changed.</summary>
		public static int Carve(Terrain[,] terrains, TerrainTilePlan tiles, IReadOnlyList<ScenePath> paths, Func<float, float, float> ground,
			Func<float, float, bool> wet, IReadOnlyList<PointOfInterestPad> pads)
		{
			if (paths == null || paths.Count == 0)
			{
				return 0;
			}
			float[][] targets = Targets(paths, ground, wet);
			var index = new PathSegmentIndex(paths, p => p.MaxHalfWidth + ScenePathStyle.For(p.Class).Shoulder);
			float minX = float.PositiveInfinity, minZ = float.PositiveInfinity, maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
			foreach (ScenePath path in paths)
			{
				float r = path.MaxHalfWidth + ScenePathStyle.For(path.Class).Shoulder;
				foreach (Vector3 p in path.Points)
				{
					minX = Mathf.Min(minX, p.x - r);
					minZ = Mathf.Min(minZ, p.z - r);
					maxX = Mathf.Max(maxX, p.x + r);
					maxZ = Mathf.Max(maxZ, p.z + r);
				}
			}
			if (minX > maxX)
			{
				return 0;
			}
			bool InPad(float x, float z)
			{
				if (pads == null)
				{
					return false;
				}
				foreach (PointOfInterestPad pad in pads)
				{
					float dx = x - pad.Centre.x, dz = z - pad.Centre.z;
					if (dx * dx + dz * dz < pad.Radius * pad.Radius)
					{
						return true;
					}
				}
				return false;
			}
			return PointOfInterestTerrain.EditHeights(terrains, tiles, Rect.MinMaxRect(minX, minZ, maxX, maxZ), (x, z, h) =>
			{
				float level = Blend(index, targets, x, z, out float weight);
				if (float.IsNaN(level) || weight <= 0f || (wet != null && wet(x, z)) || InPad(x, z))
				{
					return h;
				}
				return Mathf.Lerp(h, level, weight);
			});
		}
	}
}
#endif
