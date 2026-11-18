#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Proves the baked props on a throwaway scene: generates FISHMMO_PROPS_SCENE's atlas entry (Baoakraal by
	/// default) under the name "Prop Bake Probe", counts what the scene holds (objects, renderers, LOD groups,
	/// colliders, props per source) and how big its files are, renders a cliff and a tree view through the edit-mode
	/// prop path, then deletes the scene and its folders. Writes report.txt and PNGs to FISHMMO_PROPS_OUT.
	/// </summary>
	public static class ScenePropProbe
	{
		private const string ProbeName = "Prop Bake Probe";

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_PROPS_OUT") ?? Path.Combine(Path.GetTempPath(), "fishmmo-props");
			Directory.CreateDirectory(output);
			string sceneName = Environment.GetEnvironmentVariable("FISHMMO_PROPS_SCENE") ?? "Baoakraal Hyena-den";
			WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().FirstOrDefault(e => e != null && e.SceneName == sceneName);
			var report = new List<string>();
			if (entry == null || entry.Body == null)
			{
				Debug.LogError($"[Prop probe] no placed atlas entry '{sceneName}'.");
				return;
			}
			string scenePath = null, terrainFolder = SceneGenerator.TerrainFolder(entry.Body, ProbeName);
			try
			{
				SceneGenerationResult result = SceneGenerator.Generate(new SceneGenerationRequest
				{
					SceneName = ProbeName,
					Body = entry.Body,
					Layer = entry.Layer,
					Latitude = entry.Latitude,
					Longitude = entry.Longitude,
					SizeKm = entry.SizeKm,
					HeadingDegrees = entry.HeadingDegrees,
					FineDetail = true,
					Erosion = true,
					ErosionStrength = entry.ErosionStrength,
				});
				if (result == null || !result.Success)
				{
					Debug.LogError($"[Prop probe] generation failed: {result?.Problem}");
					return;
				}
				scenePath = result.ScenePath;
				foreach (string note in result.Notes)
				{
					if (note.Contains("Props") || note.Contains("NavMesh") || note.Contains("Cliffs") || note.Contains("boulders") || note.Contains("[Cliffs]"))
					{
						report.Add("note: " + note);
					}
				}
				Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

				// What the scene holds.
				var all = new List<GameObject>();
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
					{
						all.Add(t.gameObject);
					}
				}
				int renderers = all.Count(g => g.GetComponent<MeshRenderer>() != null);
				int lods = all.Count(g => g.GetComponent<LODGroup>() != null);
				int colliders = all.Count(g => g.GetComponent<MeshCollider>() != null);
				report.Add($"scene objects {all.Count:N0}, MeshRenderers {renderers:N0}, LODGroups {lods:N0}, MeshColliders {colliders:N0}");
				SceneProps props = UnityEngine.Object.FindAnyObjectByType<SceneProps>();
				if (props != null)
				{
					foreach (ScenePropSet set in props.Sets)
					{
						if (set != null)
						{
							report.Add($"set {set.Source}: {set.Props.Length:N0} props of {set.Prototypes.Length} prototypes ({AssetDatabase.GetAssetPath(set)})");
						}
					}
				}
				else
				{
					report.Add("no SceneProps in the scene");
				}
				ScenePropColliders collision = UnityEngine.Object.FindAnyObjectByType<ScenePropColliders>();
				if (collision != null)
				{
					foreach (ScenePropCollisionSet set in collision.Sets)
					{
						if (set != null)
						{
							int meshes = set.Prototypes.Count(p => p.Mesh != null);
							int tris = set.Prototypes.Where(p => p.Mesh != null).Sum(p => (int)p.Mesh.GetIndexCount(0) / 3);
							report.Add($"collision {set.Source}: {set.Instances.Length:N0} collidable of {set.Prototypes.Length} prototypes ({meshes} with a mesh, {tris:N0} triangles across them)");
						}
					}
				}
				// What the streamer merges and cooks per chunk: collision triangles per 32 m chunk.
				if (collision != null)
				{
					var perChunk = new Dictionary<long, long>();
					long total = 0;
					foreach (ScenePropCollisionSet set in collision.Sets)
					{
						if (set == null)
						{
							continue;
						}
						foreach (ScenePropCollisionSet.Instance instance in set.Instances)
						{
							Mesh mesh = set.Prototypes[instance.Prototype].Mesh;
							if (mesh == null)
							{
								continue;
							}
							long tris = 0;
							for (int sub = 0; sub < mesh.subMeshCount; sub++)
							{
								tris += mesh.GetIndexCount(sub) / 3;
							}
							long key = ((long)Mathf.FloorToInt(instance.Position.x / PropColliderStreamer.CellMetres) << 32) | (uint)Mathf.FloorToInt(instance.Position.z / PropColliderStreamer.CellMetres);
							perChunk[key] = (perChunk.TryGetValue(key, out long had) ? had : 0) + tris;
							total += tris;
						}
					}
					var counts = perChunk.Values.OrderBy(v => v).ToList();
					if (counts.Count > 0)
					{
						report.Add($"collision triangles: {total:N0} in {counts.Count:N0} chunks; per chunk mean {total / counts.Count:N0}, p95 {counts[(int)(counts.Count * 0.95f)]:N0}, max {counts[counts.Count - 1]:N0}");
					}
				}
				// The NavMesh, with the props' collision fed straight to the builder.
				// FISHMMO_PROPS_NAVMESH_VARIANTS="on:0.333,off:0.5" bakes each (height mesh, voxel metres) in turn to compare them.
				string variants = Environment.GetEnvironmentVariable("FISHMMO_PROPS_NAVMESH_VARIANTS");
				if (Environment.GetEnvironmentVariable("FISHMMO_PROPS_NAVMESH") == "1" || !string.IsNullOrEmpty(variants))
				{
					var area = Terrain.activeTerrains.Where(t => t.gameObject.scene == scene && t.terrainData != null).Sum(t => t.terrainData.size.x * t.terrainData.size.z);
					report.Add($"navmesh ground {area / 1e6f:0.0} km2");
					foreach (string variant in (string.IsNullOrEmpty(variants) ? "default" : variants).Split(','))
					{
						string[] parts = variant.Split(':');
						bool? height = parts.Length == 2 ? parts[0] == "on" : (bool?)null;
						float? voxel = parts.Length == 2 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : (float?)null;
						SceneNavMeshBaker.Bake(scene, report, height, voxel);
						EditorSceneManager.SaveScene(scene);
						AssetDatabase.SaveAssets();
						string navPath = ScenePropBaker.AssetPath(scene, "NavMesh");
						var triangulation = UnityEngine.AI.NavMesh.CalculateTriangulation();
						string size = navPath != null && File.Exists(navPath) ? $"{new FileInfo(navPath).Length / 1048576.0:0.0} MB" : "no file";
						string head = navPath != null && File.Exists(navPath) ? (File.ReadLines(navPath).First().StartsWith("%YAML") ? "text" : "binary") : "?";
						report.Add($"navmesh [{variant}] {size} ({head}), {triangulation.indices.Length / 3:N0} triangles");
						// Written as each variant lands, so a later hang cannot lose the numbers.
						File.WriteAllLines(Path.Combine(output, "report.txt"), report);
						Debug.Log($"[Prop probe] navmesh [{variant}] done");
					}
				}
				report.Add($"scene file {new FileInfo(scenePath).Length / 1048576.0:0.0} MB");
				if (Directory.Exists(terrainFolder))
				{
					foreach (string file in Directory.GetFiles(terrainFolder, "*.asset").Where(f => f.Contains("Props") || f.Contains("Collision")))
					{
						report.Add($"  {Path.GetFileName(file)}: {new FileInfo(file).Length / 1048576.0:0.00} MB");
					}
				}

				Debug.Log("[Prop probe] rendering");
				// Two views through the edit-mode prop path.
				var camera = new GameObject("Probe Camera").AddComponent<Camera>();
				camera.fieldOfView = 50f;
				camera.nearClipPlane = 0.3f;
				camera.farClipPlane = 4000f;
				Type cameraData = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
				if (cameraData != null)
				{
					Component data = camera.gameObject.AddComponent(cameraData);
					cameraData.GetProperty("requiresDepthTexture")?.SetValue(data, true);
				}
				var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
				camera.targetTexture = target;
				int shot = 0;
				foreach (string source in new[] { CliffPlacer.PropSource, ScenePropBaker.ScatterSource })
				{
					ScenePropSet set = props != null ? props.Sets.FirstOrDefault(s => s != null && s.Source == source) : null;
					if (set == null || set.Props.Length == 0)
					{
						continue;
					}
					Vector3 at = set.Props[set.Props.Length / 2].Position;
					foreach ((string name, Vector3 offset) in new[] { ("near", new Vector3(-40f, 25f, -40f)), ("far", new Vector3(-220f, 140f, -220f)) })
					{
						camera.transform.position = at + offset;
						camera.transform.LookAt(at);
						for (int i = 0; i < 3; i++)
						{
							camera.Render();
						}
						RenderTexture previous = RenderTexture.active;
						RenderTexture.active = target;
						var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
						image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
						image.Apply();
						RenderTexture.active = previous;
						string file = Path.Combine(output, $"props_{shot++:00}_{source}_{name}.png");
						File.WriteAllBytes(file, image.EncodeToPNG());
						UnityEngine.Object.DestroyImmediate(image);
						report.Add($"{Path.GetFileName(file)} at ({at.x:0}, {at.y:0}, {at.z:0})");
					}
				}
			}
			finally
			{
				File.WriteAllLines(Path.Combine(output, "report.txt"), report);
				Debug.Log("[Prop probe]\n" + string.Join("\n", report));
				Debug.Log("[Prop probe] cleaning up");
				// The throwaway scene and everything generated beside it.
				EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				if (scenePath != null)
				{
					AssetDatabase.DeleteAsset(scenePath);
				}
				if (AssetDatabase.IsValidFolder(terrainFolder))
				{
					AssetDatabase.DeleteAsset(terrainFolder);
				}
				// Its NavMesh catalogue entry and server-group registration, if it baked one.
				SceneNavMeshCatalogueEditor.Remove(ProbeName);
				// The atlas entry the generator made for it, or the World Atlas lists the probe as a placed scene.
				WorldAtlasScene probeEntry = WorldAtlasScene.Find(ProbeName);
				if (probeEntry != null)
				{
					AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(probeEntry));
					WorldAtlasScene.EditorLookup.Invalidate();
				}
				string arrays = $"Assets/Prefabs/Client/TerrainArrays/{ProbeName}";
				if (AssetDatabase.IsValidFolder(arrays))
				{
					AssetDatabase.DeleteAsset(arrays);
				}
			}
		}
	}
}
#endif
