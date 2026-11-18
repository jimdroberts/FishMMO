#if UNITY_EDITOR
using System;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How the ground wears across one scene: its biomes' terrain process profiles blended into one
	/// smooth field, the rain the climate drops on it, and the rock under it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What the shaping passes read.</b> Erosion, drainage and the plateau pass each ask a point
	/// how readily its surface washes away, how much rain falls on it and how hard the rock is at
	/// the height they have cut it down to. This answers all three from three sources kept apart on
	/// purpose: the biome's way of wearing (<see cref="TerrainProcess"/>), the climate's rain
	/// (<see cref="MoistureModel.Precipitation"/>) and the planet's rock (<see cref="PlanetGeology"/>).
	/// </para>
	/// <para>
	/// <b>Blended wide.</b> The biome field's own boundaries blend over <see cref="SceneBiomeField.DefaultBlendMetres"/>,
	/// which suits textures; erosion acting on a profile that switches over 48 m would carve that line
	/// into the ground as a step in gully density. So the profiles are box-blurred again over
	/// <see cref="BlendMetres"/>, and erosion's character changes across a hillside, not at a line.
	/// </para>
	/// <para>
	/// <b>Chosen from the unshaped ground.</b> The biomes are read from the ground before any pass
	/// changes it, since the passes need the profile to start; the finished scene's biomes are read
	/// again from the finished ground when it is painted, as they always were.
	/// </para>
	/// <para>Safe to share between threads once built.</para>
	/// </remarks>
	public sealed class SceneTerrainProcess
	{
		/// <summary>Metres between the field's cells.</summary>
		public const float CellMetres = 32f;

		/// <summary>Half-width of the blend between two biomes' ways of wearing, in metres.</summary>
		public const float BlendMetres = 200f;

		/// <summary>Rain grid points per side over the scene, as <see cref="ScenePlacementClimate.GridSize"/> lays its moisture.</summary>
		public const int RainGridSize = ScenePlacementClimate.GridSize;

		/// <summary>Cells west to east and south to north.</summary>
		public readonly int Width;
		public readonly int Depth;

		/// <summary>The scene's extent in metres, centred on its origin.</summary>
		public readonly float WidthMetres;
		public readonly float DepthMetres;

		/// <summary>The biomes the profiles were blended from, read from the unshaped ground.</summary>
		public readonly SceneBiomeField Biomes;

		/// <summary>The planet's rock, at the radius the scene is cut at.</summary>
		public readonly PlanetGeology Geology;

		private readonly TerrainProcess[] cells;
		private readonly float[] rain;
		/// <summary>Sea-level temperature on the rain grid, on the climate's −1 … 1 scale.</summary>
		private readonly float[] seaTemperature;
		/// <summary>Climate-scale cooling per scene metre of altitude.</summary>
		private readonly float lapse;
		private readonly AtlasFootprint footprint;
		private readonly double radiusKm;

		private SceneTerrainProcess(SceneBiomeField biomes, PlanetGeology geology, TerrainProcess[] cells, int width, int depth,
			float widthMetres, float depthMetres, float[] rain, float[] seaTemperature, float lapse, in AtlasFootprint footprint, double radiusKm)
		{
			this.seaTemperature = seaTemperature;
			this.lapse = lapse;
			Biomes = biomes;
			Geology = geology;
			this.cells = cells;
			Width = width;
			Depth = depth;
			WidthMetres = widthMetres;
			DepthMetres = depthMetres;
			this.rain = rain;
			this.footprint = footprint;
			this.radiusKm = radiusKm;
		}

		/// <summary>
		/// Reads the scene's biomes, climate and rock. Main thread: it asks the biome assets and the body.
		/// </summary>
		/// <param name="request">The scene being cut.</param>
		/// <param name="plan">Its tile grid, for how far it reaches.</param>
		/// <param name="ground">Scene metres above sea level at (east, north): the ground before any pass has shaped it.</param>
		/// <param name="system">The solar system, for the climate and the world's conditions.</param>
		public static SceneTerrainProcess Build(SceneGenerationRequest request, TerrainTilePlan plan,
			Func<float, float, float> ground, SolarSystemProfile system)
		{
			if (request == null)
			{
				throw new ArgumentNullException(nameof(request));
			}
			SceneBiomeField biomes = SceneBiomeField.Build(request, plan, ground, system);
			PlanetClimateField climate = PlanetClimateField.For(system, request.Body);
			PlanetGeology geology = PlanetGeology.For(request.Body, climate.Conditions, request.ResolvedRadiusKm);
			float verticalScale = Mathf.Max(1e-6f, request.VerticalScale);
			return Build(biomes, geology, plan.WidthMetres, plan.DepthMetres, request.Footprint, request.ResolvedRadiusKm,
				(direction, latitude) => climate.Moisture.Precipitation(latitude, direction, climate.Moisture.Terrain),
				(direction, latitude) => climate.TemperatureAt(latitude, direction, 0f), climate.LapsePerMetre / verticalScale);
		}

		/// <summary>The same from its parts, for tests.</summary>
		/// <param name="rainAt">Rain, 0 … 1, at a unit direction and its latitude in degrees.</param>
		/// <param name="seaLevelTemperatureAt">Sea-level temperature on the climate scale at a direction and latitude; null for 0 (temperate) everywhere.</param>
		/// <param name="lapsePerSceneMetre">Climate-scale cooling per scene metre of altitude.</param>
		public static SceneTerrainProcess Build(SceneBiomeField biomes, PlanetGeology geology, float widthMetres, float depthMetres,
			in AtlasFootprint footprint, double radiusKm, Func<Vector3, double, float> rainAt,
			Func<Vector3, double, float> seaLevelTemperatureAt = null, float lapsePerSceneMetre = 0f)
		{
			int width = Mathf.Max(2, Mathf.CeilToInt(widthMetres / CellMetres) + 1);
			int depth = Mathf.Max(2, Mathf.CeilToInt(depthMetres / CellMetres) + 1);

			// Each cell: its biomes' profiles, weighted as the biome field weights them.
			var processes = new TerrainProcess[biomes != null ? biomes.Biomes.Count : 0];
			for (int b = 0; b < processes.Length; b++)
			{
				BiomeTemplate biome = biomes.Biomes[b];
				processes[b] = biome != null ? biome.ResolvedTerrainProcess : TerrainProcess.Temperate;
			}
			var cells = new TerrainProcess[width * depth];
			var weights = new float[Mathf.Max(1, processes.Length)];
			for (int z = 0; z < depth; z++)
			{
				float north = CellNorth(z, depth, depthMetres);
				for (int x = 0; x < width; x++)
				{
					float east = CellEast(x, width, widthMetres);
					TerrainProcess sum = TerrainProcess.Zero;
					float total = 0f;
					if (processes.Length > 0)
					{
						biomes.WeightsAt(east, north, weights);
						for (int b = 0; b < processes.Length; b++)
						{
							if (weights[b] > 0f)
							{
								sum.Accumulate(processes[b], weights[b]);
								total += weights[b];
							}
						}
					}
					cells[z * width + x] = total > 1e-6f ? sum.Scaled(1f / total) : TerrainProcess.Temperate;
				}
			}
			Blur(cells, width, depth, Mathf.RoundToInt(BlendMetres / CellMetres));

			// Rain from the climate's moisture walks, on a coarse grid: it changes over kilometres.
			var rain = new float[RainGridSize * RainGridSize];
			var seaTemperature = new float[RainGridSize * RainGridSize];
			for (int j = 0; j < RainGridSize; j++)
			{
				double zKm = (j / (double)(RainGridSize - 1) - 0.5) * depthMetres / 1000.0;
				for (int i = 0; i < RainGridSize; i++)
				{
					double xKm = (i / (double)(RainGridSize - 1) - 0.5) * widthMetres / 1000.0;
					Vector3 direction = AtlasGeometry.SceneToUnit(footprint, xKm, zKm, radiusKm).ToVector3().normalized;
					double latitude = PlanetClimateField.LatitudeOf(direction);
					rain[j * RainGridSize + i] = rainAt != null ? Mathf.Clamp01(rainAt(direction, latitude)) : 0f;
					seaTemperature[j * RainGridSize + i] = seaLevelTemperatureAt != null ? seaLevelTemperatureAt(direction, latitude) : 0f;
				}
			}

			return new SceneTerrainProcess(biomes, geology, cells, width, depth, widthMetres, depthMetres, rain, seaTemperature, lapsePerSceneMetre, footprint, radiusKm);
		}

		/// <summary>How the ground wears at a scene position: bilinear between the blended cells.</summary>
		public TerrainProcess ProcessAt(float eastMetres, float northMetres)
		{
			float gx = Mathf.Clamp((eastMetres + WidthMetres * 0.5f) / WidthMetres * (Width - 1), 0f, Width - 1);
			float gz = Mathf.Clamp((northMetres + DepthMetres * 0.5f) / DepthMetres * (Depth - 1), 0f, Depth - 1);
			int x0 = Mathf.Min((int)gx, Width - 2), z0 = Mathf.Min((int)gz, Depth - 2);
			float fx = gx - x0, fz = gz - z0;
			TerrainProcess result = TerrainProcess.Zero;
			result.Accumulate(cells[z0 * Width + x0], (1f - fx) * (1f - fz));
			result.Accumulate(cells[z0 * Width + x0 + 1], fx * (1f - fz));
			result.Accumulate(cells[(z0 + 1) * Width + x0], (1f - fx) * fz);
			result.Accumulate(cells[(z0 + 1) * Width + x0 + 1], fx * fz);
			return result;
		}

		/// <summary>The rain at a scene position, 0 dry … 1 the wettest the climate makes: bilinear on the rain grid.</summary>
		public float RainAt(float eastMetres, float northMetres) => OnRainGrid(rain, eastMetres, northMetres);

		/// <summary>
		/// The mean temperature at a scene position and altitude (scene metres), on the climate's −1 … 1
		/// scale: the planet's own at sea level, cooled by its lapse rate — what
		/// <see cref="PlanetClimateField.TemperatureAt"/> gives, read off a grid.
		/// </summary>
		public float TemperatureAt(float eastMetres, float northMetres, float altitudeMetres)
		{
			return Mathf.Clamp(OnRainGrid(seaTemperature, eastMetres, northMetres) - Mathf.Max(0f, altitudeMetres) * lapse, -1f, 1f);
		}

		private float OnRainGrid(float[] values, float eastMetres, float northMetres)
		{
			float[] rain = values;
			int n = RainGridSize;
			float u = Mathf.Clamp((eastMetres / WidthMetres + 0.5f) * (n - 1), 0f, n - 1);
			float v = Mathf.Clamp((northMetres / DepthMetres + 0.5f) * (n - 1), 0f, n - 1);
			int i = Mathf.Min((int)u, n - 2), j = Mathf.Min((int)v, n - 2);
			float fu = u - i, fv = v - j;
			int a = j * n + i;
			return Mathf.Lerp(Mathf.Lerp(rain[a], rain[a + 1], fu), Mathf.Lerp(rain[a + n], rain[a + n + 1], fu), fv);
		}

		/// <summary>The rock under a scene position, all the way down (see <see cref="GeologyColumn"/>).</summary>
		public GeologyColumn ColumnAt(float eastMetres, float northMetres)
		{
			Vector3 direction = AtlasGeometry.SceneToUnit(footprint, eastMetres / 1000.0, northMetres / 1000.0, radiusKm).ToVector3();
			return Geology.ColumnAt(direction);
		}

		private static float CellEast(int x, int width, float widthMetres) => x / (float)(width - 1) * widthMetres - widthMetres * 0.5f;

		private static float CellNorth(int z, int depth, float depthMetres) => z / (float)(depth - 1) * depthMetres - depthMetres * 0.5f;

		/// <summary>A separable box blur of every field, <paramref name="radius"/> cells each way, edges clamped.</summary>
		private static void Blur(TerrainProcess[] cells, int width, int depth, int radius)
		{
			if (radius <= 0)
			{
				return;
			}
			var scratch = new TerrainProcess[cells.Length];
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					TerrainProcess sum = TerrainProcess.Zero;
					for (int k = -radius; k <= radius; k++)
					{
						sum.Accumulate(cells[z * width + Mathf.Clamp(x + k, 0, width - 1)], 1f);
					}
					scratch[z * width + x] = sum.Scaled(1f / (2 * radius + 1));
				}
			}
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					TerrainProcess sum = TerrainProcess.Zero;
					for (int k = -radius; k <= radius; k++)
					{
						sum.Accumulate(scratch[Mathf.Clamp(z + k, 0, depth - 1) * width + x], 1f);
					}
					cells[z * width + x] = sum.Scaled(1f / (2 * radius + 1));
				}
			}
		}
	}
}
#endif
