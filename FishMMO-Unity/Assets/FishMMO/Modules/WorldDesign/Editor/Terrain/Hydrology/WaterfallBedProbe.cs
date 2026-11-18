#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A waterfall built to be judged: a 50 m cliff with a river channel cut to its lip, a boulder standing in the channel
	/// just above the lip, a ledge high on the face under one side of the fall, a rock lodged lower down and a pillar of rock
	/// against the face, in the curtain's path. The rocks are baked collision (a ScenePropCollisionSet), as a real scene's
	/// are. Renders the views to FISHMMO_FALLBED_OUT; saves nothing into the project.
	/// </summary>
	public static class WaterfallBedProbe
	{
		private const int Width = 1600;
		private const int Height = 900;
		private const float Plateau = 60f;
		private const float Base = 10f;
		private const float Size = 128f;
		/// <summary>The boulder in the channel: x, surface, z, radius.</summary>
		private static readonly Vector4 Boulder = new Vector4(0f, Plateau, -2.2f, 0.6f);
		/// <summary>The prop rock: x centre, y centre, how far out from the face (m), width (m).</summary>
		private static readonly Vector4 PropRock = new Vector4(-1.7f, 20f, 3.6f, 1.4f);
		/// <summary>A ledge high on the face under the fall's left side: its centre, and its size (it stands out of the face).</summary>
		private static readonly Vector3 Ledge = new Vector3(-1.8f, Plateau - 6f, 1.6f);
		private static readonly Vector3 LedgeSize = new Vector3(2.6f, 1.2f, 4.2f);
		/// <summary>The pillar against the face: x from, x to, how far out from the face (m), its top (m).</summary>
		private static readonly Vector4 Pillar = new Vector4(0.9f, 2.5f, 2.5f, 34f);

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_FALLBED_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = Path.Combine(Path.GetTempPath(), "fishmmo-fallbed");
			}
			Directory.CreateDirectory(output);
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

			Terrain terrain = BuildTerrain();
			SceneHydrology hydrology = BuildHydrology();

			// The boulder as rock to see.
			var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			stone.name = "Boulder";
			stone.transform.position = new Vector3(Boulder.x, Plateau - 0.6f, Boulder.z);
			stone.transform.localScale = new Vector3(Boulder.w * 2f, Boulder.w * 1.6f, Boulder.w * 2f);
			stone.GetComponent<Renderer>().sharedMaterial = terrain.materialTemplate != null ? new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.33f, 0.31f) } : null;

			// A prop rock against the face lower down, the other side of the channel: collision only (a baked prop's), no terrain.
			// A rounded boulder lodged on the face, its collision its own mesh (a baked prop's MeshCollider).
			var prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			prop.name = "Prop rock";
			prop.transform.position = new Vector3(PropRock.x, PropRock.y, 0.5f * PropRock.z - 0.3f);
			prop.transform.rotation = Quaternion.Euler(12f, 30f, 18f);
			prop.transform.localScale = new Vector3(PropRock.w * 1.3f, 3.2f, PropRock.z + 0.8f);
			UnityEngine.Object.DestroyImmediate(prop.GetComponent<Collider>());
			prop.GetComponent<Renderer>().sharedMaterial = stone.GetComponent<Renderer>().sharedMaterial;
			/* Its collision as a real scene has it: in the scene's baked collision set, not as a live collider. The falls are
			 * built before the streamer puts any prop chunk into physics, so a live collider here proved nothing about a
			 * real scene's rocks. And a ledge high on the face, under one side of the fall, which splits it. */
			var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
			ledge.name = "Ledge";
			UnityEngine.Object.DestroyImmediate(ledge.GetComponent<Collider>());
			ledge.transform.position = Ledge;
			ledge.transform.localScale = LedgeSize;
			ledge.GetComponent<Renderer>().sharedMaterial = stone.GetComponent<Renderer>().sharedMaterial;
			var owner = new GameObject(ScenePropColliders.ObjectName).AddComponent<ScenePropColliders>();
			var set = ScriptableObject.CreateInstance<ScenePropCollisionSet>();
			set.Source = "Bed";
			set.Prototypes = new[]
			{
				new ScenePropCollisionSet.Prototype { Mesh = Readable(prop.GetComponent<MeshFilter>().sharedMesh) },
				new ScenePropCollisionSet.Prototype { Mesh = Readable(ledge.GetComponent<MeshFilter>().sharedMesh) },
			};
			set.Instances = new[]
			{
				new ScenePropCollisionSet.Instance { Prototype = 0, Position = prop.transform.position, Rotation = prop.transform.rotation, Scale = prop.transform.localScale },
				new ScenePropCollisionSet.Instance { Prototype = 1, Position = ledge.transform.position, Rotation = Quaternion.identity, Scale = LedgeSize },
			};
			owner.Sets.Add(set);
			Physics.SyncTransforms();

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
			FishMMO.Water.InlandWaterRenderer.LogSheets = true;
			renderer.Rebuild();
			renderer.SetFlow(RiverFlowBake.SolveInMemory(hydrology));
			FishMMO.Water.InlandWaterRenderer.LogSheets = false;
			Shader.EnableKeyword("_WATER_DEPTH");
			Shader.EnableKeyword("_WATER_REFRACTION");
			float time = float.TryParse(Environment.GetEnvironmentVariable("FISHMMO_FALLBED_TIME"), out float t) ? t : 12.5f;
			Shader.SetGlobalFloat("_FishInlandTime", time);
			Shader.SetGlobalFloat("_FishFallDebug", Environment.GetEnvironmentVariable("FISHMMO_FALLBED_DEBUG") == "1" ? 1f : 0f);

			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.4f;
			sun.color = new Color(1f, 0.96f, 0.9f);
			sun.shadows = LightShadows.Soft;
			sun.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
			RenderSettings.sun = sun;
			RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
			RenderSettings.ambientMode = AmbientMode.Skybox;
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();
			var probe = new GameObject("Reflection").AddComponent<ReflectionProbe>();
			probe.mode = ReflectionProbeMode.Realtime;
			probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
			probe.size = new Vector3(2000f, 600f, 2000f);
			probe.transform.position = new Vector3(0f, 80f, 30f);
			probe.resolution = 256;
			probe.RenderProbe();

			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.Skybox;
			camera.fieldOfView = 45f;
			camera.nearClipPlane = 0.2f;
			camera.farClipPlane = 3000f;
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

			float mid = 0.5f * (Plateau + Base);
			var views = new List<(string, Vector3, Vector3)>
			{
				("front", new Vector3(0f, mid + 4f, 70f), new Vector3(0f, mid, 1f)),
				("side", new Vector3(36f, mid + 6f, 20f), new Vector3(0f, mid, 1f)),
				("pillar", new Vector3(9f, Pillar.w + 3f, 16f), new Vector3(1.6f, Pillar.w - 2f, 1.5f)),
				("lip", new Vector3(0f, Plateau + 14f, 12f), new Vector3(0f, Plateau - 4f, -2f)),
				("lipback", new Vector3(-4f, Plateau + 6f, -22f), new Vector3(0f, Plateau - 2f, 2f)),
				("foot", new Vector3(-10f, Base + 6f, 30f), new Vector3(0f, Base + 6f, 3f)),
				("prop", new Vector3(-9f, PropRock.y + 3f, 14f), new Vector3(PropRock.x, PropRock.y - 2f, 1f)),
				// Straight at each obstacle from in front, close enough to see the curtain part round it.
				("pillar_front", new Vector3(0.5f * (Pillar.x + Pillar.y), Pillar.w - 3f, 20f), new Vector3(0.5f * (Pillar.x + Pillar.y), Pillar.w - 6f, 1f)),
				("prop_front", new Vector3(PropRock.x, PropRock.y + 1f, 20f), new Vector3(PropRock.x, PropRock.y - 2f, 1f)),
				("ledge", new Vector3(-12f, Ledge.y + 2f, 16f), new Vector3(Ledge.x, Ledge.y - 3f, 2f)),
				// From beneath the lip's level along the face, side on: the slab's depth, and where it is cut.
				("under", new Vector3(14f, Pillar.w - 8f, 8f), new Vector3(0f, Pillar.w - 4f, 1.5f)),
			};
			var log = new List<string>();
			for (int i = 0; i < views.Count; i++)
			{
				(string name, Vector3 eye, Vector3 look) = views[i];
				camera.transform.position = eye;
				camera.transform.LookAt(look);
				string file = $"fallbed_{i:00}_{name}.png";
				InlandWaterRenderProbe.Capture(camera, target, Path.Combine(output, file));
				log.Add($"{file}: {name} from {eye} to {look}");
			}
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
			Debug.Log($"[Waterfall bed] {views.Count} views written to '{output}'; {renderer.Built.Count} surfaces.");
		}

		/// <summary>A readable copy of a mesh (a baked collision set's meshes are read on the CPU).</summary>
		private static Mesh Readable(Mesh source)
		{
			var mesh = new Mesh { name = source.name + " (collision)", vertices = source.vertices, triangles = source.triangles };
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>The plateau, the cliff, the pool's basin below it, the channel to the lip, and the pillar.</summary>
		private static Terrain BuildTerrain()
		{
			const int res = 513;
			float cell = Size / (res - 1);
			var heights = new float[res, res];
			for (int zi = 0; zi < res; zi++)
			{
				float z = -0.5f * Size + zi * cell;
				for (int xi = 0; xi < res; xi++)
				{
					float x = -0.5f * Size + xi * cell;
					float h = z <= 0f ? Plateau : Base - 1.5f;
					// The channel on the plateau, to the lip.
					if (z <= 0f && Mathf.Abs(x) < 3.5f)
					{
						h = Plateau - 1.2f * (1f - Mathf.Pow(Mathf.Abs(x) / 3.5f, 2f));
					}
					// The pool's basin under the fall, and the river leaving it.
					float pool = new Vector2(x, z - 8f).magnitude;
					if (z > 0f && pool < 11f)
					{
						h = Mathf.Min(h, Base - 1.5f - 4f * (1f - pool / 11f));
					}
					if (z > 8f && Mathf.Abs(x) < 3f)
					{
						h = Mathf.Min(h, Base - 2.4f);
					}
					// The pillar against the face: a buttress of rough rock, rounded in plan and at its crown, not a box.
					if (z > 0f)
					{
						float cx = 0.5f * (Pillar.x + Pillar.y), half = 0.5f * (Pillar.y - Pillar.x);
						float wobble = 0.25f * (Mathf.PerlinNoise(x * 1.3f + 11f, z * 1.3f + 5f) - 0.5f);
						float d = Mathf.Pow((x - cx) / half, 2f) + Mathf.Pow(z / Pillar.z, 2f) + wobble;
						if (d < 1f)
						{
							float crown = Pillar.w - 1.6f * d - 0.8f * Mathf.PerlinNoise(x * 2.1f + 3f, z * 2.1f + 7f);
							h = Mathf.Max(h, crown);
						}
					}
					heights[zi, xi] = h / 80f;
				}
			}
			var data = new TerrainData { heightmapResolution = res, size = new Vector3(Size, 80f, Size) };
			data.SetHeights(0, 0, heights);
			TerrainLayer rock = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_Rock.terrainlayer");
			TerrainLayer grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>("Assets/Prefabs/Shared/Biomes/Generated/TerrainLayers/Ground_GrassMeadow.terrainlayer");
			data.terrainLayers = new[] { grass, rock };
			int alpha = 256;
			data.alphamapResolution = alpha;
			var splat = new float[alpha, alpha, 2];
			for (int z = 0; z < alpha; z++)
			{
				for (int x = 0; x < alpha; x++)
				{
					// Rock from the cliff's foot out to a few metres either side of the face, grass on the plateau and below.
					float worldZ = -0.5f * Size + (z + 0.5f) / alpha * Size;
					float r = worldZ > -2f && worldZ < Pillar.z + 1f ? 1f : 0f;
					splat[z, x, 0] = 1f - r;
					splat[z, x, 1] = r;
				}
			}
			data.SetAlphamaps(0, 0, splat);
			GameObject go = Terrain.CreateTerrainGameObject(data);
			go.transform.position = new Vector3(-0.5f * Size, 0f, -0.5f * Size);
			Terrain terrain = go.GetComponent<Terrain>();
			terrain.materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
			terrain.heightmapPixelError = 1f;
			return terrain;
		}

		/// <summary>One river: down the channel to the lip, over the cliff, into the pool and away.</summary>
		private static SceneHydrology BuildHydrology()
		{
			var points = new List<Vector3>();
			var reach = new List<byte>();
			for (float z = -60f; z < -0.4f; z += 2f)
			{
				points.Add(new Vector3(0f, Plateau - 0.25f, z));
				reach.Add(0);
			}
			points.Add(new Vector3(0f, Plateau - 0.25f, -0.2f));
			reach.Add(0);
			// Over the ledge: three falling points down the face to the foot.
			points.Add(new Vector3(0f, Plateau - 16f, 0.8f));
			reach.Add(4);
			points.Add(new Vector3(0f, Base + 14f, 1.6f));
			reach.Add(4);
			points.Add(new Vector3(0f, Base - 1.8f, 3.5f));
			reach.Add(2);
			for (float z = 6f; z <= 60f; z += 2f)
			{
				points.Add(new Vector3(0f, Base - 1.8f - 0.01f * (z - 6f), z));
				reach.Add(z < 16f ? (byte)2 : (byte)0);
			}
			int n = points.Count;
			var river = new SceneHydrology.River
			{
				Id = 0,
				Perennial = true,
				Points = points.ToArray(),
				Bed = new float[n],
				Width = new float[n],
				Depth = new float[n],
				Discharge = new float[n],
				Speed = new float[n],
				Reach = reach.ToArray(),
			};
			for (int i = 0; i < n; i++)
			{
				bool inPool = points[i].z > 0f && points[i].z < 18f;
				river.Width[i] = inPool ? 14f : 6f;
				river.Depth[i] = inPool ? 4f : 0.9f;
				river.Bed[i] = points[i].y - river.Depth[i];
				river.Discharge[i] = 8f;
				river.Speed[i] = reach[i] == 4 ? 3f : 1.4f;
			}
			var hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			hydrology.Rivers.Add(river);
			hydrology.Boulders.Add(Boulder);
			return hydrology;
		}
	}
}
#endif
