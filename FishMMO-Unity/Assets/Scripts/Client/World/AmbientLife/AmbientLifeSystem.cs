using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Core;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// The land's background life, client only and purely visual: flocks wheeling over fields and coasts and settling
	/// in the trees, songbirds and crows on the crowns and the ground, ducks on the ponds, buzzards and vultures
	/// circling in thermals, bats at dusk, and rats, mice, squirrels, rabbits, lizards, shore crabs and frogs about
	/// the player's feet — each where and when its kind would be.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same life for everyone, with nothing on the wire.</b> Like the sea's (<see cref="SeaLifeSystem"/>):
	/// where each group lives is decided per cell of a grid from the scene's name, the cell, the kind, the ground, the
	/// biome and the painted climate (<see cref="AmbientLifeMotion.TryPlace"/>); what it is doing is a function of the
	/// shared world clock (<see cref="WorldMotion.Seconds"/>, never faster than real time). Every player sees the same
	/// flock land in the same field. Only a fright is local: who startles a bird depends on who walks up to it.
	/// </para>
	/// <para>
	/// <b>Who is about follows the moment.</b> The light, the temperature, rain, snow, wind, storms and the season
	/// (<see cref="AmbientGating.Activity"/>) decide what share of each kind is out; each group has a fixed rank, so
	/// the same bats come out first for everyone at dusk. Under water, nothing.
	/// </para>
	/// <para>
	/// <b>Drawn, not spawned.</b> No GameObjects: one procedural mesh and one material per kind
	/// (<see cref="AmbientCreatureMeshes"/>, FishMMO/Ambient Life), drawn with <c>Graphics.DrawMeshInstanced</c> —
	/// WebGL2-safe — the wing beats and strides done in the vertex shader. The nearest groups are drawn first up to a
	/// budget (<see cref="AmbientLifeSettings.MaxVisible"/>); each family has its own draw distance. Cells are placed
	/// a few a frame, the near families (birds alone, bats, critters) only within their short reach.
	/// </para>
	/// <para>
	/// <b>Dressed for their biome.</b> Each group wears one of the looks of the biome it is placed in (<see cref="AmbientLooks"/>,
	/// from <see cref="FloraPaletteMap"/>): the same twelve the grass and flowers there are drawn in, moved by the world's
	/// star and air, so a rabbit is sandy on the steppe and russet in the beech wood, and a frozen moon's beetles wear its
	/// own colours. One float4 an instance beside the tint; the shader swaps the mesh's colour roles for the look's.
	/// </para>
	/// <para>
	/// <b>And under the hill.</b> A cave's animals are placed from the cut's cave-life spots (<see cref="SceneCaveLife"/>, filed
	/// by the same cells) and kept to them (<see cref="AmbientCaveLife"/>), never from the terrain's height; their moment is the
	/// cave's (no day past the twilight, no weather, the year's mean air). From outside only a mouth's and the twilight's are
	/// evaluated: the deep cave is behind the rock until the camera is in it (<see cref="CaveVisibility"/>).
	/// </para>
	/// <para>
	/// <b>Hooked like the sea life</b>: a static system started after the first scene loads, driven by the render
	/// pipeline's callbacks; its settings on the Weather Render Profile ("Ambient life"), its shader referenced there
	/// so a client build includes it.
	/// </para>
	/// </remarks>
	public static class AmbientLifeSystem
	{
		private const int Batch = 1023;
		/// <summary>Cell tiers (near or far) decided a frame at most: placement samples the ground.</summary>
		private const int CellsPerFrame = 10;
		/// <summary>Metres within which an off-screen group is still evaluated, so it can be startled before it is seen.</summary>
		private const float OffscreenReach = 20f;
		/// <summary>Characters other than the player counted as threats, nearest first, within this many metres.</summary>
		private const float ThreatReach = 80f;

		private static readonly int AnimId = Shader.PropertyToID("_Anim");
		private static readonly int TintId = Shader.PropertyToID("_Tint");
		private static readonly int ModeId = Shader.PropertyToID("_Mode");
		private static readonly int AmplitudeId = Shader.PropertyToID("_Amplitude");
		private static readonly int DihedralId = Shader.PropertyToID("_Dihedral");
		private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
		private static readonly int ShoulderId = Shader.PropertyToID("_Shoulder");
		private static readonly int MeanLevelId = Shader.PropertyToID("_FishWaterMeanLevel");
		private static readonly int SeasonId = Shader.PropertyToID("_FishSeason");
		private static readonly int SwarmStyleId = Shader.PropertyToID("_SwarmStyle");
		private static readonly int BugSizeId = Shader.PropertyToID("_BugSize");
		private static readonly int GlowColourId = Shader.PropertyToID("_GlowColour");
		private static readonly int LookId = Shader.PropertyToID("_Look");
		private static readonly int LookMixId = Shader.PropertyToID("_LookMix");
		private static readonly int OwnBackId = Shader.PropertyToID("_OwnBack");
		private static readonly int OwnBellyId = Shader.PropertyToID("_OwnBelly");
		private static readonly int OwnAccentId = Shader.PropertyToID("_OwnAccent");

		/// <summary>The project's shader, for the editor when the profile does not reference it yet.</summary>
		public const string ShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishAmbientLife.shader";
		public const string ShaderName = "FishMMO/Ambient Life";

		private static bool hooked;
		private static int updatedFrame = -1;

		/// <summary>What the system is doing, for the log and the console. Logged when it changes.</summary>
		public static string Status { get; private set; } = "not started";
		private static string loggedReason;
		private static uint countedSeed;

		/// <summary>The kinds (code, not the profile: see <see cref="AmbientLifeSettings"/>).</summary>
		public static readonly AmbientCreatureKind[] Kinds = AmbientLifeCatalogue.Defaults();

		private static Mesh[] meshes = new Mesh[0];
		private static Material[] materials = new Material[0];
		private static Shader builtWith;
		private static readonly float[] activity = new float[Kinds.Length];

		// The placement: decided cells, for one scene and one perch index.
		private sealed class Cell
		{
			public readonly List<AmbientGroup> Groups = new List<AmbientGroup>();
			public AmbientMemberState[] States = Array.Empty<AmbientMemberState>();
			public List<Vector4> Perches;
			/// <summary>The same trees' trunks, index for index with <see cref="Perches"/>.</summary>
			public List<AmbientTrunk> Trunks;
			/// <summary>The cave-life spots in the cell (AmbientCaveLife), indexed from a cave group's <see cref="AmbientGroup.Cave"/> − 1; null for none.</summary>
			public List<SceneCaveLifeSpot> CaveSpots;
			public bool Near, Far;
		}

		/// <summary>One cave-life spot filed by cell: the scene it is in (its placement seed) and its index there.</summary>
		private struct CaveSpotRef
		{
			public uint SceneSeed;
			public int Index;
			public SceneCaveLifeSpot Spot;
		}
		private static readonly Dictionary<long, List<CaveSpotRef>> caveCells = new Dictionary<long, List<CaveSpotRef>>();
		private static int caveVersion = -1;
		private static float caveCellMetres;
		/// <summary>Whether the camera is inside a cave this frame (CaveVisibility): from outside, the deep cave's life is behind the hill.</summary>
		private static bool eyeInCave;
		private static readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
		private static uint placedSeed;
		private static float placedCellMetres;
		private static bool scenesChanged = true;
		private static readonly AmbientPerchIndex perches = new AmbientPerchIndex();
		private static readonly TerrainWorld world = new TerrainWorld();
		private static readonly List<AmbientGroup> scratchGroups = new List<AmbientGroup>();

		// This frame's drawing.
		private struct Candidate
		{
			public Cell Cell;
			public int Group;
			public float Distance;
			/// <summary>The share of its kind about where it is (the land's moment, or its cave's).</summary>
			public float About;
		}
		private sealed class ByDistance : IComparer<Candidate>
		{
			public int Compare(Candidate a, Candidate b) => a.Distance.CompareTo(b.Distance);
		}
		private static readonly ByDistance byDistance = new ByDistance();
		private static readonly List<Candidate> candidates = new List<Candidate>();
		private static readonly List<AmbientInstance>[] byKind = new List<AmbientInstance>[Kinds.Length];
		private static readonly List<AmbientInstance> scratch = new List<AmbientInstance>();
		private static readonly Matrix4x4[] matrices = new Matrix4x4[Batch];
		private static readonly Vector4[] anims = new Vector4[Batch];
		private static readonly Vector4[] tints = new Vector4[Batch];
		private static readonly Vector4[] looks = new Vector4[Batch];
		// The biome looks the placed groups wear were chosen from: re-placed when they change (a scene's palette is
		// built a frame or so after it loads, and again when the looks are switched off or on).
		private static string placedLooks;
		private static bool placedLooksOn;
		private static MaterialPropertyBlock block;
		private static readonly Plane[] planes = new Plane[6];
		private static readonly AmbientThreats threats = new AmbientThreats();
		private static readonly Dictionary<long, Vector3> lastThreatPositions = new Dictionary<long, Vector3>();
		private static Vector3 lastEye;
		private static float lastEyeTime = -1f;
		private static Vector3 drawnEye;
		private static int drawnCount;

		/// <summary>The default settings, used when no profile is loaded or it has none.</summary>
		private static readonly AmbientLifeSettings defaults = new AmbientLifeSettings();

		private static AmbientLifeSettings Settings => WeatherRenderProfile.Active?.AmbientLife ?? defaults;

		private static Shader AmbientShader
		{
			get
			{
				Shader shader = WeatherRenderProfile.Active?.AmbientLifeShader;
#if UNITY_EDITOR
				if (shader == null)
				{
					shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
				}
#endif
				return shader != null ? shader : Shader.Find(ShaderName);
			}
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			Unhook();
			updatedFrame = -1;
			loggedReason = null;
			countedSeed = 0;
			Status = "not started";
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			scenesChanged = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
			Application.quitting += Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
		}

		private static void Unhook()
		{
			if (!hooked)
			{
				return;
			}
			hooked = false;
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			Application.quitting -= Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
		}

#if UNITY_EDITOR
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
			{
				Shutdown();
			}
		}
