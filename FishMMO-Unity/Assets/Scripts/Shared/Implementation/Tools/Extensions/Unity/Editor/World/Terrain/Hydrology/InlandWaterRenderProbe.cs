#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Photographs a scene's lakes and rivers as the running game draws them: a window of a real scene's
	/// ground shaped with its water, laid into a terrain made in memory, with <see cref="FishMMO.Water.InlandWaterRenderer"/>
	/// on it. Writes PNGs to FISHMMO_INLAND_OUT; saves nothing into the project.
	/// </summary>
	public static class InlandWaterRenderProbe
	{
		private const int Width = 1600;
		private const int Height = 900;
		private const int Window = 1024;

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_INLAND_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = Path.Combine(Path.GetTempPath(), "fishmmo-inland");
			}
			Directory.CreateDirectory(output);
			string sceneName = Environment.GetEnvironmentVariable("FISHMMO_INLAND_SCENE") ?? "Baoakraal Hyena-den";
			WorldAtlasScene entry = null;
			foreach (WorldAtlasScene candidate in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (candidate != null && candidate.SceneName == sceneName)
				{
					entry = candidate;
				}
			}
			if (entry == null || entry.Body == null)
			{
				Debug.LogError($"[Inland water probe] no placed atlas entry '{sceneName}'.");
				return;
			}
			var request = new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
				ErosionStrength = entry.ErosionStrength,
			};
			/* FISHMMO_INLAND_AT="lat,lon": a 3 km sample of the same body there instead of the entry's rectangle.
			 * "seamouth" or "lakemouth": the mouth of the body's largest river ending in the sea, or in a lake. */
			string at = Environment.GetEnvironmentVariable("FISHMMO_INLAND_AT");
			RiverEnd mouth = at == "seamouth" ? RiverEnd.Sea : at == "lakemouth" ? RiverEnd.Lake : RiverEnd.Edge;
			if (!string.IsNullOrEmpty(at))
			{
				request.SceneName = "Inland Water Probe";
				request.Layer = null;
				if (mouth != RiverEnd.Edge)
				{
					if (!Mouth(entry.Body, mouth, out double mouthLat, out double mouthLon))
					{
						Debug.LogError($"[Inland water probe] '{entry.Body.name}' has no river ending in a {mouth}.");
						return;
					}
					request.Latitude = mouthLat;
					request.Longitude = mouthLon;
				}
				else
				{
					string[] parts = at.Split(',');
					request.Latitude = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
					request.Longitude = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
				}
				request.SizeKm = new Vector2(3f, 3f);
				request.HeadingDegrees = 0f;
				request.ErosionStrength = 1f;
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(entry.Body);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			SceneHeightField field = SceneGround.Shape(request, plan, system, new List<string>(), out _, out SceneWater water);
			if (water == null || !water.Any || water.Grid == null)
			{
				Debug.LogError("[Inland water probe] the scene has no water.");
				return;
			}

			// The window with the most water in it; FISHMMO_INLAND_FOCUS=rivers for the one with the most river.
			bool riversOnly = Environment.GetEnvironmentVariable("FISHMMO_INLAND_FOCUS") == "rivers";
			int bestX = 0, bestZ = 0, best = -1, window = Mathf.Min(Window, Mathf.Min(field.Width, field.Depth) - 1);
			for (int z0 = 0; z0 + window < field.Depth; z0 += window / 4)
			{
				for (int x0 = 0; x0 + window < field.Width; x0 += window / 4)
				{
					int count = 0;
					for (int z = z0; z < z0 + window; z += 8)
					{
						for (int x = x0; x < x0 + window; x += 8)
						{
							WaterKind kind = water.Grid.Kind[z * field.Width + x];
							count += kind == WaterKind.Lake ? (riversOnly ? 0 : 1) : kind == WaterKind.River ? (riversOnly ? 40 : 6) : kind == WaterKind.Bar ? (riversOnly ? 20 : 3) : 0;
						}
					}
					if (count > best)
					{
						best = count;
						bestX = x0;
						bestZ = z0;
					}
				}
			}

			// At a mouth, the window is centred on the largest such river's end.
			RiverPath mouthRiver = null;
			if (mouth != RiverEnd.Edge)
			{
				foreach (RiverPath river in water.Rivers)
				{
					if (river.Perennial && river.End == mouth && river.Count > 10 && (mouthRiver == null || river.Discharge[river.Count - 1] > mouthRiver.Discharge[mouthRiver.Count - 1]))
					{
						mouthRiver = river;
					}
				}
				if (mouthRiver != null)
				{
					int last = mouthRiver.Count - 1;
					// The mouth in numbers: the river's last points, the ground under them and what the water grid made of it.
					var dump = new List<string> { "k,s,x,z,surface,bed,width,depth,ground,kind,level" };
					for (int k = Mathf.Max(0, last - 40); k <= last; k++)
					{
						int sample = water.Grid.SampleAt(mouthRiver.X[k], mouthRiver.Z[k]);
						dump.Add($"{k},{mouthRiver.S[k]:0.0},{mouthRiver.X[k]:0.0},{mouthRiver.Z[k]:0.0},{mouthRiver.Surface[k]:0.00},{mouthRiver.Bed[k]:0.00},{mouthRiver.Width[k]:0.0},{mouthRiver.Depth[k]:0.00}," +
							$"{field.MetresAt(mouthRiver.X[k], mouthRiver.Z[k]):0.00},{(sample >= 0 ? water.Grid.Kind[sample].ToString() : "-")},{(sample >= 0 ? water.Grid.Level[sample] : float.NaN):0.00}");
					}
					foreach (SceneLake lake in water.Lakes)
					{
						dump.Add($"lake {lake.Id}: level {lake.Level:0.00}, covered {lake.Covered}");
					}
					dump.Add($"sea level {water.SeaLevel:0.00}; river {mouthRiver.Id} ends {mouthRiver.End}, lake {mouthRiver.EndLake}");
					// On past the end along its heading, and across it there: what stands between the river and the water it meets.
					float hx = mouthRiver.X[last] - mouthRiver.X[last - 3], hz = mouthRiver.Z[last] - mouthRiver.Z[last - 3];
					float hl = Mathf.Sqrt(hx * hx + hz * hz);
					hx /= hl;
					hz /= hl;
					dump.Add("past,across,ground,kind,level");
					for (float d = 0f; d <= 40f; d += 2f)
					{
						for (float a = -12f; a <= 12f; a += 6f)
						{
							float px = mouthRiver.X[last] + hx * d - hz * a, pz = mouthRiver.Z[last] + hz * d + hx * a;
							int sample = water.Grid.SampleAt(px, pz);
							dump.Add($"{d:0},{a:0},{field.MetresAt(px, pz):0.00},{(sample >= 0 ? water.Grid.Kind[sample].ToString() : "-")},{(sample >= 0 ? water.Grid.Level[sample] : float.NaN):0.00}");
						}
					}
					File.WriteAllLines(Path.Combine(output, "mouth.csv"), dump);
					bestX = Mathf.Clamp(Mathf.RoundToInt((mouthRiver.X[last] - field.EastOf(0)) / field.Spacing) - window / 2, 0, field.Width - 1 - window);
					bestZ = Mathf.Clamp(Mathf.RoundToInt((mouthRiver.Z[last] - field.NorthOf(0)) / field.Spacing) - window / 2, 0, field.Depth - 1 - window);
				}
				else
				{
					Debug.LogWarning($"[Inland water probe] the sample has no river ending in a {mouth}; the window with the most water instead.");
				}
			}

			// FISHMMO_INLAND_FOCUS=fall: the window centred on the scene's biggest fall, and views of it.
			bool fallFocus = Environment.GetEnvironmentVariable("FISHMMO_INLAND_FOCUS") == "fall";
			FishMMO.Water.InlandWaterRenderer.Fall? bigFall = null;
			if (fallFocus)
			{
				var scratch = ScriptableObject.CreateInstance<SceneHydrology>();
				water.WriteTo(scratch);
				List<FishMMO.Water.InlandWaterRenderer.Fall> found = FishMMO.Water.InlandWaterRenderer.FindFalls(scratch, 1.5f);
				var summary = new List<string> { $"falls found: {found.Count}" };
				foreach (FishMMO.Water.InlandWaterRenderer.Fall f in found)
				{
					summary.Add($"  river {f.River} points {f.Lip}-{f.Foot}: drop {f.Drop:0.0} m, width {f.Width:0.0} m, speed {f.Speed:0.0} m/s, foot ({f.FootPoint.x:0}, {f.FootPoint.y:0}, {f.FootPoint.z:0})");
					if (bigFall == null || f.Drop > bigFall.Value.Drop)
					{
						bigFall = f;
					}
				}
				if (bigFall != null)
				{
					// Round the biggest fall's lip: the water, its bed and width, and the ground at the line and either side.
					FishMMO.Water.InlandWaterRenderer.Fall f = bigFall.Value;
					SceneHydrology.River line = scratch.Rivers.Find(r => r.Id == f.River);
					summary.Add($"lip {f.Lip}, plunge {f.Plunge}, foot {f.Foot}: i, x, z, water, bed, width, reach, ground at the line, left/right at half + 1 m, kind");
					for (int k = Mathf.Max(0, f.Lip - 14); line != null && k <= Mathf.Min(line.Points.Length - 1, f.Foot + 8); k++)
					{
						Vector3 p = line.Points[k];
						Vector3 along = line.Points[Mathf.Min(line.Points.Length - 1, k + 1)] - line.Points[Mathf.Max(0, k - 1)];
						along.y = 0f;
						along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
						var left = new Vector3(-along.z, 0f, along.x);
						float reachOut = 0.5f * line.Width[k] + 1f;
						int cx = Mathf.Clamp(Mathf.RoundToInt((p.x - field.EastOf(0)) / field.Spacing), 0, field.Width - 1);
						int cz = Mathf.Clamp(Mathf.RoundToInt((p.z - field.NorthOf(0)) / field.Spacing), 0, field.Depth - 1);
						summary.Add($"  {k}: {p.x:0.0}, {p.z:0.0}, {p.y:0.00}, {line.Bed[k]:0.00}, {line.Width[k]:0.0}, {line.Reach[k]}, " +
							$"{field.MetresAt(p.x, p.z):0.00}, {field.MetresAt(p.x + left.x * reachOut, p.z + left.z * reachOut):0.00}/" +
							$"{field.MetresAt(p.x - left.x * reachOut, p.z - left.z * reachOut):0.00}, {water.Grid.Kind[cz * field.Width + cx]}");
					}
				}
				File.WriteAllLines(Path.Combine(output, "falls.txt"), summary);
				if (bigFall != null)
				{
					bestX = Mathf.Clamp(Mathf.RoundToInt((bigFall.Value.FootPoint.x - field.EastOf(0)) / field.Spacing) - window / 2, 0, field.Width - 1 - window);
					bestZ = Mathf.Clamp(Mathf.RoundToInt((bigFall.Value.FootPoint.z - field.NorthOf(0)) / field.Spacing) - window / 2, 0, field.Depth - 1 - window);
				}
				else
				{
					Debug.LogWarning("[Inland water probe] no fall of 1.5 m or more in this water.");
				}
			}

			// FISHMMO_INLAND_FOCUS=pond: the window centred on the first lake a river ponds (SceneWater.Pond; no planet lake).
			if (Environment.GetEnvironmentVariable("FISHMMO_INLAND_FOCUS") == "pond")
			{
				SceneLake pond = water.Lakes.Find(l => l.PlanetLake < 0 && l.Seeds.Count > 0);
				if (pond != null)
				{
					Vector2 centre = Vector2.zero;
					foreach (Vector2 seed in pond.Seeds)
					{
						centre += seed / pond.Seeds.Count;
					}
					bestX = Mathf.Clamp(Mathf.RoundToInt((centre.x - field.EastOf(0)) / field.Spacing) - window / 2, 0, field.Width - 1 - window);
					bestZ = Mathf.Clamp(Mathf.RoundToInt((centre.y - field.NorthOf(0)) / field.Spacing) - window / 2, 0, field.Depth - 1 - window);
					Debug.Log($"[Inland water probe] pond at {pond.Level:0.00} m round ({centre.x:0}, {centre.y:0}), {pond.Covered} samples covered.");
				}
				else
				{
					Debug.LogWarning("[Inland water probe] no pond in this water.");
				}
			}

			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			Terrain terrain = BuildTerrain(field, water, bestX, bestZ, window);
			// The sea, as a generated scene has it, where the window's ground reaches down to it: the FFT surface and its shore.
			FishMMO.Water.WaterSurface sea = null;
			if (!float.IsNegativeInfinity(water.SeaLevel) && terrain.transform.position.y < water.SeaLevel + new RiverSettings().IntertidalMetres)
			{
				var seaHost = new GameObject("Water");
				seaHost.AddComponent<MeshFilter>();
				seaHost.AddComponent<MeshRenderer>();
				seaHost.transform.position = new Vector3(0f, water.SeaLevel, 0f);
				sea = seaHost.AddComponent<FishMMO.Water.WaterSurface>();
				Material ocean = AssetDatabase.LoadAssetAtPath<Material>(SceneGenerator.WaterMaterialPath);
				sea.Material = ocean != null ? new Material(ocean) : null;
				sea.Spectrum = AssetDatabase.LoadAssetAtPath<ComputeShader>(SceneGenerator.WaterSpectrumPath);
				sea.OuterRadius = 8000f;
				sea.ReportQuality = false;
				sea.Rebuild();
				seaHost.AddComponent<FishMMO.Water.WaterShoreField>().Build();
				seaHost.AddComponent<FishMMO.Water.WaterShore>();
			}
			var hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			water.WriteTo(hydrology);

			var host = new GameObject("Inland Water");
			var bodies = host.AddComponent<SceneWaterBodies>();
			bodies.Hydrology = hydrology;
			var renderer = host.AddComponent<FishMMO.Water.InlandWaterRenderer>();
			var material = new Material(Shader.Find(SceneGenerator.InlandWaterShaderName));
			material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/FishMMO Water/Textures/WaterNormal.png"));
			material.SetTexture("_FoamTexture", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/FishMMO Water/Textures/WaterFoam.png"));
			renderer.Material = material;
			var fallMaterial = new Material(Shader.Find(SceneGenerator.WaterfallShaderName));
			fallMaterial.SetTexture("_NormalMap", material.GetTexture("_NormalMap"));
			fallMaterial.SetTexture("_FoamTexture", material.GetTexture("_FoamTexture"));
			renderer.FallMaterial = fallMaterial;
			renderer.SprayMaterial = new Material(Shader.Find(SceneGenerator.WaterfallSprayShaderName));
			renderer.Rebuild();
			// The rivers' solved flow, as a generated scene's biome map carries it.
			var flowClock = System.Diagnostics.Stopwatch.StartNew();
			renderer.SetFlow(RiverFlowBake.SolveInMemory(hydrology));
			Debug.Log($"[Inland water probe] river flow solved in {flowClock.Elapsed.TotalSeconds:0.0} s.");
			Shader.EnableKeyword("_WATER_DEPTH");
			Shader.EnableKeyword("_WATER_REFRACTION");
			Shader.SetGlobalFloat("_FishInlandTime", 12.5f);
			// FISHMMO_INLAND_DEBUG=foam: the foam's inputs as colour (FishInlandWater.hlsl _FishInlandDebugFoam).
			Shader.SetGlobalFloat("_FishInlandDebugFoam", Environment.GetEnvironmentVariable("FISHMMO_INLAND_DEBUG") == "foam" ? 1f : Environment.GetEnvironmentVariable("FISHMMO_INLAND_DEBUG") == "noglint" ? 2f : 0f);

			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.4f;
			sun.color = new Color(1f, 0.96f, 0.9f);
			sun.shadows = LightShadows.Soft;
			// FISHMMO_INLAND_SUN="elevation,heading" (degrees): a low evening sun shows what grazing light picks out.
			float sunElevation = 40f, sunHeading = 150f;
			string sunAsked = Environment.GetEnvironmentVariable("FISHMMO_INLAND_SUN");
			if (!string.IsNullOrEmpty(sunAsked))
			{
				string[] parts = sunAsked.Split(',');
				float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sunElevation);
				if (parts.Length > 1)
				{
					float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sunHeading);
				}
				sun.color = sunElevation < 15f ? new Color(1f, 0.72f, 0.45f) : sun.color;
			}
			sun.transform.rotation = Quaternion.Euler(sunElevation, sunHeading, 0f);
			RenderSettings.sun = sun;
			RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
			RenderSettings.ambientMode = AmbientMode.Skybox;
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();
			// The sky for the water to reflect: a new scene in a headless editor has no baked reflection.
			var probe = new GameObject("Reflection").AddComponent<ReflectionProbe>();
			probe.mode = ReflectionProbeMode.Realtime;
			probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
			probe.size = new Vector3(20000f, 4000f, 20000f);
			probe.transform.position = new Vector3(field.EastOf(bestX) + window, field.MetresAt(field.EastOf(bestX) + window, field.NorthOf(bestZ) + window) + 60f, field.NorthOf(bestZ) + window);
			probe.resolution = 256;
			probe.RenderProbe();

			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.Skybox;
			camera.fieldOfView = 45f;
			camera.nearClipPlane = 0.3f;
			camera.farClipPlane = 6000f;
			// The water reads the depth and opaque copies: asked for on the camera, whatever the URP asset's tier
			// (by reflection: this assembly does not reference URP).
			Type cameraData = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
			if (cameraData != null)
			{
				Component data = camera.gameObject.AddComponent(cameraData);
				cameraData.GetProperty("requiresDepthTexture")?.SetValue(data, true);
				cameraData.GetProperty("requiresColorTexture")?.SetValue(data, true);
				cameraData.GetProperty("renderShadows")?.SetValue(data, true);
			}
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;

			Rect area = new Rect(field.EastOf(bestX), field.NorthOf(bestZ), window * field.Spacing, window * field.Spacing);

			var log = new List<string>();
			var views = Views(water, area, field);
			if (bigFall != null)
			{
				FishMMO.Water.InlandWaterRenderer.Fall f = bigFall.Value;
				Vector3 down = f.FootPoint - f.LipPoint;
				down.y = 0f;
				down = down.sqrMagnitude > 1e-4f ? down.normalized : Vector3.forward;
				var side = new Vector3(-down.z, 0f, down.x);
				Vector3 middle = Vector3.Lerp(f.LipPoint, f.FootPoint, 0.5f);
				float back = Mathf.Max(18f, f.Drop * 1.8f + f.Width * 1.5f);
				views = new List<(string, Vector3, Vector3, bool)>
				{
					("fall_front", f.FootPoint + down * back + Vector3.up * (f.Drop * 0.35f + 2.5f), middle, false),
					("fall_side", middle + side * back * 0.9f + down * (back * 0.3f) + Vector3.up * (f.Drop * 0.3f + 2f), middle, false),
					("fall_pool", f.FootPoint + down * (f.PoolRadius * 1.6f + 4f) + side * (f.PoolRadius * 0.6f) + Vector3.up * 1.6f, f.FootPoint + Vector3.up * (f.Drop * 0.3f), false),
					("fall_high", f.FootPoint + down * (back * 1.4f) - side * (back * 0.6f) + Vector3.up * (back * 0.9f + f.Drop), middle, false),
					("fall_pooltop", f.FootPoint + down * (f.PoolRadius * 0.8f) + Vector3.up * (f.PoolRadius * 1.4f + 3f), f.FootPoint, false),
					// Over the lip: the river's last metres before it goes over, from above and from upstream.
					("fall_liptop", f.LipPoint + down * 4f + Vector3.up * (12f + 2f * f.Width), f.LipPoint - down * 6f, false),
					("fall_lipback", f.LipPoint - down * (10f + 2f * f.Width) + side * 3f + Vector3.up * (4f + f.Width), f.LipPoint, false),
				};
			}
			// FISHMMO_INLAND_WAKES=1: two logs on the biggest river in the window, their wakes handed to the shader as the
			// running game hands them (InlandWaterRenderer.Wakes): one held still in the current, one crossing it.
			if (Environment.GetEnvironmentVariable("FISHMMO_INLAND_WAKES") == "1")
			{
				SceneHydrology.River wakeRiver = null;
				int wakePoint = -1;
				foreach (SceneHydrology.River r in hydrology.Rivers)
				{
					for (int k = 0; r.Perennial && r.Points != null && k < r.Points.Length; k++)
					{
						Vector3 q = r.Points[k];
						if (area.Contains(new Vector2(q.x, q.z)) && (wakeRiver == null || (r.Width[k] > wakeRiver.Width[wakePoint])))
						{
							wakeRiver = r;
							wakePoint = k;
						}
					}
				}
				if (wakeRiver != null)
				{
					var wakeAt = new Vector4[FishMMO.Water.InlandWaterRenderer.MaxWakes];
					var wakeVelocity = new Vector4[FishMMO.Water.InlandWaterRenderer.MaxWakes];
					for (int w = 0; w < 2; w++)
					{
						int k = Mathf.Clamp(wakePoint + w * 6, 0, wakeRiver.Points.Length - 1);
						Vector3 q = wakeRiver.Points[k];
						var floating = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
						floating.transform.position = q + Vector3.up * 0.05f;
						floating.transform.rotation = Quaternion.Euler(0f, w * 70f, 90f);
						floating.transform.localScale = new Vector3(0.35f, 1.2f, 0.35f);
						wakeAt[w] = new Vector4(q.x, q.y, q.z, 0.5f);
						Vector3 crossing = w == 1 ? Vector3.Cross(Vector3.up, wakeRiver.Points[Mathf.Min(k + 1, wakeRiver.Points.Length - 1)] - q).normalized * 1.4f : Vector3.zero;
						wakeVelocity[w] = new Vector4(crossing.x, crossing.z, 0f, 0f);
						floating.name = w == 0 ? "Log (still)" : "Log (crossing)";
					}
					Shader.SetGlobalVectorArray("_FishWakes", wakeAt);
					Shader.SetGlobalVectorArray("_FishWakeVelocities", wakeVelocity);
					Shader.SetGlobalFloat("_FishWakeCount", 2f);
					Vector3 focus = wakeRiver.Points[wakePoint];
					Vector3 fwd = wakeRiver.Points[Mathf.Min(wakePoint + 1, wakeRiver.Points.Length - 1)] - focus;
					fwd.y = 0f;
					fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
					views.Clear();
					views.Add(("wakes_low", focus - fwd * 6f + Vector3.Cross(Vector3.up, fwd) * 5f + Vector3.up * 2.2f, focus + fwd * 4f, false));
					views.Add(("wakes_above", focus + fwd * 3f + Vector3.up * 9f - Vector3.Cross(Vector3.up, fwd) * 2f, focus + fwd * 3f, false));
				}
			}
			// A view of a river meeting the sea is taken at low water, mean level and high water.
			float tideRange = FishMMO.Water.WaterEnvironment.DefaultMaximumTideMetres;
			var tides = new[] { ("low", -tideRange), ("mean", 0f), ("high", tideRange) };
			int shot = 0;
			foreach ((string name, Vector3 eyeWanted, Vector3 look, bool tidal) in views)
			{
				// Inside the window's ground: a camera past its edge looks at the terrain's cut side, not the scene.
				Vector3 eye = eyeWanted;
				eye.x = Mathf.Clamp(eye.x, area.xMin + 15f, area.xMax - 15f);
				eye.z = Mathf.Clamp(eye.z, area.yMin + 15f, area.yMax - 15f);
				eye.y = Mathf.Max(eye.y, field.MetresAt(eye.x, eye.z) + 1.5f);
				camera.transform.position = eye;
				camera.transform.LookAt(look);
				foreach ((string tideName, float tide) in tidal && sea != null ? tides : new[] { ("", 0f) })
				{
					if (sea != null)
					{
						sea.TideMetres = tide;
					}
					string label = tideName.Length > 0 ? $"{name}_{tideName}" : name;
					string file = $"inland_{shot++:00}_{label}.png";
					Capture(camera, target, Path.Combine(output, file));
					log.Add($"{file}: {label} from ({eye.x:0}, {eye.y:0}, {eye.z:0}) to ({look.x:0}, {look.y:0}, {look.z:0})");
				}
			}
			if (sea != null)
			{
				sea.TideMetres = 0f;
			}
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
			Debug.Log($"[Inland water probe] {views.Count} views written to '{output}'; {renderer.Built.Count} surfaces.");
		}

		/// <summary>
		/// The water past a scene's edge (<see cref="BackdropWater"/>): Baoakraal (or FISHMMO_INLAND_SCENE) shaped
		/// with its water, a 3 km window across the edge where a river crosses it (or the backdrop lake nearest),
		/// the scene's ground inside and the backdrop's cut ground outside, the scene's water and the backdrop's
		/// drawn on it. Writes PNGs to FISHMMO_INLAND_OUT; saves nothing into the project.
		/// </summary>
		public static void RunBackdrop()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_INLAND_OUT") ?? Path.Combine(Path.GetTempPath(), "fishmmo-backdrop-water");
			Directory.CreateDirectory(output);
			string sceneName = Environment.GetEnvironmentVariable("FISHMMO_INLAND_SCENE") ?? "Baoakraal Hyena-den";
			WorldAtlasScene entry = null;
			foreach (WorldAtlasScene candidate in WorldEditorAssets.FindAll<WorldAtlasScene>())
			{
				if (candidate != null && candidate.SceneName == sceneName)
				{
					entry = candidate;
				}
			}
			if (entry == null || entry.Body == null)
			{
				Debug.LogError($"[Backdrop water probe] no placed atlas entry '{sceneName}'.");
				return;
			}
			var request = new SceneGenerationRequest
			{
				SceneName = entry.SceneName, Body = entry.Body, Layer = entry.Layer, Latitude = entry.Latitude, Longitude = entry.Longitude,
				SizeKm = entry.SizeKm, HeadingDegrees = entry.HeadingDegrees, ErosionStrength = entry.ErosionStrength,
			};
			SolarSystemProfile system = SolarSystemProfile.Resolve(entry.Body);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			SceneHeightField field = SceneGround.Shape(request, plan, system, new List<string>(), out _, out SceneWater water);
			float halfW = plan.WidthMetres * 0.5f, halfD = plan.DepthMetres * 0.5f;
			float reach = SceneBackdropBuilder.ReachFor(request);
			BackdropWater backdrop = BackdropWater.Build(request, water, field.MetresAt, halfW, halfD, reach);
			if (backdrop == null || backdrop.Empty)
			{
				Debug.LogError("[Backdrop water probe] no water past the edge.");
				return;
			}
			Debug.Log($"[Backdrop water probe] {backdrop.Rivers.Count} river run(s) past the edge; scene water: {(water != null ? water.Rivers.Count : 0)} river(s), {(water != null ? water.Lakes.Count : 0)} lake(s).");

			// Where to look: the first river run touching the scene's edge, else the longest run.
			BackdropWater.Line focus = null;
			Vector2 centre = Vector2.zero;
			foreach (BackdropWater.Line line in backdrop.Rivers)
			{
				foreach (Vector2 end in new[] { line.Points[0], line.Points[line.Points.Length - 1] })
				{
					float past = Mathf.Max(Mathf.Abs(end.x) - halfW, Mathf.Abs(end.y) - halfD);
					if (past < 5f && (focus == null || line.Points.Length > focus.Points.Length))
					{
						focus = line;
						centre = end;
					}
				}
			}
			if (focus == null)
			{
				foreach (BackdropWater.Line line in backdrop.Rivers)
				{
					if (focus == null || line.Points.Length > focus.Points.Length)
					{
						focus = line;
						centre = line.Points[line.Points.Length / 2];
					}
				}
			}
			float size = 3000f;
			Func<float, float, float> ground = (east, north) =>
				Mathf.Abs(east) <= halfW && Mathf.Abs(north) <= halfD ? field.MetresAt(east, north) : backdrop.Cut(east, north, field.MetresAt(east, north));

			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			int res = 1025;
			float x0 = centre.x - size * 0.5f, z0 = centre.y - size * 0.5f, step = size / (res - 1);
			var heights = new float[res, res];
			float lowest = float.MaxValue, highest = float.MinValue;
			var raw = new float[res * res];
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					float h = ground(x0 + x * step, z0 + z * step);
					raw[z * res + x] = h;
					lowest = Mathf.Min(lowest, h);
					highest = Mathf.Max(highest, h);
				}
			}
			float relief = Mathf.Max(10f, highest - lowest);
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					heights[z, x] = (raw[z * res + x] - lowest) / relief;
				}
			}
			var data = new TerrainData { heightmapResolution = res, size = new Vector3(size, relief, size) };
			data.SetHeights(0, 0, heights);
			TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_GrassMeadow.terrainlayer");
			TerrainLayer gravel = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_Gravel.terrainlayer");
			data.terrainLayers = new[] { grass, gravel };
			int alpha = 512;
			data.alphamapResolution = alpha;
			var splat = new float[alpha, alpha, 2];
			for (int z = 0; z < alpha; z++)
			{
				for (int x = 0; x < alpha; x++)
				{
					float wet = backdrop.Wetness(x0 + (x + 0.5f) * size / alpha, z0 + (z + 0.5f) * size / alpha, size / alpha);
					splat[z, x, 0] = 1f - wet;
					splat[z, x, 1] = wet;
				}
			}
			data.SetAlphamaps(0, 0, splat);
			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			terrainObject.transform.position = new Vector3(x0, lowest, z0);
			Terrain terrain = terrainObject.GetComponent<Terrain>();
			terrain.materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
			terrain.heightmapPixelError = 2f;

			var material = new Material(Shader.Find(SceneGenerator.InlandWaterShaderName));
			material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/FishMMO Water/Textures/WaterNormal.png"));
			material.SetTexture("_FoamTexture", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Plugins/FishMMO Water/Textures/WaterFoam.png"));
			// The scene's own water, as the game draws it.
			if (water != null && water.Any)
			{
				var hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
				water.WriteTo(hydrology);
				var host = new GameObject("Inland Water");
				host.AddComponent<SceneWaterBodies>().Hydrology = hydrology;
				var renderer = host.AddComponent<FishMMO.Water.InlandWaterRenderer>();
				renderer.Material = material;
				renderer.Rebuild();
			}
			// The backdrop's, on the ground drawn.
			int surfaces = 0;
			foreach ((string name, Mesh mesh, int order) in backdrop.Meshes((east, north) => terrain.SampleHeight(new Vector3(east, 0f, north)) + terrainObject.transform.position.y))
			{
				var go = new GameObject(name);
				go.AddComponent<MeshFilter>().sharedMesh = mesh;
				MeshRenderer mr = go.AddComponent<MeshRenderer>();
				mr.sharedMaterial = material;
				mr.sortingOrder = 1000 + order;
				surfaces++;
			}
			Shader.EnableKeyword("_WATER_DEPTH");
			Shader.EnableKeyword("_WATER_REFRACTION");
			Shader.SetGlobalFloat("_FishInlandTime", 12.5f);
			// FISHMMO_INLAND_DEBUG=foam: the foam's inputs as colour (FishInlandWater.hlsl _FishInlandDebugFoam).
			Shader.SetGlobalFloat("_FishInlandDebugFoam", Environment.GetEnvironmentVariable("FISHMMO_INLAND_DEBUG") == "foam" ? 1f : Environment.GetEnvironmentVariable("FISHMMO_INLAND_DEBUG") == "noglint" ? 2f : 0f);

			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.4f;
			sun.shadows = LightShadows.Soft;
			sun.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
			RenderSettings.sun = sun;
			RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
			RenderSettings.ambientMode = AmbientMode.Skybox;
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();
			var probe = new GameObject("Reflection").AddComponent<ReflectionProbe>();
			probe.mode = ReflectionProbeMode.Realtime;
			probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
			probe.size = new Vector3(20000f, 4000f, 20000f);
			probe.transform.position = new Vector3(centre.x, highest + 60f, centre.y);
			probe.RenderProbe();
			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.Skybox;
			camera.fieldOfView = 45f;
			camera.nearClipPlane = 0.3f;
			camera.farClipPlane = 8000f;
			Type cameraData = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
			if (cameraData != null)
			{
				Component d = camera.gameObject.AddComponent(cameraData);
				cameraData.GetProperty("requiresDepthTexture")?.SetValue(d, true);
				cameraData.GetProperty("requiresColorTexture")?.SetValue(d, true);
			}
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;

			float groundAt = ground(centre.x, centre.y);
			// Outward from the scene across the edge: the direction away from the scene's centre.
			Vector2 outward = centre.sqrMagnitude > 1f ? centre.normalized : Vector2.right;
			var views = new List<(string, Vector3, Vector3)>
			{
				("edge_above", new Vector3(centre.x, groundAt + 420f, centre.y) - new Vector3(outward.x, 0f, outward.y) * 300f, new Vector3(centre.x, groundAt, centre.y) + new Vector3(outward.x, 0f, outward.y) * 300f),
				("edge_low", new Vector3(centre.x, groundAt + 40f, centre.y) - new Vector3(outward.x, 0f, outward.y) * 120f, new Vector3(centre.x, groundAt, centre.y) + new Vector3(outward.x, 0f, outward.y) * 120f),
				("edge_top", new Vector3(centre.x, groundAt + 900f, centre.y) + new Vector3(0.01f, 0f, 0f), new Vector3(centre.x, groundAt, centre.y)),
				("far_look", new Vector3(centre.x, groundAt + 120f, centre.y) - new Vector3(outward.x, 0f, outward.y) * 200f, new Vector3(centre.x, groundAt - 50f, centre.y) + new Vector3(outward.x, 0f, outward.y) * 1400f),
			};
			var log = new List<string>();
			int shot = 0;
			foreach ((string name, Vector3 eye, Vector3 look) in views)
			{
				camera.transform.position = eye;
				camera.transform.LookAt(look);
				string file = $"backdrop_{shot++:00}_{name}.png";
				Capture(camera, target, Path.Combine(output, file));
				log.Add($"{file}: {name}");
			}
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
			Debug.Log($"[Backdrop water probe] {views.Count} views at ({centre.x:0}, {centre.y:0}); {surfaces} backdrop surface(s).");
		}

		/// <summary>The viewpoints: the whole window from above, each lake, each confluence, each stretch of fast water, low along a river.</summary>
		private static List<(string, Vector3, Vector3, bool)> Views(SceneWater water, Rect area, SceneHeightField field)
		{
			var views = new List<(string, Vector3, Vector3, bool)>();
			Vector2 centre = area.center;
			float groundAtCentre = field.MetresAt(centre.x, centre.y);
			views.Add(("overview", new Vector3(centre.x - area.width * 0.35f, groundAtCentre + area.width * 0.45f, centre.y - area.height * 0.45f), new Vector3(centre.x, groundAtCentre, centre.y), false));
			foreach (SceneLake lake in water.Lakes)
			{
				Vector2 c = lake.Bounds.center;
				if (!area.Contains(c) || lake.Covered == 0)
				{
					continue;
				}
				float r = Mathf.Max(80f, Mathf.Min(lake.Bounds.width, lake.Bounds.height) * 0.6f);
				views.Add(($"lake{lake.Id}", new Vector3(c.x - r, lake.Level + r * 0.5f, c.y - r), new Vector3(c.x, lake.Level, c.y), false));
			}
			foreach (RiverPath river in water.Rivers)
			{
				if (!river.Perennial || river.Count < 10)
				{
					continue;
				}
				// Its end, where it meets other water, and a low view along its middle.
				int last = river.Count - 1;
				var end = new Vector2(river.X[last], river.Z[last]);
				if (area.Contains(end) && (river.End == RiverEnd.Confluence || river.End == RiverEnd.Lake || river.End == RiverEnd.Sea))
				{
					int back = Mathf.Max(0, last - 20);
					Vector3 from = new Vector3(river.X[back], river.Surface[back], river.Z[back]);
					Vector3 to = new Vector3(end.x, river.Surface[last], end.y);
					Vector3 dir = (to - from).normalized;
					bool sea = river.End == RiverEnd.Sea;
					views.Add(($"river{river.Id}_{river.End}".ToLowerInvariant(), to - dir * 40f + Vector3.up * 25f + Vector3.Cross(dir, Vector3.up) * 20f, to, sea));
					if (river.End != RiverEnd.Confluence)
					{
						// Straight down on the mouth.
						views.Add(($"river{river.Id}_{river.End}_top".ToLowerInvariant(), to + Vector3.up * 110f - dir * 0.5f, to, sea));
					}
					if (sea)
					{
						// The whole of its lower course across the shore, from higher and further back.
						int further = Mathf.Max(0, last - 120);
						Vector3 middle = new Vector3(river.X[(further + last) / 2], river.Surface[(further + last) / 2], river.Z[(further + last) / 2]);
						views.Add(($"river{river.Id}_sea_wide", middle - dir * 160f + Vector3.up * 90f + Vector3.Cross(dir, Vector3.up) * 60f, middle, true));
					}
				}
				// Where it leaves a lake: the outlet, from over the lake.
				var start = new Vector2(river.X[0], river.Z[0]);
				if (river.Start == RiverEnd.Lake && area.Contains(start))
				{
					int ahead = Mathf.Min(last, 20);
					Vector3 from = new Vector3(start.x, river.Surface[0], start.y);
					Vector3 to = new Vector3(river.X[ahead], river.Surface[ahead], river.Z[ahead]);
					Vector3 dir = (to - from).normalized;
					views.Add(($"river{river.Id}_outlet", from - dir * 50f + Vector3.up * 25f + Vector3.Cross(dir, Vector3.up) * 15f, to, false));
				}
				// Three places along the part of it in the window: a low view down the river and one from above at an angle.
				var inside = new List<int>();
				for (int i = 0; i < river.Count; i++)
				{
					if (area.Contains(new Vector2(river.X[i], river.Z[i])) && i + 15 <= last)
					{
						inside.Add(i);
					}
				}
				for (int k = 1; k <= 3 && inside.Count > 30; k++)
				{
					int i = inside[inside.Count * k / 4];
					Vector3 here = new Vector3(river.X[i], river.Surface[i], river.Z[i]);
					Vector3 there = new Vector3(river.X[i + 15], river.Surface[i + 15], river.Z[i + 15]);
					Vector3 dir = (there - here).normalized;
					Vector3 side = Vector3.Cross(Vector3.up, dir);
					views.Add(($"river{river.Id}_low{k}", here - dir * 14f + side * 3f + Vector3.up * 5f, there, false));
					views.Add(($"river{river.Id}_above{k}", here + side * 45f + Vector3.up * 55f, here + dir * 20f, false));
				}
			}
			return views;
		}

		/// <summary>Where the body's largest river ending in <paramref name="end"/> reaches it, by its discharge there.</summary>
		private static bool Mouth(WorldBody body, RiverEnd end, out double latitude, out double longitude)
		{
			latitude = longitude = 0.0;
			PlanetDrainage drainage = PlanetDrainage.For(body, SolarSystemProfile.Resolve(body), PlanetSurface.SceneRadiusKm(body));
			DrainageRiver best = null;
			foreach (DrainageRiver river in drainage.Result.Rivers)
			{
				int n = river.Cells.Length;
				if (river.End == end && n >= 8 && (best == null || river.Discharge[n - 2] > best.Discharge[best.Cells.Length - 2]))
				{
					best = river;
				}
			}
			if (best == null)
			{
				return false;
			}
			int cell = best.Cells[best.Cells.Length - 1];
			AtlasGeometry.FromUnit(new Vector3d(drainage.Grid.Centre[cell * 3], drainage.Grid.Centre[cell * 3 + 1], drainage.Grid.Centre[cell * 3 + 2]), out latitude, out longitude);
			Debug.Log($"[Inland water probe] the largest river ending in a {end}: #{best.Id}, {best.Discharge[best.Cells.Length - 2]:0.00} m³/s, at {latitude:0.0000}, {longitude:0.0000}.");
			return true;
		}

		/// <summary>A terrain made in memory over the window, painted grass with sand and gravel where the water laid bars and its bed.</summary>
		private static Terrain BuildTerrain(SceneHeightField field, SceneWater water, int x0, int z0, int window)
		{
			int res = window + 1;
			float lowest = float.MaxValue, highest = float.MinValue;
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					float h = field.Metres[(z0 + z) * field.Width + x0 + x];
					lowest = Mathf.Min(lowest, h);
					highest = Mathf.Max(highest, h);
				}
			}
			float relief = Mathf.Max(10f, highest - lowest);
			float size = window * field.Spacing;
			var data = new TerrainData { heightmapResolution = res, size = new Vector3(size, relief, size) };
			var heights = new float[res, res];
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					heights[z, x] = (field.Metres[(z0 + z) * field.Width + x0 + x] - lowest) / relief;
				}
			}
			data.SetHeights(0, 0, heights);
			TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_GrassMeadow.terrainlayer");
			TerrainLayer sand = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_Sand.terrainlayer");
			TerrainLayer gravel = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_Gravel.terrainlayer");
			TerrainLayer rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_Rock.terrainlayer");
			data.terrainLayers = new[] { grass, sand, gravel, rock };
			int alpha = 2048; // ~2 m a texel over the window, as a generated scene paints (1000 m tiles at 512): at 1024 each was 4 m and bar edges drew as steps
			data.alphamapResolution = alpha;
			var splat = new float[alpha, alpha, 4];
			for (int z = 0; z < alpha; z++)
			{
				for (int x = 0; x < alpha; x++)
				{
					int sx = x0 + Mathf.Clamp(Mathf.RoundToInt((x + 0.5f) / alpha * window), 0, window);
					int sz = z0 + Mathf.Clamp(Mathf.RoundToInt((z + 0.5f) / alpha * window), 0, window);
					int i = sz * field.Width + sx;
					float steep = data.GetSteepness((x + 0.5f) / alpha, (z + 0.5f) / alpha);
					// The bars as the generator paints them (SceneWater.BarAt: warped and eased, not the grid's cells).
					float worldX = field.EastOf(x0) + (x + 0.5f) / alpha * window * field.Spacing;
					float worldZ = field.NorthOf(z0) + (z + 0.5f) / alpha * window * field.Spacing;
					Vector2 barShare = water.BarAt(worldX, worldZ);
					switch (water.Grid.Kind[i])
					{
						case WaterKind.Bar:
						case WaterKind.Bank:
						case WaterKind.None:
							if (barShare.x > 0f)
							{
								// Cliffs first, as BiomeSplatPainter does: a bar takes only the ground's share, never rock.
								float r0 = Mathf.Clamp01((steep - 30f) / 15f);
								float ground = 1f - r0;
								splat[z, x, 0] = ground * (1f - barShare.x);
								splat[z, x, 3] = r0;
								splat[z, x, 1] = ground * barShare.x * barShare.y;
								splat[z, x, 2] = ground * barShare.x * (1f - barShare.y);
								break;
							}
							goto default;
						case WaterKind.River:
						case WaterKind.Lake:
						case WaterKind.DryWash:
							splat[z, x, 2] = 1f;
							break;
						default:
							float r = Mathf.Clamp01((steep - 30f) / 15f);
							splat[z, x, 0] = 1f - r;
							splat[z, x, 3] = r;
							break;
					}
				}
			}
			data.SetAlphamaps(0, 0, splat);
			GameObject go = Terrain.CreateTerrainGameObject(data);
			go.transform.position = new Vector3(field.EastOf(x0), lowest, field.NorthOf(z0));
			Terrain terrain = go.GetComponent<Terrain>();
			terrain.materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
			terrain.heightmapPixelError = 2f;
			return terrain;
		}

		internal static void Capture(Camera camera, RenderTexture target, string path)
		{
			for (int i = 0; i < 3; i++)
			{
				camera.Render();
			}
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			File.WriteAllBytes(path, image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
		}
	}
}
#endif
