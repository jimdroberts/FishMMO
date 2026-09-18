using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.TestHarness.Weather.Editor
{
	/// <summary>
	/// Builds the weather test scene: open ground, a closed house (with a shelter volume), an open
	/// pavilion to show rain stopping at a roof, a few trees, the fly camera, the sim controller and
	/// its control panel.
	/// </summary>
	public static class WeatherSimSceneGenerator
	{
		public const string ScenePath = "Assets/Scenes/Test/WeatherSim.unity";
		public const string GeneratedFolder = "Assets/TestHarness/Weather/Generated";
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

		private static readonly string[] PresetOrder =
		{
			"Clear", "Fair", "Overcast", "Mist", "Sprinkle", "Light Rain", "Medium Rain", "Heavy Rain", "Thunderstorm",
			"Light Snow", "Heavy Snow", "Blizzard", "Hailstorm", "Ashfall", "Sandstorm", "Windy", "Aurora Night",
		};

		[DashboardTool(DashboardToolAttribute.Weather, "Generate Weather Sim scene", Section = "Test bed", Order = 10,
			Tooltip = "Creates the weather content if missing and writes " + ScenePath + ": ground, a house, a pavilion, trees and the weather control panel.",
			Confirm = "Generate the Weather Sim scene? The open scenes are closed (you are asked to save them first) and " + ScenePath + " is overwritten.")]
		public static void GenerateFromDashboard()
		{
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}
			Generate();
		}

		public static Scene Generate()
		{
			WeatherContentGenerator.Generate(WeatherContentGenerator.Root, fillBiomes: false);
			WeatherRenderProfile profile = WeatherRenderAssets.Ensure();
			WorldEditorAssets.EnsureFolder(GeneratedFolder);
			WorldEditorAssets.EnsureFolder("Assets/Scenes/Test");

			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			Material ground = MaterialAsset("Ground", new Color(0.32f, 0.36f, 0.28f));
			Material wall = MaterialAsset("Wall", new Color(0.62f, 0.58f, 0.52f));
			Material roof = MaterialAsset("Roof", new Color(0.42f, 0.24f, 0.2f));
			Material wood = MaterialAsset("Wood", new Color(0.35f, 0.25f, 0.16f));
			Material leaves = MaterialAsset("Leaves", new Color(0.2f, 0.36f, 0.18f));

			var sunObject = new GameObject("Sun");
			Light sun = sunObject.AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.shadows = LightShadows.Soft;
			sun.intensity = 1.1f;
			sunObject.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
			RenderSettings.sun = sun;
			var moonObject = new GameObject("Moon");
			Light moonLight = moonObject.AddComponent<Light>();
			moonLight.type = LightType.Directional;
			moonLight.shadows = LightShadows.None;
			moonLight.intensity = 0f;
			RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
			RenderSettings.ambientEquatorColor = new Color(0.4f, 0.42f, 0.45f);
			RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f);
			RenderSettings.fog = false;

			Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(300f, 1f, 300f), ground, null);

			// A hill of real terrain beside the flat ground. The world's terrain is on its own
			// shader — a fork of Unity's, with its own snow that lifts the ground it lies on — and
			// none of that is exercised by boxes.
			BuildTerrain();

			// A closed house: walls, a door gap, a roof, and a shelter volume inside.
			var house = new GameObject("House");
			house.transform.position = new Vector3(8f, 0f, 10f);
			Box("Wall N", new Vector3(0f, 1.5f, 3f), new Vector3(8f, 3f, 0.3f), wall, house.transform);
			Box("Wall S left", new Vector3(-2.5f, 1.5f, -3f), new Vector3(3f, 3f, 0.3f), wall, house.transform);
			Box("Wall S right", new Vector3(2.5f, 1.5f, -3f), new Vector3(3f, 3f, 0.3f), wall, house.transform);
			Box("Wall E", new Vector3(4f, 1.5f, 0f), new Vector3(0.3f, 3f, 6f), wall, house.transform);
			Box("Wall W", new Vector3(-4f, 1.5f, 0f), new Vector3(0.3f, 3f, 6f), wall, house.transform);
			Box("Roof", new Vector3(0f, 3.15f, 0f), new Vector3(9f, 0.3f, 7f), roof, house.transform);
			var shelter = new GameObject("Shelter Volume");
			shelter.transform.SetParent(house.transform, false);
			shelter.transform.localPosition = new Vector3(0f, 1.5f, 0f);
			var shelterBox = shelter.AddComponent<BoxCollider>();
			shelterBox.isTrigger = true;
			shelterBox.size = new Vector3(7.6f, 3f, 5.6f);
			WeatherVolume volume = shelter.AddComponent<WeatherVolume>();
			volume.Kind = WeatherVolumeKind.Shelter;
			volume.Shape = shelterBox;
			volume.ShelterStrength = 1f;

			// An open pavilion: rain should stop at its roof and fall all around it.
			var pavilion = new GameObject("Pavilion");
			pavilion.transform.position = new Vector3(-6f, 0f, 6f);
			foreach (Vector2 corner in new[] { new Vector2(-2.5f, -2.5f), new Vector2(2.5f, -2.5f), new Vector2(-2.5f, 2.5f), new Vector2(2.5f, 2.5f) })
			{
				Box("Post", new Vector3(corner.x, 1.4f, corner.y), new Vector3(0.3f, 2.8f, 0.3f), wood, pavilion.transform);
			}
			Box("Canopy", new Vector3(0f, 2.95f, 0f), new Vector3(6f, 0.2f, 6f), roof, pavilion.transform);

			var trees = new GameObject("Trees");
			var random = new System.Random(238);
			for (int i = 0; i < 14; i++)
			{
				var at = new Vector3((float)(random.NextDouble() * 60 - 30), 0f, (float)(random.NextDouble() * 40 + 16));
				Primitive(PrimitiveType.Cylinder, "Trunk", at + new Vector3(0f, 1.5f, 0f), new Vector3(0.4f, 1.5f, 0.4f), wood, trees.transform);
				Primitive(PrimitiveType.Sphere, "Crown", at + new Vector3(0f, 4f, 0f), new Vector3(3f, 3f, 3f), leaves, trees.transform);
			}

			var settingsObject = new GameObject("World Scene Settings");
			WorldSceneSettings settings = settingsObject.AddComponent<WorldSceneSettings>();
			ClimateSettings climate = WorldEditorAssets.FindFirst<ClimateSettings>();
			if (climate != null)
			{
				settings.Climate = climate;
			}

			var dayNightObject = new GameObject("Day Night Cycle");
			WorldDayNightCycle dayNight = dayNightObject.AddComponent<WorldDayNightCycle>();
			dayNight.SunLight = sun;
			dayNight.MoonLight = moonLight;

			var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
			Camera camera = cameraObject.AddComponent<Camera>();
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane = 1500f;
			camera.clearFlags = CameraClearFlags.Skybox;
			cameraObject.AddComponent<AudioListener>();
			cameraObject.AddComponent<WeatherSimCamera>();
			cameraObject.transform.position = new Vector3(-2f, 1.8f, -6f);
			cameraObject.transform.rotation = Quaternion.Euler(4f, 20f, 0f);

			var controllerObject = new GameObject("Weather Sim");
			WeatherSimController controller = controllerObject.AddComponent<WeatherSimController>();
			controller.Profile = profile;
			controller.Camera = camera;
			controller.Sun = sun;
			controller.Settings = settings;
			controller.DayNight = dayNight;
			controller.SolarSystem = WorldEditorAssets.FindFirst<FishMMO.Shared.Celestial.SolarSystemProfile>();
			controller.Templates = WorldEditorAssets.FindAll<WeatherLayerTemplate>();
			controller.Presets = OrderedPresets();
			UIDocument document = controllerObject.AddComponent<UIDocument>();
			document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			WeatherSimPanel panel = controllerObject.AddComponent<WeatherSimPanel>();
			panel.Controller = controller;

			EditorSceneManager.SaveScene(scene, ScenePath);
			AssetDatabase.SaveAssets();
			Debug.Log($"[Weather Sim] Wrote {ScenePath}.");
			return scene;
		}

		private static List<WeatherPreset> OrderedPresets()
		{
			var all = WorldEditorAssets.FindAll<WeatherPreset>();
			var ordered = new List<WeatherPreset>();
			foreach (string name in PresetOrder)
			{
				WeatherPreset preset = all.Find(p => p.ResolvedName == name);
				if (preset != null)
				{
					ordered.Add(preset);
				}
			}
			foreach (WeatherPreset preset in all)
			{
				if (!ordered.Contains(preset))
				{
					ordered.Add(preset);
				}
			}
			return ordered;
		}

		/// <summary>
		/// A small terrain hill, on the project's weather terrain material. Its heights are a smooth
		/// mound, so a slope, a shoulder and a flat top all show at once: snow settles on the top,
		/// thins on the shoulder and sheds off the slope.
		/// </summary>
		private static void BuildTerrain()
		{
			const int Resolution = 129;
			const float Size = 120f;
			TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
			if (data == null)
			{
				data = new TerrainData
				{
					heightmapResolution = Resolution,
					size = new Vector3(Size, 18f, Size),
				};
				var heights = new float[Resolution, Resolution];
				for (int z = 0; z < Resolution; z++)
				{
					for (int x = 0; x < Resolution; x++)
					{
						float u = (x / (float)(Resolution - 1)) * 2f - 1f;
						float v = (z / (float)(Resolution - 1)) * 2f - 1f;
						float distance = Mathf.Sqrt(u * u + v * v);
						// A mound that reaches zero at the edge, so it meets the flat ground cleanly.
						float mound = Mathf.Cos(Mathf.Clamp01(distance) * Mathf.PI) * 0.5f + 0.5f;
						heights[z, x] = mound * mound * 0.55f;
					}
				}
				data.SetHeights(0, 0, heights);
				AssetDatabase.CreateAsset(data, TerrainDataPath);
			}

			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			terrainObject.name = "Terrain Hill";
			terrainObject.transform.position = new Vector3(0f, 0f, 60f);
			var terrain = terrainObject.GetComponent<Terrain>();
			Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
			if (material != null)
			{
				terrain.materialTemplate = material;
			}
			else
			{
				Debug.LogWarning($"[Weather Sim] {TerrainMaterialPath} is missing, so the hill uses Unity's own terrain material and shows no weather. Weather Tools → Weather-proof terrain creates it.");
			}
		}

		private const string TerrainDataPath = GeneratedFolder + "/Weather Sim Hill.asset";
		private const string TerrainMaterialPath = "Assets/Prefabs/Client/Materials/Ground/Weather Terrain.mat";

		private static Material MaterialAsset(string name, Color color)
		{
			string path = $"{GeneratedFolder}/{name}.mat";
			Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
			if (material != null)
			{
				return material;
			}
			// The bed is here to show what the game draws, and the game's world is on the weather
			// shader: rain wets these boxes and snow settles on them. Falling back to URP's own Lit
			// would quietly give the bed a world the weather cannot touch.
			Shader shader = Shader.Find("FishMMO/Weather Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
			material = new Material(shader) { name = name };
			material.SetColor("_BaseColor", color);
			material.color = color;
			AssetDatabase.CreateAsset(material, path);
			return material;
		}

		private static GameObject Box(string name, Vector3 localPosition, Vector3 size, Material material, Transform parent)
		{
			return Primitive(PrimitiveType.Cube, name, localPosition, size, material, parent);
		}

		private static GameObject Primitive(PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Material material, Transform parent)
		{
			GameObject go = GameObject.CreatePrimitive(type);
			go.name = name;
			if (parent != null)
			{
				go.transform.SetParent(parent, false);
			}
			go.transform.localPosition = localPosition;
			go.transform.localScale = scale;
			go.GetComponent<Renderer>().sharedMaterial = material;
			return go;
		}
	}
}
