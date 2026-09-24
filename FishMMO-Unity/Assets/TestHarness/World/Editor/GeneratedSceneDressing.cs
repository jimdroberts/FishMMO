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

		private static void Dress(Scene scene, SceneGenerationRequest request, SceneGenerationResult result)
		{
			WorldSceneSettings settings = Find<WorldSceneSettings>(scene);
			WorldDayNightCycle dayNight = Find<WorldDayNightCycle>(scene);

			Camera camera = CreateCamera(scene, request, result);
			CreateController(scene, camera, settings, dayNight);
		}

		private static Camera CreateCamera(Scene scene, SceneGenerationRequest request, SceneGenerationResult result)
		{
			var host = new GameObject("Main Camera") { tag = "MainCamera" };
			SceneManager.MoveGameObjectToScene(host, scene);

			Camera camera = host.AddComponent<Camera>();
			camera.nearClipPlane = 0.1f;
			/* Sized from the scene, not fixed.
			 *
			 * One unit is one metre, so a 4.5 km scene is 4500 units across and 6360 corner to
			 * corner — a fixed 5000 clips the far side of it, and the clouds are marched to tens of
			 * kilometres beyond that. Standing in the middle you would see the ground end in mid
			 * air. The floor keeps small scenes looking the way the world sim bed does. */
			TerrainTilePlan sized = SceneGeneration.PlanTiles(request.SizeKm);
			float diagonal = Mathf.Sqrt(sized.WidthMetres * sized.WidthMetres + sized.DepthMetres * sized.DepthMetres);
			camera.farClipPlane = Mathf.Clamp(diagonal * 2f, 5000f, 40000f);
			camera.clearFlags = CameraClearFlags.Skybox;
			host.AddComponent<AudioListener>();
			host.AddComponent<WorldSimCamera>();

			/* Eye level over whatever is underneath, looking out — and on the surface when that is
			 * the sea floor. Y is metres above sea level, so the sea is at zero; a camera stood on
			 * a seabed a kilometre down sees nothing of the sky this bed exists to show. */
			var standing = new Vector3(0f, 0f, -sized.DepthMetres * 0.25f);
			float ground = GroundHeight(scene, standing);
			float underfoot = result != null && result.HasWater ? Mathf.Max(ground, result.SeaLevelY) : ground;
			host.transform.position = new Vector3(standing.x, underfoot + 1.8f, standing.z);
			host.transform.rotation = Quaternion.Euler(4f, 0f, 0f);
			return camera;
		}

		/// <summary>
		/// World Y of the ground at a point, so the camera starts above ground rather than inside it.
		/// </summary>
		/// <remarks>
		/// Read from the one tile the point is over. <c>SampleHeight</c> clamps a point outside a
		/// tile onto that tile's edge, so asking every tile and keeping the highest answer took
		/// whichever edge anywhere in the scene stood tallest — and asking at the centre, when the
		/// camera stands a quarter of the scene south of it, put it above ground that is not there.
		/// </remarks>
		private static float GroundHeight(Scene scene, Vector3 at)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
				{
					if (terrain == null || terrain.terrainData == null)
					{
						continue;
					}
					Vector3 origin = terrain.GetPosition();
					Vector3 size = terrain.terrainData.size;
					if (at.x >= origin.x && at.x <= origin.x + size.x && at.z >= origin.z && at.z <= origin.z + size.z)
					{
						return terrain.SampleHeight(at) + origin.y;
					}
				}
			}
			return 0f;
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
