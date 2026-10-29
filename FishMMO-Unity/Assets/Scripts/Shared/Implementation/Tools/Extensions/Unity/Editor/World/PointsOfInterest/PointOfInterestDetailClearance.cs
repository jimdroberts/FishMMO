#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Empties every terrain detail layer (grass, flowers, ferns, herbs, litter, bushes) under the sites' buildings: their floors
	/// (<see cref="ScenePathPointFlags.Floor"/> yards, as <see cref="PointOfInterestTreeClearance"/> reads them, with a floor
	/// written under every ground-storey room no yard floored, <see cref="AddRoomFloors"/>), each grown by how far a plant rooted
	/// beside the wall reaches over it (2026-10-27, Moron 2: "houses don't occlude grass/flowers/details").
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why in the data.</b> Since 2026-10-13 a building was kept clear only at draw time, by the path field its floor is
	/// written to (FishGroundPaths.hlsl), and only two of the three detail renderers read that field: the blade grass and the
	/// GPU scatter. The CPU chunk renderer (TerrainDetailField) never did, and it draws every mesh detail neither GPU renderer
	/// takes: each kind outside their prefixes (ferns, herbs, mushrooms, lichens, cushions), every bush and litter type past the
	/// scatter's eight channels a terrain, and every blade look past the scene's 32. Once the biome suite gave a scene hundreds
	/// of detail kinds most of them fell to it, and stood inside every house. Emptied here, no renderer has anything to draw
	/// indoors, whichever takes a kind; the path field still frays the bare ground round the walls on the GPU paths.
	/// </para>
	/// <para>
	/// <b>How far.</b> A detail cell is emptied when a plant anywhere in it could reach indoors: its centre within the plant's
	/// reach (<see cref="Reach"/>, its mesh's footprint at its widest scale) and half the cell's diagonal of a floor's edge. A bush
	/// is cleared by whole scatter blocks (<see cref="BushBlockMetres"/>): the GPU scatter stands a block's bushes anywhere in it.
	/// A ruin has no floor and no furnished room, so its ground stays grown over.
	/// </para>
	/// <para>
	/// Run where <see cref="PointOfInterestTreeClearance"/> runs (the end of PointOfInterestStage.Place, after every site and
	/// linear feature has written its yards and rooms, before the path field and the terrain's details are baked for the client):
	/// <see cref="AddRoomFloors"/> first, so the trees, the details and the path field all keep off the same floors.
	/// </para>
	/// </remarks>
	public static class PointOfInterestDetailClearance
	{
		/// <summary>The most a detail other than a bush is kept off a wall, metres: a fern's or a tall herb's lean.</summary>
		public const float MaxPlantReachMetres = 0.75f;

		/// <summary>The least and most a bush's crown is kept off a wall, metres (a tree's, <see cref="PointOfInterestTreeClearance"/>).</summary>
		public const float MinBushReachMetres = PointOfInterestTreeClearance.MinClearanceMetres;
		public const float MaxBushReachMetres = PointOfInterestTreeClearance.MaxClearanceMetres;

		/// <summary>The GPU scatter's block (DetailScatterMath.BlockMetres): a bush stands anywhere in its block.</summary>
		public const float BushBlockMetres = 2f;

		/// <summary>Every bush prefab's name starts with this (ProceduralArtCatalogue.BushPrefix, DetailScatterSettings.BushPrefix).</summary>
		public const string BushPrefix = ProceduralArtCatalogue.BushPrefix;

		/// <summary>How far below the ground a room's floor may lie and still be the ground storey's, metres (a dug pad).</summary>
		public const float RoomBelowGroundMetres = 1f;

		/// <summary>How far above it, metres: a raised plank floor or a step up, but not a stilt house's deck.</summary>
		public const float RoomAboveGroundMetres = 1.2f;

		/// <summary>
		/// How far a detail rooted beside a building reaches over its wall, metres: a bush by its crown, held to the tree
		/// clearance's bounds; anything else by its footprint, held to <see cref="MaxPlantReachMetres"/>.
		/// </summary>
		public static float Reach(float footprintMetres, bool bush)
		{
			float r = float.IsNaN(footprintMetres) || footprintMetres < 0f ? 0f : footprintMetres;
			return bush ? Mathf.Clamp(r, MinBushReachMetres, MaxBushReachMetres) : Mathf.Min(r, MaxPlantReachMetres);
		}

		/// <summary>
		/// A room as the floor it stands on: the box's plan grown by <paramref name="grow"/> metres a side as a squared floor (end
		/// midpoint to end midpoint along its x), in the shape <see cref="PointOfInterestTreeClearance.Edge"/> and the path field read.
		/// </summary>
		public static PointOfInterestTreeClearance.Floor RoomFloor(in SceneRoom room, float grow = 0f)
		{
			Vector3 axis3 = Quaternion.Euler(0f, room.Yaw, 0f) * Vector3.right;
			var axis = new Vector2(axis3.x, axis3.z);
			var centre = new Vector2(room.Centre.x, room.Centre.z);
			float along = Mathf.Max(0.01f, room.HalfSize.x + grow), across = Mathf.Max(0.01f, room.HalfSize.z + grow);
			return new PointOfInterestTreeClearance.Floor
			{
				A = centre - axis * along,
				B = centre + axis * along,
				HalfA = across,
				HalfB = across,
				Squared = true,
			};
		}

		/// <summary>True when a room's floor lies on the ground under it (its ground storey), not over it (an upper storey, a stilt deck) or under it (a crypt, a passage).</summary>
		public static bool OnTheGround(in SceneRoom room, float ground)
		{
			if (float.IsNaN(ground) || float.IsInfinity(ground))
			{
				return false;
			}
			float floor = room.Centre.y - room.HalfSize.y;
			return floor >= ground - RoomBelowGroundMetres && floor <= ground + RoomAboveGroundMetres;
		}

		/// <summary>How far past a room's box its floor yard reaches, metres: the box stands a hand in from the walls' inner faces, and the walls themselves.</summary>
		public const float RoomWallMetres = 0.45f;

		/// <summary>
		/// Writes a Floor yard (squared, worn bare, <see cref="ScenePathPointFlags.Floor"/>) under every ground-storey room of
		/// <paramref name="rooms"/> whose middle no floor already covers, into <paramref name="paths"/>: the buildings whose yards were
		/// written without a floor (a keep, a market hall, a shrine, a gatehouse) have their interiors kept clear too, by the path
		/// field on the client and by the tree and detail clearances here. <paramref name="groundAt"/>: the ground's height at a point
		/// (null takes every room as on the ground). Returns the floors written.
		/// </summary>
		public static int AddRoomFloors(List<ScenePath> paths, IReadOnlyList<SceneRoom> rooms, Func<float, float, float> groundAt)
		{
			if (paths == null || rooms == null || rooms.Count == 0)
			{
				return 0;
			}
			List<PointOfInterestTreeClearance.Floor> floors = PointOfInterestTreeClearance.FloorsOf(paths);
			int id = 0;
			foreach (ScenePath existing in paths)
			{
				id = Math.Max(id, existing != null ? existing.Id : 0);
			}
			int added = 0;
			foreach (SceneRoom room in rooms)
			{
				if (room.HalfSize.x <= 0f || room.HalfSize.z <= 0f)
				{
					continue;
				}
				if (groundAt != null && !OnTheGround(room, groundAt(room.Centre.x, room.Centre.z)))
				{
					continue;
				}
				var centre = new Vector2(room.Centre.x, room.Centre.z);
				bool covered = false;
				foreach (PointOfInterestTreeClearance.Floor floor in floors)
				{
					if (PointOfInterestTreeClearance.Edge(floor, centre) < 0f)
					{
						covered = true;
						break;
					}
				}
				if (covered)
				{
					continue;
				}
				PointOfInterestTreeClearance.Floor plan = RoomFloor(room, RoomWallMetres);
				float y = room.Centre.y - room.HalfSize.y;
				byte flags = (byte)(ScenePathPointFlags.InSite | ScenePathPointFlags.Squared | ScenePathPointFlags.Floor);
				paths.Add(new ScenePath
				{
					Id = ++id,
					Class = ScenePathClass.Yard,
					FromId = -1,
					ToId = -1,
					Points = new[] { new Vector3(plan.A.x, y, plan.A.y), new Vector3(plan.B.x, y, plan.B.y) },
					HalfWidth = new[] { plan.HalfA, plan.HalfB },
					Wear = new[] { 1f, 1f },
					Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
					Flags = new[] { flags, flags },
				});
				// A building's rooms side by side: the next is judged against this one's floor too.
				floors.Add(plan);
				added++;
			}
			return added;
		}

		/// <summary>
		/// The signed distance from each detail cell's centre to the nearest interior's edge, metres (negative inside), over the
		/// <paramref name="width"/> × <paramref name="depth"/> cells from <paramref name="corner"/> (world x/z of cell (0, 0)'s
		/// corner) of side <paramref name="cellX"/> × <paramref name="cellZ"/>; +∞ past <paramref name="reach"/> of every one.
		/// Indexed [row (z), column (x)], as TerrainData's detail layers are.
		/// </summary>
		public static float[,] EdgeField(IReadOnlyList<PointOfInterestTreeClearance.Floor> floors, Vector2 corner, int width, int depth, float cellX, float cellZ, float reach)
		{
			var edge = new float[depth, width];
			for (int j = 0; j < depth; j++)
			{
				for (int i = 0; i < width; i++)
				{
					edge[j, i] = float.PositiveInfinity;
				}
			}
			if (floors == null)
			{
				return edge;
			}
			foreach (PointOfInterestTreeClearance.Floor floor in floors)
			{
				float grow = Mathf.Max(floor.HalfA, floor.HalfB) + reach + Mathf.Max(cellX, cellZ);
				int i0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(floor.A.x, floor.B.x) - grow - corner.x) / cellX));
				int i1 = Mathf.Min(width - 1, Mathf.CeilToInt((Mathf.Max(floor.A.x, floor.B.x) + grow - corner.x) / cellX));
				int j0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(floor.A.y, floor.B.y) - grow - corner.y) / cellZ));
				int j1 = Mathf.Min(depth - 1, Mathf.CeilToInt((Mathf.Max(floor.A.y, floor.B.y) + grow - corner.y) / cellZ));
				for (int j = j0; j <= j1; j++)
				{
					float z = corner.y + (j + 0.5f) * cellZ;
					for (int i = i0; i <= i1; i++)
					{
						float d = PointOfInterestTreeClearance.Edge(floor, new Vector2(corner.x + (i + 0.5f) * cellX, z));
						if (d < edge[j, i])
						{
							edge[j, i] = d;
						}
					}
				}
			}
			return edge;
		}

		/// <summary>
		/// Empties the cells of one detail layer's <paramref name="map"/> a plant of <paramref name="reach"/> could reach an interior
		/// from (<paramref name="edge"/>, <see cref="EdgeField"/>, the same cells). <paramref name="block"/> cells a side clears in
		/// whole blocks of that many cells from (0, 0) (a bush's, <see cref="BushBlockMetres"/>); 1 cell by cell. Returns the cells emptied.
		/// </summary>
		public static int ClearLayer(int[,] map, float[,] edge, float reach, float halfDiagonal, int block)
		{
			int depth = map.GetLength(0), width = map.GetLength(1);
			block = Mathf.Max(1, block);
			int cleared = 0;
			for (int bj = 0; bj < depth; bj += block)
			{
				for (int bi = 0; bi < width; bi += block)
				{
					int jEnd = Mathf.Min(depth, bj + block), iEnd = Mathf.Min(width, bi + block);
					// The block's nearest cell to an interior: any plant of the block may stand there.
					float nearest = float.PositiveInfinity;
					bool any = false;
					for (int j = bj; j < jEnd; j++)
					{
						for (int i = bi; i < iEnd; i++)
						{
							nearest = Mathf.Min(nearest, edge[j, i]);
							any |= map[j, i] != 0;
						}
					}
					if (!any || !(nearest < reach + halfDiagonal))
					{
						continue;
					}
					for (int j = bj; j < jEnd; j++)
					{
						for (int i = bi; i < iEnd; i++)
						{
							if (block == 1 && !(edge[j, i] < reach + halfDiagonal))
							{
								continue;
							}
							if (map[j, i] != 0)
							{
								map[j, i] = 0;
								cleared++;
							}
						}
					}
				}
			}
			return cleared;
		}

		/// <summary>True when a detail prototype's prefab is a bush (<see cref="BushPrefix"/>).</summary>
		public static bool IsBush(string name) => !string.IsNullOrEmpty(name) && name.StartsWith(BushPrefix, StringComparison.Ordinal);

		/// <summary>
		/// Empties every detail layer of every tile within each layer's reach of a building floor in <paramref name="paths"/>
		/// (the room floors <see cref="AddRoomFloors"/> wrote among them). Returns the detail cells emptied.
		/// </summary>
		public static int Clear(Terrain[,] terrains, IReadOnlyList<ScenePath> paths, List<string> notes)
		{
			List<PointOfInterestTreeClearance.Floor> floors = PointOfInterestTreeClearance.FloorsOf(paths);
			if (terrains == null || floors.Count == 0)
			{
				return 0;
			}
			var footprints = new Dictionary<GameObject, float>();
			int cleared = 0, tiles = 0;
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || data.detailResolution <= 0)
				{
					continue;
				}
				DetailPrototype[] prototypes = data.detailPrototypes;
				if (prototypes.Length == 0)
				{
					continue;
				}
				int res = data.detailResolution;
				float cellX = data.size.x / res, cellZ = data.size.z / res;
				int block = Mathf.Max(1, Mathf.RoundToInt(BushBlockMetres / Mathf.Max(1e-3f, cellX)));
				var reach = new float[prototypes.Length];
				var bush = new bool[prototypes.Length];
				float widest = 0f;
				for (int p = 0; p < prototypes.Length; p++)
				{
					DetailPrototype proto = prototypes[p];
					GameObject prefab = proto != null && proto.usePrototypeMesh ? proto.prototype : null;
					float footprint;
					if (prefab != null)
					{
						if (!footprints.TryGetValue(prefab, out footprint))
						{
							footprints[prefab] = footprint = TerrainScatter.FootprintRadius(prefab);
						}
						footprint *= Mathf.Max(proto.minWidth, proto.maxWidth);
						bush[p] = IsBush(prefab.name);
					}
					else
					{
						// A billboard grass: half its widest card.
						footprint = proto != null ? 0.5f * Mathf.Max(proto.minWidth, proto.maxWidth) : 0f;
					}
					reach[p] = Reach(footprint, bush[p]);
					widest = Mathf.Max(widest, reach[p] + (bush[p] ? block * Mathf.Max(cellX, cellZ) : 0f));
				}

				// The tile's cells near any interior, in the bounds of all of them (grown by the widest reach and a block).
				Vector3 origin = terrain.transform.position;
				var corner = new Vector2(origin.x, origin.z);
				float grow = widest + 2f * block * Mathf.Max(cellX, cellZ);
				int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
				var near = new List<PointOfInterestTreeClearance.Floor>();
				foreach (PointOfInterestTreeClearance.Floor floor in floors)
				{
					float half = Mathf.Max(floor.HalfA, floor.HalfB) + grow;
					int fx0 = Mathf.FloorToInt((Mathf.Min(floor.A.x, floor.B.x) - half - corner.x) / cellX);
					int fx1 = Mathf.CeilToInt((Mathf.Max(floor.A.x, floor.B.x) + half - corner.x) / cellX);
					int fz0 = Mathf.FloorToInt((Mathf.Min(floor.A.y, floor.B.y) - half - corner.y) / cellZ);
					int fz1 = Mathf.CeilToInt((Mathf.Max(floor.A.y, floor.B.y) + half - corner.y) / cellZ);
					if (fx1 < 0 || fz1 < 0 || fx0 >= res || fz0 >= res)
					{
						continue;
					}
					near.Add(floor);
					x0 = Mathf.Min(x0, fx0);
					z0 = Mathf.Min(z0, fz0);
					x1 = Mathf.Max(x1, fx1);
					z1 = Mathf.Max(z1, fz1);
				}
				if (near.Count == 0)
				{
					continue;
				}
				// Whole blocks from the tile's corner, as the scatter sums them.
				x0 = Mathf.Max(0, x0 / block * block);
				z0 = Mathf.Max(0, z0 / block * block);
				x1 = Mathf.Min(res - 1, x1);
				z1 = Mathf.Min(res - 1, z1);
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				if (w <= 0 || h <= 0)
				{
					continue;
				}
				var from = new Vector2(corner.x + x0 * cellX, corner.y + z0 * cellZ);
				float[,] edge = EdgeField(near, from, w, h, cellX, cellZ, widest);
				float halfDiagonal = 0.5f * Mathf.Sqrt(cellX * cellX + cellZ * cellZ);
				int[] present = data.GetSupportedLayers(x0, z0, w, h);
				int before = cleared;
				foreach (int layer in present)
				{
					if (layer < 0 || layer >= prototypes.Length)
					{
						continue;
					}
					int[,] map = data.GetDetailLayer(x0, z0, w, h, layer);
					int emptied = ClearLayer(map, edge, reach[layer], halfDiagonal, bush[layer] ? block : 1);
					if (emptied > 0)
					{
						data.SetDetailLayer(x0, z0, layer, map);
						cleared += emptied;
					}
				}
				if (cleared != before)
				{
					tiles++;
					EditorUtility.SetDirty(data);
				}
			}
			if (cleared > 0)
			{
				notes?.Add($"Points of interest: {cleared:N0} detail cell(s) (grass, flowers, ferns, bushes) emptied under {floors.Count:N0} building floor(s) on {tiles} tile(s).");
			}
			return cleared;
		}
	}
}
#endif
