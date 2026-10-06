using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The sea's background life, client only and purely visual: schools of baitfish and jacks, reef fish over
	/// the coral, sharks, mantas, turtles, whales, drifting jellies and crabs on the bed — and the sea's globals
	/// the sea-floor plants sway and stay under the surface by (FishSea.hlsl).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same sea for everyone, with nothing on the wire.</b> Where each group lives is decided per cell
	/// of a grid from the scene's name, the cell, the ground and the climate (<see cref="SeaLifePlacement"/>);
	/// where it is now is a function of the shared world clock (<see cref="WorldDayNightCycle.SkyClockHours"/>,
	/// carried by the server tick). Every player sees the same school turn at the same rock. Nothing here can be
	/// touched or targeted, and the server never knows it exists.
	/// </para>
	/// <para>
	/// <b>Drawn, not spawned.</b> No GameObjects: one procedural mesh and one material per kind
	/// (<see cref="SeaCreatureMeshes"/>, FishMMO/Sea Life), drawn with <c>Graphics.DrawMeshInstanced</c> for
	/// each camera — WebGL2-safe — in batches of 1023, the swimming done in the vertex shader from the phase
	/// the CPU hands each animal. Only cells within the draw distance are placed, a few a frame.
	/// </para>
	/// <para>
	/// <b>Hooked like the blade grass</b>: a static system started after the first scene loads, driven by
	/// the render pipeline's callbacks, its settings on the Weather Render Profile ("Sea life"), its shader
	/// referenced there so a client build includes it.
	/// </para>
	/// </remarks>
	public static class SeaLifeSystem
	{
		private const int Batch = 1023;
		/// <summary>New cells decided a frame at most: placement samples the ground, so it is spread out.</summary>
		private const int CellsPerFrame = 8;

		private static readonly int SeaId = Shader.PropertyToID("_FishSea");
		private static readonly int SurgeId = Shader.PropertyToID("_FishSeaSurge");
		private static readonly int MeanLevelId = Shader.PropertyToID("_FishWaterMeanLevel");
		private static readonly int WaterWindId = Shader.PropertyToID("_FishWaterWind");
		private static readonly int AnimId = Shader.PropertyToID("_Anim");
		private static readonly int TintId = Shader.PropertyToID("_Tint");
		private static readonly int ModeId = Shader.PropertyToID("_Mode");
		private static readonly int AmplitudeId = Shader.PropertyToID("_Amplitude");
		private static readonly int WaveLengthId = Shader.PropertyToID("_WaveLength");
		private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
		private static readonly int TranslucentId = Shader.PropertyToID("_Translucent");
		private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
		private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
		private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

		private static bool hooked;
		private static int publishedFrame = -1;

		// What the camera's sea looks like this frame (Publish).
		private static bool seaPresent;
		private static float meanLevel;
		/// <summary>The surface the animals keep under now: the sea's level less what its waves' troughs need.</summary>
		private static float lifeSurface;

		/// <summary>
		/// What the system is doing, for the log and the console: why nothing is drawn, or what is. Logged
		/// when it changes (and the first full count of a scene once its cells are placed).
		/// </summary>
		public static string Status { get; private set; } = "not started";
		private static string loggedReason;
		private static uint countedSeed;

		// Per kind: its mesh, its material and what they were built from.
		private static Mesh[] meshes = new Mesh[0];
		private static Material[] materials = new Material[0];
		private static int[] lookHashes = new int[0];
		private static Shader builtWith;

		// The placement: decided cells, for one scene and one set of kinds.
		private sealed class Cell
		{
			public readonly List<SeaGroup> Groups = new List<SeaGroup>();
		}
		private static readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
		private static uint placedSeed;
		private static int placedHash;
		private static float placedCellMetres;

		private static readonly List<SeaInstance>[] byKind = new List<SeaInstance>[32];
		private static readonly Matrix4x4[] matrices = new Matrix4x4[Batch];
		private static readonly Vector4[] anims = new Vector4[Batch];
		private static readonly Vector4[] tints = new Vector4[Batch];
		private static MaterialPropertyBlock block;
		private static readonly Plane[] planes = new Plane[6];
		private static readonly TerrainGround ground = new TerrainGround();

		/// <summary>
		/// The clock the sea's motion runs on: the world's shared motion clock (<see cref="WorldMotion.Seconds"/>),
		/// so every player's plants rock and fish swim in step, and never faster than real time however fast a
		/// preview runs the sky (Jim, 2026-10-06: "animated too fast").
		/// </summary>
		/// <remarks>
		/// It had its own copy of that rule: the sky's clock, stepped at most a real frame at a time and drawn
		/// back onto the sky's only while within thirty seconds of it. So a gap of thirty seconds to an hour —
		/// the first anchor arriving after the client's own clock, a scene load longer than half a minute, a
		/// large clock correction — was kept for good: that player's fish swam out of step with everyone else's
		/// for the rest of the session (audit 2026-10-06). The shared clock jumps such a gap and leans across a
		/// small one.
		/// </remarks>
		public static double MotionSeconds => WorldMotion.Seconds;

		private static SeaLifeSettings Settings
		{
			get
			{
				SeaLifeSettings settings = WeatherRenderProfile.Active?.SeaLife;
				// A profile written against older defaults is brought up to date the first time it is read.
				settings?.Upgrade();
				return settings;
			}
		}
		private static Shader SeaLifeShader => WeatherRenderProfile.Active?.SeaLifeShader;

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Release();
			Unhook();
			publishedFrame = -1;
			loggedReason = null;
			countedSeed = 0;
			Status = "not started";
			Shader.SetGlobalVector(SeaId, Vector4.zero);
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
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

		private static void Shutdown()
		{
			Release();
			Unhook();
			Shader.SetGlobalVector(SeaId, Vector4.zero);
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
			lookHashes = new int[0];
			builtWith = null;
			cells.Clear();
			placedHash = 0;
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

		// ── The sea's globals ─────────────────────────────────────────

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => Publish();

		/// <summary>
		/// <c>_FishSea</c> and <c>_FishSeaSurge</c> (FishSea.hlsl), once a frame: the surface now, the low-tide
		/// ceiling the plants stay under (less the swell's troughs), whether there is a sea, the shared clock
		/// folded to 256 s, and the surge's direction and strength from the sea's waves.
		/// </summary>
		private static void Publish()
		{
			if (publishedFrame == Time.frameCount)
			{
				return;
			}
			publishedFrame = Time.frameCount;
			seaPresent = SurfaceWater.TryGetLevel(out float level) && !float.IsInfinity(level);
			if (!seaPresent)
			{
				Shader.SetGlobalVector(SeaId, Vector4.zero);
				Shader.SetGlobalVector(SurgeId, Vector4.zero);
				return;
			}
			// The sea's mean surface (the water publishes it); the level less its tide if it has not yet.
			meanLevel = Shader.GetGlobalFloat(MeanLevelId);
			if (Mathf.Abs(level - meanLevel) > SeaLifePlacement.LowTideMetres + 1f)
			{
				meanLevel = level;
			}
			float waves = Mathf.Max(0f, SurfaceWater.WaveHeight);
			float ceiling = Mathf.Min(level, meanLevel - SeaLifePlacement.LowTideMetres) - 0.2f - 0.5f * waves;
			lifeSurface = level - 0.5f * Mathf.Min(2f, waves);
			double folded = MotionSeconds % 256.0;
			if (folded < 0.0)
			{
				folded += 256.0;
			}
			Shader.SetGlobalVector(SeaId, new Vector4(level, ceiling, 1f, (float)folded));
			Vector4 wind = Shader.GetGlobalVector(WaterWindId);
			var toward = new Vector2(wind.x, wind.y);
			toward = toward.sqrMagnitude > 1e-6f ? toward.normalized : new Vector2(0.8f, 0.6f);
			Shader.SetGlobalVector(SurgeId, new Vector4(toward.x, toward.y, Mathf.Clamp01(0.35f + 0.3f * waves), 0f));
		}

		// ── Drawing ───────────────────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			Publish();
			if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
			{
				return;
			}
			if (camera.cameraType == CameraType.Game && camera != Camera.main)
			{
				return;
			}
			bool main = camera.cameraType == CameraType.Game;
			SeaLifeSettings settings = Settings;
			Shader shader = SeaLifeShader;
			if (settings == null || settings.Kinds == null)
			{
				Report(main, "idle: no Weather Render Profile with sea life is loaded");
				return;
			}
			if (!settings.Enabled)
			{
				Report(main, "idle: switched off on the Weather Render Profile (Sea life)");
				return;
			}
			if (shader == null || !shader.isSupported)
			{
				Report(main, shader == null ? "idle: the profile has no Sea Life shader (Weather Tools → ensure the render assets)" : "idle: FishMMO/Sea Life is not supported on this device");
				return;
			}
			if (!seaPresent)
			{
				Report(main, "idle: this scene has no sea");
				return;
			}
			Vector3 eye = camera.transform.position;
			if (eye.y - meanLevel > settings.MaxCameraHeight)
			{
				Report(main, $"idle: the camera is {eye.y - meanLevel:0} m above the sea (sea life is drawn below {settings.MaxCameraHeight:0} m)");
				return;
			}
			EnsureLooks(settings, shader);
			if (!ground.Refresh())
			{
				Report(main, "idle: no terrain is loaded");
				return;
			}
			bool settled = Place(settings, eye);
			int drawn = Draw(settings, camera, eye);
			Report(main, "drawing");
			if (main && settled && countedSeed != ground.SceneSeed)
			{
				countedSeed = ground.SceneSeed;
				Debug.Log($"[Sea life] {Census(settings)}; {drawn} drawn this frame within {settings.DrawDistance:0} m of the camera at {eye.y - meanLevel:0.0} m to the sea's mean surface.");
			}
		}

		/// <summary>Logs the system's state when it changes (the game camera's only: the scene view's would flicker it).</summary>
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
				Debug.Log(reason == "drawing" ? "[Sea life] drawing: placing the sea round the camera (a count follows once it is placed)." : $"[Sea life] {reason}.");
			}
		}

		/// <summary>How many groups of each kind the placed cells hold.</summary>
		private static string Census(SeaLifeSettings settings)
		{
			var counts = new int[settings.Kinds.Length];
			int animals = 0;
			foreach (Cell cell in cells.Values)
			{
				foreach (SeaGroup g in cell.Groups)
				{
					if (g.Kind < counts.Length)
					{
						counts[g.Kind]++;
						animals += g.Count;
					}
				}
			}
			var parts = new List<string>();
			for (int k = 0; k < counts.Length; k++)
			{
				if (settings.Kinds[k] != null)
				{
					parts.Add($"{settings.Kinds[k].Name} {counts[k]}");
				}
			}
			return $"{cells.Count} cells placed round the camera: {animals} animals in groups of {string.Join(", ", parts)}";
		}

		/// <summary>A kind's mesh and material, rebuilt when its shape or colours change.</summary>
		private static void EnsureLooks(SeaLifeSettings settings, Shader shader)
		{
			SeaCreatureKind[] kinds = settings.Kinds;
			if (builtWith != shader || meshes.Length != kinds.Length)
			{
				for (int i = 0; i < meshes.Length; i++)
				{
					Destroy(meshes[i]);
					Destroy(materials[i]);
				}
				meshes = new Mesh[kinds.Length];
				materials = new Material[kinds.Length];
				lookHashes = new int[kinds.Length];
				builtWith = shader;
			}
			for (int k = 0; k < kinds.Length; k++)
			{
				SeaCreatureKind kind = kinds[k];
				if (kind == null)
				{
					continue;
				}
				int hash = LookHash(kind);
				if (meshes[k] == null || lookHashes[k] != hash)
				{
					Destroy(meshes[k]);
					meshes[k] = SeaCreatureMeshes.Build(kind);
					lookHashes[k] = hash;
				}
				if (materials[k] == null)
				{
					materials[k] = new Material(shader) { name = "Sea life: " + kind.Name, hideFlags = HideFlags.DontSave, enableInstancing = true };
				}
				Material m = materials[k];
				bool jelly = kind.Shape == SeaCreatureShape.Jelly;
				m.SetFloat(ModeId, ModeOf(kind.Shape));
				m.SetFloat(AmplitudeId, kind.Stroke);
				m.SetFloat(WaveLengthId, kind.WaveLength);
				m.SetFloat(SmoothnessId, kind.Smoothness);
				m.SetFloat(TranslucentId, jelly ? 1f : 0f);
				m.SetFloat(SrcBlendId, (float)BlendMode.One);
				m.SetFloat(DstBlendId, jelly ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
				m.SetFloat(ZWriteId, jelly ? 0f : 1f);
				// Jellies after the sea's surface (Transparent-100) and before the underwater pass (-90), which fogs them.
				m.renderQueue = jelly ? (int)RenderQueue.Transparent - 95 : (int)RenderQueue.Geometry;
			}
		}

		private static float ModeOf(SeaCreatureShape shape)
		{
			switch (shape)
			{
				case SeaCreatureShape.Whale: return 1f;
				case SeaCreatureShape.Ray: return 2f;
				case SeaCreatureShape.Turtle: return 3f;
				case SeaCreatureShape.Jelly: return 4f;
				case SeaCreatureShape.Crab: return 5f;
				default: return 0f;
			}
		}

		private static int LookHash(SeaCreatureKind kind)
		{
			unchecked
			{
				int h = (int)kind.Shape * 397;
				h = h * 31 + kind.Back.GetHashCode();
				h = h * 31 + kind.Belly.GetHashCode();
				return h;
			}
		}

		/// <summary>What the kinds' placement is made from: a change re-decides every cell.</summary>
		private static int PlacementHash(SeaLifeSettings settings)
		{
			unchecked
			{
				int h = settings.Kinds.Length;
				foreach (SeaCreatureKind kind in settings.Kinds)
				{
					if (kind == null)
					{
						h = h * 31 + 1;
						continue;
					}
					h = h * 31 + (kind.Enabled ? 1 : 0);
					h = h * 31 + (int)kind.Behaviour;
					h = h * 31 + kind.GroupsPerKm2.GetHashCode();
					h = h * 31 + kind.GroupSize.GetHashCode();
					h = h * 31 + kind.Length.GetHashCode();
					h = h * 31 + kind.Roam.GetHashCode();
					h = h * 31 + kind.Speed.GetHashCode();
					h = h * 31 + kind.Depth.GetHashCode();
					h = h * 31 + kind.MinWater.GetHashCode();
					h = h * 31 + kind.FloorClearance.GetHashCode();
					h = h * 31 + kind.Temperature.GetHashCode();
					h = h * 31 + kind.PreferWeight.GetHashCode();
					h = h * 31 + kind.ElsewhereWeight.GetHashCode();
					if (kind.Prefers != null)
					{
						foreach (string p in kind.Prefers)
						{
							h = h * 31 + (p != null ? p.GetHashCode() : 0);
						}
					}
				}
				return h;
			}
		}

		/// <summary>Decides the cells within reach of the camera that are not decided yet, a few a frame; true once all are.</summary>
		private static bool Place(SeaLifeSettings settings, Vector3 eye)
		{
			float cellMetres = Mathf.Max(16f, settings.CellMetres);
			uint seed = ground.SceneSeed;
			int hash = PlacementHash(settings);
			if (seed != placedSeed || hash != placedHash || cellMetres != placedCellMetres)
			{
				cells.Clear();
				placedSeed = seed;
				placedHash = hash;
				placedCellMetres = cellMetres;
			}
			float reach = settings.DrawDistance + MaxRoam(settings);
			int range = Mathf.CeilToInt(reach / cellMetres);
			int cx = Mathf.FloorToInt(eye.x / cellMetres), cz = Mathf.FloorToInt(eye.z / cellMetres);
			int budget = CellsPerFrame;
			bool settled = true;
			/* Nearest cells first. Scanned row by row, a scene's ground left the far corner cells to be decided
			 * first, and when they could not be (a scene is cut round, so its bounding box has corners with no
			 * terrain in them) they took the whole budget every frame and the cells round the camera were never
			 * placed: no fish anywhere. */
			foreach (Vector2Int offset in OffsetsWithin(range))
			{
				if (budget <= 0)
				{
					settled = false;
					break;
				}
				long key = Key(cx + offset.x, cz + offset.y);
				if (cells.ContainsKey(key) || !ground.Overlaps((cx + offset.x) * cellMetres, (cz + offset.y) * cellMetres, cellMetres))
				{
					continue;
				}
				var cell = new Cell();
				for (int k = 0; k < settings.Kinds.Length; k++)
				{
					SeaLifePlacement.TryPlace(settings.Kinds[k], k, seed, cx + offset.x, cz + offset.y, cellMetres, meanLevel, ground, cell.Groups);
				}
				budget--;
				cells[key] = cell;
			}
			// Forget cells well out of reach, so a long swim does not keep the whole sea.
			if (cells.Count > (2 * range + 3) * (2 * range + 3) * 2)
			{
				var far = new List<long>();
				foreach (long key in cells.Keys)
				{
					int x = (int)(key >> 32), z = (int)(key & 0xFFFFFFFF);
					if (Math.Abs(x - cx) > range + 1 || Math.Abs(z - cz) > range + 1)
					{
						far.Add(key);
					}
				}
				foreach (long key in far)
				{
					cells.Remove(key);
				}
			}
			return settled;
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

		private static float MaxRoam(SeaLifeSettings settings)
		{
			float most = 0f;
			foreach (SeaCreatureKind kind in settings.Kinds)
			{
				if (kind != null && kind.Enabled)
				{
					most = Mathf.Max(most, kind.Roam.y * 1.2f + kind.Spread);
				}
			}
			return most;
		}

		private static long Key(int x, int z) => ((long)x << 32) | (uint)z;

		/// <summary>Draws the animals in reach; returns how many.</summary>
		private static int Draw(SeaLifeSettings settings, Camera camera, Vector3 eye)
		{
			SeaCreatureKind[] kinds = settings.Kinds;
			int kindCount = Mathf.Min(kinds.Length, byKind.Length);
			for (int k = 0; k < kindCount; k++)
			{
				(byKind[k] ??= new List<SeaInstance>()).Clear();
			}
			GeometryUtility.CalculateFrustumPlanes(camera, planes);
			double seconds = MotionSeconds;
			float distance = settings.DrawDistance;
			float fadeStart = distance * (1f - Mathf.Clamp(settings.FadeBand, 0.05f, 0.5f));
			foreach (Cell cell in cells.Values)
			{
				foreach (SeaGroup group in cell.Groups)
				{
					if (group.Kind >= kindCount)
					{
						continue;
					}
					SeaCreatureKind kind = kinds[group.Kind];
					if (kind == null || !kind.Enabled || meshes[group.Kind] == null)
					{
						continue;
					}
					float reach = group.Radius * 1.3f + kind.Spread * 1.7f + group.Length * 2f;
					float toHome = Vector3.Distance(new Vector3(eye.x, group.Home.y, eye.z), group.Home);
					if (toHome - reach > distance)
					{
						continue;
					}
					// Whole loop in view or not: a cheap test against the loop's box.
					var box = new Bounds(group.Home, new Vector3(reach * 2f, kind.Spread * 2f + group.Length * 2f + 8f, reach * 2f));
					if (!GeometryUtility.TestPlanesAABB(planes, box))
					{
						continue;
					}
					List<SeaInstance> list = byKind[group.Kind];
					int start = list.Count;
					SeaLifePlacement.Evaluate(group, kind, seconds, lifeSurface, ground, 1f, list);
					// Each animal dissolves on its own at the draw distance; past it, none is drawn.
					for (int i = list.Count - 1; i >= start; i--)
					{
						SeaInstance instance = list[i];
						Vector3 at = instance.Matrix.GetColumn(3);
						float d = Vector3.Distance(eye, at);
						if (d >= distance)
						{
							list.RemoveAt(i);
							continue;
						}
						instance.Tint.w = d <= fadeStart ? 1f : 1f - (d - fadeStart) / Mathf.Max(0.01f, distance - fadeStart);
						list[i] = instance;
					}
				}
			}

			block ??= new MaterialPropertyBlock();
			int drawn = 0;
			for (int k = 0; k < kindCount; k++)
			{
				List<SeaInstance> list = byKind[k];
				if (list.Count == 0 || meshes[k] == null || materials[k] == null)
				{
					continue;
				}
				SeaCreatureKind kind = kinds[k];
				drawn += list.Count;
				bool shadows = kind.CastShadows && kind.Shape != SeaCreatureShape.Jelly;
				for (int first = 0; first < list.Count; first += Batch)
				{
					int count = Mathf.Min(Batch, list.Count - first);
					bool anyNearForShadow = false;
					for (int i = 0; i < count; i++)
					{
						SeaInstance s = list[first + i];
						matrices[i] = s.Matrix;
						anims[i] = s.Anim;
						tints[i] = s.Tint;
						if (shadows && !anyNearForShadow && Vector3.Distance(eye, s.Matrix.GetColumn(3)) <= settings.ShadowDistance)
						{
							anyNearForShadow = true;
						}
					}
					block.Clear();
					block.SetVectorArray(AnimId, anims);
					block.SetVectorArray(TintId, tints);
					Graphics.DrawMeshInstanced(meshes[k], 0, materials[k], matrices, count, block,
						anyNearForShadow ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0, camera);
				}
			}
			return drawn;
		}

		// ── The ground ────────────────────────────────────────────────

		/// <summary>The loaded terrains as the sea floor, and the scene's biomes and climate over them.</summary>
		private sealed class TerrainGround : ISeaGround
		{
			private readonly List<Terrain> terrains = new List<Terrain>();
			private Terrain last;
			private Rect extent;
			private WorldSceneSettings settings;

			/// <summary>The placement seed: the name of the scene the terrains belong to.</summary>
			public uint SceneSeed { get; private set; }

			/// <summary>Takes this frame's terrains; false when none is loaded.</summary>
			public bool Refresh()
			{
				Terrain.GetActiveTerrains(terrains);
				if (terrains.Count == 0)
				{
					return false;
				}
				Terrain any = terrains[0];
				if (any == null)
				{
					return false;
				}
				UnityEngine.SceneManagement.Scene scene = any.gameObject.scene;
				SceneSeed = SeaLifePlacement.SceneSeed(scene.name);
				if (settings == null || settings.gameObject.scene != scene)
				{
					WorldSceneSettings.TryGetForScene(scene, out settings);
				}
				if (last == null || !terrains.Contains(last))
				{
					last = null;
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
				return !first;
			}

			/// <summary>True when a square of ground (its corner and side) overlaps the loaded terrains' extent.</summary>
			public bool Overlaps(float x, float z, float side)
			{
				return x + side > extent.xMin && z + side > extent.yMin && x < extent.xMax && z < extent.yMax;
			}

			public bool TryGround(float x, float z, out float y)
			{
				if (Contains(last, x, z))
				{
					y = last.SampleHeight(new Vector3(x, 0f, z)) + last.transform.position.y;
					return true;
				}
				foreach (Terrain t in terrains)
				{
					if (t != last && Contains(t, x, z))
					{
						last = t;
						y = t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
						return true;
					}
				}
				// No terrain here — past the scene's edge, or in a corner of its bounding box its round cut leaves
				// empty: no sea floor, so no water to swim in. (A scene's tiles load with it, so this is never
				// "not loaded yet": treated so, those corners were asked again every frame for ever.)
				y = float.PositiveInfinity;
				return true;
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

			public void Habitat(Vector3 position, out string biome, out float temperature)
			{
				biome = null;
				temperature = 0f;
				if (settings == null)
				{
					return;
				}
				SceneBiomeMap map = settings.BiomeMap;
				BiomeTemplate template = map != null ? map.Sample(position) : null;
				biome = template != null ? template.name : null;
				// The climate the ground was painted from, without the weather's runtime offsets: placement must
				// not depend on the hour it is first seen.
				ScenePlacementClimate placement = settings.PlacementClimate;
				if (placement != null && placement.Generated)
				{
					temperature = placement.SampleAt(position, out _).Temperature;
				}
			}
		}
	}
}
