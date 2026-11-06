using System;
using System.Globalization;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FishMMO.TestHarness.World
{
	/// <summary>
	/// The rendering test beds' runtime switches, by name: the browser probe's SetSetting (WebGpuSmokeProbe) and the native
	/// player's --set-a / --set-b (PlayerPerfProbe) apply the same list, so a browser A/B and a native A/B mean the same thing.
	/// </summary>
	public static class HarnessSettings
	{
		private static float skyProbeIntensity = -1f;
		private static bool distanceFogOff;
		private static bool hooked;

		/// <summary>Applies "name=value;name=value" (empty parts skipped).</summary>
		public static void ApplyList(string list)
		{
			foreach (string part in (list ?? "").Split(';'))
			{
				if (part.Length == 0) continue;
				int at = part.IndexOf('=');
				if (at <= 0) throw new ArgumentException("Expected name=value");
				Apply(part.Substring(0, at), part.Substring(at + 1));
			}
		}

		public static void Apply(string name, string value)
		{
			var inv = CultureInfo.InvariantCulture;
			var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			switch (name)
			{
				case "shadowCascades": urp.shadowCascadeCount = int.Parse(value, inv); break;
				case "shadowDistance": urp.shadowDistance = float.Parse(value, inv); break;
				case "shadowResolution": urp.mainLightShadowmapResolution = int.Parse(value, inv); break;
				case "additionalShadowResolution": urp.additionalLightsShadowmapResolution = int.Parse(value, inv); break;
				case "msaa": urp.msaaSampleCount = int.Parse(value, inv); break;
				case "ambientLife": if (WeatherRenderProfile.Active != null) WeatherRenderProfile.Active.AmbientLife.Enabled = value == "1"; break;
				case "ambientLifeMax": if (WeatherRenderProfile.Active != null) WeatherRenderProfile.Active.AmbientLife.MaxVisible = int.Parse(value, inv); break;
				case "scatter": DetailScatterSystem.Enabled = value == "1"; break;
				case "scatterBatching": DetailScatterRenderer.BatchDraws = value == "1"; break;
				// The scatter's takes effect at its next layout; the props' only for models registered after (a scene load).
				case "scatterBrg": DetailScatterRenderer.UseBrg = value == "1"; break;
				case "vegTable": DetailScatterRenderer.VegFamilies = value == "1"; break;
				case "runGather": TerrainGpuRenderer.RunGather = value == "1"; break;
				// The props' camera draws joined per model level (TerrainGpuRenderer.JoinSlotParts); takes effect at the next layout.
				case "joinParts": TerrainGpuRenderer.JoinSlotParts = value == "1"; break;
				case "rockDemand": RockContactField.DemandOnly = value == "1"; break;
				// What each feature costs a frame: drawn or not, everything else as ever. hideFamilies takes the props' model
				// families (Structure,Rock,Tree…: the props tally's names), hideLayers layer names off the main camera.
				case "hideGrass": GrassBladeRenderer.HideDraws = value == "1"; break;
				// The grass A/B: its distance (metres over the player's setting, 0 the player's), every ring's density × k, and the
				// distances its blades drop to the 7-vertex strip and the triangle ("-" restores what the profile shipped with).
				case "grassDistance": GrassBladeSystem.DistanceOverride = float.Parse(value, inv); break;
				case "grassEarly": GrassBladeRenderer.EarlyExits = value == "1"; break;
				case "grassDensity":
				case "grassLod0":
				case "grassLod1":
				{
					GrassBladeSettings g = WeatherRenderProfile.Active != null ? WeatherRenderProfile.Active.Grass : null;
					if (g == null) break;
					shippedRings ??= g.Rings;
					if (shippedLod0 < 0f) { shippedLod0 = g.Lod0Distance; shippedLod1 = g.Lod1Distance; }
					if (name == "grassDensity")
					{
						float k = value == "-" ? 1f : float.Parse(value, inv);
						var rings = new Vector2[shippedRings.Length];
						for (int i = 0; i < rings.Length; i++) rings[i] = new Vector2(shippedRings[i].x, shippedRings[i].y * k);
						g.Rings = rings;
					}
					else if (name == "grassLod0") g.Lod0Distance = value == "-" ? shippedLod0 : float.Parse(value, inv);
					else g.Lod1Distance = value == "-" ? shippedLod1 : float.Parse(value, inv);
				}
				break;
				case "hideTerrain": TerrainHeightmapSystem.HideDraws = value == "1"; break;
				// The ground's A/B: its render queue (−1 the shader's, 2450 after the other opaques: the default), the strongest layers
				// a pixel blends (4 every layer, the old look; 2 or 3; 0 the shader's default, 3), and the distance its layers' normal
				// and mask maps fade out over ("from,to" metres; "0" never, the old look; "default" the shader's 40–80 m).
				case "terrainQueue": TerrainHeightmapSystem.QueueOverride = int.Parse(value, inv); break;
				case "terrainLayers": Shader.SetGlobalFloat("_FishTerrainMaxLayers", float.Parse(value, inv)); break;
				case "terrainFarDetail":
				{
					string[] range = value.Split(',');
					Shader.SetGlobalVector("_FishTerrainDetailFar", range.Length == 2
						? new Vector4(float.Parse(range[0], inv), float.Parse(range[1], inv), 0f, 0f)
						: value == "default" ? Vector4.zero : new Vector4(0f, -1f, 0f, 0f));
				}
				break;
				case "hideFamilies": TerrainGpuRenderer.HideFamilies = value == "-" ? "" : value; break;
				case "hideLayers":
				{
					Camera main = Camera.main;
					if (main != null)
					{
						if (layersShown < 0) layersShown = main.cullingMask;
						int mask = 0;
						foreach (string layer in value.Split(',')) if (layer.Length > 0 && layer != "-") mask |= LayerMask.GetMask(layer);
						main.cullingMask = layersShown & ~mask;
					}
				}
				break;
				case "mergeMaterials": DetailScatterRenderer.MergeIdenticalMaterials = TerrainGpuRenderer.MergeIdenticalMaterials = value == "1"; break;
				case "ssao": SetFeature("ScreenSpaceAmbientOcclusion", value == "1"); break;
				case "paused": { var world = Object.FindAnyObjectByType<WorldSimController>(); if (world != null) world.Paused = value == "1"; } break;
				case "runRate": { var world = Object.FindAnyObjectByType<WorldSimController>(); if (world != null) world.RunRate = double.Parse(value, inv); } break;
				case "cachedShadows": CachedMainLightShadows.Enabled = value == "1"; break;
				case "sunStep": CachedMainLightShadows.SunStepDegrees = float.Parse(value, inv); break;
				case "shadowCacheDebug": CachedMainLightShadows.DebugOverwrite = value == "1"; break;
				case "shadowCacheClear": CachedMainLightShadows.DebugClearDepth = float.Parse(value, inv); break;
				case "shadowCacheReadback": CachedMainLightShadows.ReadbackRequested = value == "1"; break;
				case "propsBrg": TerrainGpuRenderer.UseBrg = value != "0"; break;
				case "angularMargins": TerrainGpuRenderer.AngularMargins = value != "0"; break;
				case "exactLevels": TerrainGpuRenderer.ExactRunLevels = value != "0"; break;
				case "treeDistance": TerrainTreeInstancing.DistanceCap = float.Parse(value, inv); break;
				case "treeLodBias": TerrainTreeInstancing.LodBiasScale = float.Parse(value, inv); break;
				case "billboardVary": Shader.SetGlobalFloat("_FishBillboardVary", value == "1" ? 1f : 0f); break;
				case "billboardSwitch": TerrainTreeInstancing.BillboardSwitchScale = float.Parse(value, inv); break;
				case "billboards": TerrainGpuRenderer.BillboardDiagnostic = value == "hide" ? 1 : value == "primeonly" ? 2 : 0; break;
				case "shadowCacheExact": CachedMainLightShadows.DebugForceExact = value == "1"; break;
				case "shadowCacheLog": Debug.Log($"[Shadow cache] {CachedMainLightShadows.Describe()}"); break;
				// Reflection diagnosis: the sky probe's intensity (held over SkySystem's own each frame; "auto" releases), the props'
				// look arrays, the ground cover (dry, wet, or the clock's own: auto) and the fog features.
				case "skyProbe": skyProbeIntensity = value == "auto" ? -1f : float.Parse(value, inv); Hook(); break;
				case "lookArrays": TerrainGpuRenderer.LookArrays = value == "1"; break;
				case "cover":
				{
					var world = Object.FindAnyObjectByType<WorldSimController>();
					if (world != null) world.CoverOverride = value == "auto" ? (WeatherCover?)null : value == "wet" ? new WeatherCover { Wet = 1f } : default(WeatherCover);
				}
				break;
				// Every fog the harness can take away: the height and volumetric fog passes and the pipeline's distance fog (MixFog in
				// every lit shader, RenderSettings written by FogComposer each Update: held off here before each frame). The fog layer
				// the cloud march draws is the clouds' own and stays.
				case "fog":
					SetFeature("FishHeightFogFeature", value == "1"); SetFeature("FishVolumetricFogFeature", value == "1");
					distanceFogOff = value != "1"; Hook();
					break;
				// N extra point and spot lights round what the camera looks at (0 removes them): the light-table A/B needs more
				// visible additional lights than the views carry, with shadowed point and spot lights and a cookie among them.
				case "testLights": SpawnTestLights(int.Parse(value, inv)); break;
				default: throw new ArgumentException("Unknown setting " + name);
			}
		}

		private static int layersShown = -1;
		private static Vector2[] shippedRings;
		private static float shippedLod0 = -1f, shippedLod1;

		private static GameObject testLights;
		private static Texture2D testCookie;

		/// <summary>
		/// count lights spread over the ground the camera looks at, laid out from the camera alone (the same view gives the
		/// same lights in every player): every third a spot, the first four casting shadows (point soft, spot soft, point
		/// hard, spot hard: 14 shadow slices) and the fifth wearing a striped cookie.
		/// </summary>
		private static void SpawnTestLights(int count)
		{
			if (testLights != null) Object.Destroy(testLights);
			testLights = null;
			Camera camera = Object.FindAnyObjectByType<WorldSimController>()?.Camera ?? Camera.main;
			if (count <= 0 || camera == null) return;
			testLights = new GameObject("Harness Test Lights");
			Transform eye = camera.transform;
			// Where the view meets the ground (in 2 m steps, up to 600 m), else 30 m ahead.
			Vector3 centre = eye.position + eye.forward * 30f;
			for (float d = 2f; d <= 600f; d += 2f)
			{
				Vector3 at = eye.position + eye.forward * d;
				if (GroundAt(camera, at, out float y) && at.y <= y) { centre = new Vector3(at.x, y, at.z); break; }
			}
			float spread = Mathf.Max(8f, 0.25f * Vector3.Distance(eye.position, centre));
			Vector3 right = Vector3.ProjectOnPlane(eye.right, Vector3.up).normalized, forward = Vector3.Cross(right, Vector3.up);
			for (int i = 0; i < count; i++)
			{
				// A sunflower spiral: even cover of the disc, deterministic.
				float radius = spread * Mathf.Sqrt((i + 0.5f) / count), angle = i * 2.39996323f;
				Vector3 at = centre + right * (radius * Mathf.Cos(angle)) + forward * (radius * Mathf.Sin(angle));
				if (GroundAt(camera, at, out float y)) at.y = y;
				float reach = Mathf.Max(9f, 0.3f * spread);
				var light = new GameObject("Test Light " + i).AddComponent<Light>();
				light.transform.SetParent(testLights.transform, false);
				bool spot = i % 3 == 1 || i == 4;
				light.type = spot ? LightType.Spot : LightType.Point;
				light.transform.SetPositionAndRotation(at + Vector3.up * (spot ? 0.5f * reach : 2f), Quaternion.LookRotation(Vector3.down + 0.4f * Mathf.Cos(angle) * right, forward));
				light.range = spot ? 1.5f * reach : reach;
				light.spotAngle = 70f;
				light.innerSpotAngle = 40f;
				light.intensity = 3f;
				light.color = Color.HSVToRGB(i * 0.137f % 1f, 0.55f, 1f);
				light.shadows = i == 0 || i == 1 ? LightShadows.Soft : i == 2 || i == 3 ? LightShadows.Hard : LightShadows.None;
				if (i == 4) light.cookie = TestCookie();
				DirectionalShadowDraws.ConfigurePunctualLight(light);
			}
		}

		/// <summary>The ground's height under p: the camera scene's colliders (a world scene simulates its own physics), else a terrain tile.</summary>
		private static bool GroundAt(Camera camera, Vector3 p, out float y)
		{
			if (camera.gameObject.scene.GetPhysicsScene().Raycast(p + Vector3.up * 300f, Vector3.down, out RaycastHit hit, 600f) ||
				Physics.Raycast(p + Vector3.up * 300f, Vector3.down, out hit, 600f))
			{
				y = hit.point.y;
				return true;
			}
			foreach (Terrain terrain in Terrain.activeTerrains)
			{
				Vector3 origin = terrain.GetPosition(), size = terrain.terrainData.size;
				if (p.x < origin.x || p.z < origin.z || p.x > origin.x + size.x || p.z > origin.z + size.z) continue;
				y = origin.y + terrain.SampleHeight(p);
				return true;
			}
			y = 0f;
			return false;
		}

		private static Texture2D TestCookie()
		{
			if (testCookie != null) return testCookie;
			testCookie = new Texture2D(64, 64, TextureFormat.R8, false) { name = "Harness Test Cookie", wrapMode = TextureWrapMode.Clamp };
			var pixels = new Color32[64 * 64];
			for (int y = 0; y < 64; y++)
				for (int x = 0; x < 64; x++)
					pixels[y * 64 + x] = new Color32((byte)(((x / 8 + y / 8) & 1) == 0 ? 255 : 40), 0, 0, 255);
			testCookie.SetPixels32(pixels);
			testCookie.Apply(false, true);
			return testCookie;
		}

		private static void SetFeature(string typeName, bool active)
		{
			var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			if (urp == null) return;
			foreach (var data in urp.rendererDataList)
				if (data != null)
					foreach (var feature in data.rendererFeatures)
						if (feature != null && feature.GetType().Name.Contains(typeName)) feature.SetActive(active);
		}

		// After every Update (SkySystem writes the probe's intensity there), before the frame renders.
		private static void Hook()
		{
			if (hooked) return;
			hooked = true;
			RenderPipelineManager.beginContextRendering += (context, cameras) =>
			{
				if (distanceFogOff) RenderSettings.fog = false;
				if (skyProbeIntensity < 0f) return;
				foreach (var probe in Object.FindObjectsByType<ReflectionProbe>())
					if (probe.name == "Sky Reflection") probe.intensity = skyProbeIntensity;
			};
		}
	}
}
