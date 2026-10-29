using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU detail scatter: the mesh counterpart of the blade grass (<see cref="GrassBladeSystem"/>). It takes EVERY mesh
	/// detail prototype the blade grass does not (2026-10-27: bushes, pebbles, rocks, shells, litter, and the ferns, herbs,
	/// mushrooms, lichens, cushions, shrubs and sea life the CPU chunk renderer used to draw) of every terrain the detail
	/// driver (<see cref="TerrainDetailInstancing"/>) takes, and generates their instances on the GPU every frame around each
	/// camera (<see cref="DetailScatterRenderer"/>, FishDetailScatter.compute). There is no CPU path: a kind it cannot draw
	/// (no procedural-instancing twin, more than one sub-mesh, a texture detail) or one past a cap is an error, at the cut
	/// (TerrainDetailBaker) and in play.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it buys.</b> No CPU chunk builds (~1–1.6 ms a dense chunk, two milliseconds a frame, so chunks lagged
	/// seconds behind a teleport or a fast run), no instances held in memory, and a density the CPU never has to pay
	/// for. Where things stand is still the scatter's: each prototype's detail layer is summed into 2 m blocks of
	/// exact expected counts at take-on (<see cref="DetailScatterTerrain"/>, one terrain at a time, a few ms a frame).
	/// </para>
	/// <para>
	/// <b>When.</b> Play mode, client, compute-capable devices, and only on terrains the detail driver takes, so Unity's
	/// own detail drawing never draws them as well. Edit mode is Unity's own drawing. Reflection cameras and the minimap
	/// draw none.
	/// </para>
	/// </remarks>
	public static class DetailScatterSystem
	{
		/// <summary>
		/// Milliseconds a frame of the main thread the terrain being taken may spend reading its layers and heights (the sums
		/// and the packing run on a worker: DetailScatterTerrain.Builder). Was 2 with them on the main thread.
		/// </summary>
		public const double BuildBudgetMilliseconds = 1.0;

		/// <summary>
		/// The budget while the terrain being built is within its draw distance of the camera: its details are on screen. Was 6
		/// with the sums on the main thread; the reads alone fit in 1.
		/// </summary>
		public const double VisibleBuildBudgetMilliseconds = 1.0;

		/// <summary>
		/// The most types the scatter keeps in a scene (one per prefab; 2026-10-27, was 64). Moron 2 paints 84 mesh kinds and
		/// Moron 3 115; the audit's spec count for the busiest scene is 127 + 127. Past it a prefab is not drawn: an error.
		/// </summary>
		public const int MaxTypes = 1024;

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
		/// <summary>Terrains whose build failed on a worker once (taken again once, then given up: their mesh details are not drawn).</summary>
		private static readonly HashSet<Terrain> failedOnce = new HashSet<Terrain>();
		private static readonly DetailScatterSettings defaults = new DetailScatterSettings();
		private static readonly GrassBladeSettings grassDefaults = new GrassBladeSettings();
		private static readonly HashSet<string> warned = new HashSet<string>();
		private static readonly System.Func<DetailScatterTerrain, float> distanceOf = st => TerrainDetailInstancing.DetailDistanceOf(st.Terrain);
		private static DetailScatterTerrain.Builder building;
		private static DetailScatterRenderer renderer;
		/// <summary>The renderer, for diagnostics.</summary>
		public static DetailScatterRenderer Renderer => renderer;
		private static bool rendererTried;
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;
		private static int summaryTerrains, summaryFrames, summaryLayers, summaryBaked;

		/// <summary>Milliseconds a frame of the main thread spent taking terrains from the scene's bake (as the blade grass's).</summary>
		public const double BakedBudgetMilliseconds = 4.0;
		private static double summaryMilliseconds;

		/// <summary>The session's switch (A/B); off draws no mesh detail at all.</summary>
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
		/// <summary>Whether a terrain is being taken on now (its build in progress).</summary>
		public static bool Building => building != null;
		public static int TypeCount => types.Count;
		public static int LastDrawCount { get; private set; }

		/// <summary>A line for the dashboard: state, terrains, types and the last game camera's work.</summary>
		public static string Describe()
		{
			if (!enabled)
			{
				return "GPU detail scatter OFF: no mesh detail is drawn (there is no CPU path).";
			}
			if (renderer == null)
			{
				return rendererTried ? "GPU detail scatter unavailable on this device or without its compute: no mesh detail is drawn; see the log." : "GPU detail scatter not started (enter play mode on a terrain scene).";
			}
			var s = new System.Text.StringBuilder();
			s.Append($"GPU detail scatter ON: {taken.Count} terrain(s){(building != null ? $" (building '{building.Terrain?.name}': {building.Progress}, {building.Frames} frames, {building.Milliseconds:F0} ms)" : "")}, {types.Count} type(s)");
			s.Append($"; last game camera: {renderer.LastItems} work items, upper bound {renderer.LastBound} instances; GPU buffers {renderer.GpuBytes / (1024f * 1024f):F1} MiB");
			s.Append($"; table {renderer.TableBytes / (1024f * 1024f):F1} MiB");
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
					// The variant meshes it cycles share the slots (2026-10-26): level 0 above is its first variant's.
					for (int v = 1; v < DetailScatterRenderer.VariantsOf(types[t]) && DetailScatterRenderer.SlotOf(t, v, 0) < counts.Length; v++)
					{
						s.Append($" V{v} {counts[DetailScatterRenderer.SlotOf(t, v, 0)]}");
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
					Debug.LogError($"[Detail scatter] GPU detail scatter off: {reason}. No mesh detail is drawn (there is no CPU path).");
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
				bool started = false, spent = false;
				long bakedStart = System.Diagnostics.Stopwatch.GetTimestamp();
				while (!started && !spent)
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
					// Taken from the scene's bake, it is done at once: handed over now, and the next looked for while the
					// frame's budget for it lasts.
					if (started && building != null && building.Done)
					{
						// Counted with the terrains it adds to (one baked as having nothing is not one of them).
						summaryBaked += building.Result != null ? 1 : 0;
						Finish(building);
						building = null;
						started = false;
						spent = (System.Diagnostics.Stopwatch.GetTimestamp() - bakedStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= BakedBudgetMilliseconds;
					}
				}
				if (!started && !spent && summaryTerrains > 0)
				{
					Debug.Log($"[Detail scatter] Scattering {summaryLayers} prototype layer(s) of {types.Count} type(s) on the GPU on {summaryTerrains} terrain(s); {summaryBaked} taken from the scene's bake, the rest built in {summaryMilliseconds:F0} ms of the main thread over {summaryFrames} frames (the sums on workers).");
					summaryTerrains = summaryFrames = summaryLayers = summaryBaked = 0;
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
			SelectScattered(data, terrain.gameObject.layer, settings, GrassSettings, types, typesByPrefab, warned, prototypeScratch, typeScratch);
			building = prototypeScratch.Count > 0 ? DetailScatterTerrain.Begin(terrain, prototypeScratch, typeScratch, TerrainDetailInstancing.DensityScaleOf(terrain)) : null;
			if (building == null)
			{
				none[terrain] = data;
				return false;
			}
			// The scene's bake, when it was made from just this (and at this density scale): taken from it at once, not read.
			FishMMO.Shared.TerrainDetailBake.Tile tile = FishMMO.Shared.SceneTerrainBake.TileOf(data, out _);
			if (tile != null)
			{
				building.TakeBaked(tile);
			}
			return true;
		}

		/// <summary>
		/// A terrain's scattered prototypes and each one's type, new types made and added to <paramref name="types"/> (up to
		/// <see cref="MaxTypes"/>): every PAINTED mesh prototype the blade grass does not claim (<see cref="TerrainDetailRouting"/>),
		/// bushes first. The play's choice and the editor's bake (TerrainDetailBaker) are this one. Every painted prototype it
		/// cannot draw is an error, said once in the log and, when <paramref name="problems"/> is given (the cut), added to it.
		/// </summary>
		/// <remarks>
		/// Painted only (2026-10-27): a cut listed the whole scene's prototypes on every terrain (578 on Moron 2's) but painted a
		/// tile with a few dozen; channels were spent on the rest. A cut now gives a tile only the prototypes it paints (TerrainScatter). A prototype with nothing painted anywhere on the terrain
		/// weighs nothing anywhere, so leaving it out changes no plant.
		/// </remarks>
		public static void SelectScattered(TerrainData data, int layer, DetailScatterSettings settings, GrassBladeSettings grass, List<DetailScatterType> types,
			Dictionary<GameObject, DetailScatterType> typesByPrefab, HashSet<string> warned, List<int> prototypeScratch, List<DetailScatterType> typeScratch,
			ICollection<string> problems = null)
		{
			DetailPrototype[] prototypes = data.detailPrototypes;
			prototypeScratch.Clear();
			typeScratch.Clear();
			bool[] painted = TerrainDetailRouting.Painted(data);
			// Bushes first: the lowest type indices, as before.
			for (int pass = 0; pass < 2; pass++)
			for (int p = 0; p < prototypes.Length; p++)
			{
				DetailPrototype proto = prototypes[p];
				if (!painted[p] || TerrainDetailRouting.RouteOf(proto, grass) != TerrainDetailRouting.Route.Scatter || DetailScatterSettings.IsBush(proto.prototype.name) != (pass == 0))
				{
					continue;
				}
				GameObject prefab = proto.prototype;
				if (!typesByPrefab.TryGetValue(prefab, out DetailScatterType type))
				{
					if (types.Count >= MaxTypes)
					{
						Problem(warned, problems, "types " + prefab.name, $"[Detail scatter] More than {MaxTypes} scattered types in the scene: '{prefab.name}' is not drawn (GPU only: raise DetailScatterSystem.MaxTypes).");
						continue;
					}
					type = DetailScatterType.Build(proto, layer, out string reason);
					if (type == null)
					{
						Problem(warned, problems, reason, $"[Detail scatter] {reason}: not drawn (GPU only). Give the prefab one sub-mesh and a material with a procedural-instancing twin.");
						continue;
					}
					type.Index = types.Count;
					types.Add(type);
					typesByPrefab.Add(prefab, type);
				}
				prototypeScratch.Add(p);
				typeScratch.Add(type);
			}
			// What neither GPU renderer can take (a texture detail, an empty prototype) is an error too, once.
			for (int p = 0; p < prototypes.Length; p++)
			{
				if (painted[p] && TerrainDetailRouting.RouteOf(prototypes[p], grass) == TerrainDetailRouting.Route.None)
				{
					string name = TerrainDetailRouting.NameOf(prototypes[p], p);
					Problem(warned, problems, "unrouted " + name, $"[Terrain details] '{name}' is painted but no GPU renderer can draw it ({TerrainDetailRouting.WhyNot(prototypes[p])}): not drawn.");
				}
			}
		}

		/// <summary>Logs a problem once a session as an error, and adds it to the cut's list when there is one.</summary>
		private static void Problem(HashSet<string> warned, ICollection<string> problems, string key, string message)
		{
			problems?.Add(message);
			if (warned == null || warned.Add(key))
			{
				Debug.LogError(message);
			}
		}

		/// <summary>Hands a finished terrain to the scatter (its prototypes claimed in the driver's books).</summary>
		private static void Finish(DetailScatterTerrain.Builder done)
		{
			Terrain terrain = done.Terrain;
			DetailScatterTerrain scatter = done.Result;
			summaryFrames += done.Frames;
			summaryMilliseconds += done.Milliseconds;
			if (scatter == null)
			{
				// A build that failed on a worker is taken again from the start, once; one with nothing to scatter is remembered.
				if (!done.Failed || !failedOnce.Add(terrain))
				{
					none[terrain] = terrain.terrainData;
				}
				return;
			}
			if (scatter.ClampedBlocks > 0 && scatter.ClampedChannel >= 0 && warned.Add("clamp " + terrain.name))
			{
				string name = types[scatter.ChannelSettings[scatter.ClampedChannel].Type].Name;
				Debug.LogWarning($"[Detail scatter] '{terrain.name}': {scatter.ClampedBlocks} block(s) painted denser than {DetailScatterMath.MaxPerBlock} instances per {DetailScatterMath.BlockMetres:F0} m block (first: '{name}'); they draw at that cap.");
			}
			foreach (string over in done.Overflow)
			{
				Problem(warned, null, "channels " + terrain.name + over, $"[Detail scatter] '{terrain.name}' paints more than {DetailScatterMath.MaxChannels} scattered types: '{over}' is not drawn there (GPU only: raise DetailScatterMath.MaxChannels).");
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
			summaryTerrains = summaryFrames = summaryLayers = summaryBaked = 0;
			summaryMilliseconds = 0;
			foreach (Taken t in new List<Taken>(taken.Values))
			{
				Release(t);
			}
			taken.Clear();
			scatterTerrains.Clear();
			none.Clear();
			failedOnce.Clear();
			types.Clear();
			typesByPrefab.Clear();
			renderer?.Dispose();
			renderer = null;
			rendererTried = false;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("DetailScatterSystem.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
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
