#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Which biome lies where in a scene being generated, and how strongly each one reaches every
	/// point: the one answer the splat painter, the scatter and the scene's baked biome map share.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Asked of the planet, cell by cell.</b> Each cell carries the biome
	/// <see cref="PlanetClimateField.BiomeAt(Vector3, float, out PlanetSurfacePoint)"/> chooses at
	/// its centre, from the direction the atlas footprint gives it and the ground the scene
	/// actually has there. The globe, the scene names and the ground therefore cannot disagree
	/// about a place, which is the whole reason the field is shared. The ground is read from the
	/// finished heightmap, not the planet's smooth surface, so a ridge too small for the globe
	/// to show is still colder than the valley beside it.
	/// </para>
	/// <para>
	/// <b>Soft edges are blurred one-hot maps, not a neighbour search.</b> Every biome present gets
	/// its own coverage grid (1 in its cells, 0 elsewhere), blurred over
	/// <see cref="BlendMetres"/>; a point reads every grid bilinearly at a position nudged by
	/// low-frequency noise, so a boundary meanders rather than following the cell grid. The
	/// weights at any point sum to one by construction — each cell is one-hot and a blur is an
	/// average — so nothing downstream has to renormalise across biomes.
	/// </para>
	/// <para>
	/// The dominant biome is the arg-max of those same weights, so the biome a point is
	/// <em>called</em> and the one painted most strongly on it are always the same.
	/// </para>
	/// </remarks>
	public sealed class SceneBiomeField
	{
		/// <summary>No biome fitted the cell: no template registered, or none allowed on this world.</summary>
		public const byte None = byte.MaxValue;

		/// <summary>Finest spacing between the points the planet is asked about, in metres.</summary>
		public const float PreferredCellMetres = 16f;

		/// <summary>
		/// Most cells along either side. A 2 km scene keeps 16 m cells; a 20 km one is coarsened
		/// so it costs the same quarter-million planet samples rather than a million and a half.
		/// </summary>
		public const int MaximumCellsPerSide = 512;

		/// <summary>Half-width of a soft boundary between two biomes, in metres.</summary>
		public const float DefaultBlendMetres = 48f;

		/// <summary>Grid columns, along world X.</summary>
		public readonly int Width;
		/// <summary>Grid rows, along world Z.</summary>
		public readonly int Height;
		/// <summary>Metres per cell.</summary>
		public readonly float CellMetres;
		/// <summary>World X/Z of the grid's south-west corner.</summary>
		public readonly Vector2 Origin;
		/// <summary>World X/Z extent of the grid.</summary>
		public readonly Vector2 Size;
		/// <summary>The half-width boundaries were blurred over, in metres.</summary>
		public readonly float BlendMetres;

		/// <summary>Every biome present in the scene, in key order so indices are stable between runs.</summary>
		public readonly IReadOnlyList<BiomeTemplate> Biomes;

		private readonly byte[] cells;
		private readonly float[][] coverage;
		private readonly float warpMetres;
		private readonly float warpFrequency;
		private readonly Vector2 warpOffset;

		private SceneBiomeField(int width, int height, float cellMetres, Vector2 origin, float blendMetres,
			List<BiomeTemplate> biomes, byte[] cells, uint seed)
		{
			Width = width;
			Height = height;
			CellMetres = cellMetres;
			Origin = origin;
			Size = new Vector2(width * cellMetres, height * cellMetres);
			BlendMetres = blendMetres;
			Biomes = biomes;
			this.cells = cells;

			/* The nudge is as wide as the blend and slow enough that a boundary wanders over a few
			 * hundred metres: wider and biomes would leak into one another, faster and every edge
			 * would fray into speckle. */
			warpMetres = Mathf.Max(cellMetres, blendMetres);
			warpFrequency = 1f / Mathf.Max(64f, blendMetres * 6f);
			var rng = new System.Random(unchecked((int)seed));
			warpOffset = new Vector2((float)rng.NextDouble() * 4096f, (float)rng.NextDouble() * 4096f);

			coverage = new float[biomes.Count][];
			// Two box passes of radius r make a tent reaching 2r, so r is half the blend.
			int radius = Mathf.Max(0, Mathf.RoundToInt(blendMetres / (2f * cellMetres)));
			for (int b = 0; b < biomes.Count; b++)
			{
				var grid = new float[width * height];
				for (int i = 0; i < grid.Length; i++)
				{
					grid[i] = cells[i] == b ? 1f : 0f;
				}
				coverage[b] = radius > 0 ? Blur(grid, width, height, radius) : grid;
			}
		}

		/// <summary>
		/// Reads the planet under every cell of a scene.
		/// </summary>
		/// <param name="request">The scene being cut: its body, footprint and radius.</param>
		/// <param name="plan">Its tile grid, for how far it reaches.</param>
		/// <param name="sceneAltitude">
		/// Scene metres above sea level at a scene position (east, north from the scene's centre).
		/// The generator passes its finished terrain; tests pass anything.
		/// </param>
		/// <param name="system">The solar system, for starlight and the world's conditions. Null is Earth-like.</param>
		/// <param name="blendMetres">Half-width of a soft boundary.</param>
		public static SceneBiomeField Build(SceneGenerationRequest request, TerrainTilePlan plan,
			Func<float, float, float> sceneAltitude, SolarSystemProfile system, float blendMetres = DefaultBlendMetres)
		{
			return Build(request, plan.WidthMetres, plan.DepthMetres, sceneAltitude, system, blendMetres);
		}

		/// <summary>
		/// Reads the planet under every cell of a rectangle centred on the scene — wider than the
		/// scene itself for the backdrop, which shows the ground out to the horizon.
		/// </summary>
		/// <param name="widthMetres">Extent along world X, centred on the scene's origin.</param>
		/// <param name="depthMetres">Extent along world Z, centred on the scene's origin.</param>
		public static SceneBiomeField Build(SceneGenerationRequest request, float widthMetres, float depthMetres,
			Func<float, float, float> sceneAltitude, SolarSystemProfile system, float blendMetres = DefaultBlendMetres)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}
			if (sceneAltitude == null)
			{
				throw new ArgumentNullException(nameof(sceneAltitude));
			}

			widthMetres = Mathf.Max(1f, widthMetres);
			depthMetres = Mathf.Max(1f, depthMetres);
			float cellMetres = Mathf.Max(PreferredCellMetres, Mathf.Max(widthMetres, depthMetres) / MaximumCellsPerSide);
			int width = Mathf.Max(1, Mathf.CeilToInt(widthMetres / cellMetres));
			int height = Mathf.Max(1, Mathf.CeilToInt(depthMetres / cellMetres));
			// The grid covers the scene exactly; cells stretch by the remainder rather than overhang.
			cellMetres = Mathf.Max(widthMetres / width, depthMetres / height);
			var origin = new Vector2(-widthMetres * 0.5f, -depthMetres * 0.5f);

			PlanetClimateField field = PlanetClimateField.For(system, request.Body);
			AtlasFootprint footprint = request.Footprint;
			double radiusKm = request.ResolvedRadiusKm;
			/* Scene metres back to the planet's: the scene IS the planet's ground scaled by this,
			 * local detail included, so dividing gives the altitude the climate expects. */
			float verticalScale = request.VerticalScale > 1e-6f ? request.VerticalScale : 1f;

			var chosen = new BiomeTemplate[width * height];
			var present = new HashSet<BiomeTemplate>();
			for (int z = 0; z < height; z++)
			{
				float north = origin.y + (z + 0.5f) * cellMetres;
				for (int x = 0; x < width; x++)
				{
					float east = origin.x + (x + 0.5f) * cellMetres;
					Vector3 direction = AtlasGeometry.SceneToUnit(footprint, east / 1000.0, north / 1000.0, radiusKm).ToVector3();
					float altitude = sceneAltitude(east, north) / verticalScale;
					BiomeTemplate biome = field.BiomeAt(direction, altitude, out _);
					chosen[z * width + x] = biome;
					if (biome != null)
					{
						present.Add(biome);
					}
				}
			}

			var biomes = new List<BiomeTemplate>(present);
			biomes.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
			if (biomes.Count >= None)
			{
				// 255 distinct biomes in one scene is not a scene, it is a bug upstream.
				throw new InvalidOperationException($"'{request.SceneName}' resolved {biomes.Count} distinct biomes; at most {None - 1} fit the field.");
			}
			var index = new Dictionary<BiomeTemplate, byte>(biomes.Count);
			for (int i = 0; i < biomes.Count; i++)
			{
				index[biomes[i]] = (byte)i;
			}
			var cells = new byte[chosen.Length];
			for (int i = 0; i < chosen.Length; i++)
			{
				cells[i] = chosen[i] != null ? index[chosen[i]] : None;
			}

			uint seed = request.Body != null ? request.Body.ResolvedTerrainSeed : 1u;
			seed ^= unchecked((uint)(request.SceneName ?? string.Empty).GetDeterministicHashCode());
			return new SceneBiomeField(width, height, cellMetres, origin, Mathf.Max(0f, blendMetres), biomes, cells, seed);
		}

		/// <summary>
		/// A field from cells already chosen, for tests and for tools that paint biomes by hand.
		/// </summary>
		/// <param name="cellIndices">One index into <paramref name="biomes"/> per cell, row-major from the south-west; <see cref="None"/> for none.</param>
		public static SceneBiomeField FromCells(int width, int height, float cellMetres, Vector2 origin,
			IReadOnlyList<BiomeTemplate> biomes, byte[] cellIndices, float blendMetres = DefaultBlendMetres, uint seed = 1u)
		{
			if (cellIndices == null || cellIndices.Length != width * height)
			{
				throw new ArgumentException("One cell index per cell is required.", nameof(cellIndices));
			}
			return new SceneBiomeField(width, height, cellMetres, origin, blendMetres,
				new List<BiomeTemplate>(biomes), (byte[])cellIndices.Clone(), seed);
		}

		/// <summary>The biome index of a cell, or <see cref="None"/>.</summary>
		public byte CellIndex(int x, int z)
		{
			x = Mathf.Clamp(x, 0, Width - 1);
			z = Mathf.Clamp(z, 0, Height - 1);
			return cells[z * Width + x];
		}

		/// <summary>
		/// How strongly each biome reaches a world position: one weight per entry of
		/// <see cref="Biomes"/>, summing to one wherever any biome was chosen nearby.
		/// </summary>
		/// <param name="worldX">World X (east at a heading of 0).</param>
		/// <param name="worldZ">World Z (north at a heading of 0).</param>
		/// <param name="weights">At least <c>Biomes.Count</c> long; filled, never resized.</param>
		public void WeightsAt(float worldX, float worldZ, float[] weights)
		{
			Warp(ref worldX, ref worldZ);
			float gx = (worldX - Origin.x) / CellMetres - 0.5f;
			float gz = (worldZ - Origin.y) / CellMetres - 0.5f;
			int x0 = Mathf.FloorToInt(gx);
			int z0 = Mathf.FloorToInt(gz);
			float fx = gx - x0;
			float fz = gz - z0;
			int xa = Mathf.Clamp(x0, 0, Width - 1), xb = Mathf.Clamp(x0 + 1, 0, Width - 1);
			int za = Mathf.Clamp(z0, 0, Height - 1), zb = Mathf.Clamp(z0 + 1, 0, Height - 1);

			for (int b = 0; b < coverage.Length; b++)
			{
				float[] grid = coverage[b];
				float south = Mathf.Lerp(grid[za * Width + xa], grid[za * Width + xb], fx);
				float north = Mathf.Lerp(grid[zb * Width + xa], grid[zb * Width + xb], fx);
				weights[b] = Mathf.Lerp(south, north, fz);
			}
		}

		/// <summary>The index of the biome that reaches a world position most strongly, or -1 where none does.</summary>
		public int DominantIndexAt(float worldX, float worldZ, float[] scratch)
		{
			WeightsAt(worldX, worldZ, scratch);
			int best = -1;
			float bestWeight = 0f;
			for (int b = 0; b < coverage.Length; b++)
			{
				if (scratch[b] > bestWeight)
				{
					bestWeight = scratch[b];
					best = b;
				}
			}
			return best;
		}

		/// <summary>The biome that reaches a world position most strongly, or null.</summary>
		public BiomeTemplate DominantAt(float worldX, float worldZ)
		{
			int best = DominantIndexAt(worldX, worldZ, new float[Math.Max(1, coverage.Length)]);
			return best >= 0 ? Biomes[best] : null;
		}

		/// <summary>The share of the scene's cells each biome holds, in <see cref="Biomes"/> order.</summary>
		public float[] Coverage()
		{
			var share = new float[Biomes.Count];
			if (cells.Length == 0)
			{
				return share;
			}
			for (int i = 0; i < cells.Length; i++)
			{
				if (cells[i] != None)
				{
					share[cells[i]] += 1f;
				}
			}
			for (int b = 0; b < share.Length; b++)
			{
				share[b] /= cells.Length;
			}
			return share;
		}

		/// <summary>
		/// The field as a <see cref="SceneBiomeMap"/>'s grid: template IDs, row-major from the
		/// south-west, at the field's own resolution. The map stores what the cells chose, not the
		/// blur — a runtime lookup wants an identity, and an identity has no half-way.
		/// </summary>
		public void WriteTo(SceneBiomeMap map)
		{
			if (map == null)
			{
				throw new ArgumentNullException(nameof(map));
			}
			var ids = new int[cells.Length];
			for (int i = 0; i < cells.Length; i++)
			{
				ids[i] = cells[i] != None ? BiomeRegistry.IDOf(Biomes[cells[i]]) : 0;
			}
			map.Set(Width, Height, ids, Origin, Size);
		}

		private void Warp(ref float x, ref float z)
		{
			if (warpMetres <= 0f)
			{
				return;
			}
			float u = x * warpFrequency + warpOffset.x;
			float v = z * warpFrequency + warpOffset.y;
			x += (Mathf.PerlinNoise(u, v) - 0.5f) * 2f * warpMetres;
			z += (Mathf.PerlinNoise(u + 137.1f, v - 59.3f) - 0.5f) * 2f * warpMetres;
		}

		/// <summary>Separable box blur, edge-clamped, run twice so the falloff is a tent and not a step.</summary>
		private static float[] Blur(float[] grid, int width, int height, int radius)
		{
			float[] result = (float[])grid.Clone();
			float[] rows = new float[grid.Length];
			for (int pass = 0; pass < 2; pass++)
			{
				BoxRows(result, rows, width, height, radius);
				BoxColumns(rows, result, width, height, radius);
			}
			return result;
		}

		private static void BoxRows(float[] source, float[] target, int width, int height, int radius)
		{
			float span = 2 * radius + 1;
			for (int z = 0; z < height; z++)
			{
				int row = z * width;
				float sum = 0f;
				for (int k = -radius; k <= radius; k++)
				{
					sum += source[row + Mathf.Clamp(k, 0, width - 1)];
				}
				for (int x = 0; x < width; x++)
				{
					target[row + x] = sum / span;
					sum += source[row + Mathf.Min(x + radius + 1, width - 1)] - source[row + Mathf.Max(x - radius, 0)];
				}
			}
		}

		private static void BoxColumns(float[] source, float[] target, int width, int height, int radius)
		{
			float span = 2 * radius + 1;
			for (int x = 0; x < width; x++)
			{
				float sum = 0f;
				for (int k = -radius; k <= radius; k++)
				{
					sum += source[Mathf.Clamp(k, 0, height - 1) * width + x];
				}
				for (int z = 0; z < height; z++)
				{
					target[z * width + x] = sum / span;
					sum += source[Mathf.Min(z + radius + 1, height - 1) * width + x] - source[Mathf.Max(z - radius, 0) * width + x];
				}
			}
		}
	}
}
#endif
