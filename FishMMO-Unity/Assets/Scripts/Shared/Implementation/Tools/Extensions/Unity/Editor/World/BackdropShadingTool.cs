#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Puts a generated scene's backdrop on the scene's own texture arrays (FishMMO/Backdrop Ground) without
	/// re-cutting it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A re-cut builds a shaded backdrop by itself (<see cref="SceneBackdropBuilder.Build"/>), but it also
	/// throws away whatever was sculpted. This bakes only the backdrop's control maps — the scene's layer
	/// weights past its edge (<see cref="SceneGenerator.HorizonShading"/>) — and switches its material over in
	/// place. The meshes, the colour bake, the terrain and the scene file are left as they are.
	/// </para>
	/// <para>
	/// <b>The ground it weighs</b> is the terrain's own heights on the scene and the planet past them: what the
	/// generator built the backdrop on, less the river channels it cut into the near ground, which only shift a
	/// bank's steepness by a few degrees at a 26 m texel.
	/// </para>
	/// <para>
	/// <b>The control maps follow the scene's palette.</b> A repaint can reorder the scene's layers, so
	/// <see cref="BiomeRepaintTool"/> reshades a backdrop that is already shaded.
	/// </para>
	/// </remarks>
	public static class BackdropShadingTool
	{
		[DashboardTool(DashboardToolAttribute.WorldSceneDetails, "Shade backdrop with terrain arrays", Section = "Generated scenes", Order = 11,
			Tooltip = "Bakes control maps for the open generated scene's backdrop from its biomes and switches the backdrop to FishMMO/Backdrop Ground, so the scene's own terrain textures carry on past its edge. Meshes, colour bake, terrain and scene file are untouched.",
			Confirm = "Shade the open scene's backdrop with its terrain arrays? Its control maps are written and its material is switched over in place.")]
		public static void ShadeOpenScene()
		{
			var wrote = new List<string>();
			string problem = Shade(EditorSceneManager.GetActiveScene(), wrote);
			if (problem != null)
			{
				Debug.LogWarning($"[Backdrop shading] {problem}");
				return;
			}
			Debug.Log($"[Backdrop shading] Wrote {wrote.Count} asset(s):\n{string.Join("\n", wrote)}");
		}

		/// <summary>Shades a saved generated scene's backdrop. Returns why it did nothing, or null.</summary>
		public static string Shade(Scene scene, List<string> wrote)
		{
			if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
			{
				return "Open a saved generated scene first.";
			}
			string atlasName = LocalArtScope.AtlasSceneName(scene.path, scene.name);
			WorldAtlasScene entry = WorldAtlasScene.Find(atlasName);
			if (entry == null || entry.Body == null || !entry.Placed)
			{
				return $"'{atlasName}' has no placed atlas entry, so there is no planet to read its biomes from.";
			}
			SceneGenerationRequest request = BiomeRepaintTool.RequestFor(entry);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(entry.SizeKm);
			if (!BiomeRepaintTool.Arrange(scene, plan, out Terrain[,] terrains, out string arrangeProblem))
			{
				return arrangeProblem;
			}
			var tiles = new List<Terrain>();
			foreach (Terrain terrain in terrains)
			{
				tiles.Add(terrain);
			}
			LocalArtScope scope = LocalArtScope.For(scene, tiles);
			if (LocalArtScope.IsLocalScenePath(scene.path))
			{
				// The backdrop's assets are the committed scene's: weights from a LOCAL palette would be written over them.
				return $"'{scene.name}' is a LOCAL copy; shade the committed scene's backdrop instead.";
			}
			return Shade(scene, request, plan, terrains, scope, wrote);
		}

		/// <summary>Shades the backdrop of a scene whose request, tiles and art scope the caller already has.</summary>
		internal static string Shade(Scene scene, SceneGenerationRequest request, TerrainTilePlan plan, Terrain[,] terrains,
			LocalArtScope scope, List<string> wrote)
		{
			// A headless run has no biomes until they are loaded by hand (their loader waits on a delayCall).
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			var planet = new SceneAltitude(request);
			float halfW = plan.WidthMetres * 0.5f, halfD = plan.DepthMetres * 0.5f;
			Func<float, float, float> ground = (east, north) => Mathf.Abs(east) <= halfW && Mathf.Abs(north) <= halfD
				? SceneGenerator.GroundAltitude(terrains, plan, east, north)
				: planet.At(east, north);

			TerrainLayer[] layers = null;
			foreach (Terrain terrain in terrains)
			{
				if (terrain != null && terrain.terrainData != null)
				{
					layers = terrain.terrainData.terrainLayers;
					break;
				}
			}
			if (layers == null || layers.Length == 0)
			{
				return "the scene's terrain has no layers.";
			}

			BiomeTerrainLayers.ClearCache();
			SceneGenerator.HorizonShading(request, plan, ground, layers, scope.PaletteResolver(BiomeTerrainLayers.Resolve), out BackdropLayerWeigher weigher);
			if (weigher == null)
			{
				return "none of the biomes past the edge paints with the scene's terrain layers.";
			}
			string problem = SceneBackdropBuilder.Reshade(scene, ground, weigher, layers.Length, wrote);
			if (problem == null)
			{
				TerrainArrayBinder.BindAll(scene, bakeIfStale: false);
			}
			return problem;
		}

		/// <summary>Whether a scene's backdrop is already drawn with the arrays (and so must follow its palette).</summary>
		internal static bool IsShaded(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (SceneBackdrop backdrop in root.GetComponentsInChildren<SceneBackdrop>(true))
				{
					foreach (MeshRenderer renderer in backdrop.GetComponentsInChildren<MeshRenderer>(true))
					{
						Material material = renderer.sharedMaterial;
						if (material != null && material.shader != null && material.shader.name == SceneBackdropBuilder.ShadedShaderName)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Headless: <c>-executeMethod FishMMO.Shared.WorldDesign.BackdropShadingTool.ShadeFromCommandLine</c>, with
		/// FISHMMO_SCENE (the scene's path) and optionally FISHMMO_SNAPSHOT_DIR. Shades the backdrop, then renders it
		/// once from the scene's edge — which makes Unity compile the backdrop and terrain shaders' variants for
		/// real — and logs every message either shader has. Exits 1 on a problem or a shader error.
		/// </summary>
		public static void ShadeFromCommandLine()
		{
			int exit = 0;
			try
			{
				string path = Environment.GetEnvironmentVariable("FISHMMO_SCENE");
				Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
				var wrote = new List<string>();
				string problem = Shade(scene, wrote);
				if (problem != null)
				{
					Debug.LogError($"[Backdrop shading] {problem}");
					exit = 1;
				}
				else
				{
					Debug.Log($"[Backdrop shading] Wrote {wrote.Count} asset(s):\n{string.Join("\n", wrote)}");
				}

				Render(scene, Environment.GetEnvironmentVariable("FISHMMO_SNAPSHOT_DIR"));
				foreach (string name in new[] { SceneBackdropBuilder.ShadedShaderName, TerrainArrayBinder.ShaderName })
				{
					Shader shader = Shader.Find(name);
					if (shader == null)
					{
						Debug.LogError($"[Backdrop shading] shader '{name}' not found");
						exit = 1;
						continue;
					}
					ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
					bool error = ShaderUtil.ShaderHasError(shader);
					Debug.Log($"[Backdrop shading] shader '{name}': {(error ? "HAS ERRORS" : "ok")}, {messages.Length} message(s)");
					foreach (ShaderMessage m in messages)
					{
						Debug.Log($"[Backdrop shading]   {m.severity} {m.platform} {m.file}:{m.line} {m.message} {m.messageDetails}");
					}
					if (error)
					{
						exit = 1;
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				exit = 1;
			}
			EditorApplication.Exit(exit);
		}

		/// <summary>Two frames from 150 m above the middle of the south edge, looking out, the second saved as a PNG.</summary>
		private static void Render(Scene scene, string folder)
		{
			SceneBackdrop backdrop = null;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				backdrop = backdrop != null ? backdrop : root.GetComponentInChildren<SceneBackdrop>(true);
			}
			if (backdrop == null)
			{
				return;
			}
			TerrainArrayBinder.BindAll(scene, bakeIfStale: false);
			var host = new GameObject("Backdrop shading camera");
			SceneManager.MoveGameObjectToScene(host, scene);
			try
			{
				Camera camera = host.AddComponent<Camera>();
				camera.farClipPlane = backdrop.FarPlaneMetres;
				camera.nearClipPlane = 0.5f;
				float edge = -backdrop.SceneSizeMetres.y * 0.5f + 40f;
				float ground = 0f;
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>())
					{
						Vector3 p = terrain.transform.position, size = terrain.terrainData.size;
						if (0f >= p.x && 0f <= p.x + size.x && edge >= p.z && edge <= p.z + size.z)
						{
							ground = terrain.SampleHeight(new Vector3(0f, 0f, edge)) + p.y;
						}
					}
				}
				camera.transform.SetPositionAndRotation(new Vector3(0f, ground + 150f, edge), Quaternion.Euler(12f, 180f, 0f));
				var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
				camera.targetTexture = target;
				// The first render in a fresh headless editor can draw before the shaders are ready; judge the second.
				camera.Render();
				camera.Render();
				if (!string.IsNullOrEmpty(folder))
				{
					Directory.CreateDirectory(folder);
					RenderTexture.active = target;
					var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
					image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
					image.Apply();
					RenderTexture.active = null;
					File.WriteAllBytes(Path.Combine(folder, "backdrop-shaded.png"), image.EncodeToPNG());
					UnityEngine.Object.DestroyImmediate(image);
				}
				camera.targetTexture = null;
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
		}
	}
}
#endif
