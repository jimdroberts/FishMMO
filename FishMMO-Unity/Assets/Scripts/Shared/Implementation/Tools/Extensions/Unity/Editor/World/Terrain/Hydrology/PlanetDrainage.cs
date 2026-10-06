#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A whole body's drainage: where its rivers run and its lakes stand, worked out once over the
	/// globe and shared by every scene cut from it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the whole planet.</b> A river is made by everything upstream of it, and a scene holds a few
	/// kilometres of a catchment that may be fifty across. Worked out per scene, every scene would make
	/// its own rivers from its own ground, two neighbours would disagree about where one crosses between
	/// them, and no river could be bigger than one scene's rain. Worked out on the globe they agree,
	/// big rivers arrive from upstream, a big lake has one level, and the globe can draw them.
	/// </para>
	/// <para>
	/// <b>The ground</b> is the planet's own at the atlas radius, in scene metres (the vertical and
	/// crater scales a scene is cut at), on a <see cref="CubeSphereGrid"/> of about
	/// <see cref="CellMetres"/>. A scene's own finer detail and erosion come later and conform to it:
	/// the scene snaps each river to its own valley floor.
	/// </para>
	/// <para>
	/// <b>The water</b> is the climate's: rain from the moisture model, evaporation from the mean
	/// temperature, runoff by Budyko's curve (<see cref="WaterBudget"/>). Karst ground keeps its
	/// hollows as sinks. A world with no air or nothing to condense has no rain and so no rivers.
	/// </para>
	/// <para>
	/// Cached per body, radius and settings for the editor's session: a few seconds the first time a
	/// body is asked, nothing after.
	/// </para>
	/// </remarks>
	public sealed class PlanetDrainage
	{
		/// <summary>The cell size aimed for, in metres.</summary>
		public const float CellMetres = 200f;

		/// <summary>The most cells along a face's edge: a body over about 65 km in radius runs coarser.</summary>
		public const int MaximumFaceCells = 512;

		/// <summary>Cells a side of the blocks the climate is read at: rain changes over kilometres.</summary>
		private const int ClimateBlock = 4;

		/// <summary>Bump when anything here changes what is found, so the cache never hands back an old answer.</summary>
		private const int Version = 2;

		public readonly CubeSphereGrid Grid;
		public readonly DrainageResult Result;
		/// <summary>Per cell, the ground in scene metres above the datum.</summary>
		public readonly float[] Height;
		/// <summary>Per cell, rain in metres a year.</summary>
		public readonly float[] Rain;
		/// <summary>Per cell, mean temperature in °C at the ground.</summary>
		public readonly float[] Celsius;
		/// <summary>Per cell, true where karst swallows what reaches it.</summary>
		public readonly bool[] Sink;
		/// <summary>The sea's surface in scene metres, or negative infinity where there is none.</summary>
		public readonly float SeaLevel;
		/// <summary>The radius the ground was laid at, km.</summary>
		public readonly double RadiusKm;
		/// <summary>How long it took, seconds.</summary>
		public readonly double Seconds;

		private static readonly Dictionary<string, PlanetDrainage> cache = new Dictionary<string, PlanetDrainage>();

		private PlanetDrainage(CubeSphereGrid grid, DrainageResult result, float[] height, float[] rain, float[] celsius, bool[] sink, float seaLevel, double radiusKm, double seconds)
		{
			Sink = sink;
			Grid = grid;
			Result = result;
			Height = height;
			Rain = rain;
			Celsius = celsius;
			SeaLevel = seaLevel;
			RadiusKm = radiusKm;
			Seconds = seconds;
		}

		/// <summary>Forgets every body's drainage, so the next ask works it out again.</summary>
		public static void ClearCache() => cache.Clear();

		/// <summary>
		/// The drainage of the body a scene stands on, at the radius it is cut at. Null for a scene with
		/// no body, or underground. Main thread: it reads the body and its climate.
		/// </summary>
		public static PlanetDrainage For(SceneGenerationRequest request, SolarSystemProfile system, DrainageSettings settings = null)
		{
			if (request == null || request.Body == null || (request.Layer != null && request.Layer.Underground))
			{
				return null;
			}
			return For(request.Body, system, request.ResolvedRadiusKm, settings);
		}

		/// <summary>The drainage of a body at a radius. Main thread.</summary>
		public static PlanetDrainage For(WorldBody body, SolarSystemProfile system, double radiusKm, DrainageSettings settings = null)
		{
			if (body == null)
			{
				return null;
			}
			settings ??= new DrainageSettings();
			uint seed = body.ResolvedTerrainSeed;
			string key = $"{seed}|{body.GetInstanceID()}|{radiusKm:R}|{settings.MinLakeDepthMetres:R}|{settings.MinLakeAreaSquareMetres:R}|" +
				$"{settings.MinRiverDischarge:R}|{settings.MinChannelAreaSquareMetres:R}|{Version}";
			if (cache.TryGetValue(key, out PlanetDrainage known))
			{
				return known;
			}
			PlanetDrainage built = Build(body, system, radiusKm, seed, settings);
			cache[key] = built;
			return built;
		}

		private static PlanetDrainage Build(WorldBody body, SolarSystemProfile system, double radiusKm, uint seed, DrainageSettings settings)
		{
			/* Each cell's biome decides whether its hollows keep their water (karst), so the biomes must be
			 * registered first: without them every hollow drains, and a batch run found a different planet's
			 * rivers from the editor's (127 against 119 on Arthis). */
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			Stopwatch clock = Stopwatch.StartNew();
			int n = CubeSphereGrid.FaceCellsFor(radiusKm * 1000.0, CellMetres, MaximumFaceCells);
			var grid = new CubeSphereGrid(n, radiusKm * 1000.0);
			int count = grid.Count;

			// The ground, as a scene cut at this radius has it: everything resolved here, sampled on every core.
			float cratering = PlanetSurface.CrateringOf(body);
			PlanetSurface.PlanetProfile profile = PlanetSurface.ProfileOf(seed, body);
			float relief = PlanetSurface.ReliefMetres(body);
			float verticalScale = PlanetSurface.SceneVerticalScale(body, radiusKm);
			float craterScale = PlanetSurface.SceneCraterScale(body, radiusKm);
			var height = new float[count];
			var planetMetres = new float[count];
			double[] centre = grid.Centre;
			Parallel.For(0, count, c =>
			{
				var direction = new Vector3((float)centre[c * 3], (float)centre[c * 3 + 1], (float)centre[c * 3 + 2]);
				PlanetSurface.AltitudeParts(seed, cratering, profile, relief, direction, out float planet, out float uncratered);
				planetMetres[c] = planet;
				height[c] = uncratered * verticalScale + (planet - uncratered) * craterScale;
			});

			SurfaceLiquid liquid = SurfaceLiquids.For(system, body, out float levelMetres);
			float seaLevel = liquid == SurfaceLiquid.None ? float.NegativeInfinity : levelMetres * verticalScale;

			// The climate on blocks of cells, in order: the climate and the biome assets are the main thread's.
			PlanetClimateField climate = PlanetClimateField.For(system, body);
			var rain = new float[count];
			var celsius = new float[count];
			var keeping = new float[count];
			int blocks = (n + ClimateBlock - 1) / ClimateBlock;
			for (int face = 0; face < 6; face++)
			{
				for (int bj = 0; bj < blocks; bj++)
				{
					for (int bi = 0; bi < blocks; bi++)
					{
						int ci = Math.Min(n - 1, bi * ClimateBlock + ClimateBlock / 2), cj = Math.Min(n - 1, bj * ClimateBlock + ClimateBlock / 2);
						int sample = grid.Index(face, ci, cj);
						var direction = new Vector3((float)centre[sample * 3], (float)centre[sample * 3 + 1], (float)centre[sample * 3 + 2]);
						double latitude = PlanetClimateField.LatitudeOf(direction);
						float precipitation = climate.Valid ? climate.Moisture.Precipitation(latitude, direction, climate.Moisture.Terrain) : 0f;
						float blockRain = WaterBudget.RainMetres(precipitation, MoistureModel.Midpoint);
						BiomeTemplate biome = climate.Valid ? climate.BiomeAt(direction, Mathf.Max(0f, planetMetres[sample]), out _) : null;
						float keep = biome != null ? biome.ResolvedTerrainProcess.DepressionKeeping : 0f;
						for (int j = bj * ClimateBlock; j < Math.Min(n, (bj + 1) * ClimateBlock); j++)
						{
							for (int i = bi * ClimateBlock; i < Math.Min(n, (bi + 1) * ClimateBlock); i++)
							{
								int c = grid.Index(face, i, j);
								rain[c] = blockRain;
								keeping[c] = keep;
							}
						}
					}
				}
			}
			// Temperature per cell, since it falls with each cell's own height: the lapse rate is the block's latitude term less height.
			for (int c = 0; c < count; c++)
			{
				var direction = new Vector3((float)centre[c * 3], (float)centre[c * 3 + 1], (float)centre[c * 3 + 2]);
				double latitude = PlanetClimateField.LatitudeOf(direction);
				float scale = climate.Valid ? climate.TemperatureAt(latitude, direction, Mathf.Max(0f, planetMetres[c])) : 0f;
				celsius[c] = (float)(ClimateModel.ToKelvin(scale) - 273.15);
			}

			/* Rivers and lakes need rain: a world with no air, or with nothing its air condenses lying liquid
			 * anywhere (an airless moon, an ice moon frozen through, a dry or a boiling world), has none. Its
			 * drainage is worked out all the same, so a scene asking finds no water rather than nothing. */
			bool rains = false;
			for (int c = 0; c < count && !rains; c++)
			{
				rains = rain[c] > 0f;
			}

			var sea = new bool[count];
			var sink = new bool[count];
			var runoff = new float[count];
			var evaporation = new float[count];
			for (int c = 0; c < count; c++)
			{
				sea[c] = height[c] < seaLevel;
				evaporation[c] = WaterBudget.PotentialEvaporationMetres(celsius[c]);
				runoff[c] = WaterBudget.RunoffMetres(rain[c], evaporation[c]);
			}
			// Karst keeps its hollows: a cell lower than all its neighbours swallows what reaches it.
			for (int c = 0; c < count; c++)
			{
				if (sea[c] || keeping[c] < 0.5f)
				{
					continue;
				}
				bool lowest = true;
				for (int k = grid.NeighbourStart[c]; k < grid.NeighbourStart[c + 1] && lowest; k++)
				{
					lowest = height[grid.Neighbours[k]] >= height[c];
				}
				sink[c] = lowest;
			}

			DrainageResult result = DrainageSolver.Solve(height, grid.Area, grid.NeighbourStart, grid.Neighbours, grid.NeighbourMetres,
				sea, sink, rain, runoff, evaporation, settings);
			var drainage = new PlanetDrainage(grid, result, height, rain, celsius, sink, seaLevel, radiusKm, clock.Elapsed.TotalSeconds);
			UnityEngine.Debug.Log($"[Planet drainage] '{body.name}' at {radiusKm:0.#} km{(rains ? string.Empty : " (no rain falls: no rivers or lakes)")}: {count:N0} cells of {grid.RadiusMetres * Math.PI * 0.5 / n:0} m, " +
				$"{result.Rivers.Count:N0} rivers, {result.Lakes.Count:N0} lakes ({result.DrainedBasins:N0} basins drained by their outlets) in {drainage.Seconds:0.0} s.");
			return drainage;
		}

		/// <summary>The cell under a unit direction.</summary>
		public int CellOf(Vector3 direction) => Grid.CellOf(direction.x, direction.y, direction.z);

		/// <summary>A cell's centre as a unit vector.</summary>
		public Vector3 CentreOf(int cell) => new Vector3((float)Grid.Centre[cell * 3], (float)Grid.Centre[cell * 3 + 1], (float)Grid.Centre[cell * 3 + 2]);
	}
}
#endif
