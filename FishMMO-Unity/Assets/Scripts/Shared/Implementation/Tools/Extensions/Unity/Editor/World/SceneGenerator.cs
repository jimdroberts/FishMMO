#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Water;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a generation attempt did, or why it did nothing.</summary>
	public sealed class SceneGenerationResult
	{
		public bool Success;
		/// <summary>Why nothing was generated. Null on success.</summary>
		public string Problem;
		public string ScenePath;
		public WorldAtlasScene Entry;
		/// <summary>Every asset path written, so a person can see exactly what appeared.</summary>
		public readonly List<string> Wrote = new List<string>();
		public TerrainTilePlan Plan;
		/// <summary>Lowest and highest ground in the finished scene, in metres above its own floor.</summary>
		public float ReliefMetres;
		/// <summary>The scene's altitude above the body's sea level, in metres.</summary>
		public float BaseAltitudeMetres;
		/// <summary>
		/// Metres above the body's sea level of the scene's lowest ground — what its y = 0 is.
		/// </summary>
		/// <remarks>
		/// A scene is built around its own origin, so this is the one place the metres between the
		/// planet's water line and the ground underfoot are written down. A scene cut from a
		/// plateau three kilometres up and one cut from a beach are otherwise indistinguishable
		/// once the terrain exists.
		/// </remarks>
		public float GroundAltitudeMetres;
		/// <summary>True when the scene reaches the body's water line and was given a sea.</summary>
		public bool HasWater;
		/// <summary>Scene-space Y of the sea's surface, when there is one.</summary>
		public float SeaLevelY;

		public static SceneGenerationResult Failed(string problem) => new SceneGenerationResult { Problem = problem };
	}

	/// <summary>
	/// Cuts a rectangle out of a planet and writes it as a world scene that is ready to play.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Everything it writes is new.</b> The scene, its terrain data and its atlas entry all land
	/// in a folder that must not already exist, and the run stops if it does. Nothing existing is
	/// opened, saved or edited — which matters because a Unity scene save rewrites far more of a
	/// file than the change itself, and a generator is exactly the tool somebody runs twice by
	/// accident.
	/// </para>
	/// <para>
	/// <b>It places what the systems need and nothing else:</b> the terrain, the boundary the scene
	/// cannot be read without, and the components that drive weather, clouds, climate and the sky.
	/// Spawn points, teleporters and the rest are decisions about a place, and a generator that
	/// guesses them leaves work to undo rather than work already done.
	/// </para>
	/// <para>
	/// <b>The ground comes from the globe.</b> Every height is <see cref="PlanetSurface"/> asked
	/// through <see cref="SceneGeneration.AltitudeMetres"/>, so the coastline on the world map is
	/// the coastline underfoot. Terrain data is written as assets and is <em>source</em> from that
	/// moment: the generator seeds it and a designer sculpts it afterwards, so it is committed,
	/// unlike the surface textures which are pure build output.
	/// </para>
	/// </remarks>
	public static class SceneGenerator
	{
		/// <summary>
		/// Extra work to do on a generated scene, contributed by assemblies this one cannot see.
		/// </summary>
		/// <remarks>
		/// The camera and the world-sim controller live in the test harness, which references this
		/// assembly and not the other way round — and must, since it is compiled out of a server
		/// build entirely. So the harness registers itself here, the same way the client registers
		/// its renderer checks with the world-systems audit. A server-subtarget editor simply has
		/// no dressing to run.
		/// </remarks>
		public static readonly List<Action<Scene, SceneGenerationRequest>> Dress =
			new List<Action<Scene, SceneGenerationRequest>>();

		/// <summary>
		/// The terrain material generated ground is drawn with.
		/// </summary>
		/// <remarks>
		/// A <see cref="Terrain"/> created from script gets Unity's built-in terrain material,
		/// which the Universal pipeline cannot render — the ground comes out magenta, the colour
		/// URP uses for a shader it has no pass for. It is not a missing texture and no amount of
		/// terrain layers fixes it. This is the same weather-capable material the world sim bed
		/// uses, so generated ground also takes snow, wetness and cloud shadow.
		/// </remarks>
		public const string TerrainMaterialPath = "Assets/Prefabs/Client/Materials/Ground/Weather Terrain.mat";

		/// <summary>Where a world's scenes live.</summary>
		public static string WorldFolder(WorldBody body)
		{
			string world = body != null ? WorldEditorAssets.Sanitize(body.ResolvedName) : "Unplaced";
			return $"{Constants.Configuration.WorldScenePath.Replace('\\', '/').TrimEnd('/')}/{world}";
		}

		/// <summary>Where one scene's terrain data lives, beside its scene.</summary>
		public static string TerrainFolder(WorldBody body, string sceneName)
			=> $"{WorldFolder(body)}/{WorldEditorAssets.Sanitize(sceneName)} Terrain";

		/// <summary>Every world scene name already in the project, at any depth.</summary>
		public static List<string> ExistingSceneNames()
		{
			var names = new List<string>();
			foreach (string path in WorldEditorAssets.WorldScenePaths())
			{
				names.Add(Path.GetFileNameWithoutExtension(path));
			}
			return names;
		}

		/// <summary>
		/// Generates the scene. Validates first and writes nothing if anything is wrong.
		/// </summary>
		public static SceneGenerationResult Generate(SceneGenerationRequest request)
		{
			if (request == null)
			{
				return SceneGenerationResult.Failed("Nothing to generate.");
			}
			if (request.Body == null)
			{
				return SceneGenerationResult.Failed("A scene has to stand on a celestial body. Choose one on the atlas first.");
			}

			string nameProblem = SceneGeneration.NameProblem(request.SceneName, ExistingSceneNames());
			if (nameProblem != null)
			{
				return SceneGenerationResult.Failed(nameProblem);
			}

			string scenePath = $"{WorldFolder(request.Body)}/{request.SceneName}.unity";
			if (File.Exists(scenePath))
			{
				return SceneGenerationResult.Failed($"'{scenePath}' already exists.");
			}
			string terrainFolder = TerrainFolder(request.Body, request.SceneName);
			if (AssetDatabase.IsValidFolder(terrainFolder))
			{
				return SceneGenerationResult.Failed($"'{terrainFolder}' already exists. Move or delete it first — this never writes over anything.");
			}

			var result = new SceneGenerationResult { Plan = SceneGeneration.PlanTiles(request.SizeKm) };
			TerrainTilePlan plan = result.Plan;

			/* Bounded before anything is created, because every tile has to share one height range
			 * and one floor. Tiles normalised against their own range are what makes a stitched
			 * landmass step at its seams, and the climate read differently on either side. A BOUND
			 * and not a measurement: a range that merely sampled the ground is a range some peak
			 * between the samples falls outside, and the heightmap flattens whatever falls outside
			 * it. Tighten() gives the slack back once the real ground is on disk. */
			SceneGeneration.Bounds(request, plan, out float lowest, out float highest);
			result.BaseAltitudeMetres = SceneGeneration.AltitudeMetres(request, 0f, 0f);
			float relief = Mathf.Max(SceneGeneration.MinimumTerrainHeightMetres, highest - lowest);

			WorldEditorAssets.EnsureFolder(WorldFolder(request.Body));
			WorldEditorAssets.EnsureFolder(terrainFolder);

			/* Unity refuses to add a scene additively while an UNTITLED scene is open, which is
			 * exactly what a freshly launched editor has — and what a batch editor always has. So
			 * the untitled case makes the new scene the only one instead; there is nothing open
			 * worth returning to, by definition. Modified scenes are offered for saving first so
			 * that path can never discard somebody's work. */
			if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return SceneGenerationResult.Failed("Cancelled while saving open scenes; nothing was generated.");
			}

			bool untitled = string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path);
			NewSceneMode mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
			try
			{
				var terrains = new Terrain[plan.CountX, plan.CountZ];
				for (int tz = 0; tz < plan.CountZ; tz++)
				{
					for (int tx = 0; tx < plan.CountX; tx++)
					{
						terrains[tx, tz] = CreateTile(request, plan, tx, tz, lowest, relief, scene, terrainFolder, result);
					}
				}
				Stitch(terrains, plan);

				/* Now that every height is written, the tiles can be shrunk onto the ground they
				 * actually hold — and the colour bands painted against a height that means
				 * something. Painting before this read every scene as lowland, because the bound
				 * has to allow for a peak that this particular scene does not have. */
				relief = Tighten(terrains, ref lowest, relief);
				result.ReliefMetres = relief;
				result.GroundAltitudeMetres = lowest;
				foreach (Terrain terrain in terrains)
				{
					if (terrain != null && terrain.terrainData != null)
					{
						Paint(terrain.terrainData, relief);
					}
				}

				AddBoundary(scene, plan, relief);
				AddWater(scene, plan, request, lowest, relief, result);

				// The same components the audit adds to a scene somebody forgot to finish.
				bool wantsSky = request.Layer == null || !request.Layer.Underground;
				GameObject host = SceneWorldSystems.Configure(scene, wantsSky);
				host.transform.position = Vector3.zero;

				/* The camera and the sim controller, if the harness is in this project. A failure
				 * here must not lose the scene: the terrain is minutes of work and the dressing is
				 * seconds, so it is reported and the scene kept. */
				foreach (Action<Scene, SceneGenerationRequest> dress in Dress)
				{
					try
					{
						dress?.Invoke(scene, request);
					}
					catch (Exception ex)
					{
						Debug.LogError($"[Scene generator] Dressing '{request.SceneName}' threw, the scene is still fine: {ex}");
					}
				}

				if (!EditorSceneManager.SaveScene(scene, scenePath))
				{
					return SceneGenerationResult.Failed($"'{scenePath}' could not be saved.");
				}
				result.Wrote.Add(scenePath);
			}
			finally
			{
				// Only ours to close when it was added alongside something else; closing the only
				// open scene leaves the editor with nothing.
				if (mode == NewSceneMode.Additive)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
			}

			result.Entry = EnsureEntry(request, result);

			/* The body needs a climate, or every scene on it silently runs the built-in defaults
			 * and loses the authored climate variants and the default weather profile with them.
			 * The same rule the audit applies: only when the project has exactly one authored
			 * climate, because with several, which to use is somebody's decision. */
			EnsureBodyClimate(request.Body);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			WorldAtlasScene.EditorLookup.Invalidate();

			result.ScenePath = scenePath;
			result.Success = true;
			return result;
		}

		/// <summary>Creates one terrain tile and its data asset.</summary>
		private static Terrain CreateTile(SceneGenerationRequest request, TerrainTilePlan plan, int tx, int tz,
			float lowest, float relief, Scene scene, string terrainFolder, SceneGenerationResult result)
		{
			var data = new TerrainData
			{
				heightmapResolution = plan.Resolution,
				size = new Vector3(plan.TileMetres, relief, plan.TileMetres),
			};

			int resolution = data.heightmapResolution;
			var heights = new float[resolution, resolution];
			float halfWidth = plan.WidthMetres * 0.5f;
			float halfDepth = plan.DepthMetres * 0.5f;
			float step = plan.TileMetres / (resolution - 1);

			for (int z = 0; z < resolution; z++)
			{
				// Scene coordinates, not tile coordinates. Sampling each tile in its own frame is
				// what would put a cliff along every seam.
				float north = tz * plan.TileMetres + z * step - halfDepth;
				for (int x = 0; x < resolution; x++)
				{
					float east = tx * plan.TileMetres + x * step - halfWidth;
					// Unity indexes its heightmap [z, x], and stores a fraction of the terrain's height.
					heights[z, x] = Mathf.Clamp01((SceneGeneration.AltitudeMetres(request, east, north) - lowest) / relief);
				}
			}
			data.SetHeights(0, 0, heights);

			string dataPath = $"{terrainFolder}/{WorldEditorAssets.Sanitize(request.SceneName)} {tx}_{tz}.asset";
			AssetDatabase.CreateAsset(data, dataPath);
			result.Wrote.Add(dataPath);

			var host = new GameObject($"Terrain {tx}_{tz}");
			SceneManager.MoveGameObjectToScene(host, scene);
			// Every tile shares one floor, so the landmass is one slope rather than a set of terraces.
			host.transform.position = new Vector3(tx * plan.TileMetres - halfWidth, 0f, tz * plan.TileMetres - halfDepth);

			Terrain terrain = host.AddComponent<Terrain>();
			terrain.terrainData = data;

			Material material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
			if (material != null)
			{
				terrain.materialTemplate = material;
			}
			else
			{
				Debug.LogWarning($"[Scene generator] '{TerrainMaterialPath}' is missing, so this terrain will render magenta under URP. " +
					"Run Weather → Weather Tools → Weather-proof terrain to create it, then re-generate or assign it by hand.");
			}

			host.AddComponent<TerrainCollider>().terrainData = data;
			terrain.allowAutoConnect = true;
			return terrain;
		}

		/// <summary>The ocean material every generated scene shares.</summary>
		public const string WaterMaterialPath = "Assets/Plugins/FishMMO Water/Materials/OceanWater.mat";

		/// <summary>
		/// Puts the sea in the scene, at the height the planet says its sea level is.
		/// </summary>
		/// <param name="groundAltitudeMetres">
		/// Metres above the body's sea level of the scene's lowest ground — the altitude that the
		/// terrain's y = 0 stands for.
		/// </param>
		/// <remarks>
		/// <para>
		/// <b>The Y is the whole point.</b> A scene is built around its own origin, with its lowest
		/// ground at zero, so the planet's water line lands at <c>-groundAltitudeMetres</c> and
		/// nowhere else. Put the sea at a round number instead and the coastline in the scene stops
		/// being the coastline on the globe — the map shows a bay and the ground has none, or the
		/// whole scene drowns. One unit is one metre, so this is a subtraction and not a
		/// conversion.
		/// </para>
		/// <para>
		/// <b>Not every scene gets one.</b> A world with no liquid water has no sea to put in, and
		/// a scene cut entirely from high ground never reaches the water line — a sea plane under
		/// its floor would be invisible from every point in it while still costing a full-screen
		/// transparent pass. Measured on an Earth-like world, about 70% of randomly cut scenes DO
		/// reach it, because that is how much of such a world is ocean.
		/// </para>
		/// </remarks>
		private static void AddWater(Scene scene, TerrainTilePlan plan, SceneGenerationRequest request,
			float groundAltitudeMetres, float relief, SceneGenerationResult result)
		{
			if (request.Layer != null && request.Layer.Underground)
			{
				return;
			}
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			if (!BiomeWorldConditions.For(system, request.Body).HasLiquidWater)
			{
				return;
			}
			// The scene's floor is above the water line, so the sea is not in this scene.
			if (groundAltitudeMetres >= 0f)
			{
				return;
			}

			var host = new GameObject("Water");
			SceneManager.MoveGameObjectToScene(host, scene);
			host.transform.position = new Vector3(0f, -groundAltitudeMetres, 0f);

			host.AddComponent<MeshFilter>();
			var renderer = host.AddComponent<MeshRenderer>();
			var surface = host.AddComponent<WaterSurface>();

			Material material = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
			if (material != null)
			{
				surface.Material = material;
				renderer.sharedMaterial = material;
			}
			else
			{
				Debug.LogWarning($"[Scene generator] '{WaterMaterialPath}' is missing, so '{request.SceneName}' has a " +
					"water surface with no material. Assign one on its Water object.", host);
			}

			/* Far enough to reach the horizon from anywhere in the scene, and no further: past the
			 * camera's far plane the rings are drawn and clipped. The diagonal is what a viewer at
			 * one corner has to see across. */
			float diagonal = Mathf.Sqrt(plan.WidthMetres * plan.WidthMetres + plan.DepthMetres * plan.DepthMetres);
			surface.OuterRadius = Mathf.Clamp(diagonal * 2f, 4000f, 20000f);
			surface.Rebuild();

			/* The depth field the shoaling, the surf and the swash all read, and the driver that
			 * connects the sea to the world's wind and moons. Both are wanted on every generated
			 * scene: without the field a beach gets open-ocean waves that stop dead at the
			 * waterline, and without the driver the sea runs on whatever the component was
			 * authored with instead of on the weather. */
			host.AddComponent<WaterShoreField>();
			host.AddComponent<WaterEnvironment>();

			result.SeaLevelY = host.transform.position.y;
			result.HasWater = true;
		}

		/// <summary>
		/// Shrinks every tile onto the ground the scene actually has, and returns the new relief.
		/// </summary>
		/// <param name="terrains">Every tile of the scene, already written.</param>
		/// <param name="lowest">Metres above sea level at height 0; replaced with the new floor.</param>
		/// <param name="relief">The bound the tiles were written against, in metres.</param>
		/// <remarks>
		/// <para>
		/// The bound the heights were written against has to allow for the highest peak local
		/// detail could produce anywhere, so that nothing is ever clamped. Most scenes are nowhere
		/// near it — a couple of kilometres of gentle ground sits inside a tenth of the range its
		/// world allows — and a terrain whose nominal height is several times its real relief is
		/// not a harmless overshoot. Its <c>size.y</c> IS the metres that a normalised height of 0
		/// to 1 spans, which is what <see cref="Biomes.SceneTerrainExtent"/> reports and what turns
		/// a real lapse rate into the climate scale's. Left inflated, a scene cools from valley to
		/// ridge by a fraction of what its own relief says it should.
		/// </para>
		/// <para>
		/// Done by rescaling what is on disk rather than by sampling the planet again: the heights
		/// are already the answer, and asking the noise a second time would double the cost of the
		/// slowest part of generating a scene. The stored values are 16-bit, so requantising them
		/// against a smaller range loses at most one step of the OLD range — about a centimetre on
		/// any scene this produces, against a metre being the unit the world is built in.
		/// </para>
		/// </remarks>
		private static float Tighten(Terrain[,] terrains, ref float lowest, float relief)
		{
			float minimum = 1f;
			float maximum = 0f;
			bool any = false;

			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				int resolution = data.heightmapResolution;
				float[,] heights = data.GetHeights(0, 0, resolution, resolution);
				for (int z = 0; z < resolution; z++)
				{
					for (int x = 0; x < resolution; x++)
					{
						float height = heights[z, x];
						minimum = Mathf.Min(minimum, height);
						maximum = Mathf.Max(maximum, height);
						any = true;
					}
				}
			}

			if (!any)
			{
				return relief;
			}

			float floor = lowest + minimum * relief;
			float tightened = Mathf.Max(SceneGeneration.MinimumTerrainHeightMetres, (maximum - minimum) * relief);
			// Nothing to give back, and rewriting every heightmap to change nothing would only add
			// a requantisation to the scene for free.
			if (tightened >= relief - 0.5f)
			{
				return relief;
			}

			foreach (Terrain terrain in terrains)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null)
				{
					continue;
				}
				int resolution = data.heightmapResolution;
				float[,] heights = data.GetHeights(0, 0, resolution, resolution);
				for (int z = 0; z < resolution; z++)
				{
					for (int x = 0; x < resolution; x++)
					{
						float metres = lowest + heights[z, x] * relief;
						heights[z, x] = Mathf.Clamp01((metres - floor) / tightened);
					}
				}
				Vector3 size = data.size;
				data.size = new Vector3(size.x, tightened, size.z);
				data.SetHeights(0, 0, heights);
				EditorUtility.SetDirty(data);
			}

			lowest = floor;
			return tightened;
		}

		/// <summary>
		/// Paints the tile with the plain colour bands, so the ground can be read.
		/// </summary>
		/// <remarks>
		/// Without any layer a terrain renders as one untextured grey mass where a cliff, a beach
		/// and a snowfield look identical — which makes it impossible to tell whether the heightmap
		/// did anything at all, the one question a freshly generated scene has to answer.
		/// </remarks>
		private static void Paint(TerrainData data, float relief)
		{
			TerrainLayer[] layers = GeneratedTerrainLayers.Ensure();
			data.terrainLayers = layers;

			// Coarser than the heightmap on purpose: these are flat colour bands, and a splat map
			// as dense as the heights would cost memory to store the same value many times over.
			data.alphamapResolution = 256;
			int resolution = data.alphamapResolution;
			var splat = new float[resolution, resolution, layers.Length];
			var weights = new float[layers.Length];

			for (int z = 0; z < resolution; z++)
			{
				float v = z / (float)(resolution - 1);
				for (int x = 0; x < resolution; x++)
				{
					float u = x / (float)(resolution - 1);
					// GetSteepness and GetInterpolatedHeight take (x, z) in that order, and height
					// comes back in metres.
					float height01 = relief > 0f ? Mathf.Clamp01(data.GetInterpolatedHeight(u, v) / relief) : 0f;
					GeneratedTerrainLayers.Weights(height01, data.GetSteepness(u, v), weights);
					for (int layer = 0; layer < layers.Length; layer++)
					{
						splat[z, x, layer] = weights[layer];
					}
				}
			}
			data.SetAlphamaps(0, 0, splat);
		}

		/// <summary>
		/// Tells each tile who its neighbours are.
		/// </summary>
		/// <remarks>
		/// Without this Unity lights and levels each tile as an island: normals do not match across
		/// a seam and the two sides drop detail independently, so a visible crack opens and closes
		/// as the camera moves, on ground whose heights match exactly.
		/// </remarks>
		private static void Stitch(Terrain[,] terrains, TerrainTilePlan plan)
		{
			for (int tz = 0; tz < plan.CountZ; tz++)
			{
				for (int tx = 0; tx < plan.CountX; tx++)
				{
					terrains[tx, tz].SetNeighbors(
						tx > 0 ? terrains[tx - 1, tz] : null,
						tz + 1 < plan.CountZ ? terrains[tx, tz + 1] : null,
						tx + 1 < plan.CountX ? terrains[tx + 1, tz] : null,
						tz > 0 ? terrains[tx, tz - 1] : null);
				}
			}
		}

		/// <summary>
		/// Adds the boundary the scene cannot be read without.
		/// </summary>
		/// <remarks>
		/// <c>WorldSceneDetailsCacheReader</c> refuses a scene with no <c>IBoundary</c> outright —
		/// "Boundaries are required for safety purposes" — so a generated scene without one would
		/// never reach the cache and would silently not exist to the game.
		/// </remarks>
		private static void AddBoundary(Scene scene, TerrainTilePlan plan, float relief)
		{
			var host = new GameObject("Scene Boundary");
			SceneManager.MoveGameObjectToScene(host, scene);
			host.transform.position = new Vector3(0f, relief * 0.5f, 0f);
			// Generous vertically: a boundary that hugs the ground catches anyone who jumps.
			host.AddComponent<SceneBoundary>().BoundarySize =
				new Vector3(plan.WidthMetres, relief * 2f + 1000f, plan.DepthMetres);
		}

		/// <summary>Gives the body a base climate if it has none and the project has exactly one.</summary>
		private static void EnsureBodyClimate(WorldBody body)
		{
			if (body == null || body.BaseClimate != null)
			{
				return;
			}
			FishMMO.Shared.Biomes.ClimateSettings climate = SceneWorldSystems.OnlyAuthoredClimate();
			if (climate == null)
			{
				return;
			}
			Undo.RecordObject(body, "Base climate");
			body.BaseClimate = climate;
			EditorUtility.SetDirty(body);
			Debug.Log($"[Scene generator] {body.ResolvedName} had no climate; it now uses \"{climate.name}\".");
		}

		/// <summary>Creates or updates the scene's atlas entry, placed where the rectangle was drawn.</summary>
		private static WorldAtlasScene EnsureEntry(SceneGenerationRequest request, SceneGenerationResult result)
		{
			WorldAtlasScene entry = WorldAtlasScene.Find(request.SceneName);
			if (entry == null)
			{
				entry = WorldEditorAssets.Create<WorldAtlasScene>(WorldEditorAssets.ScenesFolder, request.SceneName);
				result.Wrote.Add(AssetDatabase.GetAssetPath(entry));
			}
			else
			{
				Undo.RecordObject(entry, "Place generated scene");
			}

			entry.SceneName = request.SceneName;
			entry.Body = request.Body;
			entry.Layer = request.Layer;
			entry.Latitude = request.Latitude;
			entry.Longitude = request.Longitude;
			entry.HeadingDegrees = request.HeadingDegrees;
			entry.SizeKm = new Vector2(result.Plan.WidthMetres / 1000f, result.Plan.DepthMetres / 1000f);
			// Placed, because the rectangle IS the placement. Everything else on the entry keeps
			// its default: climate, biome map and client cap are overrides, and empty means
			// "whatever my body and my layer say".
			entry.Placed = true;

			EditorUtility.SetDirty(entry);
			return entry;
		}
	}
}
#endif
