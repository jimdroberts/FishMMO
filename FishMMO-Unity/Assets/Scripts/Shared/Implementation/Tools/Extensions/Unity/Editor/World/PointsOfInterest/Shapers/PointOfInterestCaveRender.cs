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
	/// Environment: <c>FISHMMO_CAVE_RENDER_OUT</c> (default <c>WorldRenders/caves</c> under <see cref="ProbeOutput.Root"/>);
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
		private const float HeightRange = 200f;

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

		/// <summary>
		/// The escarpment: a foot at 45 m (z under 0; well over the sea at 0, so a cave has room to descend), a face rising
		/// 60 m over 30 m, a rolling plateau behind it.
		/// </summary>
		private static float Escarpment(float x, float z)
		{
			float t = Mathf.Clamp01(z / 30f);
			float face = 45f + 60f * (t * t * (3f - 2f * t));
			float roll = 3f * Mathf.Sin(x * 0.05f) + 2f * Mathf.Sin(z * 0.031f) * Mathf.Clamp01(z / 40f);
			return face + roll * Mathf.Clamp01((z + 10f) / 30f) - 0.04f * Mathf.Max(0f, -z);
		}

		private static bool Render()
		{
			string output = ProbeOutput.FromEnvironment("FISHMMO_CAVE_RENDER_OUT", "WorldRenders", "caves");
			string rock = Env("FISHMMO_CAVE_RENDER_ROCK", "Limestone");
			Directory.CreateDirectory(output);
			var log = new List<string>();
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			Lighting();

			// The terrain, one tile in memory, its corner at the scene's south-west like a generated scene's.
			var data = new TerrainData { heightmapResolution = Resolution };
			data.size = new Vector3(TileMetres, HeightRange, TileMetres);
			var heights = new float[Resolution, Resolution];
			float step = TileMetres / (Resolution - 1);
			for (int k = 0; k < Resolution; k++)
			{
				for (int i = 0; i < Resolution; i++)
				{
					heights[k, i] = Mathf.Clamp01(Escarpment(-0.5f * TileMetres + i * step, -0.5f * TileMetres + k * step) / HeightRange);
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
			var caves = new List<(string Name, CaveSystem System)>();
			string[] wanted = Env("FISHMMO_CAVE_RENDER_CAVES", "Cave:2,Cave:1,Cave:0,LavaTube:1").Split(',');
			for (int c = 0; c < wanted.Length; c++)
			{
				string[] parts = wanted[c].Split(':');
				if (!Enum.TryParse(parts[0], true, out CaveForm form))
				{
					continue;
				}
				int size = parts.Length > 1 && int.TryParse(parts[1], out int s) ? s : 1;
				float x = -300f + 70f + c * 85f;
				CaveGround ground = CaveShaper.SampleGround(groundAt, tiles, x, -1f, 380f);
				CaveSystem system = CaveSystem.Plan(form, size, new Vector3(x, 0f, -1f), 180f, 1000 + c, c, ground, 0f, out string problem);
				string name = $"{form}_{size}";
				if (system == null)
				{
					log.Add($"{name}: refused, {problem}");
					continue;
				}
				List<Vector2Int> cells = system.HoleCells(0, groundAt, holeStep, gridX, gridZ);
				CaveShaper.CutHoles(terrains, tiles, cells, holeStep, gridX, gridZ);
				Bounds e = system.Extent;
				CaveGround meshGround = CaveShaper.SampleGround(groundAt, tiles, system.Origin.x + e.min.x - 4f, system.Origin.z + e.min.z - 4f, system.Origin.x + e.max.x + 4f, system.Origin.z + e.max.z + 4f);
				var clock = System.Diagnostics.Stopwatch.StartNew();
				List<CaveMesher.Piece> pieces = CaveMesher.Build(system, meshGround, out string report);
				Mesh[] meshes = CaveShaper.WriteMeshes(null, c, pieces, null);
				var root = new GameObject(name).transform;
				CaveShaper.Build(root, system, pieces, meshes, material);
				// In the game the rock takes the ground where it comes up through the hillside (CaveVisibility's cave mode of
				// the shader's ground contact); this render has no ground map, so it shows the same by colour.
				if (Env("FISHMMO_CAVE_RENDER_CONTACT", "1") == "1")
				{
					ShowContact(root, system.Origin, groundAt);
				}
				caves.Add((name, system));
				Physics.SyncTransforms();
				int gaps = Gaps(system, meshGround, cells, groundAt, holeStep, gridX, gridZ, out string worst);
				log.Add($"{name}: gap probe over {cells.Count} cut cells: {gaps} gap point(s){worst}");
				log.Add($"{name}: radius {system.Radius:0.0} m, {system.PassageLength:0} m of passages, {system.Depth:0} m deep, {system.Chambers.Count} chambers, {cells.Count} holes, " +
					$"{report}, {clock.ElapsedMilliseconds} ms");
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
			foreach ((string name, CaveSystem system) in caves)
			{
				Vector3 o = system.Origin;
				float r = system.Radius;
				List<CaveNode> trunk = system.Passages[0].Nodes;
				Shot(name + "_outside", o + new Vector3(0.6f * r + 3f, 1.4f * r + 3f, -(4f * r + 12f)), o + new Vector3(0f, 0.8f * r, 4f), false);
				Shot(name + "_mouth", o + new Vector3(0f, 1.7f, -0.5f * r), o + trunk[Mathf.Min(4, trunk.Count - 1)].Centre, true);
				if (system.TryChamberOf(system.Mouths[0].SiteId, out CaveChamber chamber))
				{
					Vector3 room = o + new Vector3(chamber.Centre.x, chamber.Floor + 1.7f, chamber.Centre.z);
					Shot(name + "_chamber", room, o + trunk[Mathf.Max(0, trunk.Count - 5)].Centre, true);
				}
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

		/// <summary>
		/// Checks the edge of the terrain cut for a cave's mouth: every 0.25 m along each side of a cut cell that borders an
		/// uncut one, the drawn rock must stand at the ground there (or above it), or the terrain's cut edge shows with nothing
		/// under it — a gap. Inside the cut the rock may fall away into the opening; at its edge it may not.
		/// </summary>
		private static int Gaps(CaveSystem system, CaveGround ground, List<Vector2Int> cells, Func<float, float, float> groundAt, float step, float gridX, float gridZ, out string worst)
		{
			var holed = new HashSet<long>();
			foreach (Vector2Int c in cells)
			{
				holed.Add(((long)c.x << 32) ^ (uint)c.y);
			}
			int samples = 0, gaps = 0;
			float deepest = 0f;
			Vector3 at = Vector3.zero;
			foreach (Vector2Int c in cells)
			{
				for (int side = 0; side < 4; side++)
				{
					int nx = c.x + (side == 0 ? 1 : side == 1 ? -1 : 0), nz = c.y + (side == 2 ? 1 : side == 3 ? -1 : 0);
					if (holed.Contains(((long)nx << 32) ^ (uint)nz))
					{
						continue;
					}
					for (float f = 0.125f; f < 1f; f += 0.25f)
					{
						// On the shared edge, a hair inside the cut cell.
						float ex = side == 0 ? c.x + 0.98f : side == 1 ? c.x + 0.02f : c.x + f;
						float ez = side == 2 ? c.y + 0.98f : side == 3 ? c.y + 0.02f : c.y + f;
						float x = gridX + ex * step, z = gridZ + ez * step, g = groundAt(x, z);
						samples++;
						// Just over where the ground was: inside the rock (an outcrop over it), it is covered; in the open, a ray
						// down must meet rock at the ground.
						if (system.Field(new Vector3(x, g + 0.4f, z) - system.Origin, ground) < 0f)
						{
							continue;
						}
						bool hit = Physics.Raycast(new Vector3(x, g + 0.4f, z), Vector3.down, out RaycastHit h, 40f);
						float drop = hit ? g - h.point.y : 40f;
						if (drop > 0.15f && InsideDrawnRock(new Vector3(x, g + 0.4f, z)))
						{
							// The drawn surface stands a little off the field's (the mesher's cell): the start was inside rock.
							continue;
						}
						if (drop > 0.15f)
						{
							gaps++;
							if (drop > deepest)
							{
								deepest = drop;
								at = new Vector3(x, g, z);
							}
						}
					}
				}
			}
			// The control: the same rays down the cut cells' middles must fall through somewhere (the opening), or the probe
			// cannot see a gap at all.
			int through = 0;
			foreach (Vector2Int c in cells)
			{
				float x = gridX + (c.x + 0.5f) * step, z = gridZ + (c.y + 0.5f) * step, g = groundAt(x, z);
				if (system.Field(new Vector3(x, g + 0.4f, z) - system.Origin, ground) >= 0f
					&& (!Physics.Raycast(new Vector3(x, g + 0.4f, z), Vector3.down, out RaycastHit h, 40f) || g - h.point.y > 0.8f))
				{
					through++;
				}
			}
			worst = $" over {samples} edge samples (control: {through} of {cells.Count} cut middles fall through to the opening){(gaps > 0 ? $"; deepest {deepest:0.00} m under the ground at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0})" : string.Empty)}";
			return gaps;
		}

		/// <summary>
		/// Colours a cave's drawn rock as the game's cave-mode ground contact shows it: the ground's green where the rock is
		/// within the contact band of the hillside's surface (solid under 0.175 m, fading out by 0.5 m), its rock grey where it
		/// stands proud of it or lies more than 0.3 m under it (its walls and roof).
		/// </summary>
		private static void ShowContact(Transform root, Vector3 origin, Func<float, float, float> groundAt)
		{
			Shader shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
			if (shader == null)
			{
				return;
			}
			var preview = new Material(shader);
			preview.SetColor("_BaseColor", Color.white);
			var rock = new Color(0.55f, 0.53f, 0.5f);
			var turf = new Color(0.36f, 0.43f, 0.24f);
			foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
			{
				var renderer = filter.GetComponent<MeshRenderer>();
				if (filter.sharedMesh == null || renderer == null)
				{
					continue;
				}
				Mesh mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
				Vector3[] vertices = mesh.vertices;
				var colours = new Color[vertices.Length];
				Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
				for (int i = 0; i < vertices.Length; i++)
				{
					Vector3 v = toWorld.MultiplyPoint3x4(vertices[i]);
					float raw = v.y - groundAt(v.x, v.z);
					float weight = 0f;
					if (raw >= -0.3f)
					{
						float height = Mathf.Max(0f, raw);
						float c = Mathf.Clamp01(1f - height / 0.5f);
						c = c * c * (3f - 2f * c);
						float f = Mathf.Clamp01(1f - height / 0.175f);
						f = f * f * (3f - 2f * f);
						weight = Mathf.Lerp(c * 0.75f, 1f, f);
					}
					colours[i] = Color.Lerp(rock, turf, weight);
				}
				mesh.colors = colours;
				filter.sharedMesh = mesh;
				renderer.sharedMaterial = preview;
			}
		}

		/// <summary>Whether a point is inside the drawn rock: a ray up from it meets the underside of a surface.</summary>
		private static bool InsideDrawnRock(Vector3 at)
		{
			bool backfaces = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				return Physics.Raycast(at, Vector3.up, out RaycastHit h, 60f) && Vector3.Dot(h.normal, Vector3.up) > 0.05f;
			}
			finally
			{
				Physics.queriesHitBackfaces = backfaces;
			}
		}

		private static readonly System.Collections.Generic.Dictionary<StructureMaterial, Material> kitMaterials = new System.Collections.Generic.Dictionary<StructureMaterial, Material>();

		/// <summary>A structure kit piece built in memory (level 0) with flat stand-in colours per material, for a render.</summary>
		private static void AddKitPiece(string id, int variant, Vector3 position, Quaternion rotation, Vector3 scale, Transform parent)
		{
			StructurePiece piece = StructureKit.Get(id);
			if (piece == null)
			{
				return;
			}
			StructureMesh built = StructurePieces.Build(piece, variant, 0, ProceduralArtCatalogue.DefaultSeed);
			var go = new GameObject(id);
			go.transform.SetParent(parent, true);
			go.transform.SetPositionAndRotation(position, rotation);
			go.transform.localScale = scale;
			go.AddComponent<MeshFilter>().sharedMesh = built.Mesh.ToMesh(id);
			var materials = new Material[built.Materials.Length];
			for (int i = 0; i < materials.Length; i++)
			{
				StructureMaterial kind = built.Materials[i];
				if (!kitMaterials.TryGetValue(kind, out Material m) || m == null)
				{
					m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
					Color colour = kind == StructureMaterial.Earth ? new Color(0.24f, 0.2f, 0.12f)
						: kind == StructureMaterial.Timber ? new Color(0.33f, 0.24f, 0.15f)
						: new Color(0.45f, 0.45f, 0.42f);
					m.SetColor("_BaseColor", colour);
					kitMaterials[kind] = m;
				}
				materials[i] = m;
			}
			go.AddComponent<MeshRenderer>().sharedMaterials = materials;
		}

		private static void Lighting()
		{
			var sun = new GameObject("Sun").AddComponent<Light>();
			sun.type = LightType.Directional;
			sun.intensity = 1.6f;
			sun.color = new Color(1f, 0.95f, 0.88f);
			sun.shadows = LightShadows.Soft;
			// Over the shoulder of the shots, onto the escarpment's face (it faces −z).
			sun.transform.rotation = Quaternion.Euler(38f, 25f, 0f);
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
