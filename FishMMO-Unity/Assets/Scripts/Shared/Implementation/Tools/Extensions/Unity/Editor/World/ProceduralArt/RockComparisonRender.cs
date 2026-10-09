#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Studio renders of every generated rock — each formation shape of each <see cref="RockTypes"/> type, the
	/// legacy boulders, and every level of each <see cref="CliffSections"/> variant — through URP with their real
	/// prefabs and materials, one PNG per rock, for comparing against reference art.
	/// </summary>
	/// <remarks>
	/// Headless: <c>-executeMethod FishMMO.Shared.WorldDesign.RockComparisonRender.Run</c> with graphics
	/// (under xvfb, not -nographics), the generated art already on disk. Output to
	/// <c>FISHMMO_ROCK_RENDER_OUT</c> (default /tmp/rockrender): <c>{name}.png</c> plus <c>index.txt</c>
	/// (name, size, triangles). <c>FISHMMO_ROCK_RENDER_ONLY</c> = comma-separated type names limits it ("Legacy" for
	/// the boulders, "CliffSections" for the sections, which are built live and wear the limestone material).
	/// Every rock is seen from the same three-quarter view a little above, framed to its own size, on a dark
	/// floor that hides the buried part, as a sculpt sheet shows a rock.
	/// </remarks>
	public static class RockComparisonRender
	{
		private const int Size = 720;

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_ROCK_RENDER_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = "/tmp/rockrender";
			}
			string onlyVar = Environment.GetEnvironmentVariable("FISHMMO_ROCK_RENDER_ONLY");
			var only = new HashSet<string>(string.IsNullOrEmpty(onlyVar) ? Array.Empty<string>() : onlyVar.Split(','));
			int code = 0;
			RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
			System.Reflection.PropertyInfo shadowDistance = pipeline != null ? pipeline.GetType().GetProperty("shadowDistance") : null;
			object previousShadowDistance = shadowDistance != null ? shadowDistance.GetValue(pipeline) : null;
			try
			{
				Directory.CreateDirectory(output);
				// Close shadows at full resolution; put back after, never saved.
				shadowDistance?.SetValue(pipeline, 30f);
				RenderAll(output, only);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				code = 1;
			}
			finally
			{
				if (shadowDistance != null && previousShadowDistance != null)
				{
					shadowDistance.SetValue(pipeline, previousShadowDistance);
				}
			}
			EditorApplication.Exit(code);
		}

		private static void RenderAll(string output, HashSet<string> only)
		{
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.6f;
			sun.color = new Color(1f, 0.96f, 0.9f);
			sun.shadows = LightShadows.Soft;
			sun.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.5f, 0.56f, 0.66f);
			RenderSettings.ambientEquatorColor = new Color(0.36f, 0.36f, 0.36f);
			RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.16f);
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();

			GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
			floor.transform.localScale = new Vector3(20f, 1f, 20f);
			var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			floorMaterial.SetColor("_BaseColor", new Color(0.13f, 0.13f, 0.13f));
			floorMaterial.SetFloat("_Smoothness", 0f);
			floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
			camera.fieldOfView = 26f;
			camera.nearClipPlane = 0.05f;
			camera.farClipPlane = 500f;
			var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
			camera.targetTexture = target;

			var log = new List<string>();
			foreach (RockType type in RockTypes.All)
			{
				if (only.Count > 0 && !only.Contains(type.Name))
				{
					continue;
				}
				foreach (FormationShape shape in type.Shapes)
				{
					string name = RockArtNames.FormationPrefab(type.Name, shape.Name, 0);
					Shot(camera, target, output, name, $"{type.Name} {shape.Name} ({shape.Kind})", log);
				}
			}
			if (only.Count == 0 || only.Contains("Legacy"))
			{
				foreach (RockMaterialSpec material in ProceduralArtCatalogue.RockMaterials)
				{
					foreach (RockShape shape in ProceduralArtCatalogue.BoulderShapes)
					{
						string name = ProceduralArtCatalogue.BoulderPrefab(material.Name, shape.Name);
						Shot(camera, target, output, name, $"Legacy {material.Name} {shape.Name}", log);
					}
				}
			}
			if (only.Count == 0 || only.Contains("CliffSections"))
			{
				var limestone = AssetDatabase.LoadAssetAtPath<Material>(ProceduralArtCatalogue.MaterialPath(CliffRocks.MaterialName("Limestone")));
				foreach (string style in CliffSections.Styles)
				{
					for (int v = 0; v < CliffSections.VariantCount; v++)
					{
						MeshBuilder[] levels = CliffSections.BuildMeshes(style, v, ProceduralArtCatalogue.DefaultSeed, out string report);
						for (int lod = 0; lod < levels.Length; lod++)
						{
							string name = CliffSections.MeshName(style, v, lod);
							Mesh mesh = levels[lod].ToMesh(name);
							var rock = new GameObject(name);
							rock.AddComponent<MeshFilter>().sharedMesh = mesh;
							rock.AddComponent<MeshRenderer>().sharedMaterial = limestone;
							Frame(camera, target, output, rock, name, $"Cliff section {style} {v} LOD{lod} ({report})", levels[lod].TriangleCount, log);
							UnityEngine.Object.DestroyImmediate(mesh);
						}
					}
				}
			}
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
		}

		private static void Shot(Camera camera, RenderTexture target, string output, string prefabName, string label, List<string> log)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(prefabName));
			if (prefab == null)
			{
				log.Add($"{prefabName}\t{label}\tMISSING {ProceduralArtCatalogue.PrefabPath(prefabName)}");
				return;
			}
			var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
			int triangles = 0;
			foreach (LODGroup group in rock.GetComponentsInChildren<LODGroup>())
			{
				group.ForceLOD(0);
				LOD[] lods = group.GetLODs();
				if (lods.Length > 0)
				{
					foreach (Renderer r in lods[0].renderers)
					{
						if (r != null && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
						{
							triangles += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
						}
					}
				}
			}
			Frame(camera, target, output, rock, prefabName, label, triangles, log);
		}

		/// <summary>Frames a placed rock, captures it, logs it and destroys it.</summary>
		private static void Frame(Camera camera, RenderTexture target, string output, GameObject rock, string name, string label, int triangles, List<string> log)
		{
			rock.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 20f, 0f));
			bool any = false;
			var bounds = new Bounds();
			foreach (Renderer r in rock.GetComponentsInChildren<Renderer>())
			{
				if (!r.enabled)
				{
					continue;
				}
				if (any) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
				any = true;
			}
			// The part above the floor, seen from the south-west a fifth of the way up the sky.
			float top = Mathf.Max(0.1f, bounds.max.y);
			var centre = new Vector3(bounds.center.x, top * 0.42f, bounds.center.z);
			float radius = 0.5f * Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z + top * top) * 1.02f;
			float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
			float az = 35f * Mathf.Deg2Rad, el = 20f * Mathf.Deg2Rad;
			var back = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
			camera.transform.position = centre + back * distance;
			camera.transform.LookAt(centre);
			Capture(camera, target, Path.Combine(output, name + ".png"));
			log.Add($"{name}\t{label}\t{bounds.size.x:0.0}×{bounds.size.z:0.0}×{top:0.0} m\t{triangles} tris");
			UnityEngine.Object.DestroyImmediate(rock);
		}

		private static void Capture(Camera camera, RenderTexture target, string path)
		{
			// The first renders of a fresh headless editor can come out before the shaders are ready: warm up.
			for (int i = 0; i < 3; i++)
			{
				camera.Render();
			}
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			var image = new Texture2D(Size, Size, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			File.WriteAllBytes(path, image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
		}
	}
}
#endif
