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
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Builds the world test scene: a wide ground so the horizon reads, a hill of real terrain, a
	/// closed house and an open pavilion to stop rain with, trees, landmarks to catch the light, and
	/// one camera, clock and control panel over the lot.
	/// </summary>
	/// <remarks>
	/// This is the two old scenes put together. What stopped them being one scene was never the
	/// props — it was that each carried its own camera, sun, clock and scene settings, and two of
	/// any of those fight. There is one of each here, and the props from both, because the sky bed
	/// wanted somewhere for cloud shadows to land and the weather bed wanted a horizon.
	/// </remarks>
	public static class WorldSimSceneGenerator
	{
		public const string ScenePath = "Assets/Scenes/Test/WorldSim.unity";
		public const string GeneratedFolder = "Assets/TestHarness/World/Generated";
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string TerrainDataPath = GeneratedFolder + "/World Sim Hill.asset";
		private const string MountainDataPath = GeneratedFolder + "/World Sim Mountain.asset";
		private const string TerrainMaterialPath = "Assets/Prefabs/Client/Materials/Ground/Weather Terrain.mat";

		private static readonly string[] PresetOrder =
		{
			"Clear", "Fair", "Overcast", "Mist", "Sprinkle", "Light Rain", "Medium Rain", "Heavy Rain", "Thunderstorm",
			"Light Snow", "Heavy Snow", "Blizzard", "Hailstorm", "Ashfall", "Sandstorm", "Windy", "Aurora Night",
		};

		[DashboardTool(DashboardToolAttribute.Weather, "Generate World Sim scene", Section = "Test bed", Order = 10,
			Tooltip = "Creates the weather content if missing and writes " + ScenePath + ": ground, terrain, a house, a pavilion, trees, landmarks and the world control panel.",
			Confirm = "Generate the World Sim scene? The open scenes are closed (you are asked to save them first) and " + ScenePath + " is overwritten.")]
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
			WeatherRenderAssets.EnsureSkyProfile();
			WorldEditorAssets.EnsureFolder(GeneratedFolder);
			WorldEditorAssets.EnsureFolder("Assets/Scenes/Test");

			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			Material ground = MaterialAsset("Ground", new Color(0.3f, 0.33f, 0.27f));
			Material wall = MaterialAsset("Wall", new Color(0.62f, 0.58f, 0.52f));
			Material roof = MaterialAsset("Roof", new Color(0.42f, 0.24f, 0.2f));
			Material wood = MaterialAsset("Wood", new Color(0.35f, 0.25f, 0.16f));
			Material leaves = MaterialAsset("Leaves", new Color(0.2f, 0.36f, 0.18f));
			Material stone = MaterialAsset("Stone", new Color(0.55f, 0.54f, 0.5f));
			Material white = MaterialAsset("White", new Color(0.86f, 0.86f, 0.86f));

			// No sun and no moon in the scene. The sky makes its own lights when the scene runs and
			// drives them from the solar system; a light placed here would light the bed a second time.
			RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
			RenderSettings.ambientEquatorColor = new Color(0.4f, 0.42f, 0.45f);
			RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f);
			RenderSettings.fog = false;

			// One ground, and it is the wide one: a cloud deck is only honest against a horizon, and
			// the weather bed's 300 m pad had none. Its top sits a couple of centimetres under zero so
			// the terrain beside it wins where the two overlap instead of z-fighting with it.
			GameObject groundObject = Primitive(PrimitiveType.Cylinder, "Ground", new Vector3(0f, -0.52f, 0f), new Vector3(2000f, 0.5f, 2000f), ground, null);
			/* A box, not the cylinder's own capsule. A capsule much wider than it is tall is a SPHERE,
			 * and this one was a kilometre across with the whole bed inside it. A ray that starts
			 * inside a collider does not see it, so the sky occlusion map's rays went straight through
			 * the ground and recorded it half a kilometre down. Rain and cover never showed it; the
			 * splashes, which stand on that map, were all drawn down there, out of sight. */
			Object.DestroyImmediate(groundObject.GetComponent<Collider>());
			groundObject.AddComponent<BoxCollider>();

			// A hill of real terrain. The world's terrain is on its own shader — a fork of Unity's,
			// with its own snow that lifts the ground it lies on — and none of that is exercised by
			// boxes.
			BuildTerrain();
			BuildMountain();

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

			// Landmarks from the sky bed: plain shapes whose shading says what the light is doing,
			// with nothing else on them to argue about. The ring sits wider than the trees so a cloud
			// shadow crossing it is readable.
			var landmarks = new GameObject("Landmarks");
			Primitive(PrimitiveType.Sphere, "Sphere", new Vector3(16f, 1.5f, -12f), Vector3.one * 3f, white, landmarks.transform);
			Primitive(PrimitiveType.Cube, "Block", new Vector3(-17f, 1.5f, -11f), new Vector3(3f, 3f, 3f), stone, landmarks.transform);
			for (int i = 0; i < 8; i++)
			{
				float angle = i * Mathf.PI * 2f / 8f;
				var at = new Vector3(Mathf.Sin(angle) * 34f, 2.5f, Mathf.Cos(angle) * 34f - 8f);
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

			var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
			Camera camera = cameraObject.AddComponent<Camera>();
			camera.nearClipPlane = 0.1f;
			// The far plane is the sky bed's: the clouds are marched to tens of kilometres and the
			// weather bed's 1.5 km would have clipped the horizon off them.
			camera.farClipPlane = 5000f;
			camera.clearFlags = CameraClearFlags.Skybox;
			cameraObject.AddComponent<AudioListener>();
			cameraObject.AddComponent<WorldSimCamera>();
			cameraObject.transform.position = new Vector3(-2f, 1.8f, -14f);
			cameraObject.transform.rotation = Quaternion.Euler(-4f, 12f, 0f);

			var controllerObject = new GameObject("World Sim");
			WorldSimController controller = controllerObject.AddComponent<WorldSimController>();
			controller.Profile = profile;
			controller.Camera = camera;
			controller.Settings = settings;
			controller.DayNight = dayNight;
			controller.SolarSystem = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			controller.SkyProfiles = WorldEditorAssets.FindAll<SkyProfile>();
			controller.Templates = WorldEditorAssets.FindAll<WeatherLayerTemplate>();
			controller.Presets = OrderedPresets();
			UIDocument document = controllerObject.AddComponent<UIDocument>();
			document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			WorldSimPanel panel = controllerObject.AddComponent<WorldSimPanel>();
			// The game's theme and the panel's own layout. The panel finds them by path when they are
			// missing, so a scene from before this still looks right; assigned here so it does not have to.
			panel.Theme = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Scripts/Client/GUI/FishMMO-Theme.uss");
			panel.Layout = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/TestHarness/World/WorldSimPanel.uss");
			panel.Controller = controller;

			EditorSceneManager.SaveScene(scene, ScenePath);
			AssetDatabase.SaveAssets();
			Debug.Log($"[World Sim] Wrote {ScenePath}.");
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
			terrainObject.transform.position = new Vector3(-60f, 0f, 40f);
			var terrain = terrainObject.GetComponent<Terrain>();
			Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
			if (material != null)
			{
				terrain.materialTemplate = material;
			}
			else
			{
				Debug.LogWarning($"[World Sim] {TerrainMaterialPath} is missing, so the hill uses Unity's own terrain material and shows no weather. Weather Tools → Weather-proof terrain creates it.");
			}
		}

		/// <summary>
		/// A mountain to the north, tall enough to stand through the cloud deck, so what the terrain
		/// does to the sky can be seen: cloud gathering on its windward flank and clearing in its lee,
		/// and the fog lying on its slopes instead of at sea level.
		/// </summary>
		/// <remarks>
		/// Due north of the origin on purpose. At the bed's default latitude the trades blow out of
		/// the east, so looking north from the origin the mountain is seen side-on to the wind: the
		/// windward flank is on the right and the lee on the left, and the difference between them is
		/// across the frame rather than hidden behind the peak. Its summit is at 1600 m — the deck's
		/// base moves between about 500 and 1000 m with the weather, so the peak is always through
		/// it — and its middle is 3.2 km out, inside the bed camera's five-kilometre far plane.
		/// </remarks>
		private static void BuildMountain()
		{
			const int Resolution = 257;
			const float Size = 4400f;
			const float Height = 1600f;
			TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(MountainDataPath);
			if (data == null)
			{
				data = new TerrainData
				{
					heightmapResolution = Resolution,
					size = new Vector3(Size, Height, Size),
				};
				var heights = new float[Resolution, Resolution];
				for (int z = 0; z < Resolution; z++)
				{
					for (int x = 0; x < Resolution; x++)
					{
						float u = (x / (float)(Resolution - 1)) * 2f - 1f;
						float v = (z / (float)(Resolution - 1)) * 2f - 1f;
						// A massif a little longer north to south than it is wide, so it stands
						// broadside to an easterly.
						float distance = Mathf.Sqrt(u * u / (0.8f * 0.8f) + v * v);
						float mound = Mathf.Cos(Mathf.Clamp01(distance) * Mathf.PI) * 0.5f + 0.5f;
						// Ridges and gullies, stronger toward the top; nothing at the rim, so it meets
						// the flat ground cleanly.
						float ridged = 1f - Mathf.Abs(Mathf.PerlinNoise(u * 3.1f + 7.3f, v * 3.1f + 2.9f) * 2f - 1f);
						float fine = Mathf.PerlinNoise(u * 9.7f + 1.1f, v * 9.7f + 5.3f);
						float shape = Mathf.Pow(mound, 1.35f);
						heights[z, x] = Mathf.Clamp01(shape * (0.72f + 0.22f * ridged + 0.06f * fine));
					}
				}
				data.SetHeights(0, 0, heights);
				AssetDatabase.CreateAsset(data, MountainDataPath);
			}

			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			terrainObject.name = "Terrain Mountain";
			terrainObject.transform.position = new Vector3(-Size * 0.5f, -1f, 1000f);
			var terrain = terrainObject.GetComponent<Terrain>();
			// Drawn to the far plane at full detail: it is the one distant thing in the bed.
			terrain.heightmapPixelError = 8f;
			terrain.basemapDistance = 6000f;
			Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
			if (material != null)
			{
				terrain.materialTemplate = material;
			}
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
