#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cut's check of its finished detail layers: no tile holds more detail prototypes than Unity can address
	/// (<see cref="TerrainDetailLayerBudget.MaxLayers"/>), and every painted prototype stands on its own biome's ground.
	/// Each failure is an ERROR note and one error in the log.
	/// </summary>
	/// <remarks>
	/// Run after everything that paints details (the scatter, then the sites), from the scene's biome map. A prototype whose
	/// biome is nowhere within a block of where it is painted, in more than <see cref="TerrainDetailLayerBudget.MisplacedShare"/>
	/// of its blocks, is another rule's map in this rule's prototype — what 2026-10-27's carpet of holly over Moron 2's
	/// woodland was (Woodland's pasture grass written into the Forest's bush, layer 560 stored as 48).
	/// </remarks>
	public static class TerrainDetailLayerCheck
	{
		/// <summary>A block's side, detail cells (~64 m at the cut's 1 m cells).</summary>
		public const int BlockCells = 64;

		/// <summary>Biome map samples per block side.</summary>
		private const int SamplesPerBlock = 8;

		/// <summary>The most problems listed one by one in the notes.</summary>
		private const int Listed = 24;

		/// <summary>Checks every tile of a cut's grid (<see cref="Check(IEnumerable{Terrain}, SceneBiomeMap, List{string})"/>).</summary>
		public static int Check(Terrain[,] terrains, SceneBiomeMap map, List<string> notes)
		{
			var tiles = new List<Terrain>();
			if (terrains != null)
			{
				foreach (Terrain terrain in terrains)
				{
					tiles.Add(terrain);
				}
			}
			return Check(tiles, map, notes);
		}

		/// <summary>Checks every tile; adds the problems found to <paramref name="notes"/> and returns how many there were.</summary>
		public static int Check(IEnumerable<Terrain> tiles, SceneBiomeMap map, List<string> notes)
		{
			var problems = new List<string>();
			foreach (Terrain terrain in tiles)
			{
				if (terrain != null && terrain.terrainData != null)
				{
					CheckTile(terrain, map, problems);
				}
			}
			if (problems.Count == 0)
			{
				return 0;
			}
			string head = $"ERROR: {problems.Count} terrain detail layer problem(s): a plant painted into another's prototype or a tile past Unity's {TerrainDetailLayerBudget.MaxLayers} detail layers";
			notes?.Add(head + ".");
			for (int i = 0; i < problems.Count && i < Listed; i++)
			{
				notes?.Add("  " + problems[i]);
			}
			if (problems.Count > Listed)
			{
				notes?.Add($"  … and {problems.Count - Listed} more.");
			}
			Debug.LogError($"[Terrain details] {head}. First: {problems[0]}");
			return problems.Count;
		}

		private static void CheckTile(Terrain terrain, SceneBiomeMap map, List<string> problems)
		{
			TerrainData data = terrain.terrainData;
			DetailPrototype[] prototypes = data.detailPrototypes;
			if (prototypes.Length > TerrainDetailLayerBudget.MaxLayers)
			{
				problems.Add($"'{terrain.name}' holds {prototypes.Length} detail prototypes; Unity addresses {TerrainDetailLayerBudget.MaxLayers}, so layers from {TerrainDetailLayerBudget.MaxLayers} up are written into others.");
			}
			int res = data.detailResolution;
			if (prototypes.Length == 0 || res <= 0 || map == null)
			{
				return;
			}

			var layerBiome = new int[prototypes.Length];
			for (int i = 0; i < prototypes.Length; i++)
			{
				layerBiome[i] = TerrainDetailBiomes.BiomeOf(terrain, prototypes[i]);
			}

			int blocks = (res + BlockCells - 1) / BlockCells;
			Vector3 origin = terrain.transform.position;
			Vector3 size = data.size;
			float cell = size.x / res;
			var own = new HashSet<int>[blocks * blocks];
			var tileBiomes = new HashSet<int>();
			var layersOf = new int[blocks * blocks][];
			for (int bz = 0; bz < blocks; bz++)
			{
				for (int bx = 0; bx < blocks; bx++)
				{
					int x0 = bx * BlockCells, z0 = bz * BlockCells;
					int w = Mathf.Min(BlockCells, res - x0), h = Mathf.Min(BlockCells, res - z0);
					layersOf[bz * blocks + bx] = data.GetSupportedLayers(x0, z0, w, h);
					var set = own[bz * blocks + bx] = new HashSet<int>();
					for (int sz = 0; sz < SamplesPerBlock; sz++)
					{
						for (int sx = 0; sx < SamplesPerBlock; sx++)
						{
							float x = origin.x + (x0 + (sx + 0.5f) * w / SamplesPerBlock) * cell;
							float z = origin.z + (z0 + (sz + 0.5f) * h / SamplesPerBlock) * cell;
							int id = map.IDAt(new Vector3(x, 0f, z));
							if (id != 0)
							{
								set.Add(id);
								tileBiomes.Add(id);
							}
						}
					}
				}
			}
			// Each block with its neighbours': a plant at a blend reaches a little past the last sample of its biome.
			var near = new ICollection<int>[blocks * blocks];
			for (int bz = 0; bz < blocks; bz++)
			{
				for (int bx = 0; bx < blocks; bx++)
				{
					var set = new HashSet<int>();
					for (int dz = -1; dz <= 1; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							int nx = bx + dx, nz = bz + dz;
							if (nx >= 0 && nz >= 0 && nx < blocks && nz < blocks)
							{
								set.UnionWith(own[nz * blocks + nx]);
							}
						}
					}
					near[bz * blocks + bx] = set;
				}
			}

			var painted = new int[prototypes.Length];
			var foreign = new int[prototypes.Length];
			TerrainDetailLayerBudget.CountForeign(layersOf, near, layerBiome, painted, foreign);
			for (int i = 0; i < prototypes.Length; i++)
			{
				if (!TerrainDetailLayerBudget.IsMisplaced(painted[i], foreign[i]))
				{
					continue;
				}
				string name = prototypes[i]?.prototype != null ? prototypes[i].prototype.name : $"prototype {i}";
				string biome = BiomeRegistry.TryGetByID(layerBiome[i], out BiomeTemplate template) && template != null ? template.name : $"biome {layerBiome[i]}";
				string where = tileBiomes.Contains(layerBiome[i]) ? $"off {biome}'s ground in {foreign[i]} of its {painted[i]} blocks" : $"in {painted[i]} blocks, and {biome} is nowhere on this tile";
				problems.Add($"'{terrain.name}' layer {i} '{name}' ({biome}) is painted {where}.");
			}
		}
	}
}
#endif
