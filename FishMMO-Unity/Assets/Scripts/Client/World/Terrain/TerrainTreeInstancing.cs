using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the terrain tree channel — trees, boulders, rock formations, ice boulders, seracs — with
	/// GPU instancing on the client, in place of Unity's terrain tree renderer, which draws a LODGroup
	/// prototype one instance per draw call (42,004 draws for Cov Viaduct's trees; 437 without them).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Only the drawing is replaced.</b> The instances stay exactly the baked
	/// <see cref="TerrainData.treeInstances"/>, read once per terrain; nothing is written to the terrain
	/// data. Collision is untouched: the TerrainCollider builds tree colliders from the same prototypes and
	/// instances whatever the terrain draws, and the server (which never compiles this — it is in
	/// FishMMO.Client, excluded from server builds by its asmdef) keeps them all
	/// (ServerTerrainStripper).
	/// </para>
	/// <para>
	/// <b>How Unity's drawing is switched off.</b> Each terrain driven here has its
	/// <see cref="Terrain.treeDistance"/> set to 0 in play mode only, its own distance remembered and put
	/// back when the terrain is let go (unloaded, disabled, its data replaced, play mode exited, the
	/// player quitting). If something else sets a nonzero distance meanwhile — a settings screen — that
	/// is taken as the terrain's new distance and Unity's drawing is switched off again. In edit mode
	/// nothing happens at all: designers see Unity's trees, and no scene changes. The remembered distance
	/// is what the vegetation fade reads (<see cref="TreeDistanceOf"/>, used by
	/// <see cref="VegetationDistanceFade"/>), so trees still dissolve before their draw distance.
	/// </para>
	/// <para>
	/// <b>Not driven, left to Unity:</b> a terrain whose quality settings override the tree distance
	/// (a zero on the terrain would not stop Unity drawing, and both would draw); a terrain with no trees
	/// or a zero distance; and a terrain with a prototype the instanced path cannot draw (a Tree Creator or
	/// SpeedTree tree, a skinned or billboard renderer, a material without GPU instancing — logged once).
	/// </para>
	/// <para>
	/// <b>The hook.</b> Nothing is added to any scene: a <see cref="RuntimeInitializeOnLoadMethodAttribute"/>
	/// subscribes to the render pipeline's frame and camera callbacks, the frame callback follows
	/// <see cref="Terrain.GetActiveTerrains(List{Terrain})"/> (so additive world scenes are picked up as they
	/// load and dropped as they unload), and each camera — game, scene view in play mode, reflection
	/// probes — gathers and draws what it sees with its own level-of-detail choice.
	/// </para>
	/// <para>
	/// <b>Per camera.</b> Chunks (~64 m) are culled against the camera's frustum, or kept when their box
	/// swept along the sun can shadow the frustum; instances are bucketed by (prototype, level) across
	/// every terrain, so a prototype costs one draw per level and submesh (per 1023 instances), not one
	/// per tile. Levels are chosen per instance by Unity's LODGroup rule with the quality LOD bias, the
	/// terrain's tree LOD bias multiplier and the maximum LOD level.
	/// </para>
	/// <para>
	/// <b>Cross-fades are per instance and stateless.</b> Unity's <c>unity_LODFade</c> is per draw, so
	/// the shaders read their own instanced <c>_FishLodFade</c> (FishLodFade.hlsl). In a band just above
	/// each level's lower transition (<see cref="TerrainTreeMath.FadeWidth"/>: the LOD's own
	/// fadeTransitionWidth when the prefab sets one, else <see cref="TerrainTreeMath.DefaultFadeBandShare"/>
	/// of the transition height) an instance is drawn in both levels, the outgoing one with −f and the
	/// incoming one with +f, which partition one screen-space dither pattern in every pass, shadows
	/// included; before the last level is culled it fades out alone. Outside the bands every instance
	/// reads 0 (drawn whole). The fade follows the camera's distance, not time: standing still inside a
	/// band leaves a tree half-dithered between its two levels.
	/// </para>
	/// <para>
	/// <b>Two paths.</b> Where the platform has compute shaders and vertex-stage structured buffers
	/// (Vulkan, D3D11/12, Metal, WebGPU) the instances are resident on the GPU and culled, LOD-picked and
	/// faded per camera by FishTerrainInstancing.compute, then drawn with RenderMeshIndirect
	/// (<see cref="TerrainGpuRenderer"/>); elsewhere (WebGL2), and for any prototype the GPU path cannot take,
	/// the gather below fills RenderMeshInstanced buckets on the CPU. <see cref="PreferGpu"/> forces the fallback.
	/// </para>
	/// <para>
	/// <b>Lighting.</b> Each renderer's shadow casting, shadow receiving, reflection probe usage and
	/// rendering layer mask are kept. A probe-blending renderer is drawn with the scene's ambient probe
	/// (<see cref="LightProbeUsage.Off"/>, documented to provide it) where the scene has no baked light
	/// probes — none of FishMMO's scenes has any, and that ambient probe is exactly what Unity's own tree
	/// drawing lit them with — and with <see cref="LightProbeUsage.BlendProbes"/> where it has. The
	/// vegetation shader also floors its ambient at <c>FishTrilight</c>.
	/// </para>
	/// </remarks>
	public static class TerrainTreeInstancing
	{
		/// <summary>A terrain this renderer draws, and the tree distance it took over.</summary>
		private sealed class Driven
		{
			public Terrain Terrain;
			public TerrainData Data;
			public float Distance;
			public int Layer;
			public int TreeInstanceCount;
			public TerrainTreeField Field;
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

		private static readonly Dictionary<Terrain, Driven> driven = new Dictionary<Terrain, Driven>();
		private static readonly List<Driven> drivenList = new List<Driven>();
		private static readonly Dictionary<ModelKey, TerrainTreeModel> models = new Dictionary<ModelKey, TerrainTreeModel>();
		private static readonly List<TerrainTreeModel> modelList = new List<TerrainTreeModel>();
		/// <summary>Terrains refused, with the data they were refused for (tried again when it changes).</summary>
		private static readonly Dictionary<Terrain, TerrainData> refused = new Dictionary<Terrain, TerrainData>();
		private static readonly HashSet<string> warned = new HashSet<string>();
		private static readonly List<Terrain> active = new List<Terrain>();
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;
		private static TerrainGpuRenderer gpu;
		private static bool gpuTried;
		private static bool preferGpu = true;

		/// <summary>
		/// True (default) to draw with the GPU-driven path where the platform has it (compute shaders and
		/// vertex-stage structured buffers: Vulkan, D3D, Metal, WebGPU); false forces the RenderMeshInstanced
		/// fallback everywhere, for comparing the two. Switching lets every terrain go and takes them back.
		/// </summary>
		public static bool PreferGpu
		{
			get => preferGpu;
			set
			{
				if (preferGpu == value)
				{
					return;
				}
				preferGpu = value;
				ReleaseAll();
				syncedFrame = -1;
			}
		}

		/// <summary>True while the GPU-driven path is drawing.</summary>
		public static bool GpuActive => gpu != null;

		/// <summary>The GPU path's buffers, bytes (0 on the fallback).</summary>
		public static long GpuBytes => gpu != null ? gpu.GpuBytes : 0;

		/// <summary>SessionState keys (editor): the A/B buttons set them in edit mode too, and play mode starts from them.</summary>
		public const string PreferGpuKey = "FishMMO.TerrainInstancing.PreferGpu";
		public const string EnabledKey = "FishMMO.TerrainInstancing.Enabled";
		public const string LogUploadsKey = "FishMMO.TerrainInstancing.LogUploads";

		/// <summary>Prototype models in use, and how many of them the GPU path draws.</summary>
		public static int ModelCount => modelList.Count;
		public static int GpuModelCount
		{
			get
			{
				int n = 0;
				foreach (TerrainTreeModel m in modelList)
				{
					n += m.GpuId >= 0 ? 1 : 0;
				}
				return n;
			}
		}

		/// <summary>Reads the next frame of <paramref name="camera"/> back from the GPU path and reports it (false on the fallback).</summary>
		public static bool RequestGpuDiagnostics(Camera camera, System.Action<string> report)
		{
			if (gpu == null || camera == null)
			{
				return false;
			}
			gpu.RequestDiagnostics(camera, report);
			return true;
		}
		private static LightProbeUsage probeUsage = LightProbeUsage.Off;

		/// <summary>
		/// On by default. Switching it off lets every terrain go back to Unity's own tree drawing at its
		/// own distance (for comparing the two); on again takes them back on the next frame.
		/// </summary>
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

		/// <summary>Terrains currently drawn here.</summary>
		public static int DrivenTerrainCount => drivenList.Count;

		/// <summary>Instanced draws issued for the last camera rendered.</summary>
		public static int LastDrawCount { get; private set; }

		/// <summary>Instances gathered for the last camera rendered.</summary>
		public static int LastInstanceCount { get; private set; }

		/// <summary>
		/// The tree draw distance a terrain stands for: the distance this renderer took over when it drives
		/// the terrain (whose own <see cref="Terrain.treeDistance"/> it has set to 0), the terrain's own
		/// otherwise — always in edit mode. The vegetation fade reads this in place of the terrain's field.
		/// </summary>
		public static float TreeDistanceOf(Terrain terrain)
		{
			if (terrain == null)
			{
				return 0f;
			}
			return driven.TryGetValue(terrain, out Driven d) ? d.Distance : terrain.treeDistance;
		}

		/// <summary>True when this renderer is drawing the terrain's trees.</summary>
		public static bool IsDriving(Terrain terrain)
		{
			return terrain != null && driven.ContainsKey(terrain);
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			// With domain reload off, the last play session's state would still be here.
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
			// What the A/B buttons left (they work in edit mode too); statics do not survive a domain reload.
			preferGpu = UnityEditor.SessionState.GetBool(PreferGpuKey, true);
			enabled = UnityEditor.SessionState.GetBool(EnabledKey, true);
			TerrainGpuRenderer.LogUploads = UnityEditor.SessionState.GetBool(LogUploadsKey, false);
			TerrainInstancingProbe.Enabled = UnityEditor.SessionState.GetBool(TerrainInstancingProbe.EnabledKey, false);
#endif
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			Application.quitting += Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			// Scripts recompiled while playing reload in place: everything native must be let go first, or the
			// renderer's and the fields' NativeArrays leak (the "Leak Detected : Persistent" at each reload).
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
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
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
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

		/// <summary>Puts every terrain's own tree distance back, frees every buffer and stops listening.</summary>
		private static void Shutdown()
		{
			ReleaseAll();
			Unhook();
		}

		/// <summary>This system's once-a-frame work before the cameras render, named in the profiler (StartupTimeline reads it).</summary>
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("TerrainTreeInstancing.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using var contextScope = ContextMarker.Auto();
			Sync();
		}

		// ── Following the loaded terrains ─────────────────────────────

		/// <summary>Takes over newly active terrains and lets go of those gone or changed; once a frame.</summary>
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
			syncedFrame = Time.frameCount;
			if (!enabled)
			{
				return;
			}
			if (preferGpu && gpu == null && !gpuTried)
			{
				gpuTried = true;
				gpu = TerrainGpuRenderer.TryCreate();
				if (gpu != null)
				{
					gpu.ContactMode = 1f;   // trunk and rock bases blend into the terrain (FishGroundColour.hlsl)
				}
			}

			LightProbes probes = LightmapSettings.lightProbes;
			probeUsage = probes != null && probes.count > 0 ? LightProbeUsage.BlendProbes : LightProbeUsage.Off;

			Terrain.GetActiveTerrains(active);
			bool qualityOverride = (QualitySettings.terrainQualityOverrides & TerrainQualityOverrides.TreeDistance) != 0;

			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Driven d = drivenList[i];
				Terrain t = d.Terrain;
				bool keep = t != null
					&& active.Contains(t)
					&& t.terrainData == d.Data
					&& d.Data != null
					&& d.Data.treeInstanceCount == d.TreeInstanceCount
					&& LayerFor(t) == d.Layer
					&& !(qualityOverride && !t.ignoreQualitySettings);
				if (!keep)
				{
					Release(d);
					continue;
				}
				if (t.treeDistance != 0f)
				{
					// Something set a distance (a settings screen): that is the terrain's distance now.
					d.Distance = t.treeDistance;
					t.treeDistance = 0f;
				}
			}

			bool adopted = false;
			for (int i = 0; i < active.Count; i++)
			{
				Terrain t = active[i];
				if (t == null || driven.ContainsKey(t))
				{
					continue;
				}
				if (qualityOverride && !t.ignoreQualitySettings)
				{
					continue;
				}
				if (refused.TryGetValue(t, out TerrainData refusedData) && refusedData == t.terrainData)
				{
					continue;
				}
				adopted |= TryDrive(t);
			}
			if (adopted)
			{
				int instances = 0;
				foreach (Driven d in drivenList)
				{
					instances += d.Field.InstanceCount;
				}
				// Which path each model took: a model whose prefab has no indirect twin, an offset part or too many
				// levels stays on the RenderMeshInstanced fallback even while the GPU-driven path runs.
				int onGpu = 0;
				foreach (TerrainTreeModel model in modelList)
				{
					if (model.GpuId >= 0)
					{
						onGpu++;
					}
				}
				string path = gpu == null ? "all on the RenderMeshInstanced fallback (no GPU-driven path on this device)"
					: $"{onGpu} GPU-driven, {modelList.Count - onGpu} on the RenderMeshInstanced fallback";
				Debug.Log($"[Terrain trees] Drawing {instances} tree-channel instances of {modelList.Count} prototype models on {drivenList.Count} terrain(s) ({path}); Unity's terrain tree drawing is off on those terrains for this play session.");
			}
		}

		private static int LayerFor(Terrain terrain)
		{
			return terrain.preserveTreePrototypeLayers ? -1 : terrain.gameObject.layer;
		}

		private static bool TryDrive(Terrain terrain)
		{
			TerrainData data = terrain.terrainData;
			if (data == null || terrain.treeDistance <= 0f || data.treeInstanceCount == 0)
			{
				// Nothing to take over: Unity draws no trees here either.
				return false;
			}

			int layer = LayerFor(terrain);
			TreePrototype[] prototypes = data.treePrototypes;
			var resolved = new TerrainTreeModel[prototypes.Length];
			var created = new Dictionary<ModelKey, TerrainTreeModel>();
			for (int i = 0; i < prototypes.Length; i++)
			{
				GameObject prefab = prototypes[i]?.prefab;
				if (prefab == null)
				{
					continue;
				}
				var key = new ModelKey(prefab, layer);
				if (models.TryGetValue(key, out TerrainTreeModel model) || created.TryGetValue(key, out model))
				{
					resolved[i] = model;
					continue;
				}
				model = TerrainTreeModel.Build(prefab, layer, out string reason);
				if (model == null)
				{
					foreach (TerrainTreeModel made in created.Values)
					{
						made.Dispose();
					}
					refused[terrain] = data;
					if (warned.Add(reason))
					{
						Debug.LogWarning($"[Terrain trees] Leaving '{terrain.name}' (and any terrain with the same prototype) to Unity's own tree drawing: {reason}.");
					}
					return false;
				}
				created.Add(key, model);
				resolved[i] = model;
			}
			foreach (KeyValuePair<ModelKey, TerrainTreeModel> pair in created)
			{
				pair.Value.RegisterGpu(gpu);
				models.Add(pair.Key, pair.Value);
				modelList.Add(pair.Value);
			}

			TerrainTreeField field = TerrainTreeField.Build(terrain, resolved);
			field.UploadTo(gpu);
			for (int i = 0; i < resolved.Length; i++)
			{
				if (resolved[i] != null && field.PrototypeCounts[i] > 0)
				{
					resolved[i].Users += field.PrototypeCounts[i];
					resolved[i].EnsureCapacity(resolved[i].Users);
					gpu?.SetCapacity(resolved[i].GpuId, resolved[i].Users);
				}
			}

			var d = new Driven
			{
				Terrain = terrain,
				Data = data,
				Distance = terrain.treeDistance,
				Layer = layer,
				TreeInstanceCount = data.treeInstanceCount,
				Field = field,
			};
			driven.Add(terrain, d);
			drivenList.Add(d);
			refused.Remove(terrain);
			terrain.treeDistance = 0f;
			return true;
		}

		private static void Release(Driven d)
		{
			if (d.Terrain != null && d.Terrain.treeDistance == 0f)
			{
				d.Terrain.treeDistance = d.Distance;
			}
			driven.Remove(d.Terrain);
			drivenList.Remove(d);
			TerrainTreeModel[] used = d.Field.Models;
			for (int i = 0; i < used.Length; i++)
			{
				TerrainTreeModel model = used[i];
				if (model == null || d.Field.PrototypeCounts[i] == 0)
				{
					continue;
				}
				model.Users -= d.Field.PrototypeCounts[i];
				gpu?.SetCapacity(model.GpuId, Mathf.Max(0, model.Users));
			}
			d.Field.FreeFrom(gpu);
			d.Field.Dispose();
			for (int i = modelList.Count - 1; i >= 0; i--)
			{
				TerrainTreeModel model = modelList[i];
				if (model.Users > 0)
				{
					continue;
				}
				model.Dispose();
				modelList.RemoveAt(i);
				models.Remove(new ModelKey(model.Prefab, model.Layer));
			}
		}

		private static void ReleaseAll()
		{
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Release(drivenList[i]);
			}
			driven.Clear();
			foreach (TerrainTreeModel model in modelList)
			{
				model.Dispose();
			}
			modelList.Clear();
			models.Clear();
			refused.Clear();
			gpu?.Dispose();
			gpu = null;
			gpuTried = false;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!Application.isPlaying || !TerrainInstancingShared.Draws(camera))
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
			int instances = 0;
			// Reflection-probe faces take the CPU path: tiny, per face, and never worth a GPU cull of their own.
			TerrainGpuRenderer cameraGpu = camera.cameraType == CameraType.Reflection ? null : gpu;
			cameraGpu?.BeginCamera();
			float reach = 0f;
			for (int i = 0; i < drivenList.Count; i++)
			{
				Driven d = drivenList[i];
				if (d.Terrain == null || !d.Terrain.drawTreesAndFoliage)
				{
					continue;
				}
				instances += d.Field.Gather(in view, d.Distance, d.Terrain.treeLODBiasMultiplier, cameraGpu);
				reach = Mathf.Max(reach, d.Distance);
			}
			int draws = 0;
			for (int i = 0; i < modelList.Count; i++)
			{
				draws += modelList[i].Draw(camera, probeUsage);
			}
			if (cameraGpu != null)
			{
				draws += cameraGpu.Execute(camera, in view, reach, context);
			}
			LastDrawCount = draws;
			LastInstanceCount = instances;
		}
	}
}
