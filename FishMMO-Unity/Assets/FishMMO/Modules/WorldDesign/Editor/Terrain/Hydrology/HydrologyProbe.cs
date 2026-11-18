#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Shapes real ground with and without its rivers and lakes and writes what it finds — the planet's
	/// drainage map, every scene's ground both ways, its water, its rivers' profiles and the checks —
	/// to a folder outside the project. Writes nothing into the project.
	/// </summary>
	/// <remarks>
	/// Run headless: <c>-executeMethod FishMMO.Shared.WorldDesign.HydrologyProbe.Run</c> with
	/// FISHMMO_HYDRO_OUT naming the folder. Each scene is its atlas entry's rectangle, or a 4 km sample
	/// placed where the planet has a river or a lake, so the evidence shows water whatever the atlas holds.
	/// </remarks>
	public static class HydrologyProbe
	{
		private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

		/// <summary>
		/// Re-cuts one atlas scene headless, as the World Atlas page's Re-cut does (the old scene backed up
		/// first), and logs its water: FISHMMO_RECUT_SCENE names it.
		/// </summary>
		public static void Recut()
		{
			string name = Environment.GetEnvironmentVariable("FISHMMO_RECUT_SCENE");
			foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (entry != null && entry.SceneName == name)
				{
					SceneGenerationResult result = SceneGenerator.Recut(entry);
					Debug.Log($"[Hydrology probe] re-cut '{name}': {(result != null ? string.Join(" | ", result.Notes) : "no result")}");
					return;
				}
			}
			Debug.LogError($"[Hydrology probe] no atlas scene '{name}'.");
		}

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_HYDRO_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = Path.Combine(Path.GetTempPath(), "fishmmo-hydrology");
			}
			Directory.CreateDirectory(output);
			var index = new StringBuilder();
			index.Append("{\"bodies\":[");
			bool firstBody = true;

			var bodies = new List<WorldBody>();
			foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (entry != null && entry.Placed && entry.Body != null && !bodies.Contains(entry.Body))
				{
					bodies.Add(entry.Body);
				}
			}
			foreach (WorldBody body in bodies)
			{
				if (!PlanetSurfaceBaker.HasGround(body))
				{
					continue;
				}
				SolarSystemProfile system = SolarSystemProfile.Resolve(body);
				double radius = PlanetSurface.SceneRadiusKm(body);
				PlanetDrainage.ClearCache();
				PlanetDrainage drainage = PlanetDrainage.For(body, system, radius);
				string bodyKey = Sanitize(body.name);
				DumpCells(drainage, Path.Combine(output, $"cells_{bodyKey}.bin"));
				if (Environment.GetEnvironmentVariable("FISHMMO_HYDRO_CELLS_ONLY") == "1")
				{
					continue;
				}
				WritePlanet(drainage, Path.Combine(output, $"planet_{bodyKey}.f32"), out int mapWidth, out int mapHeight);

				if (!firstBody)
				{
					index.Append(',');
				}
				firstBody = false;
				index.Append($"{{\"name\":\"{body.name}\",\"key\":\"{bodyKey}\",\"radiusKm\":{F(radius)},\"cells\":{drainage.Grid.Count},\"cellMetres\":{F(drainage.Grid.RadiusMetres * Math.PI * 0.5 / drainage.Grid.N)}," +
					$"\"rivers\":{drainage.Result.Rivers.Count},\"perennial\":{CountPerennial(drainage)},\"lakes\":{drainage.Result.Lakes.Count},\"terminalLakes\":{CountTerminal(drainage)}," +
					$"\"seconds\":{F(drainage.Seconds)},\"seaLevel\":{F(float.IsNegativeInfinity(drainage.SeaLevel) ? -99999f : drainage.SeaLevel)},\"map\":[{mapWidth},{mapHeight}]," +
					$"\"drainedBasins\":{drainage.Result.DrainedBasins},\"lakeShare\":{F(LakeShare(drainage))},\"budget\":{Budget(drainage)},\"scenes\":[");

				var requests = new List<(string label, SceneGenerationRequest request)>();
				foreach (WorldAtlasScene entry in WorldEditorAssets.FindAll<WorldAtlasScene>())
				{
					if (entry == null || !entry.Placed || entry.Body != body || (entry.Layer != null && entry.Layer.Underground) || Mathf.Min(entry.SizeKm.x, entry.SizeKm.y) < 2f)
					{
						continue;
					}
					requests.Add(($"atlas: {entry.SceneName}", Request(entry.SceneName, body, entry.Latitude, entry.Longitude, entry.HeadingDegrees, entry.SizeKm, entry.ErosionStrength)));
				}
				foreach ((string label, double lat, double lon) in Samples(drainage))
				{
					requests.Add((label, Request($"Hydrology Probe {requests.Count}", body, lat, lon, 0f, new Vector2(3f, 3f), 1f)));
				}
				bool firstScene = true;
				foreach ((string label, SceneGenerationRequest request) in requests)
				{
					string key = $"{bodyKey}_{requests.IndexOf((label, request))}";
					string json = ProbeScene(request, system, output, key, label);
					if (json == null)
					{
						continue;
					}
					if (!firstScene)
					{
						index.Append(',');
					}
					firstScene = false;
					index.Append(json);
				}
				index.Append("]}");
			}
			index.Append("]}");
			File.WriteAllText(Path.Combine(output, "index.json"), index.ToString());
			Debug.Log($"[Hydrology probe] wrote '{output}'.");
		}

		/// <summary>
		/// Every body with ground: what its air and liquid are, so which wearing processes act on it, and
		/// what its drainage found. Writes survey.json to FISHMMO_HYDRO_OUT. Nothing goes into the project.
		/// </summary>
		public static void Survey()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_HYDRO_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = Path.Combine(Path.GetTempPath(), "fishmmo-hydrology");
			}
			Directory.CreateDirectory(output);
			var json = new StringBuilder("[");
			bool first = true;
			foreach (WorldBody body in WorldEditorAssets.FindAll<WorldBody>())
			{
				if (body == null || !PlanetSurfaceBaker.HasGround(body))
				{
					continue;
				}
				SolarSystemProfile system = SolarSystemProfile.Resolve(body);
				double radius = PlanetSurface.SceneRadiusKm(body);
				BiomeWorldConditions conditions = BiomeWorldConditions.For(system, body);
				FishMMO.Shared.Weather.PlanetAir air = FishMMO.Shared.Weather.PlanetAir.For(system, body);
				SurfaceLiquid liquid = SurfaceLiquids.For(system, body, out _);
				PlanetDrainage drainage = PlanetDrainage.For(body, system, radius);
				float wettest = 0f;
				double rainSum = 0, landArea = 0;
				for (int c = 0; c < drainage.Grid.Count; c++)
				{
					wettest = Mathf.Max(wettest, drainage.Rain[c]);
					if (drainage.Height[c] >= drainage.SeaLevel)
					{
						rainSum += drainage.Rain[c] * drainage.Grid.Area[c];
						landArea += drainage.Grid.Area[c];
					}
				}
				int perennial = CountPerennial(drainage);
				bool rains = wettest > 0f;
				bool wind = body.Atmosphere != AtmosphereKind.None;
				json.Append(first ? "" : ",").Append($"{{\"name\":\"{body.name}\",\"parent\":\"{(body.Parent != null ? body.Parent.name : "")}\",\"radiusKm\":{F(radius)}," +
					$"\"atmosphere\":\"{body.Atmosphere}\",\"condensate\":\"{air.Condensate}\",\"liquidWater\":{(conditions.HasLiquidWater ? "true" : "false")}," +
					$"\"frozenThrough\":{(conditions.IsFrozenThrough ? "true" : "false")},\"volcanic\":{(conditions.IsVolcanic ? "true" : "false")},\"surfaceLiquid\":\"{liquid}\"," +
					$"\"meanCelsius\":{F(ClimateModel.ToKelvin(conditions.MeanTemperature) - 273.15)},\"wettestRain\":{F(wettest)},\"meanLandRain\":{F(landArea > 0 ? rainSum / landArea : 0)}," +
					$"\"rivers\":{drainage.Result.Rivers.Count},\"perennial\":{perennial},\"lakes\":{drainage.Result.Lakes.Count},\"lakeShare\":{F(LakeShare(drainage))}," +
					$"\"processes\":{{\"rivers\":{(rains ? "true" : "false")},\"rainGullies\":{(rains ? "true" : "false")},\"glaciers\":{(rains ? "true" : "false")},\"wind\":{(wind ? "true" : "false")},\"slumpAndCreep\":true}}}}");
				first = false;
			}
			json.Append(']');
			File.WriteAllText(Path.Combine(output, "survey.json"), json.ToString());
			Debug.Log($"[Hydrology probe] survey written to '{output}'.");
		}

		private static SceneGenerationRequest Request(string name, WorldBody body, double lat, double lon, float heading, Vector2 size, float strength)
		{
			return new SceneGenerationRequest
			{
				SceneName = name,
				Body = body,
				Latitude = lat,
				Longitude = lon,
				HeadingDegrees = heading,
				SizeKm = size,
				FineDetail = true,
				Erosion = true,
				ErosionStrength = strength,
			};
		}

		/// <summary>Where to put sample scenes: the biggest river's middle, the biggest overflowing lake, the biggest terminal lake.</summary>
		private static IEnumerable<(string, double, double)> Samples(PlanetDrainage drainage)
		{
			DrainageResult r = drainage.Result;
			DrainageRiver best = null;
			foreach (DrainageRiver river in r.Rivers)
			{
				if (river.Cells.Length >= 12 && river.Discharge[river.Cells.Length - 2] >= 0.2f && (best == null || river.Discharge[river.Cells.Length - 2] > best.Discharge[best.Cells.Length - 2]))
				{
					best = river;
				}
			}
			if (best != null)
			{
				int c = best.Cells[best.Cells.Length * 2 / 3];
				yield return LatLon($"sample: largest river (#{best.Id}, {best.Discharge[best.Cells.Length - 2]:0.0} m³/s at its mouth)", drainage, c);
			}
			// The slowest big river: where it meanders and lays its sand.
			int gentle = -1;
			float gentleQ = 0f;
			foreach (DrainageRiver river in r.Rivers)
			{
				for (int v = 3; v + 3 < river.Cells.Length; v++)
				{
					int up = river.Cells[v - 3], down = river.Cells[v + 3];
					double fall = r.Filled[up] - r.Filled[down];
					double run = drainage.Grid.ArcBetween(up, down) * drainage.Grid.RadiusMetres;
					if (run > 1.0 && fall / run < 0.003 && river.Discharge[v] > gentleQ && drainage.Height[river.Cells[v]] > drainage.SeaLevel + 5f)
					{
						gentleQ = river.Discharge[v];
						gentle = river.Cells[v];
					}
				}
			}
			if (gentle >= 0)
			{
				yield return LatLon($"sample: a gentle river ({gentleQ:0.0} m³/s on a bed falling under 0.3%)", drainage, gentle);
			}
			DrainageLake full = null, terminal = null;
			foreach (DrainageLake lake in r.Lakes)
			{
				if (!lake.Terminal && (full == null || lake.AreaSquareMetres > full.AreaSquareMetres))
				{
					full = lake;
				}
				if (lake.Terminal && lake.AreaSquareMetres > 0 && (terminal == null || lake.AreaSquareMetres > terminal.AreaSquareMetres))
				{
					terminal = lake;
				}
			}
			if (full != null)
			{
				yield return LatLon($"sample: largest lake that overflows (#{full.Id}, {full.AreaSquareMetres / 1e6:0.00} km², out {full.Outflow:0.00} m³/s)", drainage, LakeCentre(full, drainage));
			}
			if (terminal != null)
			{
				yield return LatLon($"sample: largest terminal lake (#{terminal.Id}, {terminal.AreaSquareMetres / 1e6:0.00} km², {terminal.SpillLevel - terminal.Level:0.0} m under its spill point)", drainage, LakeCentre(terminal, drainage));
			}
		}

		private static int LakeCentre(DrainageLake lake, PlanetDrainage drainage)
		{
			Vector3 sum = Vector3.zero;
			foreach (int c in lake.Cells)
			{
				if (drainage.Result.Ground[c] < lake.Level)
				{
					sum += drainage.CentreOf(c);
				}
			}
			return sum.sqrMagnitude > 0f ? drainage.CellOf(sum.normalized) : lake.Cells[0];
		}

		private static (string, double, double) LatLon(string label, PlanetDrainage drainage, int cell)
		{
			AtlasGeometry.FromUnit(new Vector3d(drainage.Grid.Centre[cell * 3], drainage.Grid.Centre[cell * 3 + 1], drainage.Grid.Centre[cell * 3 + 2]), out double lat, out double lon);
			return (label, lat, lon);
		}

		/// <summary>Shapes one scene with and without water and writes both grounds, the water and the checks. Returns its index entry.</summary>
		private static string ProbeScene(SceneGenerationRequest request, SolarSystemProfile system, string output, string key, string label)
		{
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			var notes = new List<string>();
			request.Rivers = false;
			var dryClock = System.Diagnostics.Stopwatch.StartNew();
			SceneHeightField dry = SceneGround.Shape(request, plan, system, notes, out _, out _);
			double drySeconds = dryClock.Elapsed.TotalSeconds;
			request.Rivers = true;
			var wetClock = System.Diagnostics.Stopwatch.StartNew();
			SceneHeightField wet = SceneGround.Shape(request, plan, system, notes, out _, out SceneWater water);
			double wetSeconds = wetClock.Elapsed.TotalSeconds;

			// Every n-th sample: the pictures do not need two metres.
			int step = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(wet.Width, wet.Depth) / 1024f));
			int w = (wet.Width - 1) / step + 1, d = (wet.Depth - 1) / step + 1;
			var pack = new float[w * d * 5];
			for (int z = 0; z < d; z++)
			{
				for (int x = 0; x < w; x++)
				{
					Pack(pack, (z * w + x) * 5, z * step * wet.Width + x * step, dry, wet, water);
				}
			}
			WriteFloats(Path.Combine(output, $"scene_{key}.f32"), pack);

			// Boulders, planned on the finished ground as the generator places them.
			List<RiverBoulder> boulders = water != null ? RiverBoulders.Plan(water, wet.MetresAt, null, SceneGenerator.SceneSeed(request)) : new List<RiverBoulder>();
			var boulderCsv = new StringBuilder("x,y,z,size\n");
			foreach (RiverBoulder b in boulders)
			{
				boulderCsv.Append(string.Format(Invariant, "{0:F2},{1:F2},{2:F2},{3:F2}\n", b.Position.x, b.Position.y, b.Position.z, b.Size));
			}
			File.WriteAllText(Path.Combine(output, $"boulders_{key}.csv"), boulderCsv.ToString());

			// A full-resolution close-up where the bars are thickest: 360 samples a side.
			int crop = 360, cropX = 0, cropZ = 0, cropBars = -1;
			if (water != null && water.Grid != null)
			{
				for (int z0 = 0; z0 + crop <= wet.Depth; z0 += crop / 3)
				{
					for (int x0 = 0; x0 + crop <= wet.Width; x0 += crop / 3)
					{
						int bars = 0;
						for (int z = z0; z < z0 + crop; z += 3)
						{
							for (int x = x0; x < x0 + crop; x += 3)
							{
								WaterKind kind = water.Grid.Kind[z * wet.Width + x];
								bars += kind == WaterKind.Bar ? 3 : kind == WaterKind.River ? 1 : 0;
							}
						}
						if (bars > cropBars)
						{
							cropBars = bars;
							cropX = x0;
							cropZ = z0;
						}
					}
				}
				crop = Math.Min(crop, Math.Min(wet.Width, wet.Depth));
				var close = new float[crop * crop * 5];
				for (int z = 0; z < crop; z++)
				{
					for (int x = 0; x < crop; x++)
					{
						Pack(close, (z * crop + x) * 5, (cropZ + z) * wet.Width + cropX + x, dry, wet, water);
					}
				}
				WriteFloats(Path.Combine(output, $"close_{key}.f32"), close);
			}

			// The checks, on the full-resolution grids.
			int channel = 0, channelDry = 0, aboveWater = 0, lakeSamples = 0, lakeAbove = 0, monotoneBreaks = 0, risesTotal = 0;
			int barSamples = 0, barWet = 0, playaSamples = 0;
			double sandSum = 0;
			float worstRise = 0f, deepestCut = 0f;
			if (water != null && water.Grid != null)
			{
				SceneWaterGrid g = water.Grid;
				for (int i = 0; i < wet.Metres.Length; i++)
				{
					switch (g.Kind[i])
					{
						case WaterKind.River:
							channel++;
							if (wet.Metres[i] > g.Level[i] + 1e-3f) aboveWater++;
							break;
						case WaterKind.DryWash:
							channelDry++;
							break;
						case WaterKind.Lake:
							lakeSamples++;
							if (wet.Metres[i] >= g.Level[i]) lakeAbove++;
							break;
						case WaterKind.Bar:
							barSamples++;
							sandSum += g.Sand[i];
							if (wet.Metres[i] < g.Shore[i]) barWet++;
							break;
						case WaterKind.Playa:
							playaSamples++;
							break;
					}
					deepestCut = Mathf.Min(deepestCut, wet.Metres[i] - dry.Metres[i]);
				}
				foreach (RiverPath river in water.Rivers)
				{
					for (int k = 1; k < river.Count; k++)
					{
						risesTotal++;
						float rise = river.Surface[k] - river.Surface[k - 1];
						if (rise > 1e-4f)
						{
							monotoneBreaks++;
							worstRise = Mathf.Max(worstRise, rise);
						}
					}
				}
			}

			// Rivers: line, sizes, and the ground under them before and after.
			var csv = new StringBuilder("river,perennial,start,end,k,x,z,s,surface,bed,width,depth,discharge,speed,ground_dry,ground_wet,reach\n");
			if (water != null)
			{
				foreach (RiverPath river in water.Rivers)
				{
					for (int k = 0; k < river.Count; k++)
					{
						csv.Append(string.Format(Invariant, "{0},{1},{2},{3},{4},{5:F1},{6:F1},{7:F1},{8:F3},{9:F3},{10:F2},{11:F2},{12:F3},{13:F2},{14:F3},{15:F3},{16}\n",
							river.Id, river.Perennial ? 1 : 0, river.Start, river.End, k, river.X[k], river.Z[k], river.S[k], river.Surface[k], river.Bed[k],
							river.Width[k], river.Depth[k], river.Discharge[k], river.SpeedAt(k), dry.MetresAt(river.X[k], river.Z[k]), wet.MetresAt(river.X[k], river.Z[k]),
							river.Reach != null ? (int)river.Reach[k] : 0));
					}
				}
			}
			File.WriteAllText(Path.Combine(output, $"rivers_{key}.csv"), csv.ToString());

			var lakes = new StringBuilder("[");
			if (water != null)
			{
				for (int l = 0; l < water.Lakes.Count; l++)
				{
					SceneLake lake = water.Lakes[l];
					lakes.Append(l > 0 ? "," : "").Append($"{{\"id\":{lake.Id},\"planet\":{lake.PlanetLake},\"level\":{F(lake.Level)},\"spill\":{F(lake.SpillLevel)},\"terminal\":{(lake.Terminal ? "true" : "false")}," +
						$"\"frozen\":{(lake.Frozen ? "true" : "false")},\"outflow\":{F(lake.Outflow)},\"covered\":{lake.Covered},\"areaKm2\":{F(lake.Covered * wet.Spacing * wet.Spacing / 1e6)}}}");
				}
			}
			lakes.Append(']');
			var notesJson = new StringBuilder("[");
			for (int n = 0; n < notes.Count; n++)
			{
				notesJson.Append(n > 0 ? "," : "").Append('"').Append(notes[n].Replace("\\", "\\\\").Replace("\"", "'")).Append('"');
			}
			notesJson.Append(']');
			return $"{{\"key\":\"{key}\",\"label\":\"{label.Replace("\"", "'")}\",\"name\":\"{request.SceneName}\",\"lat\":{F(request.Latitude)},\"lon\":{F(request.Longitude)}," +
				$"\"sizeKm\":[{F(request.SizeKm.x)},{F(request.SizeKm.y)}],\"samples\":[{wet.Width},{wet.Depth}],\"spacing\":{F(wet.Spacing)},\"grid\":[{w},{d}],\"step\":{step}," +
				$"\"rivers\":{(water != null ? water.Rivers.Count : 0)},\"lakes\":{lakes},\"channelSamples\":{channel},\"dryWashSamples\":{channelDry},\"channelAboveWater\":{aboveWater}," +
				$"\"lakeSamples\":{lakeSamples},\"lakeAboveLevel\":{lakeAbove},\"surfaceSteps\":{risesTotal},\"surfaceRises\":{monotoneBreaks},\"worstRise\":{F(worstRise)},\"deepestCut\":{F(deepestCut)}," +
				$"\"barSamples\":{barSamples},\"barWet\":{barWet},\"sandShare\":{F(barSamples > 0 ? sandSum / barSamples : 0)},\"playaSamples\":{playaSamples},\"boulders\":{boulders.Count}," +
				$"\"close\":[{cropX},{cropZ},{crop}]," +
				$"\"seaLevel\":{F(water != null && !float.IsNegativeInfinity(water.SeaLevel) ? water.SeaLevel : -99999f)},\"secondsDry\":{F(drySeconds)},\"secondsWet\":{F(wetSeconds)},\"notes\":{notesJson}}}";
		}

		/// <summary>One sample as the pictures read it: ground without and with water, the water's surface, what stands there, how sandy.</summary>
		private static void Pack(float[] pack, int p, int i, SceneHeightField dry, SceneHeightField wet, SceneWater water)
		{
			pack[p] = dry.Metres[i];
			pack[p + 1] = wet.Metres[i];
			bool any = water != null && water.Grid != null;
			pack[p + 2] = any ? water.Grid.Level[i] : float.NegativeInfinity;
			pack[p + 3] = any ? (float)water.Grid.Kind[i] : 0f;
			pack[p + 4] = any ? water.Grid.Sand[i] : 0f;
		}

		/// <summary>The planet as an equirectangular map: height, lake, river discharge (negative for a dry wash), rain.</summary>
		private static void WritePlanet(PlanetDrainage drainage, string path, out int width, out int height)
		{
			width = 2048;
			height = 1024;
			var pack = new float[width * height * 4];
			DrainageResult r = drainage.Result;
			float wet = new DrainageSettings().MinRiverDischarge;
			for (int j = 0; j < height; j++)
			{
				double lat = 90.0 - (j + 0.5) * 180.0 / height;
				for (int i = 0; i < width; i++)
				{
					double lon = (i + 0.5) * 360.0 / width - 180.0;
					Vector3d unit = AtlasGeometry.ToUnit(lat, lon);
					int c = drainage.Grid.CellOf(unit.X, unit.Y, unit.Z);
					int p = (j * width + i) * 4;
					pack[p] = drainage.Height[c];
					pack[p + 1] = r.LakeOf[c] >= 0 ? (r.Lakes[r.LakeOf[c]].Terminal ? 2f : 1f) : 0f;
					pack[p + 2] = r.RiverOf[c] >= 0 ? (r.Discharge[c] >= wet ? r.Discharge[c] : -1f) : 0f;
					pack[p + 3] = drainage.Rain[c];
				}
			}
			WriteFloats(path, pack);
		}

		private static string Budget(PlanetDrainage drainage)
		{
			return $"{{\"runoff\":{F(drainage.Result.Runoff)},\"toOutlets\":{F(drainage.Result.ToOutlets)},\"evaporated\":{F(drainage.Result.Evaporated)}}}";
		}

		/// <summary>Share of the land under lakes.</summary>
		private static double LakeShare(PlanetDrainage drainage)
		{
			double land = 0, lakes = 0;
			for (int c = 0; c < drainage.Grid.Count; c++)
			{
				if (drainage.Height[c] >= drainage.SeaLevel)
				{
					land += drainage.Grid.Area[c];
				}
				if (drainage.Result.LakeOf[c] >= 0)
				{
					lakes += drainage.Grid.Area[c];
				}
			}
			return land > 0 ? lakes / land : 0;
		}

		private static int CountPerennial(PlanetDrainage drainage)
		{
			int n = 0;
			float wet = new DrainageSettings().MinRiverDischarge;
			foreach (DrainageRiver river in drainage.Result.Rivers)
			{
				n += river.Discharge[Math.Max(0, river.Cells.Length - 2)] >= wet ? 1 : 0;
			}
			return n;
		}

		private static int CountTerminal(PlanetDrainage drainage)
		{
			int n = 0;
			foreach (DrainageLake lake in drainage.Result.Lakes)
			{
				n += lake.Terminal ? 1 : 0;
			}
			return n;
		}

		/// <summary>The planet's cells as drainage was given them, for tuning outside Unity: N, radius, sea level, then height, rain, °C and sink per cell.</summary>
		private static void DumpCells(PlanetDrainage drainage, string path)
		{
			using var writer = new BinaryWriter(File.Create(path));
			writer.Write(drainage.Grid.N);
			writer.Write(drainage.Grid.RadiusMetres);
			writer.Write(drainage.SeaLevel);
			int count = drainage.Grid.Count;
			for (int c = 0; c < count; c++) writer.Write(drainage.Height[c]);
			for (int c = 0; c < count; c++) writer.Write(drainage.Rain[c]);
			for (int c = 0; c < count; c++) writer.Write(drainage.Celsius[c]);
			for (int c = 0; c < count; c++) writer.Write(drainage.Sink[c]);
		}

		private static void WriteFloats(string path, float[] data)
		{
			var bytes = new byte[data.Length * 4];
			Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
			File.WriteAllBytes(path, bytes);
		}

		private static string F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.####", Invariant);

		private static string Sanitize(string name)
		{
			var sb = new StringBuilder();
			foreach (char ch in name)
			{
				sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
			}
			return sb.ToString();
		}
	}
}
#endif
