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
		/// Metres above the body's sea level of the scene's lowest ground — which is also the world
		/// Y its terrain tiles stand at, since a generated scene's Y IS altitude.
		/// </summary>
		public float GroundAltitudeMetres;
		/// <summary>True when the scene reaches the body's water line and was given a sea.</summary>
		public bool HasWater;
		/// <summary>
		/// World Y of the liquid's surface when there is one: 0 for a sea or a magma ocean, sea level being
		/// the datum; lower for an Io's lava lakes.
		/// </summary>
		public float SeaLevelY;
		/// <summary>True when the scene reaches the body's lava and was given a lava surface (at <see cref="SeaLevelY"/>).</summary>
		public bool HasLava;
		/// <summary>The atlas radius the scene was cut at, in km.</summary>
		public double RadiusKm;
		/// <summary>Scene metres per metre of the planet's own altitude.</summary>
		public float VerticalScale;
		/// <summary>Where the scene and terrain it replaced were copied to, when it was a re-cut.</summary>
		public string BackupFolder;
		/// <summary>How far the client-only backdrop reaches past the scene's edge, in metres.</summary>
		public float BackdropReachMetres;
		/// <summary>How many vertices the backdrop's meshes hold.</summary>
		public int BackdropVertices;
		/// <summary>Which biome lies where, baked for the runtime. Null when no biome fitted the scene.</summary>
		public SceneBiomeMap BiomeMap;
		/// <summary>The biomes the scene resolved and how much of it each covers, e.g. "Forest 62%, Alpine 38%".</summary>
		public string BiomeSummary;
		/// <summary>Things a person should know about the ground that did not stop generation: missing art, dropped layers.</summary>
		public readonly List<string> Notes = new List<string>();

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
	/// <para>
	/// <b>World Y is metres above sea level.</b> The ground stands at its real altitude and the sea,
	/// when there is one, is at y = 0 — in every scene, not just the ones cut at the coast. It
	/// used to be the other way round: each scene's lowest ground at y = 0 and the sea at minus
	/// that altitude. The sea came out in the right place relative to the ground, but nothing else
	/// was told where it was. The cloud bands are shells over y = 0 and every renderer's height fog
	/// lies thickest at y = 0, so in a scene cut from a kilometre down the cloud deck hung 300 m
	/// under the water, and on a plateau the fog lay on the valley floor as if it were the shore.
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
		/// no dressing to run. It is handed the result so far, which is how it knows where the
		/// ground and the sea are without measuring the scene again.
		/// </remarks>
		public static readonly List<Action<Scene, SceneGenerationRequest, SceneGenerationResult>> Dress =
			new List<Action<Scene, SceneGenerationRequest, SceneGenerationResult>>();

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

			/* Paths, so the body and layer can be found again. Clearing a re-cut's old terrain
			 * refreshes the asset database, and opening the new scene in Single mode (what a batch
			 * editor always does) unloads unused assets. Either can leave the objects held here
			 * fake-null, and the planet then silently answers for a default Earth-like world:
			 * the first re-cut of Cov Viaduct wrote every tile flat at the floor of its bounds. */
			string bodyPath = AssetDatabase.GetAssetPath(request.Body);
			string layerPath = request.Layer != null ? AssetDatabase.GetAssetPath(request.Layer) : null;

			List<string> existingNames = ExistingSceneNames();
			string scenePath = $"{WorldFolder(request.Body)}/{request.SceneName}.unity";
			string terrainFolder = TerrainFolder(request.Body, request.SceneName);
			if (request.ReplaceExisting)
			{
				// Only this scene may already exist, and only where the generator put it.
				existingNames.RemoveAll(n => string.Equals(n, request.SceneName, StringComparison.OrdinalIgnoreCase));
				if (!File.Exists(scenePath))
				{
					return SceneGenerationResult.Failed($"'{scenePath}' does not exist, so there is nothing to re-cut. Only scenes the generator made can be re-cut.");
				}
			}

			string nameProblem = SceneGeneration.NameProblem(request.SceneName, existingNames);
			if (nameProblem != null)
			{
				return SceneGenerationResult.Failed(nameProblem);
			}

			if (!request.ReplaceExisting)
			{
				if (File.Exists(scenePath))
				{
					return SceneGenerationResult.Failed($"'{scenePath}' already exists.");
				}
				if (AssetDatabase.IsValidFolder(terrainFolder))
				{
					return SceneGenerationResult.Failed($"'{terrainFolder}' already exists. Move or delete it first — this never writes over anything.");
				}
			}

			var result = new SceneGenerationResult
			{
				Plan = SceneGeneration.PlanTiles(request.SizeKm),
				RadiusKm = request.ResolvedRadiusKm,
				VerticalScale = request.VerticalScale,
			};

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

			/* Unity refuses to add a scene additively while an UNTITLED scene is open, which is
			 * exactly what a freshly launched editor has — and what a batch editor always has. So
			 * the untitled case makes the new scene the only one instead; there is nothing open
			 * worth returning to, by definition. Modified scenes are offered for saving first so
			 * that path can never discard somebody's work. */
			if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return SceneGenerationResult.Failed("Cancelled while saving open scenes; nothing was generated.");
			}

			/* Only now, after the last chance to cancel, is anything removed or created: a re-cut
			 * cancelled at the save prompt must leave the old scene exactly as it was. */
			if (request.ReplaceExisting)
			{
				string refusal = ClearForRecut(scenePath, terrainFolder, request.SceneName, out result.BackupFolder);
				if (refusal != null)
				{
					return SceneGenerationResult.Failed(refusal);
				}
			}

			WorldEditorAssets.EnsureFolder(WorldFolder(request.Body));
			WorldEditorAssets.EnsureFolder(terrainFolder);

			bool untitled = string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path);
			NewSceneMode mode = untitled ? NewSceneMode.Single : NewSceneMode.Additive;
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
			if (!Revive(request, bodyPath, layerPath))
			{
				if (mode == NewSceneMode.Additive)
				{
					EditorSceneManager.CloseScene(scene, true);
				}
				return SceneGenerationResult.Failed($"'{bodyPath}' could not be loaded again after opening the new scene; nothing was written." +
					(result.BackupFolder != null ? $" The old scene is in '{result.BackupFolder}'." : string.Empty));
			}
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

				/* The biomes, from the finished ground: which lies where, the textures that say so,
				 * and the map the runtime reads. The flat bands only where no biome fits at all —
				 * no template registered, or none this world allows — so the scene still reads. */
				if (!PaintBiomes(scene, request, plan, terrains, terrainFolder, result, out Func<float, float, float, float, Color> groundColour))
				{
					foreach (Terrain terrain in terrains)
					{
						if (terrain != null && terrain.terrainData != null)
						{
							Paint(terrain.terrainData, relief);
						}
					}
				}

				/* The ground past the scene's edge, out to the horizon: client-only, built from the
				 * same request so it meets the terrain at the edge. Before the sea, because a scene
				 * of dry land can still look out over a coast, and that sea has to be drawn. */
				SceneBackdropResult backdrop = SceneBackdropBuilder.Build(scene, request, plan, lowest, relief, terrainFolder, groundColour);
				result.Wrote.AddRange(backdrop.Wrote);
				result.BackdropReachMetres = backdrop.ReachMetres;
				result.BackdropVertices = backdrop.Vertices;

				// The sea first: the boundary has to reach its surface, which on a scene cut from
				// the sea floor is far above the highest ground.
				AddWater(scene, plan, request, lowest, backdrop, result);
				AddBoundary(scene, plan, lowest, relief, result.HasWater || result.HasLava ? result.SeaLevelY : (float?)null);

				// The same components the audit adds to a scene somebody forgot to finish.
				bool wantsSky = request.Layer == null || !request.Layer.Underground;
				GameObject host = SceneWorldSystems.Configure(scene, wantsSky);
				host.transform.position = Vector3.zero;

				/* The camera and the sim controller, if the harness is in this project. A failure
				 * here must not lose the scene: the terrain is minutes of work and the dressing is
				 * seconds, so it is reported and the scene kept. */
				foreach (Action<Scene, SceneGenerationRequest, SceneGenerationResult> dress in Dress)
				{
					try
					{
						dress?.Invoke(scene, request, result);
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
			// Every generated scene has a boundary, so the client needs the glass that shows it.
			BoundaryGlassAssets.Ensure();
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
			/* Every tile shares one floor, so the landmass is one slope rather than a set of
			 * terraces — and that floor stands at its real altitude, so y is metres above sea level
			 * here exactly as it is for the sea, the clouds and the fog. Tighten() moves it again
			 * when it finds the ground's true floor. */
			host.transform.position = new Vector3(tx * plan.TileMetres - halfWidth, lowest, tz * plan.TileMetres - halfDepth);

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

		/// <summary>
		/// How far, in screen pixels, a generated terrain's drawn surface may stray from its true
		/// shape. See the remarks where <see cref="PaintBiomes"/> applies it.
		/// </summary>
		public const float TerrainPixelError = 2f;

		/// <summary>The ocean material every generated scene shares.</summary>
		public const string WaterMaterialPath = "Assets/Plugins/FishMMO Water/Materials/OceanWater.mat";

		/// <summary>The compute shader that simulates the sea's waves.</summary>
		public const string WaterSpectrumPath = "Assets/Plugins/FishMMO Water/Shaders/FishWaterFFT.compute";

		/// <summary>The same FFT as render passes, for machines with no compute shaders.</summary>
		public const string WaterSpectrumPassesPath = "Assets/Plugins/FishMMO Water/Shaders/FishWaterFFT.shader";

		/// <summary>The pass that keeps the foam the swash leaves on the sand.</summary>
		public const string WaterFoamMemoryPath = "Assets/Plugins/FishMMO Water/Shaders/FishWaterFoamMemory.shader";
		/// <summary>The breakers' sheet, curling at the break line where the sea fades out.</summary>
		public const string WaterBreakerPath = "Assets/Plugins/FishMMO Water/Shaders/FishWaterBreaker.shader";
		/// <summary>The breakers' spray and mist.</summary>
		public const string WaterSprayPath = "Assets/Plugins/FishMMO Water/Shaders/FishWaterSpray.shader";
		/// <summary>Molten rock, for a volcanic body's lava lakes or a world above the melting point of rock.</summary>
		public const string LavaMaterialPath = "Assets/Plugins/FishMMO Water/Materials/Lava.mat";

		/// <summary>
		/// Puts the sea in the scene, at the height the planet says its sea level is.
		/// </summary>
		/// <param name="groundAltitudeMetres">
		/// Metres above the body's sea level of the scene's lowest ground.
		/// </param>
		/// <remarks>
		/// <para>
		/// <b>At y = 0, because y is altitude.</b> The terrain stands at its real height above the
		/// body's sea level, so the planet's water line is at zero and nowhere else. Put the sea at
		/// a round number instead and the coastline in the scene stops being the coastline on the
		/// globe — the map shows a bay and the ground has none, or the whole scene drowns.
		/// </para>
		/// <para>
		/// <b>Not every scene gets one.</b> A world with no liquid water has no sea to put in, and
		/// a scene cut entirely from high ground never reaches the water line — a sea plane under
		/// its floor would be invisible from every point in it while still costing a full-screen
		/// transparent pass. Measured on an Earth-like world, about 70% of randomly cut scenes DO
		/// reach it, because that is how much of such a world is ocean — and many of those are
		/// open sea floor, every point of it kilometres under water.
		/// </para>
		/// </remarks>
		private static void AddWater(Scene scene, TerrainTilePlan plan, SceneGenerationRequest request,
			float groundAltitudeMetres, SceneBackdropResult backdrop, SceneGenerationResult result)
		{
			if (request.Layer != null && request.Layer.Underground)
			{
				return;
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(request.Body);
			/* What stands in the low ground: a sea, lava, or nothing (SurfaceLiquids). A sea and a magma
			 * ocean stand at the datum, y = 0; an Io's lava lakes lower, in the pits where its globe draws
			 * them glowing — in the planet's metres, so brought to this scene's heights by its vertical
			 * scale, exactly as the terrain is. */
			SurfaceLiquid liquid = SurfaceLiquids.For(system, request.Body, out float levelMetres);
			if (liquid == SurfaceLiquid.None)
			{
				return;
			}
			bool lava = liquid == SurfaceLiquid.Lava;
			float level = levelMetres * request.VerticalScale;
			// Neither the scene nor the ground it looks out over reaches the liquid.
			bool backdropReachesSea = backdrop != null && backdrop.Vertices > 0 && backdrop.LowestMetres < level;
			if (groundAltitudeMetres >= level && !backdropReachesSea)
			{
				return;
			}

			var host = new GameObject(lava ? "Lava" : "Water");
			SceneManager.MoveGameObjectToScene(host, scene);
			host.transform.position = new Vector3(0f, level, 0f);

			host.AddComponent<MeshFilter>();
			var renderer = host.AddComponent<MeshRenderer>();
			var surface = host.AddComponent<WaterSurface>();

			/* The FFT that makes the waves. Without it the surface is a flat sheet with ripples
			 * drawn on, and every generated scene until now shipped that way: a component added
			 * from script has no default reference, and this line was missing. */
			surface.Spectrum = AssetDatabase.LoadAssetAtPath<ComputeShader>(WaterSpectrumPath);
			// And its render-pass twin, for WebGL2 and GLES3: referenced so a build includes it.
			surface.SpectrumPasses = AssetDatabase.LoadAssetAtPath<Shader>(WaterSpectrumPassesPath);
			// The projected passes the surface finds by name in the editor, referenced for builds.
			surface.UnderwaterShader = Shader.Find(WaterSurface.UnderwaterShaderName);
			surface.CausticsShader = Shader.Find(WaterSurface.CausticsShaderName);
			if (surface.Spectrum == null)
			{
				Debug.LogWarning($"[Scene generator] '{WaterSpectrumPath}' is missing, so '{request.SceneName}' has a flat sea. " +
					"Assign the FFT compute shader on its Water object.", host);
			}

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
			/* And at least to the backdrop's far corner, or its seas end in a ring of bare sea
			 * floor ten kilometres out. */
			float backdropCorner = 0f;
			if (backdrop != null && backdrop.Vertices > 0)
			{
				float bx = plan.WidthMetres * 0.5f + backdrop.ReachMetres, bz = plan.DepthMetres * 0.5f + backdrop.ReachMetres;
				backdropCorner = Mathf.Sqrt(bx * bx + bz * bz) * 1.05f;
			}
			surface.OuterRadius = Mathf.Clamp(Mathf.Max(diagonal * 2f, backdropCorner), 4000f, 30000f);
			if (lava)
			{
				/* Lava draws its own material on the same disc. The ocean's stays assigned, so flipping
				 * Liquid back to Water in the inspector gives a working sea. */
				surface.Liquid = WaterLiquid.Lava;
				// Fumes need air to hang in: none over an Io, a haze under thin air (SurfaceLiquids.FumeDensity).
				surface.LavaFumes = SurfaceLiquids.FumeDensity(request.Body != null ? request.Body.Atmosphere : AtmosphereKind.Standard);
				// The glow-and-fume pass, found by name in the editor, referenced so a build includes it.
				surface.LavaLightShader = Shader.Find(WaterSurface.LavaLightShaderName);
				surface.LavaMaterial = AssetDatabase.LoadAssetAtPath<Material>(LavaMaterialPath);
				if (surface.LavaMaterial != null)
				{
					renderer.sharedMaterial = surface.LavaMaterial;
				}
				else
				{
					Debug.LogWarning($"[Scene generator] '{LavaMaterialPath}' is missing, so '{request.SceneName}' has a " +
						"lava surface with no material. Assign one on its Lava object.", host);
				}
			}
			surface.Rebuild();

			result.SeaLevelY = level;
			if (lava)
			{
				/* None of the sea's own systems: no shore field (a beach), no environment (wind, tide,
				 * colour), no swash and no breakers. Lava has none of them. */
				result.HasLava = true;
				return;
			}

			/* The depth field the shallows, the breakers and the swash all read, and the driver that
			 * connects the sea to the world's wind and moons. Both are wanted on every generated
			 * scene: without the field a beach gets open-ocean waves that stop dead at the
			 * waterline, and without the driver the sea runs on whatever the component was
			 * authored with instead of on the weather. */
			host.AddComponent<WaterShoreField>();
			host.AddComponent<WaterEnvironment>();
			// Referenced so a build includes the pass that keeps the foam each wave strands.
			host.AddComponent<WaterShore>().FoamMemoryShader = AssetDatabase.LoadAssetAtPath<Shader>(WaterFoamMemoryPath);
			/* The breakers: the sea fades out over the shallows and they rise at the break line. Their
			 * shaders referenced for the same reason — a build includes nothing it finds by name. */
			var breakers = host.AddComponent<WaterBreakers>();
			breakers.BreakerShader = AssetDatabase.LoadAssetAtPath<Shader>(WaterBreakerPath);
			breakers.SprayShader = AssetDatabase.LoadAssetAtPath<Shader>(WaterSprayPath);
			// Icebergs and sea ice where the climate makes them (IcePlacer; liquid water only, lava returned above).
			result.Notes.AddRange(IcePlacer.Place(scene, request, host, host.GetComponent<WaterShoreField>(), SceneSeed(request), plan).Notes);

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

				// Height 0 now means the new floor, so the tile stands there: y is altitude.
				Vector3 position = terrain.transform.position;
				terrain.transform.position = new Vector3(position.x, floor, position.z);
			}

			lowest = floor;
			return tightened;
		}

		/// <summary>
		/// Paints the scene with its biomes and bakes its biome map. False, having written nothing,
		/// when no biome fits anywhere in it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>From the finished heightmap.</b> Tighten() has run, so the ground the biome field
		/// reads is the ground on disk — local detail included — and a ridge colder than the globe
		/// can show is painted as the ridge it is.
		/// </para>
		/// <para>
		/// <b>The map is written beside the terrain</b> and assigned to the atlas entry, which is
		/// where <see cref="WorldSceneSettings.BiomeMap"/> looks first. Without it the runtime chose
		/// a biome from height normalised over the scene's own relief, so the lowest point of every
		/// scene read as sea floor and the highest as a summit whatever their real altitude, and
		/// weather, naming and exposure could disagree with the ground they stood on.
		/// </para>
		/// </remarks>
		/// <param name="groundColour">
		/// The biomes' colour at (east, north, altitude, steepness) out to the horizon, for the
		/// backdrop; null when this returns false.
		/// </param>
		/// <remarks>
		/// Also what the Repaint Biomes tool runs on a scene that already exists, so a designer can
		/// sculpt the ground and then have it re-dressed without re-cutting it: everything here reads
		/// the heights on disk and writes only layers, alphamaps, details, trees and the map.
		/// </remarks>
		/// <param name="scope">
		/// Whether LOCAL art may be referenced by what is written; null decides it from the scene and its
		/// tiles (<see cref="LocalArtScope.For"/>), which is committed-only for every scene outside
		/// Assets/LOCAL. Every reference this writes — palette layers, scatter prototypes, cliff
		/// materials — goes through it; the terrain arrays resolve textures LOCAL-first on their own.
		/// </param>
		internal static bool PaintBiomes(Scene scene, SceneGenerationRequest request, TerrainTilePlan plan, Terrain[,] terrains,
			string terrainFolder, SceneGenerationResult result, out Func<float, float, float, float, Color> groundColour,
			LocalArtScope scope = null)
		{
			groundColour = null;
			SolarSystemProfile system = SolarSystemProfile.Resolve(request.Body);
			var tiles = new List<Terrain>();
			foreach (Terrain terrain in terrains)
			{
				if (terrain != null && terrain.terrainData != null)
				{
					tiles.Add(terrain);
				}
			}

			SceneBiomeField field = SceneBiomeField.Build(request, plan, (east, north) => GroundAltitude(terrains, plan, east, north), system);
			if (field.Biomes.Count == 0)
			{
				result.Notes.Add("No biome fits anywhere in this scene (none registered, or none this world allows); it is painted with the plain height bands.");
				return false;
			}

			scope ??= LocalArtScope.For(scene, tiles);
			if (scope.AllowsLocal)
			{
				result.Notes.Add($"Painted with {scope.Reason}.");
			}

			BiomeTerrainLayers.ClearCache();
			SceneTerrainPalette palette = SceneTerrainPalette.Build(field, scope.PaletteResolver(BiomeTerrainLayers.Resolve), BiomeTerrainLayers.Placeholder);
			if (palette.Layers.Count == 0)
			{
				result.Notes.Add("None of this scene's biomes has any terrain art; it is painted with the plain height bands.");
				return false;
			}

			PlanetClimateField climate = PlanetClimateField.For(system, request.Body);
			float verticalScale = request.VerticalScale > 1e-6f ? request.VerticalScale : 1f;
			var options = new BiomeSplatOptions
			{
				// Planet-relative, so a layer's height band means the same height in every scene.
				NormalizedHeight = (x, y, z) => climate.HeightOfAltitude(y / verticalScale),
				HasLiquidWater = climate.Conditions.HasLiquidWater,
				Seed = SceneSeed(request),
			};
			BiomeSplatReport splat = BiomeSplatPainter.Paint(tiles, palette, field, options);

			/* The ground's renderer: the array terrain material on every tile and the binder that
			 * feeds it the scene's arrays. Coverage decides which biome's LOCAL art wins a layer
			 * several biomes share. The arrays themselves are build output, baked when the scene
			 * is first saved — a scene being generated has no saved name to bake under yet. */
			TerrainArraySetupResult arrays = TerrainArraySetup.Apply(scene, tiles, palette, field.Coverage());
			result.Wrote.AddRange(arrays.Wrote);
			result.Notes.AddRange(arrays.LocalConflicts);
			result.Notes.AddRange(arrays.Notes);

			/* Details and trees, from the alphamaps just written: every rule is gated on its own
			 * layer's weight there and on its biome's reach, so what grows is exactly what the
			 * ground says grows. Baked into the terrain data like the heights — committed, editable
			 * by hand, and the trees' colliders are the server's as well as the client's. */
			TerrainScatterReport scatter = TerrainScatter.Scatter(tiles, palette, field, options.Seed,
				new TerrainScatterOptions { NormalizedHeight = options.NormalizedHeight, Prefabs = scope.ScatterPrefabs, HasLiquidWater = options.HasLiquidWater });
			result.Notes.AddRange(scatter.InvalidPrefabs);
			result.Notes.AddRange(scatter.BudgetCaps);
			result.Notes.AddRange(scatter.Warnings);

			/* The ground drawn close to its true shape, so what stands on it stays standing on it.
			 * Trees, rocks and cliff pieces are placed at the exact surface height; the terrain's own
			 * level of detail then simplifies the drawn surface until it may be heightmapPixelError
			 * screen pixels off. At Unity's default of 5 that is metres on generated hills, so a trunk
			 * seemed to sink as the camera backed off and rise as it came close — the ground moving,
			 * not the tree. 2 keeps the error under what the bases are sunk by at any distance the
			 * trees are drawn; instancing is what makes that much terrain detail cheap, and both
			 * terrain shaders carry the instanced (per-pixel normal) variants. */
			foreach (Terrain tile in tiles)
			{
				tile.heightmapPixelError = TerrainPixelError;
				tile.drawInstanced = true;
			}

			/* Cliffs of large rocks on the steep ground the cliff layers were just painted on, talus cones
			 * raised below them, each rock a server-kept collider with a client-only visual. Here and
			 * not after PaintBiomes returns, so a repaint (BiomeRepaintTool) re-places them on sculpted
			 * ground too; the placer replaces its own root (and lowers its old cones), never duplicates
			 * it. Granite weathers rounder where the climate is warm and wet: the placer reads it here. */
			CliffPlacerOptions cliffOptions = scope.CliffOptions() ?? new CliffPlacerOptions();
			if (request.Body != null)
			{
				ScenePlacementClimate placement = ScenePlacementClimate.For(system, request.Body, request.Footprint, request.ResolvedRadiusKm, true);
				cliffOptions.ClimateAt = p =>
				{
					ClimateSample sample = placement.SampleAt(p, out _);
					return new Vector2(sample.Temperature, sample.Humidity);
				};
			}
			CliffPlacerReport cliffs = CliffPlacer.Place(scene, tiles, palette, field, options.Seed, options.NormalizedHeight, cliffOptions);
			result.Notes.AddRange(cliffs.Notes);

			result.BiomeSummary = Summarise(field);
			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				if (entry.Placeholder)
				{
					result.Notes.Add($"{entry.Biome.ResolvedDisplayName} has no main terrain art; a plain placeholder stands in.");
				}
			}
			foreach (string dropped in palette.Dropped)
			{
				result.Notes.Add($"Left out: {dropped}.");
			}
			if (splat.Unreached > 0)
			{
				result.Notes.Add($"{splat.Unreached:N0} texels were reached by no biome and took the first layer.");
			}

			/* Rewritten in place when it exists, so a repaint keeps the map's GUID and every
			 * reference to it; created beside the terrain otherwise. */
			string mapPath = $"{terrainFolder}/{WorldEditorAssets.Sanitize(request.SceneName)} Biome Map.asset";
			var map = AssetDatabase.LoadAssetAtPath<SceneBiomeMap>(mapPath);
			if (map != null)
			{
				field.WriteTo(map);
				EditorUtility.SetDirty(map);
			}
			else
			{
				map = ScriptableObject.CreateInstance<SceneBiomeMap>();
				field.WriteTo(map);
				AssetDatabase.CreateAsset(map, mapPath);
			}
			result.Wrote.Add(mapPath);
			result.BiomeMap = map;

			/* The backdrop's own field, out to the horizon: the ground past the edge is the same
			 * planet, so it is asked the same question, at the backdrop's own (planet + local
			 * detail) heights rather than the terrain's, which end at the scene's edge. */
			float reach = SceneBackdropBuilder.ReachFor(request);
			if (reach > 1f)
			{
				SceneBiomeField horizon = SceneBiomeField.Build(request, plan.WidthMetres + 2f * reach, plan.DepthMetres + 2f * reach,
					(east, north) => SceneGeneration.AltitudeMetres(request, east, north), system);
				var colours = new BiomeGroundColours(horizon, BiomeTerrainLayers.Resolve, BiomeTerrainLayers.Placeholder);
				groundColour = (east, north, altitude, steepness) =>
					colours.At(east, north, steepness, climate.HeightOfAltitude(altitude / verticalScale));
			}

			Debug.Log($"[Scene generator] '{request.SceneName}' biomes: {result.BiomeSummary}; {palette.Layers.Count} terrain layer(s); splat {splat}.\n{scatter}");
			return true;
		}

		/// <summary>Scene metres above sea level of the generated ground at a scene position.</summary>
		private static float GroundAltitude(Terrain[,] terrains, TerrainTilePlan plan, float east, float north)
		{
			int tx = Mathf.Clamp(Mathf.FloorToInt((east + plan.WidthMetres * 0.5f) / plan.TileMetres), 0, plan.CountX - 1);
			int tz = Mathf.Clamp(Mathf.FloorToInt((north + plan.DepthMetres * 0.5f) / plan.TileMetres), 0, plan.CountZ - 1);
			Terrain terrain = terrains[tx, tz];
			if (terrain == null)
			{
				return 0f;
			}
			// SampleHeight is relative to the tile's own position, which stands at its altitude.
			return terrain.SampleHeight(new Vector3(east, 0f, north)) + terrain.transform.position.y;
		}

		/// <summary>The scene's own seed: its body's terrain seed mixed with its name, as local detail does.</summary>
		private static uint SceneSeed(SceneGenerationRequest request)
		{
			uint seed = request.Body != null ? request.Body.ResolvedTerrainSeed : 1u;
			return seed ^ unchecked((uint)(request.SceneName ?? string.Empty).GetDeterministicHashCode());
		}

		private static string Summarise(SceneBiomeField field)
		{
			float[] share = field.Coverage();
			var order = new List<int>();
			for (int i = 0; i < share.Length; i++)
			{
				order.Add(i);
			}
			order.Sort((a, b) => share[b].CompareTo(share[a]));
			var parts = new List<string>();
			foreach (int i in order)
			{
				parts.Add($"{field.Biomes[i].ResolvedDisplayName} {Mathf.RoundToInt(share[i] * 100f)}%");
			}
			return string.Join(", ", parts);
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
		/// <param name="lowest">World Y of the lowest ground, which is its altitude.</param>
		/// <param name="relief">Metres from the lowest ground to the highest.</param>
		/// <param name="liquidY">World Y of the sea's or the lava's surface when the scene was given one; null when it was not.</param>
		/// <remarks>
		/// <para>
		/// <c>WorldSceneDetailsCacheReader</c> refuses a scene with no <c>IBoundary</c> outright —
		/// "Boundaries are required for safety purposes" — so a generated scene without one would
		/// never reach the cache and would silently not exist to the game.
		/// </para>
		/// <para>
		/// Generous vertically: a boundary that hugs the ground catches anyone who jumps. It keeps
		/// half the relief and 500 m clear below the lowest ground and above the highest surface.
		/// </para>
		/// <para>
		/// <b>The highest surface can be the sea.</b> Sized from the ground alone, a scene cut from
		/// a kilometre down had its ceiling 250 m under the water: nobody could swim up to the
		/// surface, and a boat could not be in the scene at all. The same holds for a magma ocean,
		/// whose surface also stands at the datum.
		/// </para>
		/// </remarks>
		private static void AddBoundary(Scene scene, TerrainTilePlan plan, float lowest, float relief, float? liquidY)
		{
			float margin = relief * 0.5f + 500f;
			float surface = liquidY.HasValue ? Mathf.Max(lowest + relief, liquidY.Value) : lowest + relief;
			float bottom = lowest - margin;
			float top = surface + margin;

			var host = new GameObject("Scene Boundary");
			SceneManager.MoveGameObjectToScene(host, scene);
			host.transform.position = new Vector3(0f, (bottom + top) * 0.5f, 0f);
			host.AddComponent<SceneBoundary>().BoundarySize =
				new Vector3(plan.WidthMetres, top - bottom, plan.DepthMetres);
		}

		/// <summary>
		/// Cuts a generated scene's terrain from the globe again, where its atlas entry says it is.
		/// </summary>
		/// <remarks>
		/// <para>
		/// For scenes cut before the generator changed. It is a fresh generation into the same
		/// scene file, so the scene keeps its asset GUID and anything added to it by hand since is
		/// lost. That is why the old scene and terrain are copied to
		/// <c>Library/FishMMO/RecutBackups</c> first, and why a scene that is open in the editor is
		/// refused rather than closed.
		/// </para>
		/// </remarks>
		public static SceneGenerationResult Recut(WorldAtlasScene entry, bool fineDetail = true)
		{
			if (entry == null || entry.Body == null)
			{
				return SceneGenerationResult.Failed("Only a scene placed on a body can be re-cut.");
			}
			return Generate(new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
				FineDetail = fineDetail,
				ReplaceExisting = true,
			});
		}

		/// <summary>
		/// Re-attaches the request's body and layer if a refresh or an asset unload left them
		/// fake-null. False when the body cannot be found at all.
		/// </summary>
		private static bool Revive(SceneGenerationRequest request, string bodyPath, string layerPath)
		{
			if (request.Body == null && !string.IsNullOrEmpty(bodyPath))
			{
				request.Body = AssetDatabase.LoadAssetAtPath<WorldBody>(bodyPath);
			}
			if (request.Layer == null && !string.IsNullOrEmpty(layerPath))
			{
				request.Layer = AssetDatabase.LoadAssetAtPath<WorldAtlasLayer>(layerPath);
			}
			return request.Body != null;
		}

		/// <summary>True when a scene looks like the generator's: its terrain folder sits beside it.</summary>
		public static bool IsGenerated(WorldAtlasScene entry)
		{
			return entry != null && entry.Body != null
				&& File.Exists($"{WorldFolder(entry.Body)}/{entry.SceneName}.unity")
				&& AssetDatabase.IsValidFolder(TerrainFolder(entry.Body, entry.SceneName));
		}

		/// <summary>Where re-cuts copy what they replace.</summary>
		public const string RecutBackupRoot = "Library/FishMMO/RecutBackups";

		/// <summary>
		/// Copies a generated scene and its terrain aside, then removes the terrain so it can be
		/// written again. Returns why it refused, or null.
		/// </summary>
		private static string ClearForRecut(string scenePath, string terrainFolder, string sceneName, out string backupFolder)
		{
			backupFolder = null;
			for (int i = 0; i < EditorSceneManager.sceneCount; i++)
			{
				Scene open = EditorSceneManager.GetSceneAt(i);
				if (open.IsValid() && string.Equals(open.path, scenePath, StringComparison.OrdinalIgnoreCase))
				{
					return $"'{sceneName}' is open in the editor. Close it first: a re-cut replaces the whole scene, and closing it for you could lose unsaved work.";
				}
			}

			backupFolder = $"{RecutBackupRoot}/{WorldEditorAssets.Sanitize(sceneName)} {DateTime.Now:yyyy-MM-dd HHmmss}";
			Directory.CreateDirectory(backupFolder);
			File.Copy(scenePath, $"{backupFolder}/{Path.GetFileName(scenePath)}");
			if (File.Exists(scenePath + ".meta"))
			{
				File.Copy(scenePath + ".meta", $"{backupFolder}/{Path.GetFileName(scenePath)}.meta");
			}
			if (Directory.Exists(terrainFolder))
			{
				string terrainCopy = $"{backupFolder}/{Path.GetFileName(terrainFolder)}";
				Directory.CreateDirectory(terrainCopy);
				foreach (string file in Directory.GetFiles(terrainFolder))
				{
					File.Copy(file, $"{terrainCopy}/{Path.GetFileName(file)}");
				}
				if (!AssetDatabase.DeleteAsset(terrainFolder))
				{
					return $"'{terrainFolder}' could not be removed; nothing was changed. A copy is in '{backupFolder}'.";
				}
			}
			Debug.Log($"[Scene generator] Re-cutting '{sceneName}': the old scene and terrain were copied to '{backupFolder}'.");
			return null;
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
			// What pins the body's atlas radius from now on: see AtlasModel.CutScenes.
			entry.CutRadiusKm = (float)result.RadiusKm;
			/* The generated biome map, unless somebody has assigned their own: the field is an
			 * override, and a hand-painted map is a decision. A re-cut deletes the old generated
			 * map with the terrain folder, so its reference reads null here and is replaced. */
			if (entry.BiomeMap == null && result.BiomeMap != null)
			{
				entry.BiomeMap = result.BiomeMap;
			}
			// Placed, because the rectangle IS the placement. Climate and client cap keep their
			// defaults: they are overrides, and empty means "whatever my body and my layer say".
			entry.Placed = true;

			EditorUtility.SetDirty(entry);
			return entry;
		}
	}
}
#endif
