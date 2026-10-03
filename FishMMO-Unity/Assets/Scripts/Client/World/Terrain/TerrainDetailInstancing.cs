using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the terrain detail layers (grass, flowers, ferns, shrubs, reeds, pebbles…) on the client in
	/// place of Unity's per-patch detail drawing: the sibling of <see cref="TerrainTreeInstancing"/>, on the
	/// same infrastructure (chunk culling, the GPU-driven path with the RenderMeshInstanced fallback, the
	/// vegetation fade hand-off).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Play mode only, client only.</b> Each terrain driven here has its
	/// <see cref="Terrain.detailObjectDistance"/> set to 0 (its own remembered, put back when it is let
	/// go, at play-mode exit and at quit; followed if something else sets it). Edit mode is Unity's, so
	/// painting previews as before. Details have no colliders; the server strips them anyway.
	/// </para>
	/// <para>
	/// <b>The instances</b> come from the baked detail maps, placed deterministically per world cell
	/// (<see cref="TerrainDetailMath"/>, <see cref="TerrainDetailField"/>), in ~32 m chunks built lazily
	/// within the detail distance, nearest first, within <see cref="BuildBudgetMilliseconds"/> a frame
	/// (more for the first frames after a terrain is taken: <see cref="WarmupBudgetMilliseconds"/>), cached,
	/// and released after <see cref="KeepFrames"/> frames out of reach.
	/// </para>
	/// <para>
	/// <b>Not driven, left to Unity:</b> a terrain whose quality settings override the detail distance, a
	/// terrain with no details or a zero distance, and a terrain with a prototype this cannot draw (a
	/// texture/billboard grass prototype, a mesh drawn through Unity's grass shader, a material without
	/// GPU instancing — logged once).
	/// </para>
	/// <para>
	/// <b>Shadows</b> follow each prototype prefab's MeshRenderer (the generator casts them for ferns, shrubs
	/// and cacti). <b>Tint</b> needs nothing per instance: FishVegetation hashes the healthy/dry blend from
	/// the instance's origin, between the material's colours.
	/// </para>
	/// </remarks>
	public static class TerrainDetailInstancing
	{
		/// <summary>Chunk building time per frame, milliseconds.</summary>
		public const float BuildBudgetMilliseconds = 2f;

		/// <summary>
		/// Missing chunks this close to a camera are built this frame, whatever the budget (up to
		/// <see cref="MaxNearBuildsPerFrame"/>): a bare patch at your feet is the gap people see, a late chunk
		/// at the edge of the detail distance is not. A dense chunk takes ~1–1.6 ms, so the budget alone
		/// builds about one a frame, and after a teleport or a fast run the chunks around you lagged by seconds.
		/// </summary>
		public const float NearBuildMetres = 40f;

		public const int MaxNearBuildsPerFrame = 12;

		/// <summary>Chunk building time per frame for <see cref="WarmupFrames"/> frames after a terrain is taken.</summary>
		public const float WarmupBudgetMilliseconds = 8f;

		public const int WarmupFrames = 30;

		/// <summary>Frames a built chunk is kept after the last camera wanted it.</summary>
		public const int KeepFrames = 120;

		private sealed class Driven
		{
			public Terrain Terrain;
			public TerrainData Data;
			/// <summary>The distance drawn to: the player's grass distance (<see cref="EffectiveDistance"/>).</summary>
			public float Distance;
			/// <summary>The terrain's own detail distance, as the scene authored it: put back on release.</summary>
			public float Authored;
			public int Layer;
			public int AdoptedFrame;
			public TerrainDetailField Field;
		}

		private readonly struct ModelKey : System.IEquatable<ModelKey>
		{
			public readonly GameObject Prefab;
			public readonly int Layer;
			public ModelKey(GameObject prefab, int layer) { Prefab = prefab; Layer = layer; }
			public bool Equals(ModelKey other) => ReferenceEquals(Prefab, other.Prefab) && Layer == other.Layer;
			public override bool Equals(object obj) => obj is ModelKey k && Equals(k);
			public override int GetHashCode() => (Prefab != null ? Prefab.GetHashCode() : 0) * 397 ^ Layer;
		}

		private sealed class NearestFirst : IComparer<TerrainDetailField.Missing>
		{
			public int Compare(TerrainDetailField.Missing a, TerrainDetailField.Missing b) => a.Distance.CompareTo(b.Distance);
		}

		private static readonly Dictionary<Terrain, Driven> driven = new Dictionary<Terrain, Driven>();
		private static readonly List<Driven> drivenList = new List<Driven>();
		private static readonly Dictionary<ModelKey, TerrainDetailModel> models = new Dictionary<ModelKey, TerrainDetailModel>();
		private static readonly Dictionary<TerrainDetailModel, int> users = new Dictionary<TerrainDetailModel, int>();
		private static readonly List<TerrainDetailModel> modelList = new List<TerrainDetailModel>();
		private static readonly Dictionary<Terrain, TerrainData> refused = new Dictionary<Terrain, TerrainData>();
		/// <summary>Per terrain, the prototypes another renderer draws (the procedural grass, <see cref="SetSkipped"/>).</summary>
		private static readonly Dictionary<Terrain, bool[]> skipped = new Dictionary<Terrain, bool[]>();
		private static readonly HashSet<string> warned = new HashSet<string>();
		private static readonly List<Terrain> active = new List<Terrain>();
		private static readonly List<TerrainDetailField.Missing> missing = new List<TerrainDetailField.Missing>();
		private static readonly NearestFirst nearestFirst = new NearestFirst();
		private static readonly Stopwatch stopwatch = new Stopwatch();
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;
		private static LightProbeUsage probeUsage = LightProbeUsage.Off;
		private static TerrainGpuRenderer gpu;
		private static bool gpuTried;
		private static bool lastPreferGpu = true;

		/// <summary>On by default; off gives every terrain back to Unity's detail drawing (A/B).</summary>
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

		/// <summary>True while the GPU-driven path draws the details.</summary>
		public static bool GpuActive => gpu != null;

		public static int DrivenTerrainCount => drivenList.Count;

		/// <summary>Prototype models in use, and how many of them the GPU path draws.</summary>
		public static int ModelCount => modelList.Count;
		public static int GpuModelCount
		{
			get
			{
				int n = 0;
				foreach (TerrainDetailModel m in modelList)
				{
					n += m.GpuId >= 0 ? 1 : 0;
				}
				return n;
			}
		}

		/// <summary>Reads the next frame of <paramref name="camera"/> back from the GPU path and reports it (false on the fallback).</summary>
		public static bool RequestGpuDiagnostics(Camera camera, System.Action<string> report)
		{
			if (camera == null)
			{
				return false;
			}
			// Every chunk within reach, and what this camera did with it, logged with the next frame.
			traceCamera = camera;
			traceReport = report;
			if (gpu == null)
			{
				return true;
			}
			gpu.RequestDiagnostics(camera, report);
			return true;
		}

		private static Camera traceCamera;
		private static System.Action<string> traceReport;
		public static int LastDrawCount { get; private set; }
		public static int LastInstanceCount { get; private set; }

		/// <summary>Chunks built last frame, and the milliseconds it took.</summary>
		public static int LastBuiltChunks { get; private set; }
		public static float LastBuildMilliseconds { get; private set; }

		/// <summary>Chunks resident over every driven terrain.</summary>
		public static int ResidentChunks
		{
			get
			{
				int n = 0;
				foreach (Driven d in drivenList)
				{
					n += d.Field.BuiltCount;
				}
				return n;
			}
		}

		/// <summary>
		/// The detail distance a terrain stands for: the one this renderer took over when it drives the terrain
		/// (whose own <see cref="Terrain.detailObjectDistance"/> it has set to 0), the terrain's own otherwise —
		/// always in edit mode. The vegetation fade reads this in place of the terrain's field.
		/// </summary>
		public static float DetailDistanceOf(Terrain terrain)
		{
			if (terrain == null)
			{
				return 0f;
			}
			return driven.TryGetValue(terrain, out Driven d) ? d.Distance : terrain.detailObjectDistance;
		}

		public static bool IsDriving(Terrain terrain) => terrain != null && driven.ContainsKey(terrain);

		/// <summary>
		/// Leaves the prototypes set in <paramref name="mask"/> (by index into the terrain's detail prototypes)
		/// undrawn on <paramref name="terrain"/>, because another renderer draws them — the procedural blade
		/// grass (<see cref="GrassBladeSystem"/>) while it runs there; null draws them all again. A driven
		/// terrain is let go and taken straight back with the new set, so it is never handed to Unity's own
		/// detail drawing in between. Kept across this renderer's own releases (its A/B, a path switch) until
		/// the owner clears it.
		/// </summary>
		public static void SetSkipped(Terrain terrain, bool[] mask)
		{
			if (terrain == null)
			{
				return;
			}
			bool had = skipped.TryGetValue(terrain, out bool[] old);
			if (mask == null)
			{
				if (!had)
				{
					return;
				}
				skipped.Remove(terrain);
			}
			else
			{
				if (had && SameMask(old, mask))
				{
					return;
				}
				skipped[terrain] = (bool[])mask.Clone();
			}
			refused.Remove(terrain);
			if (driven.TryGetValue(terrain, out Driven d))
			{
				Release(d);
				TryDrive(terrain, Time.frameCount);
			}
		}

		/// <summary>Whether a prototype of a terrain is left to another renderer.</summary>
		public static bool IsSkipped(Terrain terrain, int prototype)
		{
			return terrain != null && skipped.TryGetValue(terrain, out bool[] mask) && prototype >= 0 && prototype < mask.Length && mask[prototype];
		}

		private static bool SameMask(bool[] a, bool[] b)
		{
			if (a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The player's grass distance (<see cref="ClientGrassSettings"/>), cached: re-read on its change.</summary>
		private static float grassDistance = ClientGrassSettings.DefaultDistance;

		/// <summary>
		/// The distance a driven terrain's details are drawn to: the player's grass distance on the GPU path,
		/// which culls and thins on the GPU and so can go far past what the scene authored for Unity's own
		/// drawing; on the CPU fallback never past the scene's own distance, every drawn instance being CPU work there.
		/// </summary>
		private static float EffectiveDistance(float authored) => gpu != null ? grassDistance : Mathf.Min(grassDistance, authored);

		private static void OnGrassSettingsChanged() => grassDistance = ClientGrassSettings.Distance;

		/// <summary>The density scale Unity would use for a terrain: the quality override where it applies, else the terrain's own.</summary>
		public static float DensityScaleOf(Terrain terrain)
		{
			bool overridden = (QualitySettings.terrainQualityOverrides & TerrainQualityOverrides.DetailDensity) != 0 && !terrain.ignoreQualitySettings;
			return overridden ? QualitySettings.terrainDetailDensityScale : terrain.detailObjectDensity;
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
			warned.Clear();
			skipped.Clear();
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
			enabled = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.EnabledKey, true);
			lastPreferGpu = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.PreferGpuKey, true);
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

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			Sync();
		}

		// ── Following the loaded terrains ─────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			TerrainInstancingProbe.BeginFrame();
			double start = TerrainInstancingProbe.Now;
			SyncFrame();
			TerrainInstancingProbe.SyncMs += TerrainInstancingProbe.Now - start;
		}

		private static void SyncFrame()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			int frame = Time.frameCount;
			syncedFrame = frame;
			if (!enabled)
			{
				return;
			}
			if (TerrainTreeInstancing.PreferGpu != lastPreferGpu)
			{
				// The path was switched (A/B): let everything go and take it back on the new path.
				lastPreferGpu = TerrainTreeInstancing.PreferGpu;
				ReleaseAll();
			}
			if (TerrainTreeInstancing.PreferGpu && gpu == null && !gpuTried)
			{
				gpuTried = true;
				gpu = TerrainGpuRenderer.TryCreate();
			}

			LightProbes probes = LightmapSettings.lightProbes;
			probeUsage = probes != null && probes.count > 0 ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;

			Terrain.GetActiveTerrains(active);
			bool qualityOverride = (QualitySettings.terrainQualityOverrides & TerrainQualityOverrides.DetailDistance) != 0;
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Driven d = drivenList[i];
				Terrain t = d.Terrain;
				bool keep = t != null && active.Contains(t) && t.terrainData == d.Data && d.Data != null
					&& LayerFor(t) == d.Layer && !(qualityOverride && !t.ignoreQualitySettings);
				if (!keep)
				{
					Release(d);
					continue;
				}
				if (t.detailObjectDistance != 0f)
				{
					d.Authored = t.detailObjectDistance;
					t.detailObjectDistance = 0f;
				}
				// The player's grass distance can change at any time: the next gathers reach further (or less),
				// building the chunks that come into reach; the vegetation fade follows through DetailDistanceOf.
				d.Distance = EffectiveDistance(d.Authored);
				float density = DensityScaleOf(t);
				if (density != d.Field.DensityScale)
				{
					// A settings change: the counts change, so every chunk is rebuilt (the same plants, more or fewer).
					d.Field.ReleaseAll();
					d.Field.SetDensityScale(density);
				}
				d.Field.ReleaseStale(frame - KeepFrames);
			}

			bool adopted = false;
			for (int i = 0; i < active.Count; i++)
			{
				Terrain t = active[i];
				if (t == null || driven.ContainsKey(t) || (qualityOverride && !t.ignoreQualitySettings))
				{
					continue;
				}
				if (refused.TryGetValue(t, out TerrainData refusedData) && refusedData == t.terrainData)
				{
					continue;
				}
				adopted |= TryDrive(t, frame);
			}
			if (adopted)
			{
				Debug.Log($"[Terrain details] Drawing the detail layers of {drivenList.Count} terrain(s) ({modelList.Count} prototype models, {(gpu != null ? "GPU-driven" : "RenderMeshInstanced fallback")}); Unity's detail drawing is off on those terrains for this play session.");
			}

			BuildMissing(frame);
		}

		/// <summary>Builds the chunks cameras wanted last frame, nearest first, within the frame's budget.</summary>
		private static void BuildMissing(int frame)
		{
			LastBuiltChunks = 0;
			LastBuildMilliseconds = 0f;
			if (missing.Count == 0)
			{
				return;
			}
			bool warmup = false;
			foreach (Driven d in drivenList)
			{
				warmup |= frame - d.AdoptedFrame < WarmupFrames;
			}
			float budget = warmup ? WarmupBudgetMilliseconds : BuildBudgetMilliseconds;
			missing.Sort(nearestFirst);
			stopwatch.Restart();
			for (int i = 0; i < missing.Count; i++)
			{
				TerrainDetailField.Missing m = missing[i];
				if (m.Field == null || !driven.ContainsKey(m.Field.Terrain))
				{
					continue;
				}
				bool near = m.Distance < NearBuildMetres && LastBuiltChunks < MaxNearBuildsPerFrame;
				if (!near && stopwatch.Elapsed.TotalMilliseconds >= budget)
				{
					break;
				}
				m.Field.Build(m.Index);
				LastBuiltChunks++;
			}
			LastBuildMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
			stopwatch.Stop();
			TerrainInstancingProbe.Builds += LastBuiltChunks;
			TerrainInstancingProbe.BuildMs += LastBuildMilliseconds;
			missing.Clear();
		}

		private static int LayerFor(Terrain terrain) => terrain.gameObject.layer;

		private static bool TryDrive(Terrain terrain, int frame)
		{
			TerrainData data = terrain.terrainData;
			if (data == null || terrain.detailObjectDistance <= 0f)
			{
				return false;
			}
			DetailPrototype[] prototypes = data.detailPrototypes;
			if (prototypes.Length == 0)
			{
				return false;
			}
			int layer = LayerFor(terrain);
			var resolved = new TerrainDetailModel[prototypes.Length];
			var created = new Dictionary<ModelKey, TerrainDetailModel>();
			for (int i = 0; i < prototypes.Length; i++)
			{
				if (IsSkipped(terrain, i))
				{
					// Drawn by another renderer (the procedural grass): no model, no instances.
					resolved[i] = null;
					continue;
				}
				GameObject prefab = prototypes[i]?.prototype;
				var key = new ModelKey(prefab, layer);
				if (prefab != null && (models.TryGetValue(key, out TerrainDetailModel model) || created.TryGetValue(key, out model)))
				{
					resolved[i] = model;
					continue;
				}
				model = TerrainDetailModel.Build(prototypes[i], layer, out string reason);
				if (model == null)
				{
					foreach (TerrainDetailModel made in created.Values)
					{
						made.Dispose();
					}
					refused[terrain] = data;
					if (warned.Add(reason))
					{
						Debug.LogWarning($"[Terrain details] Leaving '{terrain.name}' (and any terrain with the same prototype) to Unity's own detail drawing: {reason}.");
					}
					return false;
				}
				created.Add(key, model);
				resolved[i] = model;
			}
			foreach (KeyValuePair<ModelKey, TerrainDetailModel> pair in created)
			{
				pair.Value.RegisterGpu(gpu);
				models.Add(pair.Key, pair.Value);
				modelList.Add(pair.Value);
				users[pair.Value] = 0;
			}
			foreach (TerrainDetailModel model in resolved)
			{
				if (model != null)
				{
					users[model]++;
				}
			}

			TerrainDetailField field = TerrainDetailField.Create(terrain, resolved, DensityScaleOf(terrain));
			field.SetPrototypes(prototypes);
			field.Gpu = gpu;
			if (gpu != null)
			{
				// Each prototype's slot capacity follows its measured resident count (doubling, see
				// TerrainDetailField.Upload) up to the most the detail disc can contain. Sizing the slots and the
				// instance buffer to that bound up front reserved ~160 MB on Cov Viaduct, and uploading it was
				// the stall; real ground is shared between prototypes, so the bound is never approached.
				float distance = EffectiveDistance(terrain.detailObjectDistance);
				for (int p = 0; p < resolved.Length; p++)
				{
					TerrainDetailModel model = resolved[p];
					long bound = model != null && model.GpuId >= 0 ? field.CapacityBound(p, distance) : -1;
					if (bound > 0)
					{
						model.CapacityCeiling = (int)Mathf.Max(model.CapacityCeiling, Mathf.Min(bound, 8_000_000));
					}
				}
			}
			var d = new Driven
			{
				Terrain = terrain,
				Data = data,
				Distance = EffectiveDistance(terrain.detailObjectDistance),
				Authored = terrain.detailObjectDistance,
				Layer = layer,
				AdoptedFrame = frame,
				Field = field,
			};
			driven.Add(terrain, d);
			drivenList.Add(d);
			refused.Remove(terrain);
			terrain.detailObjectDistance = 0f;
			return true;
		}

		private static void Release(Driven d)
		{
			if (d.Terrain != null && d.Terrain.detailObjectDistance == 0f)
			{
				d.Terrain.detailObjectDistance = d.Authored;
			}
			driven.Remove(d.Terrain);
			drivenList.Remove(d);
			d.Field.Dispose();
			foreach (TerrainDetailModel model in d.Field.Models)
			{
				if (model != null && users.ContainsKey(model))
				{
					users[model]--;
				}
			}
			for (int i = modelList.Count - 1; i >= 0; i--)
			{
				TerrainDetailModel model = modelList[i];
				if (users[model] > 0)
				{
					continue;
				}
				gpu?.SetCapacity(model.GpuId, 0);
				model.Dispose();
				users.Remove(model);
				modelList.RemoveAt(i);
				models.Remove(new ModelKey(model.Prefab, model.Layer));
			}
			for (int i = missing.Count - 1; i >= 0; i--)
			{
				if (missing[i].Field == d.Field)
				{
					missing.RemoveAt(i);
				}
			}
		}

		private static void ReleaseAll()
		{
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Release(drivenList[i]);
			}
			driven.Clear();
			foreach (TerrainDetailModel model in modelList)
			{
				model.Dispose();
			}
			modelList.Clear();
			models.Clear();
			users.Clear();
			refused.Clear();
			missing.Clear();
			TerrainDetailField.DisposePool();
			gpu?.Dispose();
			gpu = null;
			gpuTried = false;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!Application.isPlaying || !TerrainInstancingShared.DrawsDetails(camera))
			{
				return;
			}
			Sync();
			if (drivenList.Count == 0)
			{
				return;
			}
			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			for (int i = 0; i < modelList.Count; i++)
			{
				modelList[i].Clear();
			}
			// Reflection-probe faces take the CPU path: tiny, per face, and never worth a GPU cull of their own.
			TerrainGpuRenderer cameraGpu = camera.cameraType == CameraType.Reflection ? null : gpu;
			cameraGpu?.BeginCamera();
			int frame = Time.frameCount;
			System.Text.StringBuilder trace = null;
			System.Action<string> traceTo = null;
			if (traceCamera == camera && traceReport != null)
			{
				trace = new System.Text.StringBuilder("[Terrain detail chunks] within reach of the camera:");
				traceTo = traceReport;
				traceCamera = null;
				traceReport = null;
			}
			int instances = 0;
			float reach = 0f;
			for (int i = 0; i < drivenList.Count; i++)
			{
				Driven d = drivenList[i];
				if (d.Terrain == null || !d.Terrain.drawTreesAndFoliage)
				{
					continue;
				}
				// A camera on the CPU path (a reflection probe's) draws every instance it gathers, unthinned:
				// never past the scene's own distance, however far the GPU path reaches.
				float distance = cameraGpu != null ? d.Distance : Mathf.Min(d.Distance, d.Authored);
				instances += d.Field.Gather(in view, distance, frame, missing, cameraGpu != null, trace);
				reach = Mathf.Max(reach, distance);
			}
			int draws = 0;
			for (int i = 0; i < modelList.Count; i++)
			{
				draws += modelList[i].Draw(camera, probeUsage);
			}
			if (cameraGpu != null)
			{
				draws += cameraGpu.Execute(camera, in view, reach + TerrainDetailMath.DefaultChunkMetres, context);
			}
			LastDrawCount = draws;
			LastInstanceCount = instances;
			traceTo?.Invoke(trace.ToString());
		}
	}
}
