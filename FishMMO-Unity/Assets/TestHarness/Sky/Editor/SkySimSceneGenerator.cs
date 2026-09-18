using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.WorldDesign;
using FishMMO.TestHarness.Sky;

namespace FishMMO.TestHarness.Sky.Editor
{
	/// <summary>
	/// Builds the sky test scene: a bare horizon with a few landmarks to light, the sun and moon
	/// lights, the day/night cycle, and the sky control panel. Play it to see the sky of any planet
	/// or moon at any place, date and time.
	/// </summary>
	public static class SkySimSceneGenerator
	{
		public const string ScenePath = "Assets/Scenes/Test/SkySim.unity";
		public const string GeneratedFolder = "Assets/TestHarness/Sky/Generated";
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";

		[DashboardTool(DashboardToolAttribute.Weather, "Generate Sky Sim scene", Section = "Test bed", Order = 12,
			Tooltip = "Creates the sky assets if missing and writes " + ScenePath + ": a bare horizon, the day/night cycle and a panel for body, place, date, time, sky profile and weather.",
			Confirm = "Generate the Sky Sim scene? The open scenes are closed (you are asked to save them first) and " + ScenePath + " is overwritten.")]
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
			WeatherRenderProfile profile = WeatherRenderAssets.Ensure();
			WeatherRenderAssets.EnsureSkyProfile();
			WorldEditorAssets.EnsureFolder(GeneratedFolder);
			WorldEditorAssets.EnsureFolder("Assets/Scenes/Test");

			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			Material ground = MaterialAsset("Sky Sim Ground", new Color(0.26f, 0.27f, 0.24f));
			Material stone = MaterialAsset("Sky Sim Stone", new Color(0.55f, 0.54f, 0.5f));
			Material white = MaterialAsset("Sky Sim White", new Color(0.86f, 0.86f, 0.86f));

			var sunObject = new GameObject("Sun");
			Light sun = sunObject.AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.shadows = LightShadows.Soft;
			sun.intensity = 1.1f;
			sunObject.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
			RenderSettings.sun = sun;

			var moonObject = new GameObject("Moon");
			Light moonLight = moonObject.AddComponent<Light>();
			moonLight.type = LightType.Directional;
			moonLight.shadows = LightShadows.None;
			moonLight.intensity = 0f;

			RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
			RenderSettings.fog = false;

			// A wide, flat ground so the horizon reads, with a few things to catch the light.
			Primitive(PrimitiveType.Cylinder, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 0.5f, 2000f), ground, null);
			var landmarks = new GameObject("Landmarks");
			Primitive(PrimitiveType.Sphere, "Sphere", new Vector3(6f, 1.5f, 10f), Vector3.one * 3f, white, landmarks.transform);
			Primitive(PrimitiveType.Cube, "Block", new Vector3(-7f, 1.5f, 9f), new Vector3(3f, 3f, 3f), stone, landmarks.transform);
			for (int i = 0; i < 8; i++)
			{
				float angle = i * Mathf.PI * 2f / 8f;
				var at = new Vector3(Mathf.Sin(angle) * 22f, 2.5f, Mathf.Cos(angle) * 22f);
				Primitive(PrimitiveType.Cylinder, "Pillar", at, new Vector3(1.2f, 2.5f, 1.2f), stone, landmarks.transform);
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
			camera.farClipPlane = 5000f;
			camera.clearFlags = CameraClearFlags.Skybox;
			cameraObject.AddComponent<AudioListener>();
			cameraObject.AddComponent<SkySimCamera>();
			cameraObject.transform.position = new Vector3(0f, 1.7f, -14f);
			cameraObject.transform.rotation = Quaternion.Euler(-8f, 0f, 0f);

			var controllerObject = new GameObject("Sky Sim");
			SkySimController controller = controllerObject.AddComponent<SkySimController>();
			controller.Camera = camera;
			controller.DayNight = dayNight;
			controller.Settings = settings;
			controller.Profile = profile;
			controller.SolarSystem = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			controller.SkyProfiles = WorldEditorAssets.FindAll<SkyProfile>();
			UIDocument document = controllerObject.AddComponent<UIDocument>();
			document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			SkySimPanel panel = controllerObject.AddComponent<SkySimPanel>();
			panel.Controller = controller;

			EditorSceneManager.SaveScene(scene, ScenePath);
			AssetDatabase.SaveAssets();
			Debug.Log($"[Sky Sim] Wrote {ScenePath}. Open it and press Play; the panel drives the sky.");
			return scene;
		}

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
