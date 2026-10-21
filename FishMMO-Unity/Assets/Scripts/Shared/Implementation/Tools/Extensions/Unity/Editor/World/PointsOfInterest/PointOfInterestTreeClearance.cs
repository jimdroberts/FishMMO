#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Takes the scatter's trees and bushes (the terrain's tree instances) off the sites' building floors: the floors the
	/// grass is kept out of (<see cref="ScenePathPointFlags.Floor"/> yards, PropsFeature and PointOfInterestFarmsteads), so
	/// nothing grows up through a house any more than a blade does (2026-10-19).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scatter keeps its trees out of each site's planned keep-out only (<see cref="PointOfInterestStage.Excludes"/>),
	/// a disc decided before a building is laid; the buildings are laid after the scatter, across a town or a farmstead
	/// that reaches past it. The grass is thinned per blade by the path field the floors are written to, on the client;
	/// a tree is a scene prop once baked (ScenePropBaker.BakeTerrainTrees), so it is taken off the terrain here, between
	/// the sites' placing and that bake.
	/// </para>
	/// <para>
	/// A plant is kept out by its crown's reach (<see cref="TerrainScatter.FootprintRadius"/> at its width scale), held to
	/// <see cref="MinClearanceMetres"/> … <see cref="MaxClearanceMetres"/>: a bush's whole crown stands outside the walls,
	/// a great tree's trunk a few paces off them with its crown over the eaves. A ruin has no floors, so its trees stay.
	/// </para>
	/// </remarks>
	public static class PointOfInterestTreeClearance
	{
		/// <summary>The least a plant's stem stands off a building's plan, metres: a trunk's girth and the eaves' drip.</summary>
		public const float MinClearanceMetres = 0.5f;

		/// <summary>The most a plant is kept off a building's plan, metres: a great tree's crown may overhang the roof.</summary>
		public const float MaxClearanceMetres = 2.5f;

		/// <summary>The side of the grid the floors are bucketed in, metres.</summary>
		private const float CellMetres = 16f;

		/// <summary>One building floor: a segment between two Floor points, squared or round-ended as the path field draws it.</summary>
		public struct Floor
		{
			public Vector2 A, B;
			public float HalfA, HalfB;
			public bool Squared;
		}

		/// <summary>The building floors among <paramref name="paths"/>: every segment whose two ends are both Floor points.</summary>
		public static List<Floor> FloorsOf(IReadOnlyList<ScenePath> paths)
		{
			var floors = new List<Floor>();
			if (paths == null)
			{
				return floors;
			}
			foreach (ScenePath path in paths)
			{
				if (path == null || path.Points == null || path.Points.Length == 0)
				{
					continue;
				}
				int n = path.Points.Length;
				for (int i = 0; i < Mathf.Max(1, n - 1); i++)
				{
					int ib = Mathf.Min(i + 1, n - 1);
					ScenePathPointFlags both = path.FlagsAt(i) & path.FlagsAt(ib);
					if ((both & ScenePathPointFlags.Floor) == 0)
					{
						continue;
					}
					var a = new Vector2(path.Points[i].x, path.Points[i].z);
					var b = new Vector2(path.Points[ib].x, path.Points[ib].z);
					floors.Add(new Floor
					{
						A = a,
						B = b,
						HalfA = HalfAt(path, i),
						HalfB = HalfAt(path, ib),
						// A rectangle needs a direction: one point is drawn round (ScenePathField.Segment).
						Squared = (both & ScenePathPointFlags.Squared) != 0 && Vector2.Distance(a, b) > 0.01f,
					});
				}
			}
			return floors;
		}

		private static float HalfAt(ScenePath path, int i) => path.HalfWidth != null && i < path.HalfWidth.Length ? path.HalfWidth[i] : 0f;

		/// <summary>
		/// A point's signed distance from a floor's edge, metres: negative inside it. The same shape the path field stamps
		/// the floor's bare ground with (ScenePathField.Segment): a rectangle from end midpoint to end midpoint when squared,
		/// else a capsule (a circle for one point).
		/// </summary>
		public static float Edge(in Floor floor, Vector2 p)
		{
			Vector2 ab = floor.B - floor.A;
			float length2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
			float length = Mathf.Sqrt(length2);
			float along = Vector2.Dot(p - floor.A, ab) / length2;
			float t = Mathf.Clamp01(along);
			float half = Mathf.Lerp(floor.HalfA, floor.HalfB, t);
			if (floor.Squared)
			{
				float overEnd = Mathf.Max(-along, along - 1f) * length;
				float across = Mathf.Abs((p.x - floor.A.x) * ab.y - (p.y - floor.A.y) * ab.x) / length - half;
				float ox = Mathf.Max(overEnd, 0f), oz = Mathf.Max(across, 0f);
				return Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(overEnd, across), 0f);
			}
			return Vector2.Distance(floor.A + ab * t, p) - half;
		}

		/// <summary>How far off a building's plan a plant of crown reach <paramref name="crownMetres"/> stands, metres.</summary>
		public static float Clearance(float crownMetres)
			=> Mathf.Clamp(float.IsNaN(crownMetres) ? 0f : crownMetres, MinClearanceMetres, MaxClearanceMetres);

		/// <summary>
		/// Removes every terrain tree instance standing within its clearance of a building floor in <paramref name="paths"/>,
		/// on every tile. Returns the trees removed.
		/// </summary>
		public static int Clear(Terrain[,] terrains, IReadOnlyList<ScenePath> paths, List<string> notes)
		{
			List<Floor> floors = FloorsOf(paths);
			if (terrains == null || floors.Count == 0)
			{
				return 0;
			}
			// Each floor in every cell its bounds, grown by the most any plant is kept off, cross.
			var cells = new Dictionary<long, List<int>>();
			for (int f = 0; f < floors.Count; f++)
			{
				Floor floor = floors[f];
				float reach = Mathf.Max(floor.HalfA, floor.HalfB) + MaxClearanceMetres;
				int x0 = Cell(Mathf.Min(floor.A.x, floor.B.x) - reach), x1 = Cell(Mathf.Max(floor.A.x, floor.B.x) + reach);
				int z0 = Cell(Mathf.Min(floor.A.y, floor.B.y) - reach), z1 = Cell(Mathf.Max(floor.A.y, floor.B.y) + reach);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						long key = Key(x, z);
						if (!cells.TryGetValue(key, out List<int> list))
						{
							cells[key] = list = new List<int>();
						}
						list.Add(f);
					}
				}
			}

			var crowns = new Dictionary<GameObject, float>();
			int removed = 0;
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				TreeInstance[] trees = data.treeInstances;
				if (trees.Length == 0)
				{
					continue;
				}
				TreePrototype[] kinds = data.treePrototypes;
				var crownOf = new float[kinds.Length];
				for (int k = 0; k < kinds.Length; k++)
				{
					GameObject prefab = kinds[k]?.prefab;
					if (prefab == null)
					{
						continue;
					}
					if (!crowns.TryGetValue(prefab, out float crown))
					{
						crowns[prefab] = crown = TerrainScatter.FootprintRadius(prefab);
					}
					crownOf[k] = crown;
				}
				Vector3 origin = terrain.transform.position, size = data.size;
				var kept = new List<TreeInstance>(trees.Length);
				foreach (TreeInstance tree in trees)
				{
					var at = new Vector2(origin.x + tree.position.x * size.x, origin.z + tree.position.z * size.z);
					float crown = tree.prototypeIndex >= 0 && tree.prototypeIndex < crownOf.Length ? crownOf[tree.prototypeIndex] * tree.widthScale : 0f;
					if (Indoors(floors, cells, at, Clearance(crown)))
					{
						removed++;
						continue;
					}
					kept.Add(tree);
				}
				if (kept.Count != trees.Length)
				{
					data.SetTreeInstances(kept.ToArray(), false);
					terrain.Flush();
					EditorUtility.SetDirty(data);
				}
			}
			if (removed > 0)
			{
				notes?.Add($"Points of interest: {removed:N0} scattered tree(s) and bush(es) taken off {floors.Count:N0} building floor(s).");
			}
			return removed;
		}

		/// <summary>Whether <paramref name="at"/> lies within <paramref name="clearance"/> of any floor bucketed in <paramref name="cells"/>.</summary>
		private static bool Indoors(List<Floor> floors, Dictionary<long, List<int>> cells, Vector2 at, float clearance)
		{
			if (!cells.TryGetValue(Key(Cell(at.x), Cell(at.y)), out List<int> near))
			{
				return false;
			}
			foreach (int f in near)
			{
				if (Edge(floors[f], at) < clearance)
				{
					return true;
				}
			}
			return false;
		}

		private static int Cell(float metres) => Mathf.FloorToInt(metres / CellMetres);

		private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
	}
}
#endif
