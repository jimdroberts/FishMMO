using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU detail scatter: the mesh counterpart of the blade grass (<see cref="GrassBladeSystem"/>). It takes the mesh
	/// detail prototypes named by <see cref="DetailScatterSettings.PrototypePrefixes"/> (ground cover: pebbles, small
	/// rocks, shells, litter) of every terrain the chunk renderer (<see cref="TerrainDetailInstancing"/>) drives, and
	/// generates their instances on the GPU every frame around each camera (<see cref="DetailScatterRenderer"/>,
	/// FishDetailScatter.compute) instead of building them into chunks on the CPU; the chunk renderer then skips those
	/// prototypes on that terrain (<see cref="TerrainDetailInstancing.SetSkipped"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it buys.</b> No CPU chunk builds (~1–1.6 ms a dense chunk, two milliseconds a frame, so chunks lagged
	/// seconds behind a teleport or a fast run), no instances held in memory, and a density the CPU never has to pay
	/// for. Where things stand is still the scatter's: each prototype's detail layer is summed into 2 m blocks of
	/// exact expected counts at take-on (<see cref="DetailScatterTerrain"/>, one terrain at a time, a few ms a frame).
	/// </para>
	/// <para>
	/// <b>When.</b> Play mode, client, compute-capable devices, and only on terrains the chunk renderer drives, so Unity's
	/// own detail drawing never draws them as well. Everywhere else (WebGL2, the chunk renderer off, edit mode) the chunk
	/// renderer draws every detail exactly as before; the dashboard's "Toggle GPU detail scatter" opts back into it per
	/// editor session (SessionState <see cref="ChunkDetailsKey"/>). Reflection cameras and the minimap draw none.
	/// </para>
	/// </remarks>
	public static class DetailScatterSystem
	{
		/// <summary>SessionState key (editor): true draws every mesh detail through the chunk renderer instead (A/B).</summary>
		public const string ChunkDetailsKey = "FishMMO.DetailScatter.UseChunks";

		/// <summary>Milliseconds a frame the terrain being taken may spend summing its layers and reading its heights.</summary>
		public const double BuildBudgetMilliseconds = 2.0;

		/// <summary>The budget while the terrain being built is within its draw distance of the camera: its details are on screen.</summary>
		public const double VisibleBuildBudgetMilliseconds = 6.0;

		/// <summary>The most types the scatter keeps (one per prefab); a prefab past it stays on the chunk renderer.</summary>
		public const int MaxTypes = 64;

		private sealed class Taken
		{
			public Terrain Terrain;
			public TerrainData Data;
			public DetailScatterTerrain Scatter;
		}

		/// <summary>The scatter's key in <see cref="TerrainDetailInstancing.SetSkipped"/> (the blade grass has its own).</summary>
		private static readonly object SkipOwner = new object();

		private static readonly Dictionary<Terrain, Taken> taken = new Dictionary<Terrain, Taken>();
		private static readonly List<DetailScatterTerrain> scatterTerrains = new List<DetailScatterTerrain>();
		private static readonly Dictionary<Terrain, TerrainData> none = new Dictionary<Terrain, TerrainData>();
		private static readonly Dictionary<GameObject, DetailScatterType> typesByPrefab = new Dictionary<GameObject, DetailScatterType>();
		private static readonly List<DetailScatterType> types = new List<DetailScatterType>();
		private static readonly List<Terrain> active = new List<Terrain>();
		private static readonly List<int> prototypeScratch = new List<int>();
		private static readonly List<DetailScatterType> typeScratch = new List<DetailScatterType>();
		private static readonly List<Taken> releasing = new List<Taken>();
		private static readonly DetailScatterSettings defaults = new DetailScatterSettings();
		private static readonly GrassBladeSettings grassDefaults = new GrassBladeSettings();
		private static readonly HashSet<string> warned = new HashSet<string>();
		private static readonly System.Func<DetailScatterTerrain, float> distanceOf = st => TerrainDetailInstancing.DetailDistanceOf(st.Terrain);
		private static DetailScatterTerrain.Builder building;
		private static DetailScatterRenderer renderer;
		private static bool rendererTried;
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;
		private static int summaryTerrains, summaryFrames, summaryLayers;
		private static double summaryMilliseconds;

		/// <summary>The session's switch (A/B); off gives every scattered prototype back to the chunk renderer.</summary>
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
		public static int LastDrawCount { get; private set; }

		/// <summary>A line for the dashboard: state, terrains, types and the last game camera's work.</summary>
		public static string Describe()
		{
			if (!enabled)
			{
				return "GPU detail scatter OFF (every mesh detail draws through the chunk renderer).";
			}
			if (renderer == null)
			{
				return rendererTried ? "GPU detail scatter unavailable on this device or without its compute (the chunk renderer draws); see the log." : "GPU detail scatter not started (enter play mode on a terrain scene).";
			}
			var s = new System.Text.StringBuilder();
			s.Append($"GPU detail scatter ON: {taken.Count} terrain(s){(building != null ? $" (building '{building.Terrain?.name}': {building.Progress}, {building.Frames} frames, {building.Milliseconds:F0} ms)" : "")}, {types.Count} type(s)");
			s.Append($"; last game camera: {renderer.LastItems} work items, upper bound {renderer.LastBound} instances; GPU buffers {renderer.GpuBytes / (1024f * 1024f):F1} MiB");
			int[] counts = renderer.LastCounts;
			if (counts.Length > 0)
			{
				s.Append("; appended (camera/shadow, capacity):");
				for (int t = 0; t < types.Count && DetailScatterRenderer.SlotOf(t, 0, 1) < counts.Length; t++)
				{
					int main = DetailScatterRenderer.SlotOf(t, 0, 0), shadow = DetailScatterRenderer.SlotOf(t, 0, 1);
					s.Append($" {types[t].Name} {counts[main]}/{counts[shadow]} ({renderer.Capacity(main)})");
					for (int level = 1; level < types[t].Levels.Length && DetailScatterRenderer.SlotOf(t, level, 0) < counts.Length; level++)
					{
						s.Append($" L{level} {counts[DetailScatterRenderer.SlotOf(t, level, 0)]}");
					}
				}
			}
			return s.Append('.').ToString();
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
			enabled = !UnityEditor.SessionState.GetBool(ChunkDetailsKey, false);
#endif
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
			ReleaseAll();
			Unhook();
		}

		/// <summary>This system's once-a-frame work before the cameras render, named in the profiler (StartupTimeline reads it).</summary>
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("DetailScatterSystem.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using (ContextMarker.Auto())
			{
				Sync();
			}
		}

		private static DetailScatterSettings Settings => WeatherRenderProfile.Active?.DetailScatter;

		private static GrassBladeSettings GrassSettings => WeatherRenderProfile.Active?.Grass ?? grassDefaults;

		// ── Following the terrains ────────────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			double start = TerrainInstancingProbe.Now;
			DetailScatterSettings settings = Settings ?? defaults;
			if (!enabled || !settings.Enabled)
			{
				if (taken.Count > 0 || renderer != null || building != null)
				{
					ReleaseAll();
				}
				return;
			}
			if (renderer == null && !rendererTried)
			{
				rendererTried = true;
				renderer = DetailScatterRenderer.TryCreate(out string reason);
				if (renderer == null)
				{
					Debug.Log($"[Detail scatter] GPU detail scatter off: {reason}.");
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
				// A quality change of the density scale changes the counts: the terrain is built again.
				if (t.Terrain == null || !active.Contains(t.Terrain) || t.Terrain.terrainData != t.Data || !TerrainDetailInstancing.IsDriving(t.Terrain)
					|| TerrainDetailInstancing.DensityScaleOf(t.Terrain) != t.Scatter.DensityScale)
				{
					releasing.Add(t);
				}
			}
			foreach (Taken t in releasing)
			{
				Release(t);
			}

			if (building != null)
			{
				Terrain bt = building.Terrain;
				if (bt == null || !active.Contains(bt) || !TerrainDetailInstancing.IsDriving(bt))
				{
					building.Abandon();
					building = null;
				}
				else if (building.Step(DistanceToCamera(bt) <= TerrainDetailInstancing.DetailDistanceOf(bt) ? VisibleBuildBudgetMilliseconds : BuildBudgetMilliseconds))
				{
					Finish(building);
					building = null;
				}
			}
			if (building == null)
			{
				// Nearest the camera first: the ground underfoot must not wait behind every other terrain.
				bool started = false;
				while (!started)
				{
					Terrain next = null;
					float best = float.MaxValue;
					foreach (Terrain t in active)
					{
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
					Debug.Log($"[Detail scatter] Scattering {summaryLayers} prototype layer(s) of {types.Count} type(s) on the GPU on {summaryTerrains} terrain(s); built in {summaryMilliseconds:F0} ms over {summaryFrames} frames. The chunk renderer skips those prototypes there.");
					summaryTerrains = summaryFrames = summaryLayers = 0;
					summaryMilliseconds = 0;
				}
			}
			TerrainInstancingProbe.SyncMs += TerrainInstancingProbe.Now - start;
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

		/// <summary>Starts building a terrain's scatter; false (and remembered) when it has nothing to scatter.</summary>
		private static bool TryTake(Terrain terrain, DetailScatterSettings settings)
		{
			TerrainData data = terrain.terrainData;
			DetailPrototype[] prototypes = data.detailPrototypes;
			GrassBladeSettings grass = GrassSettings;
			int layer = terrain.gameObject.layer;
			prototypeScratch.Clear();
			typeScratch.Clear();
			// Bushes first: a terrain has DetailScatterMath.MaxChannels channels, and a type past them stays on the CPU chunk
			// renderer, which draws it to each player's own detail distance. Cover must not; pebbles may.
			for (int pass = 0; pass < 2; pass++)
			for (int p = 0; p < prototypes.Length; p++)
			{
				DetailPrototype proto = prototypes[p];
				GameObject prefab = proto?.prototype;
				// Whatever the blade grass claims is never taken here, running or not, so nothing is drawn twice.
				if (prefab == null || !proto.usePrototypeMesh || !settings.IsScatterPrototype(prefab.name) || grass.IsBladePrototype(prefab.name)
					|| DetailScatterSettings.IsBush(prefab.name) != (pass == 0))
				{
					continue;
				}
				if (!typesByPrefab.TryGetValue(prefab, out DetailScatterType type))
				{
					if (types.Count >= MaxTypes)
					{
						if (warned.Add("types"))
						{
							Debug.LogWarning($"[Detail scatter] More than {MaxTypes} scattered types: '{prefab.name}' and later ones stay on the chunk renderer.");
						}
						continue;
					}
					type = DetailScatterType.Build(proto, layer, out string reason);
					if (type == null)
					{
						if (warned.Add(reason))
						{
							Debug.LogWarning($"[Detail scatter] Leaving {reason} on the chunk renderer.");
						}
						continue;
					}
					type.Index = types.Count;
					types.Add(type);
					typesByPrefab.Add(prefab, type);
				}
				prototypeScratch.Add(p);
				typeScratch.Add(type);
			}
			building = prototypeScratch.Count > 0 ? DetailScatterTerrain.Begin(terrain, prototypeScratch, typeScratch, TerrainDetailInstancing.DensityScaleOf(terrain)) : null;
			if (building == null)
			{
				none[terrain] = data;
				return false;
			}
			return true;
		}

		/// <summary>Hands a finished terrain from the chunk renderer to the scatter.</summary>
		private static void Finish(DetailScatterTerrain.Builder done)
		{
			Terrain terrain = done.Terrain;
			DetailScatterTerrain scatter = done.Result;
			summaryFrames += done.Frames;
			summaryMilliseconds += done.Milliseconds;
			if (scatter == null)
			{
				none[terrain] = terrain.terrainData;
				return;
			}
			if (scatter.ClampedBlocks > 0 && scatter.ClampedChannel >= 0 && warned.Add("clamp " + terrain.name))
			{
				string name = types[scatter.ChannelSettings[scatter.ClampedChannel].Type].Name;
				Debug.LogWarning($"[Detail scatter] '{terrain.name}': {scatter.ClampedBlocks} block(s) painted denser than {DetailScatterMath.MaxPerBlock} instances per {DetailScatterMath.BlockMetres:F0} m block (first: '{name}'); they draw at that cap.");
			}
			taken[terrain] = new Taken { Terrain = terrain, Data = terrain.terrainData, Scatter = scatter };
			scatterTerrains.Add(scatter);
			none.Remove(terrain);
			TerrainDetailInstancing.SetSkipped(SkipOwner, terrain, scatter.Skip);
			summaryTerrains++;
			summaryLayers += scatter.Layers;
		}

		private static void Release(Taken t)
		{
			taken.Remove(t.Terrain);
			scatterTerrains.Remove(t.Scatter);
			t.Scatter.Dispose();
			if (t.Terrain != null)
			{
				TerrainDetailInstancing.SetSkipped(SkipOwner, t.Terrain, null);
			}
		}

		private static void ReleaseAll()
		{
			building?.Abandon();
			building = null;
			summaryTerrains = summaryFrames = summaryLayers = 0;
			summaryMilliseconds = 0;
			foreach (Taken t in new List<Taken>(taken.Values))
			{
				Release(t);
			}
			taken.Clear();
			scatterTerrains.Clear();
			none.Clear();
			types.Clear();
			typesByPrefab.Clear();
			renderer?.Dispose();
			renderer = null;
			rendererTried = false;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!Application.isPlaying || !TerrainInstancingShared.DrawsDetails(camera) || camera.cameraType == CameraType.Reflection)
			{
				return;
			}
			Sync();
			if (renderer == null || scatterTerrains.Count == 0)
			{
				return;
			}
			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			LastDrawCount = renderer.Execute(camera, context, in view, scatterTerrains, types, Settings ?? defaults, distanceOf);
		}
	}
}
