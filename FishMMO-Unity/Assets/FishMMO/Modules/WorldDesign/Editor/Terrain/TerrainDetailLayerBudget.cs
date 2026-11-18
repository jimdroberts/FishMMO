#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How many detail layers one terrain tile can hold, which of a scene's detail prototypes a tile is given, and the
	/// cut's check that every painted layer stands on its own biome's ground. Pure: no Unity calls.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Unity keeps a detail layer's index in one byte.</b> A detail patch stores the layers it holds as 8-bit indices, so
	/// <c>TerrainData.SetDetailLayer</c> with layer 256 or above writes into layer <c>index &amp; 0xFF</c>, another plant's,
	/// and nothing ever reads back from the layer asked for. Measured on 2026-10-27 over every world tile: the 30 tiles with
	/// more than 256 prototypes (Moron 1's 313, Moron 2's 578, Moron 3's 657) held no data in any layer from 256 up, and
	/// Moron 2's Woodland pasture grass (layer 560) had been painted into the Forest's holly bush (layer 48 = 560 − 512): a
	/// carpet of 6.5 m bushes over every woodland.
	/// </para>
	/// <para>
	/// <b>So a tile is given only the prototypes it paints</b> (<see cref="TileLayers"/>), each the first time its rule writes
	/// cells there: a few dozen biomes' out of a scene's hundreds (at most ~160 painted on any tile of the 2026-10-27 cuts,
	/// where the rules of the biomes on a tile hold up to ~365). Past the limit a prototype is refused there, loudly, rather
	/// than written into another's layer.
	/// </para>
	/// </remarks>
	public static class TerrainDetailLayerBudget
	{
		/// <summary>The most detail prototypes one terrain tile can hold and still read and write each layer as itself.</summary>
		public const int MaxLayers = 256;

		/// <summary>
		/// A share of a prototype's painted blocks off its own biome's ground past which the cut's check calls it misplaced
		/// (<see cref="IsMisplaced"/>). Biomes blend, so a plant reaching a block or two past the last sample of its biome is fine.
		/// </summary>
		public const float MisplacedShare = 0.1f;

		/// <summary>The fewest painted blocks off its own biome's ground that make a prototype misplaced.</summary>
		public const int MisplacedBlocks = 2;

		/// <summary>
		/// One tile's detail layers, given out as the scatter first writes each prototype (<see cref="LayerFor"/>): only the
		/// prototypes a tile actually paints take a layer, in the order they are first painted, and none past the limit.
		/// </summary>
		public sealed class TileLayers
		{
			private int[] layerOf = Array.Empty<int>();
			private readonly List<int> kept = new List<int>();

			/// <summary>The most layers the tile may hold.</summary>
			public int Limit { get; private set; }

			/// <summary>The scene prototype of each of the tile's layers: the tile's layer i is Kept[i].</summary>
			public IReadOnlyList<int> Kept => kept;

			/// <summary>Starts a tile: no layers yet, for a scene of <paramref name="scenePrototypes"/> detail prototypes.</summary>
			public void Begin(int scenePrototypes, int limit)
			{
				if (layerOf.Length != scenePrototypes)
				{
					layerOf = new int[Math.Max(0, scenePrototypes)];
				}
				for (int i = 0; i < layerOf.Length; i++)
				{
					layerOf[i] = -1;
				}
				kept.Clear();
				Limit = Math.Max(0, Math.Min(limit, MaxLayers));
			}

			/// <summary>The tile's layer of a scene prototype, −1 while it has none.</summary>
			public int LayerOf(int scenePrototype) => scenePrototype >= 0 && scenePrototype < layerOf.Length ? layerOf[scenePrototype] : -1;

			/// <summary>
			/// The tile's layer of a scene prototype about to be written, given the next free one the first time; −1 (refused)
			/// when the tile is full or the index is not the scene's. <paramref name="added"/> is true when a layer was given now.
			/// </summary>
			public int LayerFor(int scenePrototype, out bool added)
			{
				added = false;
				if (scenePrototype < 0 || scenePrototype >= layerOf.Length)
				{
					return -1;
				}
				int layer = layerOf[scenePrototype];
				if (layer >= 0)
				{
					return layer;
				}
				if (kept.Count >= Limit)
				{
					return -1;
				}
				layer = kept.Count;
				layerOf[scenePrototype] = layer;
				kept.Add(scenePrototype);
				added = true;
				return layer;
			}
		}

		/// <summary>
		/// The layer Unity actually stores a write to <paramref name="layer"/> in (<see cref="MaxLayers"/>): itself below the limit,
		/// another layer above it. What the scatter must never rely on; the tests model Unity's storage with it.
		/// </summary>
		public static int StoredLayer(int layer) => layer & (MaxLayers - 1);

		/// <summary>
		/// True when a painted prototype stands off its own biome's ground in too many of the blocks it is painted in: in
		/// <paramref name="foreignBlocks"/> of its <paramref name="paintedBlocks"/>, none of its biome's weight was found.
		/// </summary>
		public static bool IsMisplaced(int paintedBlocks, int foreignBlocks)
		{
			return paintedBlocks > 0 && foreignBlocks >= MisplacedBlocks && foreignBlocks > paintedBlocks * MisplacedShare;
		}

		/// <summary>
		/// For each layer of a tile: in how many blocks it is painted, and in how many of those its own biome is nowhere near.
		/// </summary>
		/// <param name="blockLayers">Each block's painted layers (Unity's GetSupportedLayers over the block).</param>
		/// <param name="blockBiomes">Each block's biome ids, its neighbours' included (the margin a blend reaches).</param>
		/// <param name="layerBiome">Each layer's biome id (TerrainDetailBiomes); 0 for one with no biome, never counted foreign.</param>
		/// <param name="painted">Filled: blocks each layer is painted in (length = layers).</param>
		/// <param name="foreign">Filled: of those, the blocks whose biomes do not include the layer's own.</param>
		public static void CountForeign(IReadOnlyList<int[]> blockLayers, IReadOnlyList<ICollection<int>> blockBiomes, int[] layerBiome, int[] painted, int[] foreign)
		{
			Array.Clear(painted, 0, painted.Length);
			Array.Clear(foreign, 0, foreign.Length);
			for (int b = 0; b < blockLayers.Count; b++)
			{
				int[] layers = blockLayers[b];
				if (layers == null)
				{
					continue;
				}
				ICollection<int> biomes = blockBiomes[b];
				foreach (int layer in layers)
				{
					if (layer < 0 || layer >= painted.Length)
					{
						continue;
					}
					painted[layer]++;
					int own = layer < layerBiome.Length ? layerBiome[layer] : 0;
					if (own != 0 && (biomes == null || !biomes.Contains(own)))
					{
						foreign[layer]++;
					}
				}
			}
		}
	}
}
#endif
