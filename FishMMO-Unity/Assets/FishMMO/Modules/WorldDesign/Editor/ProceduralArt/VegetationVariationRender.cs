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
	/// Contact sheets of the generated plants, each species drawn at several places side by side, with the
	/// vegetation shader's per-plant variation (VegVary) on and then held off — what one mesh makes of many
	/// plants, and what it made of them before.
	/// </summary>
	/// <remarks>
	/// Headless: <c>-executeMethod FishMMO.Shared.WorldDesign.VegetationVariationRender.Run</c> with graphics
	/// (under xvfb, not -nographics). Authors the biome art first, so the meshes carry their parts and the
	/// materials their figures, then lays each species out in an empty scene and renders it to PNGs in
	/// <c>FISHMMO_VEG_RENDER_OUT</c> (default /tmp/vegrender). The instances stand at different places, which
	/// is all the shader draws its figures from; "off" holds every figure at nothing through a property block.
	/// </remarks>
	public static class VegetationVariationRender
	{
		private const int Width = 1600;
		private const int Height = 900;
		private const int Instances = 5;

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_VEG_RENDER_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = "/tmp/vegrender";
			}
			int code = 0;
			try
			{
				Directory.CreateDirectory(output);
				if (Environment.GetEnvironmentVariable("FISHMMO_VEG_RENDER_SKIP_ART") != "1")
				{
					BiomeArtGenerator.Report report = BiomeArtGenerator.Generate(ProceduralArtMode.Full, ProceduralArtCatalogue.DefaultSeed);
					Debug.Log("[Vegetation render] " + report);
				}
				Render(output);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				code = 1;
			}
			EditorApplication.Exit(code);
		}

		private static void Render(string output)
		{
			// A grove is wider than the pipeline's shadow distance, and a tree without its shadow floats: the
			// distance raised for these renders and put back after, through reflection (this assembly does not
			// reference URP) and never saved.
			RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
			System.Reflection.PropertyInfo shadowDistance = pipeline != null ? pipeline.GetType().GetProperty("shadowDistance") : null;
			object previousShadowDistance = shadowDistance != null ? shadowDistance.GetValue(pipeline) : null;
			try
			{
				shadowDistance?.SetValue(pipeline, 700f);
				RenderScene(output);
			}
			finally
			{
				if (shadowDistance != null && previousShadowDistance != null)
				{
					shadowDistance.SetValue(pipeline, previousShadowDistance);
				}
			}
		}

		private static void RenderScene(string output)
		{
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.5f;
			sun.color = new Color(1f, 0.96f, 0.9f);
			sun.shadows = LightShadows.Soft;
			sun.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.8f);
			RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.45f);
			RenderSettings.ambientGroundColor = new Color(0.25f, 0.22f, 0.18f);
			// The vegetation shader's own ambient for instanced draws (FishTrilight): the same three colours.
			Shader.SetGlobalVector("_FishAmbientSky", RenderSettings.ambientSkyColor);
			Shader.SetGlobalVector("_FishAmbientEquator", RenderSettings.ambientEquatorColor);
			Shader.SetGlobalVector("_FishAmbientGround", RenderSettings.ambientGroundColor);
			RenderSettings.fog = false;

			GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
			ground.transform.localScale = new Vector3(200f, 1f, 200f);
			var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
			groundMaterial.SetColor("_BaseColor", new Color(0.32f, 0.3f, 0.24f));
			groundMaterial.SetFloat("_Smoothness", 0.05f);
			ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;

			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.62f, 0.74f, 0.88f);
			camera.fieldOfView = 30f;
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane = 2000f;
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;

			var log = new List<string>();
			foreach (TreeSpecies species in ProceduralArtCatalogue.Trees)
			{
				Sheet(camera, target, output, "tree_" + species.Name, ProceduralArtCatalogue.TreePrefab(species.Name), species.CrownWidth * species.Height * 2.4f + 2f, log);
			}
			foreach (DetailSpec detail in ProceduralArtCatalogue.Details)
			{
				Sheet(camera, target, output, "detail_" + detail.Name, ProceduralArtCatalogue.DetailPrefab(detail.Name), Mathf.Max(0.8f, detail.Plant.Radius * 4f + detail.Plant.Height), log);
			}
			// Groups: illustrative stands (dense at the heart, thinning to their edge, spaced by crown), and a meadow
			// patch — how the plants read together. Not the scene scatter's own placement (TerrainScatter).
			Grove(camera, target, output, "grove_temperate", new[] { "Oak", "Birch" }, null, 70f, 46, log);
			Grove(camera, target, output, "grove_boreal", new[] { "Spruce", "Pine" }, null, 60f, 60, log);
			Grove(camera, target, output, "grove_tropical", new[] { "Jungle", "Palm", "Bamboo" }, null, 80f, 40, log);
			Grove(camera, target, output, "grove_savanna", new[] { "Acacia" }, new[] { "GrassDry", "GrassTuft" }, 90f, 9, log);
			Meadow(camera, target, output, "meadow", log);
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
		}

		/// <summary>A stand of mixed trees in a disc, spaced by their crowns, optionally over grass, seen from above at an angle.</summary>
		private static void Grove(Camera camera, RenderTexture target, string output, string file, string[] species, string[] understorey, float radius, int count, List<string> log)
		{
			var placed = new List<(Vector3 at, float crown)>();
			var objects = new List<GameObject>();
			var rng = new DeterministicRNG(file.GetHashCode());
			int tries = 0;
			while (placed.Count < count && tries++ < count * 60)
			{
				string name = species[rng.Next(species.Length)];
				if (!ProceduralArtCatalogue.TryTree(name, out TreeSpecies tree))
				{
					continue;
				}
				// Dense at the heart, thinning out: kept with a chance that falls off with distance from the middle.
				float r = Mathf.Sqrt(rng.NextFloat()) * radius;
				if (rng.NextFloat() > 1f - 0.75f * (r / radius) * (r / radius))
				{
					continue;
				}
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				var at = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
				float crown = Mathf.Max(1.5f, tree.CrownWidth * tree.Height);
				bool clear = true;
				foreach ((Vector3 other, float otherCrown) in placed)
				{
					if ((other - at).sqrMagnitude < Mathf.Pow((crown + otherCrown) * 0.6f, 2f))
					{
						clear = false;
						break;
					}
				}
				if (!clear)
				{
					continue;
				}
				GameObject plant = Place(ProceduralArtCatalogue.TreePrefab(name), at, rng.NextFloat() * 360f, rng.Range(0.85f, 1.15f));
				if (plant != null)
				{
					placed.Add((at, crown));
					objects.Add(plant);
				}
			}
			if (understorey != null)
			{
				for (int i = 0; i < 900; i++)
				{
					float r = Mathf.Sqrt(rng.NextFloat()) * radius * 1.1f;
					float angle = rng.NextFloat() * Mathf.PI * 2f;
					GameObject plant = Place(ProceduralArtCatalogue.DetailPrefab(understorey[rng.Next(understorey.Length)]),
						new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r), rng.NextFloat() * 360f, rng.Range(0.8f, 1.3f));
					if (plant != null)
					{
						objects.Add(plant);
					}
				}
			}
			float tallest = 0f;
			foreach (GameObject o in objects)
			{
				foreach (Renderer rd in o.GetComponentsInChildren<Renderer>())
				{
					tallest = Mathf.Max(tallest, rd.bounds.max.y);
				}
			}
			// From the south, a quarter of the way up the sky, far enough to hold the whole stand.
			float distance = radius * 2.6f;
			camera.transform.position = new Vector3(0f, distance * 0.42f + tallest * 0.3f, -distance);
			camera.transform.LookAt(new Vector3(0f, tallest * 0.3f, radius * 0.1f));
			Capture(camera, target, Path.Combine(output, file + "_on.png"));
			HoldVariationOff(objects);
			Capture(camera, target, Path.Combine(output, file + "_off.png"));
			log.Add($"{file}: {placed.Count} trees ({string.Join("/", species)}) in a {radius * 2f:0} m stand{(understorey != null ? " over grass" : "")}");
			foreach (GameObject o in objects)
			{
				UnityEngine.Object.DestroyImmediate(o);
			}
		}

		/// <summary>A patch of meadow: grass, flowers and shrubs, close up.</summary>
		private static void Meadow(Camera camera, RenderTexture target, string output, string file, List<string> log)
		{
			var objects = new List<GameObject>();
			var rng = new DeterministicRNG(1234);
			string[] kinds = { "GrassLush", "GrassLush", "GrassLush", "GrassTall", "GrassTuft", "FlowersMeadow", "FlowersMeadow", "Fern", "ShrubSmall" };
			for (int i = 0; i < 260; i++)
			{
				float x = rng.Range(-7f, 7f), z = rng.Range(-4f, 9f);
				GameObject plant = Place(ProceduralArtCatalogue.DetailPrefab(kinds[rng.Next(kinds.Length)]), new Vector3(x, 0f, z), rng.NextFloat() * 360f, rng.Range(0.8f, 1.25f));
				if (plant != null)
				{
					objects.Add(plant);
				}
			}
			camera.transform.position = new Vector3(0f, 1.6f, -7.5f);
			camera.transform.LookAt(new Vector3(0f, 0.2f, 2f));
			Capture(camera, target, Path.Combine(output, file + "_on.png"));
			HoldVariationOff(objects);
			Capture(camera, target, Path.Combine(output, file + "_off.png"));
			log.Add($"{file}: {objects.Count} plants in a 14 × 13 m patch");
			foreach (GameObject o in objects)
			{
				UnityEngine.Object.DestroyImmediate(o);
			}
		}

		private static GameObject Place(string prefabName, Vector3 at, float yaw, float scale)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(prefabName));
			if (prefab == null)
			{
				return null;
			}
			var plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
			plant.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
			plant.transform.localScale = Vector3.one * scale;
			foreach (LODGroup group in plant.GetComponentsInChildren<LODGroup>())
			{
				group.ForceLOD(0);
			}
			return plant;
		}

		private static void HoldVariationOff(List<GameObject> plants)
		{
			var off = new MaterialPropertyBlock();
			off.SetFloat("_VaryLean", 0f);
			off.SetFloat("_VaryTwist", 0f);
			off.SetFloat("_VaryCrown", 0f);
			off.SetFloat("_VaryHeight", 0f);
			off.SetFloat("_VaryGirth", 0f);
			off.SetFloat("_VaryPartSwing", 0f);
			off.SetFloat("_VaryPartDroop", 0f);
			off.SetFloat("_VaryPartLength", 0f);
			off.SetVector("_VaryFullness", new Vector4(1f, 1f, 0f, 0f));
			off.SetVector("_VaryColour", Vector4.zero);
			foreach (GameObject plant in plants)
			{
				foreach (Renderer r in plant.GetComponentsInChildren<Renderer>())
				{
					r.SetPropertyBlock(off);
				}
			}
		}

		/// <summary>One species, <see cref="Instances"/> plants in a row, rendered with its variation on and off.</summary>
		private static void Sheet(Camera camera, RenderTexture target, string output, string file, string prefabName, float spacing, List<string> log)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProceduralArtCatalogue.PrefabPath(prefabName));
			if (prefab == null)
			{
				log.Add($"{file}: no prefab at {ProceduralArtCatalogue.PrefabPath(prefabName)}");
				return;
			}
			var plants = new List<GameObject>();
			for (int i = 0; i < Instances; i++)
			{
				var plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
				// Each at its own place, which is all the shader draws a plant's figures from.
				plant.transform.position = new Vector3((i - (Instances - 1) * 0.5f) * spacing, 0f, (i % 2) * spacing * 0.15f);
				plant.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);
				foreach (LODGroup group in plant.GetComponentsInChildren<LODGroup>())
				{
					group.ForceLOD(0);
				}
				plants.Add(plant);
			}
			Bounds bounds = Frame(plants);
			// Back far enough for the row to fill the frame's width, a little above its middle, looking along +z.
			float halfWidth = Mathf.Max(bounds.extents.x, bounds.extents.y * camera.aspect) * 1.1f;
			float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * camera.aspect);
			float distance = halfWidth / Mathf.Tan(horizontalFov * 0.5f) + bounds.extents.z;
			distance *= 1.12f;
			camera.transform.position = new Vector3(bounds.center.x, bounds.center.y + distance * 0.06f, bounds.center.z - distance);
			camera.transform.LookAt(bounds.center);

			Capture(camera, target, Path.Combine(output, file + "_on.png"));
			var off = new MaterialPropertyBlock();
			off.SetFloat("_VaryLean", 0f);
			off.SetFloat("_VaryTwist", 0f);
			off.SetFloat("_VaryCrown", 0f);
			off.SetFloat("_VaryHeight", 0f);
			off.SetFloat("_VaryGirth", 0f);
			off.SetFloat("_VaryPartSwing", 0f);
			off.SetFloat("_VaryPartDroop", 0f);
			off.SetFloat("_VaryPartLength", 0f);
			off.SetVector("_VaryFullness", new Vector4(1f, 1f, 0f, 0f));
			off.SetVector("_VaryColour", Vector4.zero);
			foreach (GameObject plant in plants)
			{
				foreach (Renderer r in plant.GetComponentsInChildren<Renderer>())
				{
					r.SetPropertyBlock(off);
				}
			}
			Capture(camera, target, Path.Combine(output, file + "_off.png"));
			log.Add($"{file}: {Instances} plants, row {bounds.size.x:0.0} m wide, {bounds.size.y:0.0} m tall");
			foreach (GameObject plant in plants)
			{
				UnityEngine.Object.DestroyImmediate(plant);
			}
		}

		private static Bounds Frame(List<GameObject> plants)
		{
			bool any = false;
			var bounds = new Bounds();
			foreach (GameObject plant in plants)
			{
				foreach (Renderer r in plant.GetComponentsInChildren<Renderer>())
				{
					if (any) bounds.Encapsulate(r.bounds); else bounds = r.bounds;
					any = true;
				}
			}
			return bounds;
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
			var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
			image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
			image.Apply();
			RenderTexture.active = previous;
			File.WriteAllBytes(path, image.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(image);
		}
	}
}
#endif