#endif

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => scenesChanged = true;
		private static void OnSceneUnloaded(Scene scene) => scenesChanged = true;

		private static void Shutdown()
		{
			Release();
			Unhook();
		}

		private static void Release()
		{
			for (int i = 0; i < meshes.Length; i++)
			{
				Destroy(meshes[i]);
				Destroy(materials[i]);
			}
			meshes = new Mesh[0];
			materials = new Material[0];
			builtWith = null;
			cells.Clear();
			perches.Clear();
			caveCells.Clear();
			caveVersion = -1;
			lastThreatPositions.Clear();
			scenesChanged = true;
			for (int k = 0; k < byKind.Length; k++)
			{
				byKind[k]?.Clear();
			}
		}

		private static void Destroy(UnityEngine.Object o)
		{
			if (o == null)
			{
				return;
			}
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(o);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(o);
			}
		}

		// ── Once a frame: who is about, and where ─────────────────────

		/// <summary>This system's once-a-frame work before the cameras render, named in the profiler.</summary>
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("AmbientLifeSystem.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using (ContextMarker.Auto())
			{
				Camera eye = null;
				foreach (Camera camera in cameras)
				{
					if (camera != null && camera.cameraType == CameraType.Game && camera == Camera.main)
					{
						eye = camera;
						break;
					}
				}
				if (eye == null)
				{
					foreach (Camera camera in cameras)
					{
						if (camera != null && (camera.cameraType == CameraType.Game || camera.cameraType == CameraType.SceneView))
						{
							eye = camera;
							break;
						}
					}
				}
				if (eye != null)
				{
					UpdateFrame(eye);
				}
			}
		}

		private static void UpdateFrame(Camera camera)
		{
			if (updatedFrame == Time.frameCount)
			{
				return;
			}
			updatedFrame = Time.frameCount;
			for (int k = 0; k < byKind.Length; k++)
			{
				(byKind[k] ??= new List<AmbientInstance>()).Clear();
			}
			drawnCount = 0;
			bool main = camera.cameraType == CameraType.Game;
			AmbientLifeSettings settings = Settings;
			if (!settings.Enabled || settings.MaxVisible <= 0)
			{
				Report(main, "idle: switched off on the Weather Render Profile (Ambient life)");
				return;
			}
			Shader shader = AmbientShader;
			if (shader == null || !shader.isSupported)
			{
				Report(main, shader == null ? "idle: no Ambient Life shader (Weather Tools → Create weather render profile references it)" : "idle: FishMMO/Ambient Life is not supported on this device");
				return;
			}
			Vector3 eye = camera.transform.position;
			if (SurfaceWater.IsUnder(eye))
			{
				Report(main, "idle: the camera is under water");
				return;
			}
			if (!world.Refresh())
			{
				Report(main, "idle: no terrain is loaded");
				return;
			}
			EnsureLooks(shader);
			float cellMetres = Mathf.Max(16f, settings.CellMetres);
			// Each group takes its look when it is placed: once the scene's biome looks are built (or switched), the
			// cells are placed again so every group wears its biome's. Only the cells: the perches stand.
			bool looksOn = world.LooksAvailable;
			string looksBuilt = FloraPaletteMap.Describe();
			if (looksOn != placedLooksOn || (looksOn && !ReferenceEquals(looksBuilt, placedLooks)))
			{
				placedLooksOn = looksOn;
				placedLooks = looksBuilt;
				cells.Clear();
				countedSeed = 0;
			}
			if (scenesChanged || world.SceneSeed != placedSeed || cellMetres != placedCellMetres)
			{
				scenesChanged = false;
				perches.Build(cellMetres);
				cells.Clear();
				placedSeed = world.SceneSeed;
				placedCellMetres = cellMetres;
				countedSeed = 0;
				Debug.Log($"[Ambient life] indexed {perches.Count:N0} trees as perches.");
			}
			IndexCaves(cellMetres);
			eyeInCave = false;
			foreach (CaveVisibility cave in CaveVisibility.Live)
			{
				if (cave != null && cave.Holds(eye))
				{
					eyeInCave = true;
					break;
				}
			}

			AmbientConditions conditions = Conditions();
			for (int k = 0; k < Kinds.Length; k++)
			{
				activity[k] = AmbientGating.Activity(Kinds[k], conditions);
			}
			Threats(camera, eye, settings.Flush && main);
			bool settled = Place(settings, eye, cellMetres);
			drawnCount = Evaluate(settings, camera, eye, conditions);
			drawnEye = eye;
			Report(main, "drawing");
			if (main && settled && countedSeed != world.SceneSeed)
			{
				countedSeed = world.SceneSeed;
				Debug.Log($"[Ambient life] {Census()}; {drawnCount} drawn now ({conditions.Light:0.00} light, {conditions.Temperature * 33.1f:0} °C, rain {conditions.Rain:0.00}).");
			}
		}

		/// <summary>Logs the system's state when it changes (the game camera's only).</summary>
		private static void Report(bool main, string reason)
		{
			if (!main)
			{
				return;
			}
			Status = reason;
			if (reason != loggedReason)
			{
				loggedReason = reason;
				Debug.Log(reason == "drawing" ? "[Ambient life] drawing: placing the land's animals round the camera (a count follows once they are placed)." : $"[Ambient life] {reason}.");
			}
		}

		private static string Census()
		{
			var counts = new int[Kinds.Length];
			int animals = 0, dressed = 0, groups = 0, underground = 0;
			var variants = new int[BiomeLooks.Variants];
			foreach (Cell cell in cells.Values)
			{
				foreach (AmbientGroup g in cell.Groups)
				{
					counts[g.Kind]++;
					animals += g.Count;
					groups++;
					underground += g.Cave > 0 ? 1 : 0;
					if (AmbientLooks.Has(g.Look))
					{
						dressed++;
						variants[Mathf.Min(g.LookVariant, variants.Length - 1)]++;
					}
				}
			}
			var parts = new List<string>();
			for (int k = 0; k < counts.Length; k++)
			{
				parts.Add($"{Kinds[k].Name} {counts[k]} (about: {activity[k]:0.00})");
			}
			string dressing = dressed > 0 ? $"{dressed}/{groups} groups in their biome's looks (by look: {string.Join(" ", variants)})" : "no biome looks (kinds' own colours)";
			return $"{cells.Count} cells placed: {animals} animals in groups of {string.Join(", ", parts)}; {underground} of the groups in caves; {dressing}";
		}

		/// <summary>The moment, from the weather at the viewer (a mild clear day where there is no weather).</summary>
		private static AmbientConditions Conditions()
		{
			WeatherContext context;
			WeatherFrame frame;
			WeatherPresentation presentation = WeatherPresentation.Instance;
			if (presentation != null && presentation.HasContext)
			{
				context = presentation.Context;
				frame = presentation.Shown;
			}
			else
			{
				context = WeatherClient.LastContext;
				frame = WeatherClient.LastFrame;
				if (!context.Scene.IsValid())
				{
					return AmbientConditions.MildDay;
				}
			}
			AmbientConditions.Daylight(context.LocalTime01, context.IsDaylight, out float light, out float twilight, out bool evening);
			float precipitation = frame[WeatherChannel.Precipitation];
			Vector4 season = Shader.GetGlobalVector(SeasonId);
			return new AmbientConditions
			{
				Light = light,
				Twilight = twilight,
				Evening = evening,
				Temperature = context.Temperature,
				Rain = precipitation * frame[WeatherChannel.RainWeight],
				Snow = precipitation * (frame[WeatherChannel.SnowWeight] + frame[WeatherChannel.HailWeight]),
				Storm = frame.StormSeverity,
				Wind = frame[WeatherChannel.WindSpeed],
				Cloud = frame[WeatherChannel.CloudCover],
				Summer = season.w > 0f ? season.y : 0f,
			};
		}

		/// <summary>Who frightens the animals this frame: the camera and the characters near it, each with its speed.</summary>
		private static void Threats(Camera camera, Vector3 eye, bool enabled)
		{
			threats.Clear();
			if (!enabled)
			{
				return;
			}
			float now = Time.time;
			float dt = lastEyeTime >= 0f ? Mathf.Max(1e-3f, now - lastEyeTime) : 0f;
			float eyeSpeed = dt > 0f ? Mathf.Min(12f, Vector3.Distance(eye, lastEye) / dt) : 0f;
			lastEye = eye;
			lastEyeTime = now;
			threats.Add(eye, eyeSpeed);
			float step = Mathf.Max(1e-3f, Time.deltaTime);
			foreach (KeyValuePair<long, ICharacter> pair in BaseCharacter.ClientCharacters)
			{
				ICharacter character = pair.Value;
				Transform t = character?.Transform;
				if (t == null)
				{
					continue;
				}
				Vector3 at = t.position;
				if ((at - eye).sqrMagnitude > ThreatReach * ThreatReach)
				{
					continue;
				}
				float speed = lastThreatPositions.TryGetValue(pair.Key, out Vector3 before) ? Mathf.Min(12f, Vector3.Distance(at, before) / step) : 0f;
				lastThreatPositions[pair.Key] = at;
				threats.Add(at, speed);
			}
			if (lastThreatPositions.Count > 256)
			{
				lastThreatPositions.Clear();
			}
		}

		// ── Looks ─────────────────────────────────────────────────────

		private static void EnsureLooks(Shader shader)
		{
			if (builtWith == shader && meshes.Length == Kinds.Length)
			{
				return;
			}
			for (int i = 0; i < meshes.Length; i++)
			{
				Destroy(meshes[i]);
				Destroy(materials[i]);
			}
			meshes = new Mesh[Kinds.Length];
			materials = new Material[Kinds.Length];
			builtWith = shader;
			for (int k = 0; k < Kinds.Length; k++)
			{
				AmbientCreatureKind kind = Kinds[k];
				meshes[k] = AmbientCreatureMeshes.Build(kind);
				var m = new Material(shader) { name = "Ambient life: " + kind.Name, hideFlags = HideFlags.DontSave, enableInstancing = true };
				Vector3 shoulder = AmbientCreatureMeshes.ShoulderOf(kind.Shape);
				m.SetFloat(ModeId, AmbientCreatureMeshes.ModeOf(kind.Shape));
				m.SetFloat(AmplitudeId, StrokeOf(kind.Shape));
				m.SetFloat(DihedralId, DihedralOf(kind.Shape));
				m.SetFloat(SmoothnessId, kind.Smoothness);
				m.SetVector(ShoulderId, new Vector4(shoulder.x, shoulder.y, shoulder.z, shoulder.z + 0.07f));
				// The biome looks (AmbientLooks): how far this kind takes them, and its own colours the mesh's roles swap out.
				m.SetVector(LookMixId, AmbientLooks.MixOf(kind).Shader);
				m.SetVector(OwnBackId, (Vector4)kind.Back.linear);
				m.SetVector(OwnBellyId, (Vector4)kind.Belly.linear);
				m.SetVector(OwnAccentId, (Vector4)kind.Accent.linear);
				if (kind.Shape == AmbientShape.Swarm)
				{
					// A swarm's insects are drawn at their own size (no SizeScale: that widens the cloud), and a firefly's light.
					Color glow = kind.Accent.linear * Mathf.Max(0f, kind.Glow);
					m.SetFloat(SwarmStyleId, (float)kind.SwarmStyle);
					m.SetFloat(BugSizeId, (kind.Length.x + kind.Length.y) * 0.5f * Mathf.Max(1f, Settings.SwarmSpeckScale));
					m.SetVector(GlowColourId, new Vector4(glow.r, glow.g, glow.b, kind.Glow > 0f ? 1f : 0f));
				}
				m.renderQueue = (int)RenderQueue.Geometry;
				materials[k] = m;
			}
		}

		/// <summary>
		/// The stroke per body: a bird's beat half-angle in radians (a finch's wings sweep through over a hundred
		/// degrees, a vulture's barely move), a walker's stride as a share of its length.
		/// </summary>
		private static float StrokeOf(AmbientShape shape)
		{
			switch (shape)
			{
				case AmbientShape.Songbird: return 1f;
				case AmbientShape.Crow: return 0.75f;
				case AmbientShape.Gull: return 0.6f;
				case AmbientShape.Raptor: return 0.45f;
				case AmbientShape.Vulture: return 0.35f;
				case AmbientShape.Duck: return 0.9f;
				case AmbientShape.Bat: return 1.1f;
				case AmbientShape.Rat: return 0.16f;
				case AmbientShape.Mouse: return 0.18f;
				case AmbientShape.Squirrel: return 0.22f;
				case AmbientShape.Rabbit: return 0.25f;
				case AmbientShape.Frog: return 0.3f;
				case AmbientShape.Lizard: return 0.14f;
				case AmbientShape.Owl: return 0.7f;
				case AmbientShape.Goose: return 0.65f;
				case AmbientShape.Butterfly: return 1.2f;
				case AmbientShape.Dragonfly: return 0.3f;
				case AmbientShape.Ray: return 0.5f;
				case AmbientShape.Beetle: return 0.14f;
				case AmbientShape.Spider: return 0.1f;
				case AmbientShape.Cricket: return 0.16f;
				default: return 0.12f;
			}
		}

		/// <summary>The wings' gliding angle: a vulture's deep V, a buzzard's shallow one, a gull's nearly flat.</summary>
		private static float DihedralOf(AmbientShape shape)
		{
			switch (shape)
			{
				case AmbientShape.Vulture: return 0.2f;
				case AmbientShape.Raptor: return 0.12f;
				case AmbientShape.Gull: return 0.08f;
				case AmbientShape.Bat: return 0f;
				default: return 0.05f;
			}
		}

		// ── Placement ─────────────────────────────────────────────────

		/// <summary>The farthest any member of a group of a kind strays from its home, metres.</summary>
		private static float Reach(AmbientCreatureKind kind, in AmbientGroup g)
		{
			switch (kind.Behaviour)
			{
				case AmbientBehaviour.Flock: return g.Radius * 1.4f + kind.Spread * 2f;
				case AmbientBehaviour.Soar: return g.Radius + 60f;
				case AmbientBehaviour.Hawk: return g.Radius + kind.Spread + 60f; // half of a colony may work a tree line out to 60 m
				case AmbientBehaviour.Perch: return g.Radius * 1.6f + 45f; // and the refuge it may fly to
				case AmbientBehaviour.Paddle: return g.Radius + 60f;
				case AmbientBehaviour.Transit: return g.Radius * 1.05f + kind.Spread * g.Count; // its crossing (a chord of its circle, weaving) and a V's arms
				default: return g.Radius + kind.Spread + 8f;
			}
		}

		/// <summary>Decides the cells within reach not decided yet, a few a frame; true once all are.</summary>
		private static bool Place(AmbientLifeSettings settings, Vector3 eye, float cellMetres)
		{
			float near = Mathf.Max(Mathf.Max(settings.SoloBirdDistance + 60f, settings.SwarmDistance + 20f), Mathf.Max(settings.BatDistance + 70f, settings.CritterDistance + 12f));
			float far = Mathf.Max(settings.FlockDistance + 180f, settings.RaptorDistance + 400f);
			int nearRange = Mathf.CeilToInt(near / cellMetres), farRange = Mathf.CeilToInt(far / cellMetres);
			int cx = Mathf.FloorToInt(eye.x / cellMetres), cz = Mathf.FloorToInt(eye.z / cellMetres);
			int budget = CellsPerFrame;
			bool settled = true;
			foreach (Vector2Int offset in OffsetsWithin(farRange))
			{
				if (budget <= 0)
				{
					settled = false;
					break;
				}
				int x = cx + offset.x, z = cz + offset.y;
				bool wantNear = Mathf.Abs(offset.x) <= nearRange && Mathf.Abs(offset.y) <= nearRange;
				long key = AmbientPerchIndex.Key(x, z);
				cells.TryGetValue(key, out Cell cell);
				if (cell != null && cell.Far && (cell.Near || !wantNear))
				{
					continue;
				}
				if (!world.Overlaps(x * cellMetres, z * cellMetres, cellMetres))
				{
					continue;
				}
				if (cell == null)
				{
					cell = new Cell { Perches = perches.In(x, z), Trunks = perches.TrunksIn(x, z) };
				}
				bool placed = true;
				if (!cell.Far)
				{
					placed &= PlaceTier(cell, settings, x, z, cellMetres, true);
					cell.Far = placed;
				}
				if (placed && wantNear && !cell.Near)
				{
					placed &= PlaceTier(cell, settings, x, z, cellMetres, false);
					cell.Near = placed;
				}
				budget--;
				if (cell.Far)
				{
					cells[key] = cell;
				}
			}
			// Forget cells well out of reach, so a long walk does not keep the whole land.
			if (cells.Count > (2 * farRange + 3) * (2 * farRange + 3))
			{
				scratchKeys.Clear();
				foreach (long key in cells.Keys)
				{
					int x = (int)(key >> 32), z = (int)(key & 0xFFFFFFFF);
					if (Math.Abs(x - cx) > farRange + 1 || Math.Abs(z - cz) > farRange + 1)
					{
						scratchKeys.Add(key);
					}
				}
				foreach (long key in scratchKeys)
				{
					cells.Remove(key);
				}
			}
			return settled;
		}

		private static readonly List<long> scratchKeys = new List<long>();

		/// <summary>One tier of a cell's kinds (far: flocks and raptors; near: the rest), with fright states for their members.</summary>
		private static bool PlaceTier(Cell cell, AmbientLifeSettings settings, int x, int z, float cellMetres, bool far)
		{
			scratchGroups.Clear();
			for (int k = 0; k < Kinds.Length; k++)
			{
				AmbientCreatureKind kind = Kinds[k];
				if (AmbientLifeSettings.IsFar(kind.Family) != far)
				{
					continue;
				}
				if (!AmbientLifeMotion.TryPlace(kind, k, settings.DensityOf(kind.Family), world.SceneSeed, x, z, cellMetres, world, cell.Perches, scratchGroups, cell.Trunks))
				{
					return false;
				}
			}
			// The caves under the cell, with the near tier (their life is all near: critters, bats, swarms).
			if (!far && caveCells.TryGetValue(AmbientPerchIndex.Key(x, z), out List<CaveSpotRef> caves))
			{
				cell.CaveSpots ??= new List<SceneCaveLifeSpot>();
				foreach (CaveSpotRef cave in caves)
				{
					cell.CaveSpots.Add(cave.Spot);
					int slot = cell.CaveSpots.Count;
					for (int k = 0; k < Kinds.Length; k++)
					{
						AmbientCreatureKind kind = Kinds[k];
						if (AmbientCaveLife.LivesInCaves(kind))
						{
							AmbientCaveLife.TryPlace(kind, k, settings.DensityOf(kind.Family), cave.SceneSeed, cave.Index, cave.Spot, slot, world, scratchGroups);
						}
					}
				}
			}
			int states = cell.States.Length;
			for (int i = 0; i < scratchGroups.Count; i++)
			{
				AmbientGroup g = scratchGroups[i];
				g.StateFirst = states;
				states += g.Count;
				cell.Groups.Add(g);
			}
			if (states != cell.States.Length)
			{
				Array.Resize(ref cell.States, states);
			}
			return true;
		}

		/// <summary>
		/// Files the loaded scenes' cave-life spots (<see cref="SceneCaveLife"/>) by cell, again whenever they change; the cells are
		/// placed afresh then, so a cave scene loaded after its terrain's cells were placed still gets its life.
		/// </summary>
		private static void IndexCaves(float cellMetres)
		{
			if (SceneCaveLife.Version == caveVersion && cellMetres == caveCellMetres)
			{
				return;
			}
			caveVersion = SceneCaveLife.Version;
			caveCellMetres = cellMetres;
			caveCells.Clear();
			int count = 0;
			foreach (SceneCaveLife.Set set in SceneCaveLife.Sets)
			{
				if (set.Spots == null)
				{
					continue;
				}
				uint seed = SeaLifePlacement.SceneSeed(set.Scene.name);
				for (int i = 0; i < set.Spots.Count; i++)
				{
					SceneCaveLifeSpot spot = set.Spots[i];
					long key = AmbientPerchIndex.Key(Mathf.FloorToInt(spot.Position.x / cellMetres), Mathf.FloorToInt(spot.Position.z / cellMetres));
					if (!caveCells.TryGetValue(key, out List<CaveSpotRef> list))
					{
						caveCells[key] = list = new List<CaveSpotRef>();
					}
					list.Add(new CaveSpotRef { SceneSeed = seed, Index = i, Spot = spot });
					count++;
				}
			}
			cells.Clear();
			countedSeed = 0;
			if (count > 0)
			{
				Debug.Log($"[Ambient life] indexed {count:N0} cave-life spots in {SceneCaveLife.Sets.Count} scene(s).");
			}
		}

		private static readonly List<Vector2Int> offsets = new List<Vector2Int>();
		private static int offsetsRange = -1;

		/// <summary>Every cell offset within a square range, nearest first (cached per range).</summary>
		private static List<Vector2Int> OffsetsWithin(int range)
		{
			if (range != offsetsRange)
			{
				offsetsRange = range;
				offsets.Clear();
				for (int z = -range; z <= range; z++)
				{
					for (int x = -range; x <= range; x++)
					{
						offsets.Add(new Vector2Int(x, z));
					}
				}
				offsets.Sort((a, b) => a.sqrMagnitude != b.sqrMagnitude ? a.sqrMagnitude.CompareTo(b.sqrMagnitude) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
			}
			return offsets;
		}

		// ── Evaluation and drawing ────────────────────────────────────

		/// <summary>Works out every animal in reach, nearest groups first up to the budget; returns how many.</summary>
		private static int Evaluate(AmbientLifeSettings settings, Camera camera, Vector3 eye, in AmbientConditions conditions)
		{
			GeometryUtility.CalculateFrustumPlanes(camera, planes);
			candidates.Clear();
			foreach (Cell cell in cells.Values)
			{
				for (int i = 0; i < cell.Groups.Count; i++)
				{
					AmbientGroup g = cell.Groups[i];
					AmbientCreatureKind kind = Kinds[g.Kind];
					bool underground = g.Cave > 0 && cell.CaveSpots != null && g.Cave <= cell.CaveSpots.Count;
					SceneCaveLifeSpot spot = underground ? cell.CaveSpots[g.Cave - 1] : default;
					// From outside, the cave past its twilight is behind the hill: only a mouth's and the twilight's life is seen.
					if (underground && !eyeInCave && spot.Kind != SceneCaveLifeKind.Mouth && spot.Dark > 0.35f)
					{
						continue;
					}
					float about = underground ? AmbientCaveLife.Activity(kind, conditions, g.Steady, spot.Dark) : activity[g.Kind];
					if (!kind.Enabled || about <= g.Rank)
					{
						continue;
					}
					float reach = underground ? AmbientCaveLife.Reach(kind, g, spot) : Reach(kind, g);
					float lift = !underground && (kind.Behaviour == AmbientBehaviour.Flock || kind.Behaviour == AmbientBehaviour.Soar || kind.Behaviour == AmbientBehaviour.Transit) ? g.Altitude : 0f;
					Vector3 middle = underground ? spot.Position : g.Home + Vector3.up * lift;
					float distance = Vector3.Distance(eye, middle) - reach;
					if (distance > settings.DistanceOf(kind.Family))
					{
						continue;
					}
					if (distance > OffscreenReach)
					{
						var box = new Bounds(middle, new Vector3(reach * 2f, reach + lift + 30f, reach * 2f));
						if (!GeometryUtility.TestPlanesAABB(planes, box))
						{
							continue;
						}
					}
					candidates.Add(new Candidate { Cell = cell, Group = i, Distance = Mathf.Max(0f, distance), About = about });
				}
			}
			candidates.Sort(byDistance);

			var frame = new AmbientLifeMotion.Frame
			{
				Seconds = WorldMotion.Seconds,
				Conditions = conditions,
				World = world,
				Threats = threats,
			};
			int budget = settings.MaxVisible;
			int drawn = 0;
			foreach (Candidate c in candidates)
			{
				if (budget <= 0)
				{
					break;
				}
				AmbientGroup g = c.Cell.Groups[c.Group];
				AmbientCreatureKind kind = Kinds[g.Kind];
				frame.Perches = c.Cell.Perches;
				frame.Trunks = c.Cell.Trunks;
				frame.States = c.Cell.States;
				frame.CaveSpots = c.Cell.CaveSpots;
				// A group comes out (and goes back) by dissolving as the moment's share crosses its rank.
				frame.Visible = Mathf.Clamp01((c.About - g.Rank) / 0.06f);
				scratch.Clear();
				AmbientLifeMotion.Evaluate(g, kind, ref frame, scratch);
				float distance = settings.DistanceOf(kind.Family);
				float fadeStart = distance * (1f - Mathf.Clamp(settings.FadeBand, 0.05f, 0.5f));
				// Underground the water is the cave's own (an olm swims under its pool's surface), and a lake on the hill over a
				// cave is not over its floor.
				bool grounded = g.Cave == 0 && (kind.Family == AmbientFamily.Critters || kind.Behaviour == AmbientBehaviour.Perch);
				List<AmbientInstance> list = byKind[g.Kind];
				foreach (AmbientInstance instance in scratch)
				{
					Vector3 at = instance.Matrix.GetColumn(3);
					float d = Vector3.Distance(eye, at);
					if (d >= distance)
					{
						continue;
					}
					// The tide and the rivers move: nothing walks about under them.
					if (grounded && SurfaceWater.TryGetSurfaceAt(at.x, at.z, out float water) && water > at.y + 0.03f)
					{
						continue;
					}
					AmbientInstance drawnInstance = instance;
					if (d > fadeStart)
					{
						drawnInstance.Tint.w *= 1f - (d - fadeStart) / Mathf.Max(0.01f, distance - fadeStart);
					}
					list.Add(drawnInstance);
					drawn++;
					budget--;
				}
			}
			return drawn;
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (camera.cameraType == CameraType.Game && camera != Camera.main)
			{
				return;
			}
			UpdateFrame(camera);
			if (drawnCount == 0)
			{
				return;
			}
			AmbientLifeSettings settings = Settings;
			block ??= new MaterialPropertyBlock();
			float shadowSq = settings.ShadowDistance * settings.ShadowDistance;
			for (int k = 0; k < byKind.Length && k < meshes.Length; k++)
			{
				List<AmbientInstance> list = byKind[k];
				if (list == null || list.Count == 0 || meshes[k] == null || materials[k] == null)
				{
					continue;
				}
				bool shadows = Kinds[k].CastShadows && settings.ShadowDistance > 0f;
				for (int first = 0; first < list.Count; first += Batch)
				{
					int count = Mathf.Min(Batch, list.Count - first);
					bool anyNear = false;
					for (int i = 0; i < count; i++)
					{
						AmbientInstance a = list[first + i];
						float grow = Mathf.Max(0.1f, settings.SizeScale) * (settings.DebugHighlight ? settings.DebugScale : 1f);
						matrices[i] = Mathf.Approximately(grow, 1f) ? a.Matrix : a.Matrix * Matrix4x4.Scale(Vector3.one * grow);
						anims[i] = a.Anim;
						tints[i] = settings.DebugHighlight ? new Vector4(1f, 0f, 1f, a.Tint.w) : a.Tint;
						looks[i] = settings.DebugHighlight ? Vector4.zero : a.Look;
						if (shadows && !anyNear && ((Vector3)a.Matrix.GetColumn(3) - drawnEye).sqrMagnitude <= shadowSq)
						{
							anyNear = true;
						}
					}
					block.Clear();
					block.SetVectorArray(AnimId, anims);
					block.SetVectorArray(TintId, tints);
					block.SetVectorArray(LookId, looks);
					Graphics.DrawMeshInstanced(meshes[k], 0, materials[k], matrices, count, block,
						anyNear ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0, camera);
				}
			}
			if (settings.DebugHighlight && camera.cameraType == CameraType.Game && Time.realtimeSinceStartup >= nextDebugLog)
			{
				nextDebugLog = Time.realtimeSinceStartup + 3f;
				LogWhereTheyAre(camera);
			}
		}

		private static float nextDebugLog;

		/// <summary>
		/// Debug (<see cref="AmbientLifeSettings.DebugHighlight"/>): how many animals are drawn and where they are against the
		/// camera's view (in it, above, below, to the sides, behind), per kind, with the nearest in-view one's distance and
		/// on-screen length. Read from Editor.log.
		/// </summary>
		private static void LogWhereTheyAre(Camera camera)
		{
			var sb = new System.Text.StringBuilder();
			sb.Append("[Ambient life] debug at camera ").Append(camera.transform.position.ToString("F0"))
				.Append(" pitch ").Append(camera.transform.eulerAngles.x.ToString("F0")).Append(": ");
			float pixelsPerRadian = camera.pixelHeight / Mathf.Max(1e-3f, camera.fieldOfView * Mathf.Deg2Rad);
			for (int k = 0; k < byKind.Length; k++)
			{
				List<AmbientInstance> list = byKind[k];
				if (list == null || list.Count == 0)
				{
					continue;
				}
				int inView = 0, above = 0, below = 0, behind = 0, sides = 0;
				float nearest = float.MaxValue, pixels = 0f;
				foreach (AmbientInstance a in list)
				{
					Vector3 vp = camera.WorldToViewportPoint(a.Matrix.GetColumn(3));
					if (vp.z <= 0f) { behind++; continue; }
					if (vp.y > 1f) { above++; continue; }
					if (vp.y < 0f) { below++; continue; }
					if (vp.x < 0f || vp.x > 1f) { sides++; continue; }
					inView++;
					if (vp.z < nearest)
					{
						nearest = vp.z;
						pixels = Kinds[k].Length.y / Mathf.Max(0.1f, vp.z) * pixelsPerRadian;
					}
				}
				sb.Append(Kinds[k].Name).Append(' ').Append(list.Count).Append(" drawn: ").Append(inView).Append(" in view");
				if (inView > 0)
				{
					sb.Append(" (nearest ").Append(nearest.ToString("F0")).Append(" m, ").Append(pixels.ToString("F0")).Append(" px long)");
				}
				sb.Append(", ").Append(above).Append(" above, ").Append(below).Append(" below, ").Append(sides).Append(" sides, ").Append(behind).Append(" behind; ");
			}
			Debug.Log(sb.ToString());
		}

		// ── The world ─────────────────────────────────────────────────

		/// <summary>The loaded terrains as the ground, the water over them, and the scene's biomes and climate.</summary>
		private sealed class TerrainWorld : IAmbientWorld
		{
			private readonly List<Terrain> terrains = new List<Terrain>();
			private readonly Dictionary<BiomeTemplate, AmbientHabitat> habitats = new Dictionary<BiomeTemplate, AmbientHabitat>();
			private Terrain last;
			private Rect extent;
			private WorldSceneSettings settings;
			/// <summary>
			/// A cloud world's island tiles' holes (open sky between them), read once a tile; empty anywhere else, where a
			/// hole is a cave's mouth or a wall's bed and the ground's height there is still where life is placed.
			/// </summary>
			private readonly Dictionary<TerrainData, TerrainHoleMask> skyHoles = new Dictionary<TerrainData, TerrainHoleMask>();
			private bool cloudWorld;
			private bool seaPresent;
			private float seaMean;

			/// <summary>The placement seed: the name of the scene the terrains belong to.</summary>
			public uint SceneSeed { get; private set; }

			/// <summary>
			/// What lives on the scene's planet or moon, from its air (<see cref="AmbientGating.WorldOf"/>): nothing on an
			/// airless one, whatever its biomes are called. A scene with no settings is on the home world.
			/// </summary>
			public AmbientWorld World { get; private set; } = AmbientWorld.Earthly;

			/// <summary>Takes this frame's terrains and sea; false when no terrain is loaded.</summary>
			public bool Refresh()
			{
				Terrain.GetActiveTerrains(terrains);
				if (terrains.Count == 0 || terrains[0] == null)
				{
					return false;
				}
				Scene scene = terrains[0].gameObject.scene;
				SceneSeed = SeaLifePlacement.SceneSeed(scene.name);
				if (settings == null || settings.gameObject.scene != scene)
				{
					WorldSceneSettings.TryGetForScene(scene, out settings);
					habitats.Clear();
					World = settings != null ? AmbientGating.WorldOf(settings.WorldConditions) : AmbientWorld.Earthly;
				}
				if (last == null || !terrains.Contains(last))
				{
					last = null;
				}
				bool cloud = SceneCloudWorld.Any;
				if (cloud != cloudWorld)
				{
					cloudWorld = cloud;
					skyHoles.Clear();
				}
				extent = default;
				bool first = true;
				foreach (Terrain t in terrains)
				{
					if (t == null || t.terrainData == null)
					{
						continue;
					}
					Vector3 origin = t.transform.position;
					Vector3 size = t.terrainData.size;
					var r = new Rect(origin.x, origin.z, size.x, size.z);
					extent = first ? r : Rect.MinMaxRect(Mathf.Min(extent.xMin, r.xMin), Mathf.Min(extent.yMin, r.yMin), Mathf.Max(extent.xMax, r.xMax), Mathf.Max(extent.yMax, r.yMax));
					first = false;
				}
				// The sea at its mean level (the water publishes it), so placement never depends on the tide.
				seaPresent = SurfaceWater.TryGetLevel(out float level) && !float.IsInfinity(level);
				if (seaPresent)
				{
					seaMean = Shader.GetGlobalFloat(MeanLevelId);
					if (Mathf.Abs(level - seaMean) > SeaLifePlacement.LowTideMetres + 1f)
					{
						seaMean = level;
					}
				}
				return !first;
			}

			public bool Overlaps(float x, float z, float side)
			{
				return x + side > extent.xMin && z + side > extent.yMin && x < extent.xMax && z < extent.yMax;
			}

			public bool TryGround(float x, float z, out float y)
			{
				if (Contains(last, x, z))
				{
					y = OverSky(last, x, z) ? float.PositiveInfinity : last.SampleHeight(new Vector3(x, 0f, z)) + last.transform.position.y;
					return true;
				}
				foreach (Terrain t in terrains)
				{
					if (t != last && Contains(t, x, z))
					{
						last = t;
						y = OverSky(t, x, z) ? float.PositiveInfinity : t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
						return true;
					}
				}
				// No terrain here: past the scene's edge or in a corner its round cut leaves empty. A scene's tiles
				// load with it, so this is never "not loaded yet".
				y = float.PositiveInfinity;
				return true;
			}

			/// <summary>
			/// True over a cloud world's open sky: no ground there, as past the scene's edge, so nothing that walks is put
			/// in the air between the islands (where the heightmap holds its islands' rim heights).
			/// </summary>
			private bool OverSky(Terrain t, float x, float z)
			{
				if (!cloudWorld)
				{
					return false;
				}
				TerrainData data = t.terrainData;
				if (!skyHoles.TryGetValue(data, out TerrainHoleMask mask))
				{
					mask = TerrainHoleMask.Read(data);
					skyHoles[data] = mask;
				}
				if (mask == null)
				{
					return false;
				}
				Vector3 origin = t.transform.position;
				Vector3 size = data.size;
				return mask.HoleAt((x - origin.x) / size.x, (z - origin.z) / size.z);
			}

			private static bool Contains(Terrain t, float x, float z)
			{
				if (t == null || t.terrainData == null)
				{
					return false;
				}
				Vector3 origin = t.transform.position;
				Vector3 size = t.terrainData.size;
				return x >= origin.x && z >= origin.z && x <= origin.x + size.x && z <= origin.z + size.z;
			}

			public float WaterAt(float x, float z, out bool sea)
			{
				float inland = SurfaceWater.TryGetInlandSurfaceAt(x, z, out float lake) ? lake : float.NegativeInfinity;
				float level = seaPresent ? seaMean : float.NegativeInfinity;
				sea = seaPresent && level >= inland;
				return Mathf.Max(level, inland);
			}

			public float LiveWaterAt(float x, float z)
			{
				return SurfaceWater.TryGetSurfaceAt(x, z, out float level) ? level : float.NegativeInfinity;
			}

			/// <summary>The blade grass standing there (GrassBladeSystem's cover map), for open-ground foraging.</summary>
			public float CoverAt(float x, float z) => GrassBladeSystem.CoverAt(x, z);

			/// <summary>Whether the scene's biome looks are built and switched on (<see cref="FloraPaletteMap"/>).</summary>
			public bool LooksAvailable => FloraPaletteMap.Enabled && FloraPaletteMap.LookAt(Vector3.zero, out _);

			/// <summary>The looks of the biome at a place: the same the grass and flowers there are drawn in.</summary>
			public bool LookAt(Vector3 position, out BiomeLooks.Look look)
			{
				look = null;
				return FloraPaletteMap.Enabled && FloraPaletteMap.LookAt(position, out look);
			}

			/// <summary>Metres past a point of interest's radius its people's (or its ruins') animals still count as at home.</summary>
			private const float SiteMargin = 30f;

			/// <summary>The country from the biome (<see cref="GroundHabitat"/>), with what a point of interest standing there adds.</summary>
			public void Habitat(Vector3 position, out AmbientHabitat habitat, out float temperature)
			{
				GroundHabitat(position, out habitat, out temperature);
				if ((habitat & (AmbientHabitat.Barren | AmbientHabitat.Ice)) == 0 && SceneSiteEmitters.SiteAt(position, SiteMargin, out PointOfInterestKindInfo site))
				{
					habitat |= AmbientGating.HabitatOfSite(site.Group, site.Traits);
				}
			}

			private void GroundHabitat(Vector3 position, out AmbientHabitat habitat, out float temperature)
			{
				temperature = AmbientLifeCatalogue.Celsius(15f);
				float humidity = 0f;
				ScenePlacementClimate placement = settings != null ? settings.PlacementClimate : null;
				bool climate = placement != null && placement.Generated;
				if (climate)
				{
					// The climate the ground was painted from, without the weather's offsets: placement must not
					// depend on the hour it is first seen.
					ClimateSample sample = placement.SampleAt(position, out _);
					temperature = sample.Temperature;
					humidity = sample.Humidity;
				}
				SceneBiomeMap map = settings != null ? settings.BiomeMap : null;
				BiomeTemplate template = map != null ? map.Sample(position) : null;
				if (template == null)
				{
					// A hand-made scene with no biome map: ordinary temperate country with people in it.
					habitat = climate ? AmbientGating.HabitatOfClimate(temperature, humidity) : AmbientHabitat.Grassland | AmbientHabitat.Woodland | AmbientHabitat.Farmland | AmbientHabitat.Settlement;
					return;
				}
				if (!habitats.TryGetValue(template, out habitat))
				{
					habitat = AmbientGating.HabitatOfTemplate(template);
					habitats[template] = habitat;
				}
				if (habitat == AmbientHabitat.None)
				{
					habitat = AmbientGating.HabitatOfClimate(temperature, humidity);
				}
			}
		}
	}
}
