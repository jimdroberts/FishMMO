using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The procedural blade grass (RDR2 / Ghost of Tsushima style): takes the grass detail prototypes
	/// (<see cref="GrassBladeSettings.PrototypePrefixes"/>, default "Detail_Grass") of every terrain the
	/// instanced detail renderer drives, and draws them as thin, individually shaped blades regenerated on
	/// the GPU every frame around each camera (<see cref="GrassBladeRenderer"/>, FishGrassBlades.compute,
	/// FishMMO/Grass Blades) in place of the clump meshes, which <see cref="TerrainDetailInstancing"/> then
	/// skips on that terrain (<see cref="TerrainDetailInstancing.SetSkipped"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where grass grows</b> is still the scatter's: each grass prototype's detail layer (biome rules,
	/// shore gate, slope) becomes a density map at take-on, once per terrain (CPU, one terrain a frame).
	/// The blades are a deterministic function of their world lattice cell (<see cref="GrassMath"/>), so the
	/// same blades stand in the same places every frame and on every terrain border.
	/// </para>
	/// <para>
	/// <b>When.</b> Play mode, client, compute-capable devices, and only on terrains the detail renderer
	/// drives — so Unity's own detail drawing never draws the clumps under the blades. Everywhere else
	/// (WebGL2, the detail renderer off, edit mode) the mesh grass draws exactly as before. The dashboard's
	/// It is the default wherever compute runs; the dashboard's "Toggle old mesh grass" opts back into the
	/// instanced clump meshes per editor session (SessionState <see cref="MeshGrassKey"/>, off by default).
	/// Reflection cameras draw no blades.
	/// </para>
	/// </remarks>
	public static class GrassBladeSystem
	{
		/// <summary>
		/// SessionState key (editor): true draws the old instanced clump meshes instead of the blades. A new key,
		/// opt-in and false by default, so a switch-off left over from testing the A/B no longer sticks.
		/// </summary>
		public const string MeshGrassKey = "FishMMO.ProceduralGrass.UseMeshGrass";

		private sealed class Taken
		{
			public Terrain Terrain;
			public TerrainData Data;
			public GrassTerrain Grass;
		}

		private static readonly Dictionary<Terrain, Taken> taken = new Dictionary<Terrain, Taken>();

		/// <summary>The blade grass's key in <see cref="TerrainDetailInstancing.SetSkipped"/> (the GPU detail scatter has its own).</summary>
		private static readonly object SkipOwner = new object();

		/// <summary>Milliseconds a frame the terrain being taken may spend building its density maps and height grid.</summary>
		public const double BuildBudgetMilliseconds = 2.0;

		/// <summary>The budget while the terrain being built is within the grass distance of the camera: its grass is on screen.</summary>
		public const double VisibleBuildBudgetMilliseconds = 8.0;

		/// <summary>The terrain whose grass is being built, a strip a frame (its mesh grass draws until it is done).</summary>
		private static GrassTerrain.Builder building;
		/// <summary>What has been taken since the last summary line: one line per batch, not one per terrain.</summary>
		private static int summaryTerrains, summaryFrames, summaryChannels;
		/// <summary>The frame to log one <see cref="Describe"/> line with the GPU's real blade counts, after a batch is taken (−1 none).</summary>
		private static int describeAt = -1;
		private static bool statsWereOn;
		private static double summaryMilliseconds;

		private static readonly List<GrassTerrain> grassTerrains = new List<GrassTerrain>();
		/// <summary>Terrains with no blade grass (or refused), with the data they had: not looked at again until it changes.</summary>
		private static readonly Dictionary<Terrain, TerrainData> none = new Dictionary<Terrain, TerrainData>();
		private static readonly Dictionary<GameObject, GrassType> typesByPrefab = new Dictionary<GameObject, GrassType>();
		private static readonly List<GrassType> types = new List<GrassType>();
		private static readonly List<Terrain> active = new List<Terrain>();
		private static readonly List<int> prototypeScratch = new List<int>();
		private static readonly List<GrassType> typeScratch = new List<GrassType>();
		private static readonly List<Taken> releasing = new List<Taken>();
		private static readonly GrassBladeSettings defaults = new GrassBladeSettings();
		private static GrassBladeRenderer renderer;
		private static bool rendererTried;
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;
		private static float grassDistance = ClientGrassSettings.DefaultDistance;
		private static readonly HashSet<string> warned = new HashSet<string>();
		/// <summary>The next frame to look for terrains' array sets while any taken terrain has none (a binder loads its set by address).</summary>
		private static int arraysCheckAt;
		private const int ArraysCheckInterval = 30;

		/// <summary>The session's switch (A/B); off gives every grass prototype back to the mesh renderer.</summary>
		public static bool Enabled
		{
			get => enabled;
			set
			{
				if (enabled == value)
				{
					return;
				}
				enabled = value;
				if (!value)
				{
					ReleaseAll();
				}
				syncedFrame = -1;
			}
		}

		public static bool Running => renderer != null && taken.Count > 0;
		public static int TerrainCount => taken.Count;
		public static int TypeCount => types.Count;
		public static GrassBladeRenderer Renderer => renderer;
		public static int LastDrawCount { get; private set; }

		/// <summary>A line for the dashboard: state, terrains, types, the last camera's work and (with stats on) blades.</summary>
		public static string Describe()
		{
			if (!enabled)
			{
				return "Procedural grass OFF (the grass prototypes draw as meshes).";
			}
			if (renderer == null)
			{
				return rendererTried ? "Procedural grass unavailable on this device or without its assets (the mesh grass draws); see the log." : "Procedural grass not started (enter play mode on a terrain scene).";
			}
			var s = new System.Text.StringBuilder();
			s.Append($"Procedural grass ON: {taken.Count} terrain(s){(building != null ? $" (building '{building.Terrain?.name}': {building.Progress}, {building.Frames} frames, {building.Milliseconds:F0} ms)" : "")}, {types.Count} type(s) [");
			for (int i = 0; i < types.Count; i++)
			{
				s.Append(i > 0 ? ", " : "").Append(types[i].Name).Append($" {types[i].Height:F2} m{(types[i].ColoursFromMesh ? "" : " (fallback colours)")}");
			}
			s.Append($"]; last game camera {renderer.LastCamera}: {renderer.LastTiles} tiles, {renderer.LastItems} work items, {renderer.LastCandidates} candidate threads; GPU buffers {renderer.GpuBytes / (1024f * 1024f):F1} MiB");
			if (renderer.LastBlades[0] >= 0)
			{
				s.Append($"; blades appended LOD0/1/2 {renderer.LastBlades[0]}/{renderer.LastBlades[1]}/{renderer.LastBlades[2]} (caps {renderer.Capacity(0)}/{renderer.Capacity(1)}/{renderer.Capacity(2)}), shadow {renderer.LastBlades[3]}/{renderer.LastBlades[4]}");
			}
			// "0 tiles" with terrains taken usually means the ground under the camera is not one of them.
			Terrain under = TerrainUnderCamera();
			if (under != null)
			{
				string state = taken.TryGetValue(under, out Taken t) ? $"blades ({t.Grass.Channels} type channel(s) from {t.Grass.Layers} prototype(s))"
					: building != null && building.Terrain == under ? "building"
					: none.ContainsKey(under) ? "mesh grass (no blade grass painted on it)"
					: !TerrainDetailInstancing.IsDriving(under) ? "not driven by the detail renderer"
					: "waiting to build";
				s.Append($"; under the camera '{under.name}': {state}");
			}
			return s.Append('.').ToString();
		}

		private static Terrain TerrainUnderCamera()
		{
			Camera camera = Camera.main;
			if (camera == null)
			{
				return null;
			}
			Terrain.GetActiveTerrains(active);
			foreach (Terrain t in active)
			{
				if (t != null && t.terrainData != null && DistanceToCamera(t) <= 0f)
				{
					return t;
				}
			}
			return null;
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
			warned.Clear();
			enabled = true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			syncedFrame = -1;
#if UNITY_EDITOR
			enabled = !UnityEditor.SessionState.GetBool(MeshGrassKey, false);
#endif
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			Application.quitting += Shutdown;
			grassDistance = ClientGrassSettings.Distance;
			ClientGrassSettings.OnChanged += OnGrassSettingsChanged;
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
			ClientGrassSettings.OnChanged -= OnGrassSettingsChanged;
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
			ReleaseAll();
			Unhook();
		}

		private static void OnGrassSettingsChanged() => grassDistance = ClientGrassSettings.Distance;

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => Sync();

		private static GrassBladeSettings Settings => WeatherRenderProfile.Active?.Grass;

		// ── Following the terrains ────────────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			double start = TerrainInstancingProbe.Now;
			GrassBladeSettings settings = Settings ?? defaults;
			if (!enabled || !settings.Enabled)
			{
				if (taken.Count > 0 || renderer != null)
				{
					ReleaseAll();
				}
				return;
			}
			if (renderer == null && !rendererTried)
			{
				rendererTried = true;
				renderer = GrassBladeRenderer.TryCreate(out string reason);
				if (renderer == null)
				{
					Debug.Log($"[Grass] Procedural blade grass off: {reason}.");
				}
			}
			if (renderer == null)
			{
				return;
			}

			Terrain.GetActiveTerrains(active);
			releasing.Clear();
			foreach (Taken t in taken.Values)
			{
				if (t.Terrain == null || !active.Contains(t.Terrain) || t.Terrain.terrainData != t.Data || !TerrainDetailInstancing.IsDriving(t.Terrain))
				{
					releasing.Add(t);
				}
			}
			foreach (Taken t in releasing)
			{
				Release(t);
			}

			// One terrain at a time, a few milliseconds a frame: its density maps read every grass layer at full
			// resolution, which in one go cost 25–240 ms a terrain.
			if (building != null)
			{
				Terrain bt = building.Terrain;
				if (bt == null || !active.Contains(bt) || !TerrainDetailInstancing.IsDriving(bt))
				{
					building.Abandon();
					building = null;
				}
				else if (building.Step(DistanceToCamera(bt) <= grassDistance ? VisibleBuildBudgetMilliseconds : BuildBudgetMilliseconds))
				{
					Finish(building);
					building = null;
				}
			}
			if (building == null)
			{
				// Nearest the camera first: the terrain underfoot used to wait behind every other one (30
				// terrains at 2 ms a frame is hundreds of frames), drawing the old mesh grass meanwhile.
				bool started = false;
				while (!started)
				{
					Terrain next = null;
					float best = float.MaxValue;
					for (int i = 0; i < active.Count; i++)
					{
						Terrain t = active[i];
						if (t == null || taken.ContainsKey(t) || !TerrainDetailInstancing.IsDriving(t))
						{
							continue;
						}
						if (none.TryGetValue(t, out TerrainData seen) && seen == t.terrainData)
						{
							continue;
						}
						float d = DistanceToCamera(t);
						if (d < best)
						{
							best = d;
							next = t;
						}
					}
					if (next == null)
					{
						break;
					}
					started = TryTake(next, settings);
				}
				if (!started && summaryTerrains > 0)
				{
					Debug.Log($"[Grass] Blade grass on {summaryTerrains} terrain(s) ({types.Count} grass type(s), {summaryChannels} prototype layer(s)); built in {summaryMilliseconds:F0} ms over {summaryFrames} frames ({BuildBudgetMilliseconds:F0} ms a frame). The detail renderer skips those prototypes there.");
					summaryTerrains = summaryFrames = summaryChannels = 0;
					summaryMilliseconds = 0;
					// A couple of seconds later, what the GPU really drew (types, heights, blades per slot vs caps):
					// the readback is only on while it is wanted.
					statsWereOn = GrassBladeRenderer.StatsEnabled;
					GrassBladeRenderer.StatsEnabled = true;
					describeAt = Time.frameCount + 3 * GrassBladeRenderer.StatsInterval + 30;
				}
			}
			if (describeAt >= 0 && Time.frameCount >= describeAt)
			{
				describeAt = -1;
				Debug.Log($"[Grass] {Describe()}");
				GrassBladeRenderer.StatsEnabled = statsWereOn;
			}
			ResolveArrays();
			renderer.Publish(settings, types, grassDistance);
			TerrainInstancingProbe.SyncMs += TerrainInstancingProbe.Now - start;
		}

		/// <summary>
		/// Gives each taken terrain the array set its ground is drawn with (the blades take their colour from
		/// it). Looked for every <see cref="ArraysCheckInterval"/> frames only while one is still missing.
		/// </summary>
		private static void ResolveArrays()
		{
			if (Time.frameCount < arraysCheckAt)
			{
				return;
			}
			bool missing = false;
			foreach (GrassTerrain gt in grassTerrains)
			{
				if (gt.Arrays == null || !gt.Arrays.IsUsable)
				{
					missing = true;
					break;
				}
			}
			if (!missing)
			{
				return;
			}
			arraysCheckAt = Time.frameCount + ArraysCheckInterval;
			foreach (TerrainArrayBinder binder in Object.FindObjectsByType<TerrainArrayBinder>())
			{
				TerrainArraySet set = binder.BoundSet;
				if (set == null || !set.IsUsable)
				{
					continue;
				}
				foreach (Terrain t in binder.EffectiveTerrains())
				{
					if (t != null && taken.TryGetValue(t, out Taken held))
					{
						held.Grass.Arrays = set;
					}
				}
			}
		}

		/// <summary>Metres from the main camera to a terrain's footprint (0 standing on it; far when there is no camera).</summary>
		private static float DistanceToCamera(Terrain terrain)
		{
			Camera camera = Camera.main;
			if (camera == null || terrain == null || terrain.terrainData == null)
			{
				return float.MaxValue;
			}
			Vector3 p = camera.transform.position;
			Vector3 o = terrain.GetPosition();
			Vector3 s = terrain.terrainData.size;
			float dx = Mathf.Max(0f, Mathf.Max(o.x - p.x, p.x - (o.x + s.x)));
			float dz = Mathf.Max(0f, Mathf.Max(o.z - p.z, p.z - (o.z + s.z)));
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		/// <summary>Starts building a terrain's grass; false (and remembered) when it has none to draw as blades.</summary>
		private static bool TryTake(Terrain terrain, GrassBladeSettings settings)
		{
			TerrainData data = terrain.terrainData;
			DetailPrototype[] prototypes = data.detailPrototypes;
			prototypeScratch.Clear();
			typeScratch.Clear();
			// Every blade prototype: several share a type (one per biome rule), and the build sums them into one
			// channel per type.
			for (int p = 0; p < prototypes.Length; p++)
			{
				DetailPrototype proto = prototypes[p];
				GameObject prefab = proto?.prototype;
				if (prefab == null || !proto.usePrototypeMesh || !settings.IsBladePrototype(prefab.name))
				{
					continue;
				}
				if (!typesByPrefab.TryGetValue(prefab, out GrassType type))
				{
					if (types.Count >= GrassBladeRenderer.MaxTypes)
					{
						if (warned.Add("types"))
						{
							Debug.LogWarning($"[Grass] More than {GrassBladeRenderer.MaxTypes} grass types: '{prefab.name}' and later ones stay meshes.");
						}
						continue;
					}
					type = GrassType.Build(proto, settings.TuningFor(prefab.name));
					type.Index = types.Count;
					types.Add(type);
					typesByPrefab.Add(prefab, type);
				}
				prototypeScratch.Add(p);
				typeScratch.Add(type);
			}
			building = prototypeScratch.Count > 0 ? GrassTerrain.Begin(terrain, prototypeScratch, typeScratch) : null;
			if (building == null)
			{
				none[terrain] = data;
				return false;
			}
			return true;
		}

		/// <summary>Hands a finished terrain from the mesh grass to the blades.</summary>
		private static void Finish(GrassTerrain.Builder done)
		{
			Terrain terrain = done.Terrain;
			GrassTerrain grass = done.Result;
			summaryFrames += done.Frames;
			summaryMilliseconds += done.Milliseconds;
			if (grass == null)
			{
				none[terrain] = terrain.terrainData;
				return;
			}
			taken[terrain] = new Taken { Terrain = terrain, Data = terrain.terrainData, Grass = grass };
			grassTerrains.Add(grass);
			none.Remove(terrain);
			TerrainDetailInstancing.SetSkipped(SkipOwner, terrain, grass.Skip);
			summaryTerrains++;
			summaryChannels += grass.Layers;
		}

		private static void Release(Taken t)
		{
			taken.Remove(t.Terrain);
			grassTerrains.Remove(t.Grass);
			t.Grass.Dispose();
			if (t.Terrain != null)
			{
				TerrainDetailInstancing.SetSkipped(SkipOwner, t.Terrain, null);
			}
		}

		private static void ReleaseAll()
		{
			building?.Abandon();
			building = null;
			summaryTerrains = summaryFrames = summaryChannels = 0;
			summaryMilliseconds = 0;
			foreach (Taken t in new List<Taken>(taken.Values))
			{
				Release(t);
			}
			taken.Clear();
			grassTerrains.Clear();
			none.Clear();
			types.Clear();
			typesByPrefab.Clear();
			renderer?.Dispose();
			renderer = null;
			rendererTried = false;
			arraysCheckAt = 0;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!Application.isPlaying || !TerrainInstancingShared.DrawsDetails(camera) || camera.cameraType == CameraType.Reflection)
			{
				return;
			}
			Sync();
			if (renderer == null || grassTerrains.Count == 0)
			{
				return;
			}
			GrassBladeSettings settings = Settings ?? defaults;
			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			LastDrawCount = renderer.Execute(camera, context, in view, grassTerrains, settings, grassDistance);
		}
	}
}
