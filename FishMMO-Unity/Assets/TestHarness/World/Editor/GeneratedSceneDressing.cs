using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Puts a camera and the world simulation controller into a freshly generated scene, so it can
	/// be looked at the moment it exists.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A generated scene with no camera renders nothing when you press play: the terrain, the sky,
	/// the clouds and the weather are all there and all invisible, and the only way to see whether
	/// the generator did anything is to author a camera by hand first. That is a poor first
	/// impression of a tool whose whole job is to hand you a scene that works.
	/// </para>
	/// <para>
	/// <b>The controller is what drives the weather and the clouds here.</b> Outside play the sky
	/// is driven by a server's timeline; in a scene nobody has connected yet there is no server, so
	/// <see cref="WorldSimController"/> runs the same model locally and hands the same weather to
	/// the same presenter. Without it a generated scene has a clock and no weather at all.
	/// </para>
	/// <para>
	/// Registered into the generator rather than called by it: this lives in the test harness,
	/// which references the shared tools and is compiled out of a server build entirely. Both
	/// objects are meant to be deleted before a real client or server build, which is why they are
	/// named plainly and kept together at the top of the hierarchy.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public static class GeneratedSceneDressing
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string ThemePath = "Assets/Scripts/Client/GUI/FishMMO-Theme.uss";
		private const string PanelLayoutPath = "Assets/TestHarness/World/WorldSimPanel.uss";

		static GeneratedSceneDressing()
		{
			SceneGenerator.Dress.Add(Dress);
		}

		private static void Dress(Scene scene, SceneGenerationRequest request)
		{
			WorldSceneSettings settings = Find<WorldSceneSettings>(scene);
			WorldDayNightCycle dayNight = Find<WorldDayNightCycle>(scene);

			Camera camera = CreateCamera(scene, request);
			CreateController(scene, camera, settings, dayNight);
		}

		private static Camera CreateCamera(Scene scene, SceneGenerationRequest request)
		{
			var host = new GameObject("Main Camera") { tag = "MainCamera" };
			SceneManager.MoveGameObjectToScene(host, scene);

			Camera camera = host.AddComponent<Camera>();
			camera.nearClipPlane = 0.1f;
			/* Far enough to see the clouds, which are marched to tens of kilometres. A near plane
			 * sized for a room clips the horizon off them and the sky reads as empty. */
			camera.farClipPlane = 5000f;
			camera.clearFlags = CameraClearFlags.Skybox;
			host.AddComponent<AudioListener>();
			host.AddComponent<WorldSimCamera>();

			/* Standing on the ground at the middle of the scene, looking out. The terrain's own
			 * floor is zero, so the height here is eye level above whatever is underneath. */
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			host.transform.position = new Vector3(0f, GroundHeight(scene) + 1.8f, -plan.DepthMetres * 0.25f);
			host.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
			return camera;
		}

		/// <summary>The terrain height under the middle of the scene, so the camera starts above ground rather than inside it.</summary>
		private static float GroundHeight(Scene scene)
		{
			float highest = 0f;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					if (terrain != null && terrain.terrainData != null)
					{
						highest = Mathf.Max(highest, terrain.SampleHeight(Vector3.zero) + terrain.GetPosition().y);
					}
				}
			}
			return highest;
		}

		private static void CreateController(Scene scene, Camera camera, WorldSceneSettings settings, WorldDayNightCycle dayNight)
		{
			var host = new GameObject("World Sim");
			SceneManager.MoveGameObjectToScene(host, scene);

			WorldSimController controller = host.AddComponent<WorldSimController>();
			controller.Camera = camera;
			controller.Settings = settings;
			controller.DayNight = dayNight;
			/* Cached on the component rather than loaded: the bed loads no addressables, so a
			 * generated scene opened on its own would otherwise have no presets, no templates and
			 * no solar system to stand in. */
			controller.Profile = WeatherRenderAssets.Ensure();
			controller.SolarSystem = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			controller.SkyProfiles = WorldEditorAssets.FindAll<SkyProfile>();
			controller.Templates = WorldEditorAssets.FindAll<WeatherLayerTemplate>();
			controller.Presets = WorldEditorAssets.FindAll<WeatherPreset>();

			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);

			WorldSimPanel panel = host.AddComponent<WorldSimPanel>();
			panel.Theme = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemePath);
			panel.Layout = AssetDatabase.LoadAssetAtPath<StyleSheet>(PanelLayoutPath);
			panel.Controller = controller;
		}

		private static T Find<T>(Scene scene) where T : Component
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				T found = root.GetComponentInChildren<T>(true);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}
	}
}
