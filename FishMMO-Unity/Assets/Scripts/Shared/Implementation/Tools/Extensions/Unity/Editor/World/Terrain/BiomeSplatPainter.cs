#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What the splat painter needs to know about the ground beyond the tiles themselves.</summary>
	public sealed class BiomeSplatOptions
	{
		/// <summary>Alphamap texels along each side of a tile.</summary>
		/// <remarks>
		/// 512 over a 1000 m tile is about two metres a texel: fine enough that a texture's edge
		/// follows a ridge and that the scatter's texture-weight gate is not a staircase, and
		/// coarse enough that eight control maps per tile stay at a few megabytes.
		/// </remarks>
		public int AlphamapResolution = 512;

		/// <summary>
		/// The biome system's normalised height at a world position (x, y, z): what a layer's and
		/// a cliff's height band mean. Planet-relative, so a band means the same in every scene.
		/// </summary>
		public Func<float, float, float, float> NormalizedHeight;

		/// <summary>True when the world has liquid water, so ground below y = 0 is under the sea.</summary>
		public bool HasLiquidWater = true;

		/// <summary>Metres under the water line over which a biome's submerged layer takes over.</summary>
		public float ShoreFadeMetres = 1.5f;

		/// <summary>
		/// The surface of a river or lake over a world position (x, z), or negative infinity where there is
		/// none; positive infinity for a dry wash's bed, which takes the bed layer with no water over it.
		/// Null for a scene with no inland water.
		/// </summary>
		public Func<float, float, float> InlandWaterSurface;

		/// <summary>Metres under an inland water's surface over which the bed layer takes over: a river's bank has no tide.</summary>
		public float InlandShoreFadeMetres = 0.3f;

		/// <summary>
		/// How much of the inland water at a world position (x, z) is a river's (x, 0 … 1: 1 in its channel, easing to 0 over its last
		/// stretch into a lake or the sea), the river's current there (y, m/s), and z ≥ 0.5 where the rest is the sea's (a mouth on the
		/// coast) rather than a lake's (SceneWater.RiverBedAt). What shares a biome's bed between its riverbed and its lakebed, and
		/// what a bed layer's current band reads (decision 35, 2026-10-18). Asked only under inland water; called from the painter's
		/// worker threads. Null for none: every inland bed is a lake's, as every bed was the lakebed before.
		/// </summary>
		public Func<float, float, Vector3> RiverAt;

		/// <summary>
		/// A river's bar at a world position (x, z): how much of the point is bar, 0 … 1, and how sandy, 0
		/// gravel … 1 sand. Painted with the palette's sediment layers. Null for none.
		/// </summary>
		public Func<float, float, Vector2> BarAt;

		/// <summary>
		/// The least share of a biome's cliff rock at a world position (x, z) whose ground slopes s degrees, 0 … 1: bare rock
		/// the slope rule alone leaves as ground (a waterfall's brink and face, <see cref="FallRockPaint"/>). Called from the
		/// painter's worker threads. Null for none.
		/// </summary>
		public Func<float, float, float, float> RockAt;

		/// <summary>Mixed into every layer's noise so two scenes with the same biomes do not share a pattern.</summary>
		public uint Seed = 1u;

		/// <summary>
		/// The local climate at a world point (x, y the ground's height, z): x the mean annual temperature, y the humidity, each
		/// on the climate's −1 … 1 scale — the scene's placement climate, exactly what climate-banded spawn rules read
		/// (SceneGenerator.ClimateOf). What a ground layer's climate band (TerrainTextureLayer.useClimateBand) and a climate
		/// variant's ground overrides are weighed by. Asked on the calling thread only (a tile's <see cref="TerrainClimateGrid"/>,
		/// or a single <see cref="BiomeSplatPainter.Weigher.Weigh(float, float, float, float, float[])"/>), and only when the
		/// palette has such a layer (<see cref="SceneTerrainPalette.NeedsClimate"/>). Null for none: every climate band then
		/// passes and no variant's override is painted.
		/// </summary>
		public Func<float, float, float, Vector2> ClimateAt;

		/// <summary>
		/// The ground's heat at a world point (x, z), 0 cold … 1 boiling: the scene's heat mask from its geothermal sites
		/// (2026-10-18, phase I2q; GeothermalGroundCarve), what a detail layer's heat band (TerrainTextureLayer.useHeatBand) reads.
		/// Asked only when some layer has one; called from the painter's worker threads (an array read). Null: every heat band passes.
		/// </summary>
		public Func<float, float, float> HeatAt;
	}

	/// <summary>What the splat painter did.</summary>
	public sealed class BiomeSplatReport
	{
		public int Tiles;
		public int Texels;
		public long Milliseconds;
		/// <summary>Texels no biome reached, which fell back to the strongest channel nearby (should be zero).</summary>
		public int Unreached;

		public override string ToString() => $"{Tiles} tile(s), {Texels:N0} texels in {Milliseconds} ms" + (Unreached > 0 ? $", {Unreached:N0} unreached" : string.Empty);
	}

	/// <summary>
	/// Writes every tile's alphamaps from the scene's biomes: the palette's layers, weighted by
	/// where each biome reaches and by each layer's own noise, height and slope bands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Within a biome, the ground is shared out in one order:</b> steep faces to its cliff
	/// layers, ground under the water line to its submerged layer, and what is left to its detail
	/// layers where their noise and bands allow and to its main layer everywhere else. Each step
	/// takes a <em>share</em> of what remains rather than adding a weight, so a biome's layers
	/// always sum to one before the biome's own reach scales them — and nothing is normalised
	/// within a group. That normalisation is what made a lone detail layer in WorldEditor sit at
	/// a flat half wherever it was non-zero and a lone cliff layer cover every slope at all: a
	/// group of one divided by itself is one, whatever its noise and bands said.
	/// </para>
	/// <para>
	/// <b>Several details overlap as a union</b>, 1 − Π(1 − dᵢ), split between them in proportion
	/// to their own strengths: two half-strength patches cover three quarters of the ground
	/// between them, not all of it and not half.
	/// </para>
	/// <para>
	/// <b>Everything is world-space.</b> Noise, height and slope are read at the texel's world
	/// position, so tiles meet without a seam and a re-cut paints the same ground the same way.
	/// Texel <i>i</i> of a tile sits at (i + ½) / resolution across it, which is where the terrain
	/// shader samples the control map — the endpoints-inclusive convention puts every texel half
	/// a texel off, which at two metres is a visible shift along a cliff edge.
	/// </para>
	/// <para>
	/// Unity indexes alphamaps [z, x, layer]; this writes them that way and reads heights and
	/// slopes at the same (x, z), so the transposition that mirrored WorldEditor's height and
	/// slope bands across each cell's diagonal cannot happen here.
	/// </para>
	/// </remarks>
	public static class BiomeSplatPainter
	{
		/// <summary>Paints every tile. The palette's layers become each tile's terrain layers, in order.</summary>
		public static BiomeSplatReport Paint(IReadOnlyList<Terrain> tiles, SceneTerrainPalette palette, SceneBiomeField field, BiomeSplatOptions options)
		{
			if (tiles == null) throw new ArgumentNullException(nameof(tiles));
			if (palette == null) throw new ArgumentNullException(nameof(palette));
			if (field == null) throw new ArgumentNullException(nameof(field));
			options ??= new BiomeSplatOptions();

			var report = new BiomeSplatReport();
			var clock = Stopwatch.StartNew();
			TerrainLayer[] layers = ToArray(palette.Layers);
			var workers = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
			object gate = new object();
			// No climate-banded layer, no climate read: the paint is what it was before the bands existed.
			bool climatic = options.ClimateAt != null && palette.NeedsClimate;

			foreach (Terrain terrain in tiles)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				data.terrainLayers = layers;
				data.alphamapResolution = Mathf.Max(16, options.AlphamapResolution);
				int resolution = data.alphamapResolution;
				var splat = new float[resolution, resolution, layers.Length];
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;

				/* The local climate over the tile, on the scatter's coarse grid, when any layer is banded by it (decision 29,
				 * 2026-10-18): built here because the climate and the terrain answer on this thread, read by the workers. */
				TerrainClimateGrid climate = climatic
					? TerrainClimateGrid.Build(options.ClimateAt, origin, new Vector2(size.x, size.z), (u, v) => origin.y + data.GetInterpolatedHeight(u, v))
					: null;

				// The terrain answers on the main thread only: every texel's ground height and steepness first.
				var groundY = new float[resolution * resolution];
				var steepness = new float[resolution * resolution];
				for (int z = 0; z < resolution; z++)
				{
					float v = (z + 0.5f) / resolution;
					for (int x = 0; x < resolution; x++)
					{
						float u = (x + 0.5f) / resolution;
						// GetInterpolatedHeight and GetSteepness take (x, z) normalised; height in metres.
						groundY[z * resolution + x] = origin.y + data.GetInterpolatedHeight(u, v);
						steepness[z * resolution + x] = data.GetSteepness(u, v);
					}
				}

				/* Then the weighing, rows in parallel: a texel's weights are its own, the field and the noises only read,
				 * and each worker weighs with a weigher of its own (a weigher keeps scratch). */
				int unreached = 0;
				int count = layers.Length;
				Parallel.For(0, resolution, workers, () => (new Weigher(palette, field, options), new float[count], 0), (z, _, local) =>
				{
					(Weigher weigher, float[] texelWeights, int missed) = local;
					float v = (z + 0.5f) / resolution;
					float worldZ = origin.z + v * size.z;
					for (int x = 0; x < resolution; x++)
					{
						float u = (x + 0.5f) / resolution;
						float worldX = origin.x + u * size.x;
						float y = groundY[z * resolution + x];
						Vector2 at = climate != null ? climate.Read(u, v, y) : Weigher.NoClimate;
						if (!weigher.Weigh(worldX, y, worldZ, steepness[z * resolution + x], texelWeights, at))
						{
							missed++;
						}
						for (int layer = 0; layer < count; layer++)
						{
							splat[z, x, layer] = texelWeights[layer];
						}
					}
					return (weigher, texelWeights, missed);
				}, local =>
				{
					lock (gate)
					{
						unreached += local.Item3;
					}
				});
				report.Unreached += unreached;
				data.SetAlphamaps(0, 0, splat);
				report.Tiles++;
				report.Texels += resolution * resolution;
			}

			report.Milliseconds = clock.ElapsedMilliseconds;
			return report;
		}

		private static TerrainLayer[] ToArray(IReadOnlyList<TerrainLayer> list)
		{
			var array = new TerrainLayer[list.Count];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = list[i];
			}
			return array;
		}

		/// <summary>
		/// The per-texel weighing, separate from the terrain so it can be tested and so the scatter
		/// could ask the same question about a point the alphamap does not hold.
		/// </summary>
		public sealed class Weigher
		{
			private readonly SceneTerrainPalette palette;
			private readonly SceneBiomeField field;
			private readonly BiomeSplatOptions options;
			/// <summary>The climate of a point where none is known: every climate band passes, no variant's override is painted.</summary>
			public static readonly Vector2 NoClimate = new Vector2(float.NaN, float.NaN);

			private readonly float[] biomeWeights;
			private readonly float[] detailStrength;
			private readonly Vector2[] noiseOffset;
			/// <summary>Per entry: the share of its ground slot the climate gives it (its variant's, less earlier ones', times its band).</summary>
			private readonly float[] climateShare;
			/// <summary>Per biome: its ground slots and their occupants when any is climatic (<see cref="SceneTerrainPalette.Entry.Climatic"/>), else null.</summary>
			private readonly GroundSlot[][] climaticSlots;
			private readonly bool anyClimatic;
			/// <summary>True when some detail layer has a heat band and the ground's heat is known (2026-10-18, I2q); the point's heat then.</summary>
			private readonly bool anyHeat;
			private float heat = float.NaN;
			/// <summary>Per biome: its beds (decision 35, 2026-10-18), null for a biome with none.</summary>
			private readonly BedSet[] beds;
			/// <summary>Scratch for one biome's extra beds: their strengths and shares at the point being weighed.</summary>
			private readonly float[] bedStrength = new float[BiomeTemplate.MaximumBedLayers];
			private readonly float[] bedShare = new float[BiomeTemplate.MaximumBedLayers];

			/// <summary>One biome's beds: its lakebed, its riverbed (null when it is the lakebed's channel) and its extra beds by depth.</summary>
			private sealed class BedSet
			{
				public SceneTerrainPalette.Entry Lakebed, Riverbed;
				public SceneTerrainPalette.Entry[] Extras = Array.Empty<SceneTerrainPalette.Entry>();
				public BedTextureLayer[] ExtraSources = Array.Empty<BedTextureLayer>();
			}

			/// <summary>One ground slot of a biome (0 the main layer, k detail k − 1): its own layer and the variants' overrides standing in for it.</summary>
			private sealed class GroundSlot
			{
				public int Ground;
				public SceneTerrainPalette.Entry Own;
				/// <summary>In the order the variants claim the slot: their order in the template's list.</summary>
				public readonly List<SceneTerrainPalette.Entry> Variants = new List<SceneTerrainPalette.Entry>();
			}

			public Weigher(SceneTerrainPalette palette, SceneBiomeField field, BiomeSplatOptions options)
			{
				this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
				this.field = field ?? throw new ArgumentNullException(nameof(field));
				this.options = options ?? new BiomeSplatOptions();
				biomeWeights = new float[Mathf.Max(1, field.Biomes.Count)];
				detailStrength = new float[Mathf.Max(1, palette.Entries.Count)];

				/* One offset per entry, from the layer's own authored offset and the scene's seed,
				 * so the same layer in two biomes, or in two scenes, does not lay the same patches. */
				noiseOffset = new Vector2[palette.Entries.Count];
				for (int i = 0; i < noiseOffset.Length; i++)
				{
					SceneTerrainPalette.Entry entry = palette.Entries[i];
					uint hash = this.options.Seed ^ unchecked((uint)((entry.Biome != null ? entry.Biome.Key : string.Empty) + entry.Slot).GetDeterministicHashCode());
					noiseOffset[i] = new Vector2(entry.Source.blendNoiseOffsetX + (hash & 0xFFFF) * 0.37f,
						entry.Source.blendNoiseOffsetY + (hash >> 16) * 0.37f);
				}

				if (this.options.HeatAt != null)
				{
					foreach (SceneTerrainPalette.Entry entry in palette.Entries)
					{
						anyHeat |= entry.Role == PaletteRole.Detail && entry.Source != null && entry.Source.HasHeatBand;
					}
				}

				/* Each biome's beds: the lakebed, the riverbed (a channel of its own only when its art differs: one shared terrain layer
				 * is one channel, which takes the whole bed as before) and the extra beds by depth, in slot order. */
				beds = new BedSet[Mathf.Max(1, field.Biomes.Count)];
				for (int b = 0; b < field.Biomes.Count; b++)
				{
					BedSet set = null;
					var extras = new List<SceneTerrainPalette.Entry>();
					foreach (SceneTerrainPalette.Entry entry in palette.EntriesFor(b))
					{
						if (entry.Role != PaletteRole.Submerged)
						{
							continue;
						}
						set ??= new BedSet();
						if (entry.Slot == SceneTerrainPalette.SlotLakebed) set.Lakebed ??= entry;
						else if (entry.Slot == SceneTerrainPalette.SlotRiverbed) set.Riverbed ??= entry;
						else if (entry.Source is BedTextureLayer && extras.Count < BiomeTemplate.MaximumBedLayers) extras.Add(entry);
						else set.Lakebed ??= entry;
					}
					if (set == null)
					{
						continue;
					}
					if (set.Lakebed != null && set.Riverbed != null && set.Lakebed.LayerIndex == set.Riverbed.LayerIndex)
					{
						set.Riverbed = null;
					}
					set.Extras = extras.ToArray();
					set.ExtraSources = extras.ConvertAll(e => (BedTextureLayer)e.Source).ToArray();
					beds[b] = set;
				}

				/* The biomes whose ground the climate changes, each with its ground slots gathered: everything else shares its
				 * ground out exactly as before the bands existed, whatever the climate reads. */
				climateShare = new float[Mathf.Max(1, palette.Entries.Count)];
				climaticSlots = new GroundSlot[Mathf.Max(1, field.Biomes.Count)][];
				for (int b = 0; b < field.Biomes.Count; b++)
				{
					IReadOnlyList<SceneTerrainPalette.Entry> entries = palette.EntriesFor(b);
					bool climatic = false;
					foreach (SceneTerrainPalette.Entry entry in entries)
					{
						climatic |= entry.Ground >= 0 && entry.Climatic;
					}
					if (!climatic)
					{
						continue;
					}
					anyClimatic = true;
					var slots = new SortedDictionary<int, GroundSlot>();
					foreach (SceneTerrainPalette.Entry entry in entries)
					{
						if (entry.Ground < 0 || entry.Role != PaletteRole.Main && entry.Role != PaletteRole.Detail)
						{
							continue;
						}
						if (!slots.TryGetValue(entry.Ground, out GroundSlot slot))
						{
							slots[entry.Ground] = slot = new GroundSlot { Ground = entry.Ground };
						}
						if (entry.Variant == null)
						{
							slot.Own ??= entry;
						}
						else
						{
							slot.Variants.Add(entry);
						}
					}
					var list = new List<GroundSlot>(slots.Values);
					foreach (GroundSlot slot in list)
					{
						// Stable: two overrides of one variant for one slot cannot happen; equal indices keep the palette's order.
						var ordered = new List<SceneTerrainPalette.Entry>(slot.Variants);
						ordered.Sort((x, y) => x.VariantIndex != y.VariantIndex ? x.VariantIndex.CompareTo(y.VariantIndex) : slot.Variants.IndexOf(x).CompareTo(slot.Variants.IndexOf(y)));
						slot.Variants.Clear();
						slot.Variants.AddRange(ordered);
					}
					climaticSlots[b] = list.ToArray();
				}
			}

			/// <summary>True when some biome's ground changes with the climate, so a point's climate is worth reading.</summary>
			public bool Climatic => anyClimatic;

			/// <summary>
			/// <see cref="Weigh(float, float, float, float, float[], Vector2)"/> with the point's climate asked of
			/// <see cref="BiomeSplatOptions.ClimateAt"/> (only when some biome's ground changes with it), for a caller weighing a
			/// point at a time (the backdrop).
			/// </summary>
			public bool Weigh(float worldX, float worldY, float worldZ, float slopeDegrees, float[] layerWeights)
			{
				Vector2 climate = anyClimatic && options.ClimateAt != null ? options.ClimateAt(worldX, worldY, worldZ) : NoClimate;
				return Weigh(worldX, worldY, worldZ, slopeDegrees, layerWeights, climate);
			}

			/// <summary>
			/// Fills one weight per palette layer at a world point, summing to one. False when no
			/// biome reached the point, in which case the weights are whatever the nearest reach
			/// left (all zero only if the scene has no biome at all).
			/// </summary>
			/// <param name="climate">The point's climate (x temperature, y humidity; <see cref="BiomeSplatOptions.ClimateAt"/>), or <see cref="NoClimate"/>.</param>
			public bool Weigh(float worldX, float worldY, float worldZ, float slopeDegrees, float[] layerWeights, Vector2 climate)
			{
				Array.Clear(layerWeights, 0, layerWeights.Length);
				field.WeightsAt(worldX, worldZ, biomeWeights);
				float height = options.NormalizedHeight != null ? options.NormalizedHeight(worldX, worldY, worldZ) : 0.5f;
				float submerged = options.HasLiquidWater && options.ShoreFadeMetres > 0f
					? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-worldY / options.ShoreFadeMetres))
					: 0f;
				float inland = float.NegativeInfinity;
				if (options.InlandWaterSurface != null)
				{
					// Under a river or lake: its bed, from just below its surface.
					inland = options.InlandWaterSurface(worldX, worldZ);
					if (!float.IsNegativeInfinity(inland))
					{
						float under = float.IsPositiveInfinity(inland) ? 1f
							: Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((inland - worldY) / Mathf.Max(0.05f, options.InlandShoreFadeMetres)));
						submerged = Mathf.Max(submerged, under);
					}
				}
				/* What lies over the bed (decision 35, 2026-10-18): which water, how deep below its own surface, how quick. Only where
				 * something is submerged; a river's share asked only under inland water. */
				BedWater water = BedWater.None;
				if (submerged > 0f)
				{
					Vector3 river = options.RiverAt != null && !float.IsNegativeInfinity(inland) && !float.IsPositiveInfinity(inland)
						? options.RiverAt(worldX, worldZ) : Vector3.zero;
					water = BedWater.At(worldY, options.HasLiquidWater, 0f, inland, river);
				}

				heat = anyHeat ? options.HeatAt(worldX, worldZ) : float.NaN;
				Vector2 bar = options.BarAt != null ? options.BarAt(worldX, worldZ) : Vector2.zero;
				float rock = options.RockAt != null ? Mathf.Clamp01(options.RockAt(worldX, worldZ, slopeDegrees)) : 0f;
				float reached = 0f;
				for (int b = 0; b < field.Biomes.Count; b++)
				{
					float biomeWeight = biomeWeights[b];
					if (biomeWeight <= 1e-4f)
					{
						continue;
					}
					reached += biomeWeight;
					Share(b, biomeWeight, worldX, worldZ, height, slopeDegrees, submerged, in water, bar, rock, climate, layerWeights);
				}

				float total = 0f;
				for (int i = 0; i < layerWeights.Length; i++)
				{
					total += layerWeights[i];
				}
				if (total <= 1e-6f)
				{
					// Nothing painted: the first layer rather than a hole, which renders black.
					if (layerWeights.Length > 0)
					{
						layerWeights[0] = 1f;
					}
					return false;
				}
				for (int i = 0; i < layerWeights.Length; i++)
				{
					layerWeights[i] /= total;
				}
				return reached > 1e-4f;
			}

			/// <summary>Shares one biome's reach at a point out among its layers. See the class remarks for the order.</summary>
			private void Share(int biomeIndex, float biomeWeight, float worldX, float worldZ, float height, float slope, float submerged, in BedWater water,
				Vector2 bar, float rock, Vector2 climate, float[] layerWeights)
			{
				IReadOnlyList<SceneTerrainPalette.Entry> entries = palette.EntriesFor(biomeIndex);
				SceneTerrainPalette.Entry main = null;
				SceneTerrainPalette.Entry sand = null;
				SceneTerrainPalette.Entry gravel = null;
				float cliffTotal = 0f;
				float cliffMax = 0f;
				int cliffs = 0;

				// Cliffs: each by its own slope (and optional height) curve; together they take the
				// largest of their weights, so two overlapping cliff layers do not double the rock.
				for (int i = 0; i < entries.Count; i++)
				{
					SceneTerrainPalette.Entry entry = entries[i];
					switch (entry.Role)
					{
						case PaletteRole.Main:
							if (entry.Variant == null)
							{
								main ??= entry;
							}
							break;
						case PaletteRole.Submerged: break;
						case PaletteRole.Sediment:
							if (entry.Slot == SceneTerrainPalette.SlotSand) sand ??= entry;
							else gravel ??= entry;
							break;
						case PaletteRole.Cliff:
							float w = entry.Source is CliffTextureLayer cliff ? cliff.GetCliffWeight(slope, height) : 0f;
							detailStrength[entry.Index] = w;
							cliffTotal += w;
							cliffMax = Mathf.Max(cliffMax, w);
							cliffs++;
							break;
					}
				}
				// At least the share the ground is known to be bare rock, split as the slope splits it, or evenly under every cliff's angle.
				if (cliffs > 0 && rock > cliffMax)
				{
					cliffMax = rock;
				}
				if (cliffMax > 0f)
				{
					for (int i = 0; i < entries.Count; i++)
					{
						SceneTerrainPalette.Entry entry = entries[i];
						if (entry.Role == PaletteRole.Cliff)
						{
							float split = cliffTotal > 0f ? detailStrength[entry.Index] / cliffTotal : 1f / cliffs;
							layerWeights[entry.LayerIndex] += biomeWeight * cliffMax * split;
						}
					}
				}

				float ground = 1f - cliffMax;
				// A river's bar: sand where the water was slow, gravel where it was quick.
				if (bar.x > 0f && (sand != null || gravel != null))
				{
					float sandy = sand == null ? 0f : gravel == null ? 1f : Mathf.Clamp01(bar.y);
					float share = biomeWeight * ground * Mathf.Clamp01(bar.x);
					if (sand != null) layerWeights[sand.LayerIndex] += share * sandy;
					if (gravel != null) layerWeights[gravel.LayerIndex] += share * (1f - sandy);
					ground *= 1f - Mathf.Clamp01(bar.x);
				}
				BedSet bedSet = beds[biomeIndex];
				if (bedSet != null && submerged > 0f)
				{
					// The bed: the extra beds by their bands, the rest the riverbed's under a river and the lakebed's under a lake or the sea.
					int extras = bedSet.Extras.Length;
					for (int k = 0; k < extras; k++)
					{
						bedStrength[k] = bedSet.ExtraSources[k].Strength(in water);
					}
					float taken = BiomeBedBands.Split(in water, bedSet.Lakebed != null, bedSet.Riverbed != null, bedStrength, extras, bedShare,
						out float lakebedShare, out float riverbedShare);
					float bed = biomeWeight * ground * submerged;
					if (bedSet.Lakebed != null && lakebedShare > 0f) layerWeights[bedSet.Lakebed.LayerIndex] += bed * lakebedShare;
					if (bedSet.Riverbed != null && riverbedShare > 0f) layerWeights[bedSet.Riverbed.LayerIndex] += bed * riverbedShare;
					for (int k = 0; k < extras; k++)
					{
						if (bedShare[k] > 0f) layerWeights[bedSet.Extras[k].LayerIndex] += bed * bedShare[k];
					}
					ground *= 1f - submerged * taken;
				}
				if (ground <= 0f)
				{
					return;
				}
				GroundSlot[] slots = climaticSlots[biomeIndex];
				if (slots != null)
				{
					ShareInClimate(slots, biomeWeight * ground, worldX, worldZ, height, slope, climate, layerWeights);
					return;
				}

				// Details as a union, split in proportion to their strengths.
				float keep = 1f;
				float sum = 0f;
				for (int i = 0; i < entries.Count; i++)
				{
					SceneTerrainPalette.Entry entry = entries[i];
					if (entry.Role != PaletteRole.Detail)
					{
						continue;
					}
					float d = DetailStrength(entry, worldX, worldZ, height, slope);
					detailStrength[entry.Index] = d;
					keep *= 1f - d;
					sum += d;
				}
				float union = 1f - keep;
				if (sum > 0f)
				{
					for (int i = 0; i < entries.Count; i++)
					{
						SceneTerrainPalette.Entry entry = entries[i];
						if (entry.Role == PaletteRole.Detail)
						{
							layerWeights[entry.LayerIndex] += biomeWeight * ground * union * detailStrength[entry.Index] / sum;
						}
					}
				}

				float rest = biomeWeight * ground * (1f - union);
				if (main != null)
				{
					layerWeights[main.LayerIndex] += rest;
				}
				else if (sum > 0f)
				{
					// No base art kept: the details carry the whole biome rather than leave a hole.
					for (int i = 0; i < entries.Count; i++)
					{
						SceneTerrainPalette.Entry entry = entries[i];
						if (entry.Role == PaletteRole.Detail)
						{
							layerWeights[entry.LayerIndex] += rest * detailStrength[entry.Index] / sum;
						}
					}
				}
			}

			/// <summary>
			/// The ground share of a biome whose ground changes with the climate (decision 29, 2026-10-18): what <see cref="Share"/>
			/// does with its details and main layer, with each ground slot shared among its occupants by the climate first.
			/// </summary>
			/// <remarks>
			/// <para>
			/// <b>A slot's occupants partition it.</b> Each ground slot (the main layer, each detail) is held by the biome's own
			/// layer and by every climate variant's override standing in for it. In the variants' order, each takes its envelope's
			/// weight (<see cref="BiomeClimateVariant.TextureWeight"/>) of what the earlier ones left; the biome's own layer keeps
			/// the rest. So the shares of a slot sum to one, a variant's override fades in across its envelope's soft edge as the
			/// own layer fades out, and two variants never both take a whole slot. Each occupant's share is then multiplied by its
			/// own climate band (<see cref="TerrainTextureLayer.ClimateWeight"/>).
			/// </para>
			/// <para>
			/// <b>Details</b>: an occupant's strength is its share times its noise inside its bands; a slot's strength is the sum
			/// of its occupants' (never more than one), and the slots overlap as a union exactly as <see cref="Share"/>'s details
			/// do, split in proportion to the occupants' strengths. <b>Main</b>: what the details leave goes to the main slot's
			/// occupants by their shares; whatever their bands leave over (a main layer outside its climate band) goes to the
			/// details in proportion to their strengths, else to the biome's own main layer, so the ground never has a hole.
			/// </para>
			/// <para>
			/// With no climate known (<see cref="NoClimate"/>) every band passes and no override is painted, so the ground is the
			/// biome's own as though nothing were banded.
			/// </para>
			/// </remarks>
			private void ShareInClimate(GroundSlot[] slots, float share, float worldX, float worldZ, float height, float slope, Vector2 climate, float[] layerWeights)
			{
				float t = climate.x, h = climate.y;
				GroundSlot mainSlot = null;
				foreach (GroundSlot slot in slots)
				{
					float left = 1f;
					foreach (SceneTerrainPalette.Entry entry in slot.Variants)
					{
						float claim = entry.Variant.TextureWeight(t, h);
						climateShare[entry.Index] = left * claim * entry.Source.ClimateWeight(t, h);
						left *= 1f - claim;
					}
					if (slot.Own != null)
					{
						climateShare[slot.Own.Index] = left * slot.Own.Source.ClimateWeight(t, h);
					}
					if (slot.Ground == 0)
					{
						mainSlot = slot;
					}
				}

				// Details as a union of slots, split in proportion to their occupants' strengths.
				float keep = 1f;
				float sum = 0f;
				foreach (GroundSlot slot in slots)
				{
					if (slot.Ground == 0)
					{
						continue;
					}
					float d = 0f;
					if (slot.Own != null)
					{
						d += detailStrength[slot.Own.Index] = climateShare[slot.Own.Index] * DetailStrength(slot.Own, worldX, worldZ, height, slope);
					}
					foreach (SceneTerrainPalette.Entry entry in slot.Variants)
					{
						d += detailStrength[entry.Index] = climateShare[entry.Index] * DetailStrength(entry, worldX, worldZ, height, slope);
					}
					d = Mathf.Clamp01(d);
					keep *= 1f - d;
					sum += d;
				}
				float union = 1f - keep;
				if (sum > 0f)
				{
					foreach (GroundSlot slot in slots)
					{
						if (slot.Ground == 0)
						{
							continue;
						}
						if (slot.Own != null)
						{
							layerWeights[slot.Own.LayerIndex] += share * union * detailStrength[slot.Own.Index] / sum;
						}
						foreach (SceneTerrainPalette.Entry entry in slot.Variants)
						{
							layerWeights[entry.LayerIndex] += share * union * detailStrength[entry.Index] / sum;
						}
					}
				}

				// The main slot's occupants take what the details leave, by their shares.
				float rest = share * (1f - union);
				float placed = 0f;
				SceneTerrainPalette.Entry widest = null;
				if (mainSlot != null)
				{
					if (mainSlot.Own != null)
					{
						layerWeights[mainSlot.Own.LayerIndex] += rest * climateShare[mainSlot.Own.Index];
						placed += climateShare[mainSlot.Own.Index];
					}
					foreach (SceneTerrainPalette.Entry entry in mainSlot.Variants)
					{
						layerWeights[entry.LayerIndex] += rest * climateShare[entry.Index];
						placed += climateShare[entry.Index];
						if (widest == null || climateShare[entry.Index] > climateShare[widest.Index])
						{
							widest = entry;
						}
					}
				}
				float over = rest * Mathf.Clamp01(1f - placed);
				if (over <= 0f)
				{
					return;
				}
				if (sum > 0f)
				{
					// Outside the main layers' bands (or with no main art at all), the details carry the ground.
					foreach (GroundSlot slot in slots)
					{
						if (slot.Ground == 0)
						{
							continue;
						}
						if (slot.Own != null)
						{
							layerWeights[slot.Own.LayerIndex] += over * detailStrength[slot.Own.Index] / sum;
						}
						foreach (SceneTerrainPalette.Entry entry in slot.Variants)
						{
							layerWeights[entry.LayerIndex] += over * detailStrength[entry.Index] / sum;
						}
					}
				}
				else if (mainSlot?.Own != null)
				{
					layerWeights[mainSlot.Own.LayerIndex] += over;
				}
				else if (widest != null)
				{
					layerWeights[widest.LayerIndex] += over;
				}
			}

			/// <summary>
			/// A detail layer's strength at a point, 0 … 1: its noise, sharpened, inside its bands.
			/// </summary>
			/// <remarks>
			/// <see cref="TerrainTextureLayer.blendNoiseScale"/> is read as the size of a patch in
			/// metres, and <see cref="TerrainTextureLayer.blendSharpness"/> as the exponent on the
			/// noise: 1 is soft, broad drifts; higher is fewer, harder-edged patches.
			/// </remarks>
			private float DetailStrength(SceneTerrainPalette.Entry entry, float worldX, float worldZ, float height, float slope)
			{
				TerrainTextureLayer source = entry.Source;
				float scale = Mathf.Max(1f, source.blendNoiseScale);
				Vector2 offset = noiseOffset[entry.Index];
				float x = worldX / scale + offset.x;
				float z = worldZ / scale + offset.y;
				// Two octaves; fBm pulls toward the middle, so it is stretched back out to fill 0 … 1.
				float n = Mathf.PerlinNoise(x, z) * 0.67f + Mathf.PerlinNoise(x * 2.03f + 17.1f, z * 2.03f - 9.7f) * 0.33f;
				n = Mathf.Clamp01((n - 0.5f) * 1.6f + 0.5f);
				float strength = Mathf.Pow(n, Mathf.Max(0.1f, source.blendSharpness));

				if (source.useHeightConstraint)
				{
					strength *= Band(height, source.heightRange.min, source.heightRange.max, source.heightFalloff);
				}
				if (source.useSlopeConstraint)
				{
					strength *= Band(slope, source.slopeRange.min, source.slopeRange.max, source.slopeFalloff);
				}
				if (source.useHeatBand && entry.Role == PaletteRole.Detail)
				{
					strength *= source.HeatWeight(heat);
				}
				return Mathf.Clamp01(strength);
			}
		}

		/// <summary>1 inside [min, max], easing to 0 over <paramref name="falloff"/> outside it.</summary>
		public static float Band(float value, float min, float max, float falloff)
		{
			if (value >= min && value <= max)
			{
				return 1f;
			}
			if (falloff <= 0f)
			{
				return 0f;
			}
			float distance = value < min ? min - value : value - max;
			return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / falloff));
		}
	}
}
#endif
