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

		/// <summary>
		/// Told, when set, what each build was worked out from in detail: per biome the climate blocks it took, how it keeps
		/// its hollows, its asset path (empty for a copy that is no asset) and instance. For a probe; null in a cut.
		/// </summary>
		public static Action<string> Diagnostics;

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

		/// <summary>
		/// What the drainage was worked out from, for the log: the biomes registered (count, and a hash of each one's key and
		/// how it keeps its hollows), and hashes of the ground, the rain and the karst. The editor and a batch run once
		/// drained Flo Monolith's planet differently from the same assets (141 rivers against 139; 2026-10-14); comparing two
		/// of these says which input differed.
		/// </summary>
		private static string Fingerprint(float[] height, float[] rain, float[] keeping, bool[] sink)
		{
			unchecked
			{
				uint biomes = 2166136261u;
				int registered = 0;
				foreach (string key in FishMMO.Shared.Biomes.BiomeRegistry.SupportedBiomes)
				{
					FishMMO.Shared.Biomes.BiomeTemplate biome = FishMMO.Shared.Biomes.BiomeRegistry.Get(key);
					registered++;
					foreach (char ch in key)
					{
						biomes = (biomes ^ ch) * 16777619u;
					}
					if (biome != null)
					{
						biomes = (biomes ^ (uint)BitConverter.SingleToInt32Bits(biome.ResolvedTerrainProcess.DepressionKeeping)) * 16777619u;
					}
				}
				uint Hash(float[] values)
				{
					uint h = 2166136261u;
					foreach (float v in values)
					{
						h = (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 16777619u;
					}
					return h;
				}
				int sinks = 0, karst = 0;
				double rainSum = 0.0;
				for (int c = 0; c < sink.Length; c++)
				{
					sinks += sink[c] ? 1 : 0;
					karst += keeping[c] >= 0.5f ? 1 : 0;
					rainSum += rain[c];
				}
				return $"From: {registered} biomes (hash {biomes:X8}), ground {Hash(height):X8}, rain {Hash(rain):X8} (total {rainSum:0.###}), " +
					$"{karst:N0} karst cells, {sinks:N0} sinks.";
			}
		}

		/// <summary>Where every build's record is written, under the project.</summary>
		public const string RecordFolder = "Library/FishMMO/Drainage";

		/// <summary>
		/// A record of one build, in the editor and in batch alike: what it found, and every object it was worked out from as
		/// it stood IN MEMORY (each serialised, with whether it had unsaved changes), so two builds that disagree can be
		/// compared field by field. An editor session once drained Flo Monolith's planet differently from every batch run on
		/// the same files and code (141 rivers against 139, 2026-10-14) and nothing then recorded what it had in memory.
		/// </summary>
		private static void WriteRecord(WorldBody body, SolarSystemProfile system, string found, string blocks)
		{
			try
			{
				var text = new System.Text.StringBuilder();
				text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {(Application.isBatchMode ? "batch" : "editor")}{(Application.isPlaying ? " (playing)" : string.Empty)}, " +
					$"Unity {Application.unityVersion}");
				text.AppendLine(found);
				text.AppendLine();
				void Dump(string label, UnityEngine.Object o)
				{
					if (o == null)
					{
						text.AppendLine($"== {label}: null");
						return;
					}
					string json = UnityEditor.EditorJsonUtility.ToJson(o, true);
					text.AppendLine($"== {label} '{o.name}' path '{UnityEditor.AssetDatabase.GetAssetPath(o)}' dirty {UnityEditor.EditorUtility.IsDirty(o)} hash {(uint)json.GetHashCode():X8}");
					text.AppendLine(json);
				}
				Dump("system", system);
				Dump("body", body);
				Dump("base climate", body != null ? body.BaseClimate : null);
				foreach (string key in BiomeRegistry.SupportedBiomes)
				{
					BiomeTemplate biome = BiomeRegistry.Get(key);
					Dump($"biome {key}", biome);
					if (biome != null && biome.TerrainProcess != null)
					{
						Dump($"terrain process of {key}", biome.TerrainProcess);
					}
				}
				text.AppendLine();
				text.AppendLine("== climate blocks per biome");
				text.AppendLine(blocks);
				System.IO.Directory.CreateDirectory(RecordFolder);
				string file = $"{RecordFolder}/{(body != null ? body.name : "none")} {DateTime.Now:yyyy-MM-dd HHmmss} {(Application.isBatchMode ? "batch" : "editor")}.txt";
				System.IO.File.WriteAllText(file, text.ToString());
				// The newest twenty: a record is a few megabytes.
				string[] old = System.IO.Directory.GetFiles(RecordFolder, "*.txt");
				Array.Sort(old, (x, y) => System.IO.File.GetLastWriteTimeUtc(y).CompareTo(System.IO.File.GetLastWriteTimeUtc(x)));
				for (int i = 20; i < old.Length; i++)
				{
					System.IO.File.Delete(old[i]);
				}
				UnityEngine.Debug.Log($"[Planet drainage] record written: {file}");
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogWarning($"[Planet drainage] could not write its record: {ex.Message}");
			}
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
			string blockDetail = null;
			var taken = new Dictionary<BiomeTemplate, int>();
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
						if (biome != null)
						{
							taken[biome] = taken.TryGetValue(biome, out int had) ? had + 1 : 1;
						}
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
			{
				var detail = new System.Text.StringBuilder();
				detail.AppendLine($"climate valid {climate.Valid}, mean {climate.MeanTemperature:R}, lapse {climate.LapsePerMetre:R}, relief {climate.ReliefMetres:R}");
				foreach (string key in BiomeRegistry.SupportedBiomes)
				{
					BiomeTemplate b = BiomeRegistry.Get(key);
					string path = b != null ? UnityEditor.AssetDatabase.GetAssetPath(b) : "(null)";
					detail.AppendLine($"  {key}: blocks {(b != null && taken.TryGetValue(b, out int k) ? k : 0)}, keeping {(b != null ? b.ResolvedTerrainProcess.DepressionKeeping : -1f):R}, " +
						$"selectable {(b != null && b.IsSelectable)}, path '{path}', instance {(b != null ? b.GetInstanceID() : 0)}");
				}
				foreach (KeyValuePair<BiomeTemplate, int> pair in taken)
				{
					if (pair.Key != null && BiomeRegistry.Get(pair.Key.Key) != pair.Key)
					{
						detail.AppendLine($"  NOT THE REGISTERED ONE: '{pair.Key.name}' blocks {pair.Value}, path '{UnityEditor.AssetDatabase.GetAssetPath(pair.Key)}'");
					}
				}
				Diagnostics?.Invoke(detail.ToString());
				blockDetail = detail.ToString();
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
			WriteRecord(body, system, $"{result.Rivers.Count} rivers, {result.Lakes.Count} lakes, {result.DrainedBasins} basins drained. " + Fingerprint(height, rain, keeping, sink), blockDetail);
			UnityEngine.Debug.Log($"[Planet drainage] '{body.name}' at {radiusKm:0.#} km{(rains ? string.Empty : " (no rain falls: no rivers or lakes)")}: {count:N0} cells of {grid.RadiusMetres * Math.PI * 0.5 / n:0} m, " +
				$"{result.Rivers.Count:N0} rivers, {result.Lakes.Count:N0} lakes ({result.DrainedBasins:N0} basins drained by their outlets) in {drainage.Seconds:0.0} s. " +
				Fingerprint(height, rain, keeping, sink));
			return drainage;
		}

		/// <summary>The cell under a unit direction.</summary>
		public int CellOf(Vector3 direction) => Grid.CellOf(direction.x, direction.y, direction.z);

		/// <summary>A cell's centre as a unit vector.</summary>
		public Vector3 CentreOf(int cell) => new Vector3((float)Grid.Centre[cell * 3], (float)Grid.Centre[cell * 3 + 1], (float)Grid.Centre[cell * 3 + 2]);
	}
}
#endif
