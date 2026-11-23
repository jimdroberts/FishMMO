using System.Collections.Generic;
using FishMMO.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.TestHarness.Editor
{
	/// <summary>
	/// Creates the Core Gate world: a small world scene made of core pieces only, for the world-module gate: a project with every world module removed must still log a character into a world that is lit and
	/// shadowed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Its own directional light with soft shadows (the sky module's sun is absent), a Unity terrain painted with the core
	/// ground material and the core grass and stone layers (it draws itself: the Terrain module's heightmap draw is absent),
	/// stone pillars that cast shadows, a character spawn and respawn for the starting races, a scene boundary, and the core
	/// scene settings.
	/// </para>
	/// <para>
	/// It is saved under <c>Assets/Scenes/Test/</c>, outside the world scene folder, so live character creation never offers
	/// it. The gate's build moves it into <c>Assets/Scenes/WorldScene/</c> for the build and back afterwards.
	/// </para>
	/// <para>
	/// Headless: <c>-executeMethod FishMMO.TestHarness.Editor.CoreGateSceneGenerator.Generate</c>.
	/// </para>
	/// </remarks>
	public static class CoreGateSceneGenerator
	{
		public const string SceneFolder = "Assets/Scenes/Test";
		public const string ScenePath = SceneFolder + "/Core Gate.unity";
		public const string TerrainDataPath = SceneFolder + "/Core Gate Terrain.asset";
		public const string SpawnName = "Core Gate Spawn";

		private const string TerrainMaterialPath = "Assets/Prefabs/Client/Materials/Ground/Weather Terrain.mat";
		private const string StoneMaterialPath = "Assets/Prefabs/Client/Materials/Ground/GroundStone.mat";
		private const string GrassLayerPath = "Assets/Textures/Ground/GrassLayer.terrainlayer";
		private const string StoneLayerPath = "Assets/Textures/Ground/StoneLayer.terrainlayer";

		/// <summary>The starting races (Human, Elf, Orc), as the home world's spawns allow them.</summary>
		private static readonly string[] StartingRaceGuids =
		{
			"6f8938bbed1286347b54bf3f4889f32a",
			"570b8e070c073834abac548a07706b47",
			"f1bedd6f4354a7e41be26aee87734d66",
		};

		private const int TerrainSize = 256;
		private const float TerrainHeight = 30f;
		private const int HeightmapResolution = 257;

		[DashboardTool(DashboardToolAttribute.Maintenance, "Generate Core Gate World", Section = "Test Scenes", Order = 5,
			Tooltip = "The world-module gate's core-only world: a lit, shadowed terrain with a character spawn, under Assets/Scenes/Test.",
			Confirm = "Regenerate the Core Gate world scene?")]
		public static void Generate()
		{
			if (!AssetDatabase.IsValidFolder(SceneFolder))
			{
				AssetDatabase.CreateFolder("Assets/Scenes", "Test");
			}
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			var systems = new GameObject("World Systems");
			systems.AddComponent<WorldSceneSettings>();

			Light sun = CreateSun();
			RenderSettings.sun = sun;
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
			RenderSettings.ambientEquatorColor = new Color(0.45f, 0.47f, 0.45f);
			RenderSettings.ambientGroundColor = new Color(0.28f, 0.25f, 0.2f);
			RenderSettings.fog = false;

			Terrain terrain = CreateTerrain();
			CreatePillars(terrain);

			Vector3 centre = new Vector3(TerrainSize * 0.5f, 0f, TerrainSize * 0.5f);
			centre.y = terrain.SampleHeight(centre) + 1f;
			var spawn = new GameObject(SpawnName);
			spawn.transform.position = centre;
			var initial = spawn.AddComponent<CharacterInitialSpawnPosition>();
			initial.AllowedRaces = StartingRaces();

			// A world scene needs a boundary (the scene details cache refuses one without): the terrain's square, 60 m high.
			var boundary = new GameObject("Scene Boundary");
			boundary.transform.position = new Vector3(TerrainSize * 0.5f, 20f, TerrainSize * 0.5f);
			boundary.AddComponent<SceneBoundary>().BoundarySize = new Vector3(TerrainSize - 4f, 80f, TerrainSize - 4f);

			var respawn = new GameObject("Core Gate Respawn");
			respawn.transform.position = centre + new Vector3(4f, 0f, 4f);
			respawn.AddComponent<CharacterRespawnPosition>();

			if (!EditorSceneManager.SaveScene(scene, ScenePath))
			{
				Debug.LogError($"[CoreGateSceneGenerator] FAILED to save {ScenePath}");
				return;
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"[CoreGateSceneGenerator] Wrote {ScenePath}: spawn '{SpawnName}' at {centre}, {initial.AllowedRaces.Count} races.");
		}

		private static Light CreateSun()
		{
			var go = new GameObject("Sun");
			go.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
			var light = go.AddComponent<Light>();
			light.type = LightType.Directional;
			light.color = new Color(1f, 0.95f, 0.86f);
			light.intensity = 1.6f;
			light.shadows = LightShadows.Soft;
			light.shadowStrength = 0.9f;
			return light;
		}

		private static Terrain CreateTerrain()
		{
			TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
			if (data == null)
			{
				data = new TerrainData();
				AssetDatabase.CreateAsset(data, TerrainDataPath);
			}
			data.heightmapResolution = HeightmapResolution;
			data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

			// A gentle bowl with a rise at one side: slopes for the shadows to lie on, and nowhere steep enough to stop a walk.
			var heights = new float[HeightmapResolution, HeightmapResolution];
			for (int z = 0; z < HeightmapResolution; z++)
			{
				for (int x = 0; x < HeightmapResolution; x++)
				{
					float u = x / (float)(HeightmapResolution - 1), v = z / (float)(HeightmapResolution - 1);
					float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, u)) * 0.35f;
					float swell = 0.06f * Mathf.Sin(u * 9f) * Mathf.Cos(v * 7f);
					heights[z, x] = 0.15f + rise + swell;
				}
			}
			data.SetHeights(0, 0, heights);

			var grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassLayerPath);
			var stone = AssetDatabase.LoadAssetAtPath<TerrainLayer>(StoneLayerPath);
			var layers = new List<TerrainLayer>();
			if (grass != null) layers.Add(grass);
			if (stone != null) layers.Add(stone);
			data.terrainLayers = layers.ToArray();
			if (layers.Count == 2)
			{
				int res = data.alphamapResolution;
				var alpha = new float[res, res, 2];
				for (int z = 0; z < res; z++)
				{
					for (int x = 0; x < res; x++)
					{
						float steep = data.GetSteepness(x / (float)(res - 1), z / (float)(res - 1)) / 35f;
						float s = Mathf.Clamp01(steep);
						alpha[z, x, 0] = 1f - s;
						alpha[z, x, 1] = s;
					}
				}
				data.SetAlphamaps(0, 0, alpha);
			}
			EditorUtility.SetDirty(data);

			GameObject go = Terrain.CreateTerrainGameObject(data);
			go.name = "Ground";
			Terrain terrain = go.GetComponent<Terrain>();
			var material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
			if (material != null)
			{
				terrain.materialTemplate = material;
			}
			terrain.shadowCastingMode = ShadowCastingMode.On;
			return terrain;
		}

		private static void CreatePillars(Terrain terrain)
		{
			var stone = AssetDatabase.LoadAssetAtPath<Material>(StoneMaterialPath);
			var parent = new GameObject("Pillars").transform;
			var rng = new System.Random(4);
			for (int i = 0; i < 9; i++)
			{
				float angle = i / 9f * Mathf.PI * 2f;
				float radius = 14f + (float)rng.NextDouble() * 10f;
				Vector3 at = new Vector3(TerrainSize * 0.5f + Mathf.Cos(angle) * radius, 0f, TerrainSize * 0.5f + Mathf.Sin(angle) * radius);
				float height = 3f + (float)rng.NextDouble() * 5f;
				at.y = terrain.SampleHeight(at) + height * 0.5f - 0.3f;
				GameObject pillar = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Cylinder : PrimitiveType.Cube);
				pillar.name = "Pillar " + i;
				pillar.transform.SetParent(parent, false);
				pillar.transform.position = at;
				pillar.transform.localScale = i % 3 == 0 ? new Vector3(1.5f, height * 0.5f, 1.5f) : new Vector3(1.4f, height, 1.4f);
				pillar.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 90f, 0f);
				if (stone != null)
				{
					pillar.GetComponent<MeshRenderer>().sharedMaterial = stone;
				}
			}
		}

		private static List<RaceTemplate> StartingRaces()
		{
			var races = new List<RaceTemplate>();
			foreach (string guid in StartingRaceGuids)
			{
				var race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (race != null)
				{
					races.Add(race);
				}
			}
			return races;
		}
	}
}
