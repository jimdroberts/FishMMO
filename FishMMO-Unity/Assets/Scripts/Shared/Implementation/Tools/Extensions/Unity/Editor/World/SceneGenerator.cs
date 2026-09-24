#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

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

			/* Measured before anything is created, because every tile has to share one height range
			 * and one floor. Tiles normalised against their own range are what makes a stitched
			 * landmass step at its seams, and the climate read differently on either side. */
			Bounds(request, plan, out float lowest, out float highest);
			result.BaseAltitudeMetres = SceneGeneration.AltitudeMetres(request, 0f, 0f);
			float relief = Mathf.Max(SceneGeneration.MinimumTerrainHeightMetres, highest - lowest);
			result.ReliefMetres = relief;

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

				AddBoundary(scene, plan, relief);

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

		/// <summary>The lowest and highest ground anywhere in the scene, sampled on the tile grid.</summary>
		private static void Bounds(SceneGenerationRequest request, TerrainTilePlan plan, out float lowest, out float highest)
		{
			lowest = float.MaxValue;
			highest = float.MinValue;
			float halfWidth = plan.WidthMetres * 0.5f;
			float halfDepth = plan.DepthMetres * 0.5f;
			// Coarser than the heightmap: this only needs the range, and the range does not move
			// between samples a few metres apart.
			const int Steps = 96;
			for (int z = 0; z <= Steps; z++)
			{
				float north = Mathf.Lerp(-halfDepth, halfDepth, z / (float)Steps);
				for (int x = 0; x <= Steps; x++)
				{
					float east = Mathf.Lerp(-halfWidth, halfWidth, x / (float)Steps);
					float altitude = SceneGeneration.AltitudeMetres(request, east, north);
					lowest = Mathf.Min(lowest, altitude);
					highest = Mathf.Max(highest, altitude);
				}
			}
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
			Paint(data, relief);

			string dataPath = $"{terrainFolder}/{WorldEditorAssets.Sanitize(request.SceneName)} {tx}_{tz}.asset";
			AssetDatabase.CreateAsset(data, dataPath);
			result.Wrote.Add(dataPath);

			var host = new GameObject($"Terrain {tx}_{tz}");
			SceneManager.MoveGameObjectToScene(host, scene);
			// Every tile shares one floor, so the landmass is one slope rather than a set of terraces.
			host.transform.position = new Vector3(tx * plan.TileMetres - halfWidth, 0f, tz * plan.TileMetres - halfDepth);

			Terrain terrain = host.AddComponent<Terrain>();
			terrain.terrainData = data;
			host.AddComponent<TerrainCollider>().terrainData = data;
			terrain.allowAutoConnect = true;
			return terrain;
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
