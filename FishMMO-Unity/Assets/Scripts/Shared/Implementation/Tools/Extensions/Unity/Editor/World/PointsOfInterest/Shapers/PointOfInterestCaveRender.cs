#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.NameGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Renders the terrain shapers on a synthetic escarpment built in memory: caves (outside, at the mouth looking in, and
	/// from the chamber looking out), a slab shelter, the leaning section and an arch, through URP with the generated rock
	/// materials. For judging them without cutting a scene.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>-executeMethod FishMMO.Shared.WorldDesign.PointOfInterestCaveRender.Run</c> in a non-batch editor under xvfb
	/// (graphics needed). Nothing is saved: a fresh untitled scene, a terrain in memory. Exits 0 when every shot was
	/// written, 1 otherwise.
	/// </para>
	/// <para>
	/// Environment: <c>FISHMMO_CAVE_RENDER_OUT</c> (default /tmp/claude-1000/cave-render);
	/// <c>FISHMMO_CAVE_RENDER_CAVES</c> the caves as <c>Form:size</c> pairs (default <c>Cave:2,Cave:0,LavaTube:1,Grotto:1</c>);
	/// <c>FISHMMO_CAVE_RENDER_ROCK</c> the rock type (default Limestone). The overhang section and arch need the cliff
	/// section art generated (Generate Biome Art); without it they are left out and said so in <c>index.txt</c>.
	/// </para>
	/// </remarks>
	public static class PointOfInterestCaveRender
	{
		private const int Width = 1280, Height = 800;
		private const float TileMetres = 800f;
		private const int Resolution = 513;

		public static void Run()
		{
			int code = 1;
			bool async = ShaderUtil.allowAsyncCompilation;
			try
			{
				ShaderUtil.allowAsyncCompilation = false;
				code = Render() ? 0 : 1;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				ShaderUtil.allowAsyncCompilation = async;
			}
			EditorApplication.Exit(code);
		}

		private static string Env(string name, string fallback)
		{
			string v = Environment.GetEnvironmentVariable(name);
			return string.IsNullOrEmpty(v) ? fallback : v;
		}

		/// <summary>The escarpment: a foot at 10 m (z under 0), a face rising 60 m over 30 m, a rolling plateau behind it.</summary>
		private static float Escarpment(float x, float z)
		{
			float t = Mathf.Clamp01(z / 30f);
			float face = 10f + 60f * (t * t * (3f - 2f * t));
			float roll = 3f * Mathf.Sin(x * 0.05f) + 2f * Mathf.Sin(z * 0.031f) * Mathf.Clamp01(z / 40f);
			return face + roll * Mathf.Clamp01((z + 10f) / 30f) - 0.04f * Mathf.Max(0f, -z);
		}

		private static bool Render()
		{
			string output = Env("FISHMMO_CAVE_RENDER_OUT", "/tmp/claude-1000/cave-render");
			string rock = Env("FISHMMO_CAVE_RENDER_ROCK", "Limestone");
			Directory.CreateDirectory(output);
			var log = new List<string>();
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			Lighting();

			// The terrain, one tile in memory, its corner at the scene's south-west like a generated scene's.
			var data = new TerrainData { heightmapResolution = Resolution };
			data.size = new Vector3(TileMetres, 120f, TileMetres);
			var heights = new float[Resolution, Resolution];
			float step = TileMetres / (Resolution - 1);
			for (int k = 0; k < Resolution; k++)
			{
				for (int i = 0; i < Resolution; i++)
				{
					heights[k, i] = Mathf.Clamp01(Escarpment(-0.5f * TileMetres + i * step, -0.5f * TileMetres + k * step) / 120f);
				}
			}
			data.SetHeights(0, 0, heights);
			var layer = new TerrainLayer { diffuseTexture = Flat(new Color(0.34f, 0.4f, 0.25f)), tileSize = new Vector2(4f, 4f) };
			data.terrainLayers = new[] { layer };
			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			terrainObject.transform.position = new Vector3(-0.5f * TileMetres, 0f, -0.5f * TileMetres);
			Terrain terrain = terrainObject.GetComponent<Terrain>();
			Shader terrainShader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
			if (terrainShader != null)
			{
				terrain.materialTemplate = new Material(terrainShader);
			}
			var terrains = new Terrain[1, 1];
			terrains[0, 0] = terrain;
			var tiles = new TerrainTilePlan { CountX = 1, CountZ = 1, TileMetres = TileMetres, Resolution = Resolution };
			Func<float, float, float> groundAt = (east, north) => terrain.SampleHeight(new Vector3(east, 0f, north)) + terrain.transform.position.y;
			CaveShaper.HoleGrid(terrains, tiles, out float holeStep, out float gridX, out float gridZ);

			Material material = PointOfInterestRock.MaterialOf(rock);
			if (material == null)
			{
				log.Add($"No generated material for {rock}: a plain grey stands in.");
				material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
				material.SetColor("_BaseColor", new Color(0.5f, 0.48f, 0.45f));
			}

			// ── The caves, side by side along the face ──
			var caves = new List<(string Name, CaveSolid Solid)>();
			string[] wanted = Env("FISHMMO_CAVE_RENDER_CAVES", "Cave:2,Cave:0,LavaTube:1,Grotto:1").Split(',');
			for (int c = 0; c < wanted.Length; c++)
			{
				string[] parts = wanted[c].Split(':');
				if (!Enum.TryParse(parts[0], true, out CaveForm form))
				{
					continue;
				}
				int size = parts.Length > 1 && int.TryParse(parts[1], out int s) ? s : 1;
				float x = -300f + 70f + c * 85f;
				CaveGround ground = CaveShaper.SampleGround(groundAt, tiles, x, -1f, 150f);
				CaveSolid solid = CaveSolid.Plan(form, size, new Vector3(x, 0f, -1f), 180f, 1000 + c, ground, 0f, out string problem);
				string name = $"{form}_{size}";
				if (solid == null)
				{
					log.Add($"{name}: refused, {problem}");
					continue;
				}
				List<Vector2Int> cells = solid.HoleCells(groundAt, holeStep, gridX, gridZ);
				CaveShaper.CutHoles(terrains, tiles, cells, holeStep, gridX, gridZ);
				Bounds e = solid.Extent;
				CaveGround meshGround = CaveShaper.SampleGround(groundAt, tiles, solid.Origin.x + e.min.x - 4f, solid.Origin.z + e.min.z - 4f, solid.Origin.x + e.max.x + 4f, solid.Origin.z + e.max.z + 4f);
				var clock = System.Diagnostics.Stopwatch.StartNew();
				MeshBuilder[] levels = solid.BuildMeshes(meshGround, out string report);
				Mesh[] meshes = CaveShaper.WriteMeshes(null, c, levels, null);
				var root = new GameObject(name).transform;
				CaveShaper.Build(root, solid, meshes, material);
				caves.Add((name, solid));
				log.Add($"{name}: radius {solid.Radius:0.0} m, {solid.Length:0} m long, chamber {solid.ChamberRadii.x:0.0} x {solid.ChamberRadii.y:0.0} m, {cells.Count} holes, " +
					$"{levels[0].TriangleCount}/{levels[1].TriangleCount}/{levels[2].TriangleCount} triangles, {report}, {clock.ElapsedMilliseconds} ms");
			}

			// ── A slab shelter, the leaning section, an arch ──
			float sx = -300f + 70f + wanted.Length * 85f;
			var builds = new List<(string Name, Vector3 At)>();
			foreach ((POIType kind, int size, string name) in new[] { (POIType.Overhang, 1, "Shelter_slab"), (POIType.Overhang, 2, "Shelter_section"), (POIType.NaturalArch, 1, "Arch") })
			{
				var foot = new Vector3(sx, 0f, -1f);
				sx += 60f;
				if (!OverhangShaper.PlanSite(kind, size, foot, 180f, 77, groundAt, out OverhangShaper.Plan plan, out string problem))
				{
					log.Add($"{name}: refused, {problem}");
					continue;
				}
				if (plan.Build == OverhangBuild.Slab)
				{
					OverhangPlacer.CarveAlcove(terrains, tiles, plan.Slab);
					var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RiverBoulders.PrefabFolder}/{OverhangPlacer.SlabPrefab(rock)}.prefab");
					if (prefab == null)
					{
						log.Add($"{name}: the slab art {OverhangPlacer.SlabPrefab(rock)} is missing");
						continue;
					}
					OverhangPlacer.Stretch(OverhangPlacer.ArtBounds(prefab), plan.Slab.Middle, plan.Slab.Rotation, plan.Slab.Size, out Vector3 position, out Vector3 scale);
					var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
					go.transform.SetPositionAndRotation(position, plan.Slab.Rotation);
					go.transform.localScale = scale;
					log.Add($"{name}: slab {plan.Slab.Size.x:0.0} x {plan.Slab.Size.y:0.0} x {plan.Slab.Size.z:0.0} m");
				}
				else
				{
					string style = OverhangShaper.StyleOf(plan.Build);
					var meshes = new Mesh[CliffSections.LevelCount];
					bool missing = false;
					for (int lod = 0; lod < meshes.Length; lod++)
					{
						meshes[lod] = AssetDatabase.LoadAssetAtPath<Mesh>(ProceduralArtCatalogue.MeshPath(CliffSections.MeshName(style, plan.Variant, lod)));
						missing |= meshes[lod] == null;
					}
					if (missing)
					{
						log.Add($"{name}: the {style} section art is missing (run Generate Biome Art)");
						continue;
					}
					var go = new GameObject(name);
					go.AddComponent<MeshFilter>().sharedMesh = meshes[0];
					go.AddComponent<MeshRenderer>().sharedMaterial = material;
					go.transform.SetPositionAndRotation(plan.Position, Quaternion.Euler(0f, plan.Yaw, 0f));
					go.transform.localScale = Vector3.one * plan.Scale;
					log.Add($"{name}: {style} variant {plan.Variant} at scale {plan.Scale:0.00}");
				}
				builds.Add((name, foot));
			}
			terrain.Flush();

			// ── Shots ──
			var camera = new GameObject("Camera").AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = new Color(0.55f, 0.66f, 0.8f);
			camera.fieldOfView = 55f;
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane = 2000f;
			var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
			camera.targetTexture = target;
			var lamp = new GameObject("Lamp").AddComponent<Light>();
			lamp.type = LightType.Point;
			lamp.range = 45f;
			lamp.intensity = 6f;
			lamp.color = new Color(1f, 0.9f, 0.78f);
			lamp.transform.SetParent(camera.transform, false);
			for (int warm = 0; warm < 3; warm++)
			{
				camera.Render();
			}

			int shots = 0, written = 0;
			void Shot(string file, Vector3 eye, Vector3 at, bool inside)
			{
				shots++;
				lamp.enabled = inside;
				camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye, Vector3.up));
				camera.Render();
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = target;
				var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
				image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
				image.Apply();
				RenderTexture.active = previous;
				File.WriteAllBytes(Path.Combine(output, file + ".png"), image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
				written++;
			}

			Shot("overview", new Vector3(-20f, 70f, -170f), new Vector3(-20f, 25f, 10f), false);
			foreach ((string name, CaveSolid solid) in caves)
			{
				Vector3 o = solid.Origin;
				float r = solid.Radius;
				Shot(name + "_outside", o + new Vector3(0.6f * r + 3f, 1.4f * r + 3f, -(4f * r + 12f)), o + new Vector3(0f, 0.8f * r, 4f), false);
				Shot(name + "_mouth", o + new Vector3(0f, 1.7f, -0.5f * r), o + solid.Nodes[Mathf.Min(4, solid.Nodes.Count - 1)].Centre, true);
				Vector3 room = solid.ChamberFloorWorld + Vector3.up * 1.7f;
				Shot(name + "_chamber", room, o + solid.Nodes[Mathf.Max(0, solid.Nodes.Count - 5)].Centre, true);
			}
			foreach ((string name, Vector3 foot) in builds)
			{
				float y = groundAt(foot.x, foot.z);
				Shot(name, new Vector3(foot.x + 14f, y + 6f, foot.z - 26f), new Vector3(foot.x, y + 4f, foot.z + 3f), false);
				Shot(name + "_under", new Vector3(foot.x - 4f, y + 1.7f, foot.z - 9f), new Vector3(foot.x + 2f, y + 3.5f, foot.z + 4f), false);
			}
			log.Add($"{written} of {shots} shots written to {output}");
			File.WriteAllLines(Path.Combine(output, "index.txt"), log);
			Debug.Log("[Cave render] " + string.Join("\n", log));
			return written == shots && caves.Count > 0;
		}

		private static void Lighting()
		{
			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.6f;
			sun.color = new Color(1f, 0.95f, 0.88f);
			sun.shadows = LightShadows.Soft;
			sun.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.5f, 0.56f, 0.66f);
			RenderSettings.ambientEquatorColor = new Color(0.38f, 0.38f, 0.37f);
			RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.15f);
			RenderSettings.fog = false;
			DynamicGI.UpdateEnvironment();
		}

		private static Texture2D Flat(Color colour)
		{
			var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
			var pixels = new Color[16];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = colour;
			}
			texture.SetPixels(pixels);
			texture.Apply();
			return texture;
		}
	}
}
#endif
