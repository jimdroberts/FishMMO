#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		/// A river's bar at a world position (x, z): how much of the point is bar, 0 … 1, and how sandy, 0
		/// gravel … 1 sand. Painted with the palette's sediment layers. Null for none.
		/// </summary>
		public Func<float, float, Vector2> BarAt;

		/// <summary>Mixed into every layer's noise so two scenes with the same biomes do not share a pattern.</summary>
		public uint Seed = 1u;
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
			var weigher = new Weigher(palette, field, options);
			TerrainLayer[] layers = ToArray(palette.Layers);

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
				var texelWeights = new float[layers.Length];
				Vector3 origin = terrain.transform.position;
				Vector3 size = data.size;

				for (int z = 0; z < resolution; z++)
				{
					float v = (z + 0.5f) / resolution;
					float worldZ = origin.z + v * size.z;
					for (int x = 0; x < resolution; x++)
					{
						float u = (x + 0.5f) / resolution;
						float worldX = origin.x + u * size.x;
						// GetInterpolatedHeight and GetSteepness take (x, z) normalised; height in metres.
						float worldY = origin.y + data.GetInterpolatedHeight(u, v);
						float slope = data.GetSteepness(u, v);

						if (!weigher.Weigh(worldX, worldY, worldZ, slope, texelWeights))
						{
							report.Unreached++;
						}
						for (int layer = 0; layer < layers.Length; layer++)
						{
							splat[z, x, layer] = texelWeights[layer];
						}
					}
				}
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
			private readonly float[] biomeWeights;
			private readonly float[] detailStrength;
			private readonly Vector2[] noiseOffset;

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
			}

			/// <summary>
			/// Fills one weight per palette layer at a world point, summing to one. False when no
			/// biome reached the point, in which case the weights are whatever the nearest reach
			/// left (all zero only if the scene has no biome at all).
			/// </summary>
			public bool Weigh(float worldX, float worldY, float worldZ, float slopeDegrees, float[] layerWeights)
			{
				Array.Clear(layerWeights, 0, layerWeights.Length);
				field.WeightsAt(worldX, worldZ, biomeWeights);
				float height = options.NormalizedHeight != null ? options.NormalizedHeight(worldX, worldY, worldZ) : 0.5f;
				float submerged = options.HasLiquidWater && options.ShoreFadeMetres > 0f
					? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-worldY / options.ShoreFadeMetres))
					: 0f;
				if (options.InlandWaterSurface != null)
				{
					// Under a river or lake: its bed, from just below its surface.
					float inland = options.InlandWaterSurface(worldX, worldZ);
					if (!float.IsNegativeInfinity(inland))
					{
						float under = float.IsPositiveInfinity(inland) ? 1f
							: Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((inland - worldY) / Mathf.Max(0.05f, options.InlandShoreFadeMetres)));
						submerged = Mathf.Max(submerged, under);
					}
				}

				Vector2 bar = options.BarAt != null ? options.BarAt(worldX, worldZ) : Vector2.zero;
				float reached = 0f;
				for (int b = 0; b < field.Biomes.Count; b++)
				{
					float biomeWeight = biomeWeights[b];
					if (biomeWeight <= 1e-4f)
					{
						continue;
					}
					reached += biomeWeight;
					Share(b, biomeWeight, worldX, worldZ, height, slopeDegrees, submerged, bar, layerWeights);
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
			private void Share(int biomeIndex, float biomeWeight, float worldX, float worldZ, float height, float slope, float submerged, Vector2 bar, float[] layerWeights)
			{
				IReadOnlyList<SceneTerrainPalette.Entry> entries = palette.EntriesFor(biomeIndex);
				SceneTerrainPalette.Entry main = null;
				SceneTerrainPalette.Entry under = null;
				SceneTerrainPalette.Entry sand = null;
				SceneTerrainPalette.Entry gravel = null;
				float cliffTotal = 0f;
				float cliffMax = 0f;

				// Cliffs: each by its own slope (and optional height) curve; together they take the
				// largest of their weights, so two overlapping cliff layers do not double the rock.
				for (int i = 0; i < entries.Count; i++)
				{
					SceneTerrainPalette.Entry entry = entries[i];
					switch (entry.Role)
					{
						case PaletteRole.Main: main ??= entry; break;
						case PaletteRole.Submerged: under ??= entry; break;
						case PaletteRole.Sediment:
							if (entry.Slot == SceneTerrainPalette.SlotSand) sand ??= entry;
							else gravel ??= entry;
							break;
						case PaletteRole.Cliff:
							float w = entry.Source is CliffTextureLayer cliff ? cliff.GetCliffWeight(slope, height) : 0f;
							detailStrength[entry.Index] = w;
							cliffTotal += w;
							cliffMax = Mathf.Max(cliffMax, w);
							break;
					}
				}
				if (cliffTotal > 0f)
				{
					for (int i = 0; i < entries.Count; i++)
					{
						SceneTerrainPalette.Entry entry = entries[i];
						if (entry.Role == PaletteRole.Cliff)
						{
							layerWeights[entry.LayerIndex] += biomeWeight * cliffMax * detailStrength[entry.Index] / cliffTotal;
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
				if (under != null && submerged > 0f)
				{
					layerWeights[under.LayerIndex] += biomeWeight * ground * submerged;
					ground *= 1f - submerged;
				}
				if (ground <= 0f)
				{
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
