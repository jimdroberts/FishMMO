#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Clears the ground the sites enclose (2026-10-27, Moron 2: "stone fences/enclosures/farms? Trees, bushes, and other details
	/// spawn inside it"): every field, fold, farmstead and outlying holding a site lays is kept as a field or a yard is, its wild
	/// growth taken off after every site is built and before the scatter's trees are baked into props.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> The scatter keeps its trees out of each site's planned keep-out only (PointOfInterestStage.Excludes), a disc
	/// decided before the site is built; the fields, folds and farmsteads are laid round it afterwards, out past that disc, over
	/// ground the scatter had already planted. A sown field's details were cleared for its crop (PointOfInterestFields.PaintDetails),
	/// but its trees and boulders stood on in the corn, and a pasture, a fallow, a fold or a farmyard kept all of it: oaks and
	/// boulders inside the walls, the wood's bushes and rocks up through the turf.
	/// </para>
	/// <para>
	/// <b>What goes.</b> Every terrain tree instance (trees, the bushes the scatter plants as trees, boulders, formations, deadwood)
	/// whose stem stands inside the enclosure or within <see cref="TreeMarginMetres"/> outside it, where its wall, fence or hedge
	/// runs. On turf (pasture, fallow, a fold, a farmyard: an <see cref="Enclosure"/> with <see cref="Enclosure.Turf"/>) every detail
	/// the detail scatter draws (bushes, herbs, rocks, pebbles, litter) from <see cref="DetailInsetMetres"/> in from its edge, the
	/// grass the blades draw (<see cref="BiomeGrassDefaults.IsBlade"/>) kept: the field's own sward, and the hedge's leaves and the
	/// tufts at the wall's foot left as they grew. A sown field's details are its crop's already, so only its trees go. The client's
	/// blade grass and GPU detail scatter are built from these detail layers (and baked from them at the end of the cut), so they
	/// draw the cleared ground as it is here.
	/// </para>
	/// <para>
	/// <b>Separate from the floors.</b> The buildings' floors are cleared by PointOfInterestTreeClearance; this term is the enclosures'
	/// own and takes nothing a floor does not also lie in.
	/// </para>
	/// </remarks>
	public static class PointOfInterestEnclosureClearance
	{
		/// <summary>How far outside an enclosure's edge a stem still goes, metres: the wall's, fence's or hedge's own line.</summary>
		public const float TreeMarginMetres = 1.5f;

		/// <summary>How far in from a turf enclosure's edge its wild details are cleared, metres: the hedge's leaves and the wall foot's tufts stay.</summary>
		public const float DetailInsetMetres = 1f;

		/// <summary>The side of the grid the enclosures are bucketed in, metres.</summary>
		private const float CellMetres = 32f;

		/// <summary>One enclosed piece of ground: a rectangle (centre, yaw degrees, size along its own x and z) and whether it is turf.</summary>
		public readonly struct Enclosure
		{
			public readonly Vector2 Centre;
			public readonly float Yaw;
			public readonly Vector2 Size;
			/// <summary>Kept as turf (pasture, fallow, a fold, a yard): its wild details cleared too, its grass kept. False for sown ground, whose details its crop already holds.</summary>
			public readonly bool Turf;

			public Enclosure(Vector2 centre, float yaw, Vector2 size, bool turf)
			{
				Centre = centre;
				Yaw = yaw;
				Size = size;
				Turf = turf;
			}
		}

		// ── Pure ──────────────────────────────────────────────────────

		/// <summary>Whether a terrain tree instance whose stem stands at <paramref name="p"/> is taken off an enclosure (pure).</summary>
		public static bool ClearsTree(in Enclosure enclosure, Vector2 p)
			=> PointOfInterestPaddies.Inside(enclosure.Centre, enclosure.Yaw, enclosure.Size, p.x, p.y) > -TreeMarginMetres;

		/// <summary>
		/// Whether a detail of the prefab <paramref name="prefab"/> at <paramref name="p"/> is cleared from an enclosure (pure): on turf
		/// only, a detail the scatter draws (never the blades' grass), from <see cref="DetailInsetMetres"/> in from its edge.
		/// </summary>
		public static bool ClearsDetail(in Enclosure enclosure, Vector2 p, string prefab)
			=> enclosure.Turf && !BiomeGrassDefaults.IsBlade(prefab)
				&& PointOfInterestPaddies.Inside(enclosure.Centre, enclosure.Yaw, enclosure.Size, p.x, p.y) >= DetailInsetMetres;

		/// <summary>The half diagonal an enclosure reaches from its centre, grown by the tree margin, metres (pure): its bucketing reach.</summary>
		public static float Reach(in Enclosure enclosure) => enclosure.Size.magnitude * 0.5f + TreeMarginMetres;

		// ── The terrain ───────────────────────────────────────────────

		/// <summary>
		/// Takes the wild growth off every enclosure (<see cref="ClearsTree"/>, <see cref="ClearsDetail"/>) on every tile. Returns the
		/// trees removed; the notes say how many trees and detail cells went. Nothing read or written with no enclosures.
		/// </summary>
		public static int Clear(Terrain[,] terrains, IReadOnlyList<Enclosure> enclosures, List<string> notes)
		{
			if (terrains == null || enclosures == null || enclosures.Count == 0)
			{
				return 0;
			}
			var cells = new Dictionary<long, List<int>>();
			for (int e = 0; e < enclosures.Count; e++)
			{
				Enclosure enclosure = enclosures[e];
				float reach = Reach(enclosure);
				int x0 = Cell(enclosure.Centre.x - reach), x1 = Cell(enclosure.Centre.x + reach);
				int z0 = Cell(enclosure.Centre.y - reach), z1 = Cell(enclosure.Centre.y + reach);
				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						long key = Key(x, z);
						if (!cells.TryGetValue(key, out List<int> list))
						{
							cells[key] = list = new List<int>();
						}
						list.Add(e);
					}
				}
			}

			int trees = 0;
			long details = 0;
			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position, size = data.size;
				bool changed = false;
				TreeInstance[] instances = data.treeInstances;
				if (instances.Length > 0)
				{
					var kept = new List<TreeInstance>(instances.Length);
					foreach (TreeInstance tree in instances)
					{
						var at = new Vector2(origin.x + tree.position.x * size.x, origin.z + tree.position.z * size.z);
						if (TakesTree(enclosures, cells, at))
						{
							trees++;
							continue;
						}
						kept.Add(tree);
					}
					if (kept.Count != instances.Length)
					{
						data.SetTreeInstances(kept.ToArray(), false);
						terrain.Flush();
						changed = true;
					}
				}
				long cleared = ClearDetails(data, origin, enclosures, cells);
				details += cleared;
				if (changed || cleared > 0)
				{
					EditorUtility.SetDirty(data);
				}
			}
			if (trees > 0 || details > 0)
			{
				notes?.Add($"Points of interest: {trees:N0} scattered tree(s), bush(es) and boulder(s) and {details:N0} wild detail cell(s) taken off {enclosures.Count:N0} field(s), fold(s) and holding(s).");
			}
			return trees;
		}

		/// <summary>Clears the wild details of the turf enclosures on one tile, layer by layer, over each enclosure's bounds; returns the cells cleared.</summary>
		private static long ClearDetails(TerrainData data, Vector3 origin, IReadOnlyList<Enclosure> enclosures, Dictionary<long, List<int>> cells)
		{
			int res = data.detailResolution;
			DetailPrototype[] prototypes = data.detailPrototypes;
			if (res <= 0 || prototypes == null || prototypes.Length == 0)
			{
				return 0;
			}
			var names = new string[prototypes.Length];
			bool anyWild = false;
			for (int l = 0; l < prototypes.Length; l++)
			{
				DetailPrototype prototype = prototypes[l];
				names[l] = prototype?.prototype != null ? prototype.prototype.name : prototype?.prototypeTexture != null ? prototype.prototypeTexture.name : string.Empty;
				anyWild |= !BiomeGrassDefaults.IsBlade(names[l]);
			}
			if (!anyWild)
			{
				return 0;
			}
			Vector3 size = data.size;
			float texelX = size.x / res, texelZ = size.z / res;
			long cleared = 0;
			foreach (Enclosure enclosure in enclosures)
			{
				if (!enclosure.Turf)
				{
					continue;
				}
				float reach = enclosure.Size.magnitude * 0.5f;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((enclosure.Centre.x - reach - origin.x) / texelX), 0, res - 1);
				int x1 = Mathf.Clamp(Mathf.CeilToInt((enclosure.Centre.x + reach - origin.x) / texelX), 0, res - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((enclosure.Centre.y - reach - origin.z) / texelZ), 0, res - 1);
				int z1 = Mathf.Clamp(Mathf.CeilToInt((enclosure.Centre.y + reach - origin.z) / texelZ), 0, res - 1);
				if (enclosure.Centre.x + reach < origin.x || enclosure.Centre.x - reach > origin.x + size.x
					|| enclosure.Centre.y + reach < origin.z || enclosure.Centre.y - reach > origin.z + size.z || x1 <= x0 || z1 <= z0)
				{
					continue;
				}
				int w = x1 - x0 + 1, h = z1 - z0 + 1;
				for (int layer = 0; layer < prototypes.Length; layer++)
				{
					if (BiomeGrassDefaults.IsBlade(names[layer]))
					{
						continue;
					}
					int[,] map = data.GetDetailLayer(x0, z0, w, h, layer);
					bool changed = false;
					for (int j = 0; j < h; j++)
					{
						for (int i = 0; i < w; i++)
						{
							if (map[j, i] == 0)
							{
								continue;
							}
							var p = new Vector2(origin.x + (x0 + i + 0.5f) * texelX, origin.z + (z0 + j + 0.5f) * texelZ);
							if (ClearsDetail(enclosure, p, names[layer]))
							{
								map[j, i] = 0;
								changed = true;
								cleared++;
							}
						}
					}
					if (changed)
					{
						data.SetDetailLayer(x0, z0, layer, map);
					}
				}
			}
			return cleared;
		}

		/// <summary>Whether any enclosure bucketed near <paramref name="at"/> takes a stem standing there (<see cref="ClearsTree"/>).</summary>
		private static bool TakesTree(IReadOnlyList<Enclosure> enclosures, Dictionary<long, List<int>> cells, Vector2 at)
		{
			if (!cells.TryGetValue(Key(Cell(at.x), Cell(at.y)), out List<int> near))
			{
				return false;
			}
			foreach (int e in near)
			{
				if (ClearsTree(enclosures[e], at))
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
