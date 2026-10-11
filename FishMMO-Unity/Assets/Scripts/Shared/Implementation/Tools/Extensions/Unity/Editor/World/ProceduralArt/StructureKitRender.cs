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
	/// Studio renders of every structure piece — each variant at level 0, through URP with its generated prefab and
	/// materials — one PNG each plus a contact sheet, for judging the kit.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.StructureKitRender.Run</c> in a non-batch editor under xvfb (graphics
	/// needed), the art generated first. It works one piece per editor update (<see cref="EditorApplication.update"/>)
	/// and exits through <see cref="EditorApplication.Exit"/>: 0 when every piece rendered, 1 otherwise.
	/// </para>
	/// <para>
	/// Environment: <c>FISHMMO_STRUCTURE_RENDER_OUT</c> (default /tmp/claude-1000/structure-render);
	/// <c>FISHMMO_STRUCTURE_RENDER_ONLY</c> comma-separated piece ids; <c>FISHMMO_STRUCTURE_RENDER_FINISH</c> None, Mossy,
	/// Charred or Algae; <c>FISHMMO_STRUCTURE_RENDER_LIVE</c>=1 builds every piece live (no generated art needed: the
	/// materials then are flat colours unless the generated ones exist). Writes <c>{piece}_{v}.png</c>,
	/// <c>contact.png</c> and <c>index.txt</c> (name, size, triangles, source).
	/// </para>
	/// </remarks>
	public static class StructureKitRender
	{
		private const int Size = 640;
		private const int Thumb = 256;
		private const int Columns = 10;

		/// <summary>
		/// How high a deck-anchored piece (pier, bridge span) is set above the studio ground: its origin is the walking
		/// surface and its piles reach metres below it, so at y = 0 the ground would hide everything but the planks.
		/// </summary>
		private const float DeckLift = 1.5f;

		private sealed class Job
		{
			public string Output;
			public StructureFinish Finish;
			public bool Live;
			public readonly Queue<(StructurePiece Piece, int Variant)> Todo = new Queue<(StructurePiece, int)>();
			public readonly List<string> Log = new List<string>();
			public readonly List<string> Images = new List<string>();
			public Camera Camera;
			public RenderTexture Target;
			public int Failures;
			public object PreviousShadowDistance;
			public System.Reflection.PropertyInfo ShadowDistance;
			public readonly Dictionary<StructureMaterial, Material> Flat = new Dictionary<StructureMaterial, Material>();
			public int Warmup = 3;
		}

		private static Job job;

		public static void Run()
		{
			try
			{
				job = Start();
				EditorApplication.update += Pump;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				EditorApplication.Exit(1);
			}
		}

		private static Job Start()
		{
			var j = new Job
			{
				Output = Env("FISHMMO_STRUCTURE_RENDER_OUT", "/tmp/claude-1000/structure-render"),
				Live = Env("FISHMMO_STRUCTURE_RENDER_LIVE", "0") == "1",
			};
			Enum.TryParse(Env("FISHMMO_STRUCTURE_RENDER_FINISH", "None"), true, out j.Finish);
			Directory.CreateDirectory(j.Output);
			string only = Env("FISHMMO_STRUCTURE_RENDER_ONLY", string.Empty);
			var wanted = new HashSet<string>(only.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			foreach (StructurePiece p in StructurePieces.All)
			{
				if (wanted.Count > 0 && !wanted.Contains(p.Id)) continue;
				for (int v = 0; v < p.Variants; v++)
				{
					j.Todo.Enqueue((p, v));
				}
			}

			RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
			j.ShadowDistance = pipeline != null ? pipeline.GetType().GetProperty("shadowDistance") : null;
			j.PreviousShadowDistance = j.ShadowDistance?.GetValue(pipeline);
			j.ShadowDistance?.SetValue(pipeline, 80f);

			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.5f;
			sun.color = new Color(1f, 0.96f, 0.9f);
			sun.shadows = LightShadows.Soft;
			sun.transform.rotation = Quaternion.Euler(45f, 145f, 0f);
			var fill = new GameObject("Fill").AddComponent<Light>();
			fill.type = LightType.Directional;
			fill.intensity = 0.35f;
			fill.color = new Color(0.75f, 0.82f, 1f);
			fill.shadows = LightShadows.None;
			fill.transform.rotation = Quaternion.Euler(30f, -60f, 0f);
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.52f, 0.58f, 0.68f);
			RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.4f);
			RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.17f);
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();

			GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
			ground.name = "Ground";
			ground.transform.localScale = new Vector3(40f, 1f, 40f);
			var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			groundMaterial.SetColor("_BaseColor", new Color(0.32f, 0.31f, 0.28f));
			groundMaterial.SetFloat("_Smoothness", 0f);
			ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

			j.Camera = new GameObject("Camera").AddComponent<Camera>();
			j.Camera.clearFlags = CameraClearFlags.SolidColor;
			j.Camera.backgroundColor = new Color(0.62f, 0.68f, 0.76f);
			j.Camera.fieldOfView = 30f;
			j.Camera.nearClipPlane = 0.05f;
			j.Camera.farClipPlane = 1000f;
			j.Target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
			j.Camera.targetTexture = j.Target;
			return j;
		}

		private static string Env(string name, string fallback)
		{
			string v = Environment.GetEnvironmentVariable(name);
			return string.IsNullOrEmpty(v) ? fallback : v;
		}

		private static void Pump()
		{
			if (job == null)
			{
				EditorApplication.update -= Pump;
				return;
			}
			try
			{
				if (job.Warmup > 0)
				{
					// The first frames of a fresh editor can render before the shaders are ready.
					job.Warmup--;
					job.Camera.Render();
					return;
				}
				if (job.Todo.Count > 0)
				{
					(StructurePiece piece, int variant) = job.Todo.Dequeue();
					RenderOne(job, piece, variant);
					return;
				}
				Finish(null);
			}
			catch (Exception e)
			{
				Finish(e);
			}
		}

		private static void RenderOne(Job j, StructurePiece piece, int variant)
		{
			string name = $"{piece.Id}_{variant}";
			GameObject instance = null;
			Mesh liveMesh = null;
			string source;
			int triangles = 0;
			try
			{
				GameObject prefab = j.Live ? null : StructureKit.Prefab(piece.Id, variant, j.Finish);
				if (prefab != null)
				{
					instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
					source = "prefab";
					foreach (LODGroup group in instance.GetComponentsInChildren<LODGroup>())
					{
						group.ForceLOD(0);
						LOD[] lods = group.GetLODs();
						if (lods.Length > 0)
						{
							foreach (Renderer r in lods[0].renderers)
							{
								if (r != null && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
								{
									for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
									{
										triangles += (int)(mf.sharedMesh.GetIndexCount(s) / 3);
									}
								}
							}
						}
					}
				}
				else
				{
					StructureMesh built = StructurePieces.Build(piece, variant, 0, ProceduralArtCatalogue.DefaultSeed);
					liveMesh = built.Mesh.ToMesh(name);
					triangles = built.Mesh.TriangleCount;
					instance = new GameObject(name);
					instance.AddComponent<MeshFilter>().sharedMesh = liveMesh;
					var mats = new Material[built.Materials.Length];
					for (int i = 0; i < mats.Length; i++)
					{
						mats[i] = LiveMaterial(j, built.Materials[i]);
					}
					instance.AddComponent<MeshRenderer>().sharedMaterials = mats;
					source = "live";
				}
				bool deck = piece.Anchor == StructureAnchor.Deck;
				instance.transform.SetPositionAndRotation(deck ? new Vector3(0f, DeckLift, 0f) : Vector3.zero, Quaternion.identity);
				// A lifted deck is framed down to the ground it stands in, a grounded piece to a metre of its foundation.
				Bounds bounds = Frame(j.Camera, instance, deck ? 0f : -1f);
				string path = Path.Combine(j.Output, name + ".png");
				Capture(j, path);
				j.Images.Add(path);
				j.Log.Add($"{name}\t{bounds.size.x:0.0}x{bounds.size.z:0.0}x{bounds.max.y - instance.transform.position.y:0.0} m\t{triangles} tris\t{source}\t{string.Join(",", piece.Tags)}");
			}
			catch (Exception e)
			{
				j.Failures++;
				j.Log.Add($"{name}\tFAILED {e.Message}");
				Debug.LogException(e);
			}
			finally
			{
				if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
				if (liveMesh != null) UnityEngine.Object.DestroyImmediate(liveMesh);
			}
		}

		/// <summary>The generated material when it exists, else a flat URP Lit colour for the material.</summary>
		private static Material LiveMaterial(Job j, StructureMaterial m)
		{
			var generated = AssetDatabase.LoadAssetAtPath<Material>(ProceduralArtCatalogue.MaterialPath(StructurePieces.MaterialName(m, j.Finish)));
			if (generated != null)
			{
				return generated;
			}
			if (!j.Flat.TryGetValue(m, out Material flat))
			{
				flat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
				Color c = StructureSurfaces.TintOf(m);
				switch (m)
				{
					case StructureMaterial.Timber: c = new Color(0.42f, 0.29f, 0.17f); break;
					case StructureMaterial.Plaster: c = new Color(0.84f, 0.8f, 0.7f); break;
					case StructureMaterial.Thatch: c = new Color(0.62f, 0.5f, 0.28f); break;
					case StructureMaterial.Stone: c = new Color(0.62f, 0.6f, 0.55f); break;
					case StructureMaterial.Fieldstone: c = new Color(0.47f, 0.46f, 0.43f); break;
					case StructureMaterial.Monolith: c = new Color(0.64f, 0.61f, 0.56f); break;
					case StructureMaterial.Iron: c = new Color(0.17f, 0.17f, 0.18f); break;
					case StructureMaterial.Earth: c = new Color(0.36f, 0.28f, 0.19f); break;
				}
				flat.SetColor("_BaseColor", c);
				flat.SetFloat("_Smoothness", 0.15f);
				j.Flat[m] = flat;
			}
			return flat;
		}

		/// <summary>Frames the object from the front-left, a little above, to its own size, nothing lower than <paramref name="floor"/>.</summary>
		private static Bounds Frame(Camera camera, GameObject go, float floor)
		{
			bool any = false;
			var bounds = new Bounds();
			foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
			{
				if (!r.enabled) continue;
				if (any) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
				any = true;
			}
			float top = Mathf.Max(0.2f, bounds.max.y);
			float bottom = Mathf.Max(bounds.min.y, floor);
			var centre = new Vector3(bounds.center.x, (top + bottom) * 0.5f, bounds.center.z);
			float radius = 0.5f * Mathf.Sqrt(bounds.size.x * bounds.size.x + bounds.size.z * bounds.size.z + (top - bottom) * (top - bottom)) * 1.04f;
			float distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
			float az = 35f * Mathf.Deg2Rad, el = 22f * Mathf.Deg2Rad;
			var back = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el));
			camera.transform.position = centre + back * distance;
			camera.transform.LookAt(centre);
			return bounds;
		}

		private static void Capture(Job j, string path)
		{
			j.Camera.Render();
			j.Camera.Render();
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = j.Target;
			var image = new Texture2D(Size, Size, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			File.WriteAllBytes(path, image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
		}

		/// <summary>Every render shrunk into one grid, in render order (index.txt lists the order).</summary>
		private static void ContactSheet(Job j)
		{
			if (j.Images.Count == 0)
			{
				return;
			}
			int rows = (j.Images.Count + Columns - 1) / Columns;
			var sheet = new Texture2D(Columns * Thumb, rows * Thumb, TextureFormat.RGB24, false);
			var clear = new Color32[sheet.width * sheet.height];
			for (int i = 0; i < clear.Length; i++) clear[i] = new Color32(24, 24, 24, 255);
			sheet.SetPixels32(clear);
			var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
			for (int n = 0; n < j.Images.Count; n++)
			{
				if (!source.LoadImage(File.ReadAllBytes(j.Images[n])))
				{
					continue;
				}
				int col = n % Columns, row = rows - 1 - n / Columns;
				for (int y = 0; y < Thumb; y++)
				{
					for (int x = 0; x < Thumb; x++)
					{
						Color c = source.GetPixelBilinear((x + 0.5f) / Thumb, (y + 0.5f) / Thumb);
						sheet.SetPixel(col * Thumb + x, row * Thumb + y, c);
					}
				}
			}
			sheet.Apply();
			File.WriteAllBytes(Path.Combine(j.Output, "contact.png"), sheet.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(sheet);
			UnityEngine.Object.DestroyImmediate(source);
		}

		private static void Finish(Exception failure)
		{
			EditorApplication.update -= Pump;
			Job j = job;
			job = null;
			int code = 0;
			try
			{
				if (failure != null)
				{
					Debug.LogException(failure);
					code = 1;
				}
				if (j != null)
				{
					ContactSheet(j);
					File.WriteAllLines(Path.Combine(j.Output, "index.txt"), j.Log);
					if (j.Failures > 0) code = 1;
					RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
					if (j.ShadowDistance != null && j.PreviousShadowDistance != null && pipeline != null)
					{
						j.ShadowDistance.SetValue(pipeline, j.PreviousShadowDistance);
					}
					Debug.Log($"[Structure render] {j.Images.Count} rendered, {j.Failures} failed, to {j.Output}");
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				code = 1;
			}
			EditorApplication.Exit(code);
		}
	}
}
#endif
