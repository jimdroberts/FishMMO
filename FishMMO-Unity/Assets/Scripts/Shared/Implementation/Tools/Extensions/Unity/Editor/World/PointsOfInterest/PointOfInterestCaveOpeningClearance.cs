#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Keeps the ground round every cave opening bare of what would stand over it (2026-10-27, Moron 2: "we need to ensure they
	/// are proper cave entrances/holes and the ground details don't overlap the hole itself"): every detail layer (grass, flowers,
	/// ferns, litter, scattered stones and bushes) emptied within <see cref="DetailMargin"/> of an opening's edge, and every
	/// scattered tree, bush and boulder (the terrain's tree instances) whose crown would reach within <see cref="TreeMargin"/> of
	/// it taken off. An opening is every hole the cut made at a cave's mouths, its skylights and its root windows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why after the sites.</b> The cut empties the detail cells under a hole as it cuts it (PointOfInterestTerrain.ClearDetailsInHoles),
	/// and the scatter skips the hole cells themselves; neither keeps a margin, and the sites' own growth (a cave mouth's bushes,
	/// ferns and ivy, a skylight's rim, a herb grove) is written after the holes, into the cells round them. A plant rooted on the
	/// edge leans or spreads out over the opening, and the client's 2 m density texels and scatter blocks spread what the cells
	/// beside an opening hold up to its edge. So this runs once every site has written its ground, where the enclosures' and the
	/// buildings' clearances run, before the trees are baked into props and the detail maps are read for the client.
	/// </para>
	/// <para>
	/// <b>Its own mask.</b> The opening margin is this class's alone (<see cref="OpeningIndex"/>): the buildings' and the enclosures'
	/// clearances keep their own. The client keeps a smaller margin round every hole besides (TerrainHoleMask.PlantClearMetres), so
	/// what a 2 m block still spreads toward an opening never roots on its edge.
	/// </para>
	/// </remarks>
	public static class PointOfInterestCaveOpeningClearance
	{
		/// <summary>How far past an opening's edge no detail cell keeps anything, metres: a fern's or a bush's spread and a scatter block's reach.</summary>
		public const float DetailMargin = 2.5f;

		/// <summary>How far past an opening's edge a scattered tree's, bush's or boulder's crown is kept, metres.</summary>
		public const float TreeMargin = 1f;

		/// <summary>The most a crown is reckoned to reach, metres: a great tree's trunk stands a few paces off an opening, its crown may overhang.</summary>
		public const float MaxCrownMetres = 3f;

		/// <summary>How far round a mouth's floor its holes are looked for, metres past its zone and bore (CaveSystem.HoleCells' own reach).</summary>
		private const float MouthReach = 10f;

		/// <summary>
		/// The opening cells (their centres) and the reach queries ask within: bucketed so the cut's millions of detail cells
		/// ask it cheaply. Pure; read only once built.
		/// </summary>
		public sealed class OpeningIndex
		{
			private const float Bucket = 8f;
			private readonly Dictionary<long, List<Vector2>> buckets = new Dictionary<long, List<Vector2>>();

			/// <summary>Half the diagonal of an opening cell's square, metres: a query's reach is to its edge, not its centre.</summary>
			public readonly float CellHalfDiagonal;

			/// <summary>The opening cells' bounds (empty when there are none).</summary>
			public Rect Bounds { get; private set; }

			public int Count { get; private set; }

			public OpeningIndex(IEnumerable<Vector2> cells, float cellMetres)
			{
				CellHalfDiagonal = 0.70710678f * Mathf.Max(0f, cellMetres);
				var seen = new HashSet<long>();
				float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
				foreach (Vector2 c in cells)
				{
					// The same cell found round two mouths counts once.
					long id = ((long)Mathf.RoundToInt(c.x * 8f) << 32) ^ (uint)Mathf.RoundToInt(c.y * 8f);
					if (!seen.Add(id))
					{
						continue;
					}
					long key = Key(Mathf.FloorToInt(c.x / Bucket), Mathf.FloorToInt(c.y / Bucket));
					if (!buckets.TryGetValue(key, out List<Vector2> list))
					{
						buckets[key] = list = new List<Vector2>();
					}
					list.Add(c);
					minX = Mathf.Min(minX, c.x);
					minZ = Mathf.Min(minZ, c.y);
					maxX = Mathf.Max(maxX, c.x);
					maxZ = Mathf.Max(maxZ, c.y);
					Count++;
				}
				Bounds = Count > 0 ? Rect.MinMaxRect(minX, minZ, maxX, maxZ) : default;
			}

			/// <summary>Whether <paramref name="p"/> (east, north) lies within <paramref name="reach"/> metres of an opening cell's square.</summary>
			public bool Within(Vector2 p, float reach)
			{
				if (Count == 0)
				{
					return false;
				}
				float r = reach + CellHalfDiagonal;
				float most = r * r;
				int x0 = Mathf.FloorToInt((p.x - r) / Bucket), x1 = Mathf.FloorToInt((p.x + r) / Bucket);
				int z0 = Mathf.FloorToInt((p.y - r) / Bucket), z1 = Mathf.FloorToInt((p.y + r) / Bucket);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						if (!buckets.TryGetValue(Key(x, z), out List<Vector2> list))
						{
							continue;
						}
						foreach (Vector2 c in list)
						{
							if ((c - p).sqrMagnitude <= most)
							{
								return true;
							}
						}
					}
				}
				return false;
			}

			private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
		}

		/// <summary>
		/// The cells the cut holed at every cave's openings (its mouths, skylights and root windows, as stored in
		/// <paramref name="asset"/>'s cave shapes), read back from the tiles round each: the holes a canyon wall cut further off are
		/// not a cave's.
		/// </summary>
		public static OpeningIndex Openings(Terrain[,] terrains, TerrainTilePlan tiles, ScenePointsOfInterest asset)
		{
			var cells = new List<Vector2>();
			float step = 1f;
			if (terrains != null && asset?.Shapes != null && CaveShaper.HoleGrid(terrains, tiles, out step, out _, out _))
			{
				foreach (PointOfInterestShape shape in asset.Shapes)
				{
					if (shape == null || shape.Shaper != CaveShaper.ShaperName || shape.Values == null || CaveShaper.IsLink(shape.Values, out _))
					{
						continue;
					}
					// A joined site's mouths are its host's, stored with the host's system.
					foreach (CaveMouth mouth in CaveSystem.MouthsFromValues(shape.Values))
					{
						Vector3 at = shape.Position + mouth.Floor;
						float reach = mouth.Zone + 2f * mouth.Radius + MouthReach;
						cells.AddRange(PointOfInterestTerrain.HolesIn(terrains, tiles, new Rect(at.x - reach, at.z - reach, 2f * reach, 2f * reach)));
						if (mouth.Kind == CaveMouthKind.Skylight)
						{
							// A skylight's (or a root window's) opening is on the ground over its floor point, off to the side as its shaft leans.
							Vector3 top = shape.Position + CaveSystem.SkylightOpening(mouth);
							float r = mouth.Radius * (1f + CaveSystem.SkylightFlare) + mouth.Collar + MouthReach;
							cells.AddRange(PointOfInterestTerrain.HolesIn(terrains, tiles, new Rect(top.x - r, top.z - r, 2f * r, 2f * r)));
						}
					}
				}
			}
			return new OpeningIndex(cells, step);
		}

		/// <summary>How far a plant of crown reach <paramref name="crownMetres"/> is kept off an opening's edge, metres.</summary>
		public static float TreeClearance(float crownMetres)
			=> TreeMargin + Mathf.Clamp(float.IsNaN(crownMetres) ? 0f : crownMetres, 0f, MaxCrownMetres);

		/// <summary>
		/// Whether a detail cell of side <paramref name="cellMetres"/> centred at <paramref name="centre"/> keeps nothing: a plant
		/// anywhere in it would stand within <see cref="DetailMargin"/> of an opening's edge.
		/// </summary>
		public static bool DetailCellCleared(OpeningIndex openings, Vector2 centre, float cellMetres)
			=> openings != null && openings.Within(centre, DetailMargin + 0.70710678f * cellMetres);

		/// <summary>
		/// Empties the detail cells and takes off the scattered trees round every cave opening (see the class). Returns the detail
		/// cells emptied; the trees removed are noted with them.
		/// </summary>
		public static int Clear(Terrain[,] terrains, TerrainTilePlan tiles, ScenePointsOfInterest asset, List<string> notes)
		{
			OpeningIndex openings = Openings(terrains, tiles, asset);
			if (openings.Count == 0)
			{
				return 0;
			}
			float reach = Mathf.Max(DetailMargin, TreeClearance(MaxCrownMetres)) + openings.CellHalfDiagonal + 2f;
			Rect around = openings.Bounds;
			around = Rect.MinMaxRect(around.xMin - reach, around.yMin - reach, around.xMax + reach, around.yMax + reach);
			int cleared = 0, removed = 0;
			var crowns = new Dictionary<GameObject, float>();
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = data.size;
				if (!new Rect(origin.x, origin.z, size.x, size.z).Overlaps(around))
				{
					continue;
				}
				bool changed = false;
				int layers = data.detailPrototypes.Length, res = data.detailResolution;
				if (layers > 0 && res > 0)
				{
					float cell = size.x / res;
					int x0 = Mathf.Clamp(Mathf.FloorToInt((around.xMin - origin.x) / cell), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((around.xMax - origin.x) / cell), 0, res - 1);
					int z0 = Mathf.Clamp(Mathf.FloorToInt((around.yMin - origin.z) / cell), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((around.yMax - origin.z) / cell), 0, res - 1);
					int w = x1 - x0 + 1, h = z1 - z0 + 1;
					var bare = new bool[h, w];
					bool any = false;
					for (int j = 0; j < h; j++)
					{
						for (int i = 0; i < w; i++)
						{
							var centre = new Vector2(origin.x + (x0 + i + 0.5f) * cell, origin.z + (z0 + j + 0.5f) * cell);
							if (DetailCellCleared(openings, centre, cell))
							{
								bare[j, i] = true;
								any = true;
							}
						}
					}
					if (any)
					{
						for (int layer = 0; layer < layers; layer++)
						{
							int[,] map = data.GetDetailLayer(x0, z0, w, h, layer);
							bool emptied = false;
							for (int j = 0; j < h; j++)
							{
								for (int i = 0; i < w; i++)
								{
									if (bare[j, i] && map[j, i] != 0)
									{
										map[j, i] = 0;
										emptied = true;
										cleared++;
									}
								}
							}
							if (emptied)
							{
								data.SetDetailLayer(x0, z0, layer, map);
								changed = true;
							}
						}
					}
				}
				TreeInstance[] trees = data.treeInstances;
				if (trees.Length > 0)
				{
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
					var kept = new List<TreeInstance>(trees.Length);
					foreach (TreeInstance tree in trees)
					{
						var at = new Vector2(origin.x + tree.position.x * size.x, origin.z + tree.position.z * size.z);
						float crown = tree.prototypeIndex >= 0 && tree.prototypeIndex < crownOf.Length ? crownOf[tree.prototypeIndex] * tree.widthScale : 0f;
						if (around.Contains(at) && openings.Within(at, TreeClearance(crown)))
						{
							removed++;
							continue;
						}
						kept.Add(tree);
					}
					if (kept.Count != trees.Length)
					{
						data.SetTreeInstances(kept.ToArray(), false);
						changed = true;
					}
				}
				if (changed)
				{
					terrain.Flush();
					EditorUtility.SetDirty(data);
				}
			}
			if (cleared > 0 || removed > 0)
			{
				notes?.Add($"Caves: the ground round {openings.Count:N0} opening cell(s) kept bare: {cleared:N0} detail cell(s) emptied, {removed:N0} scattered tree(s), bush(es) and boulder(s) taken off.");
			}
			return cleared;
		}
	}
}
#endif
