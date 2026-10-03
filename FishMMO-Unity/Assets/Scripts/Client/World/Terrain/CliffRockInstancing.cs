using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the generated cliffs' rocks (every <see cref="GeneratedCliffs"/> root's LOD groups) through the
	/// GPU-driven path: one <see cref="TerrainGpuRenderer"/> culls each rock per camera, picks its level with
	/// the LOD group's own transitions and cross-fade, and draws each (mesh, level, view) with one indirect
	/// call. A scene with kilometres of cliff — 40 rocks per 100 m, each its own LOD group, shadow-casting —
	/// was 1,000–2,500 draws as scene objects; here it is a few per distinct rock mesh.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Play mode only, client only, and only where the GPU path runs.</b> The rocks' own renderers are
	/// switched off (<see cref="Renderer.forceRenderingOff"/>) while this draws them and switched back on
	/// when it lets them go; their colliders are untouched (the server keeps those, and builds strip the
	/// renderers' "Visual" children from servers anyway). Without compute (WebGL2) or with the GPU path
	/// switched off (the dashboard's A/B button), nothing is taken over and Unity draws the LOD groups.
	/// </para>
	/// <para>
	/// <b>Rocks are static.</b> Each root's rocks are read once when its scene is first seen — models keyed
	/// by (meshes, material, layer, transitions), instance transforms from the "Visual" objects, grouped into
	/// 64 m chunks so the CPU only submits the chunks a camera (or its sun-swept shadow volume) can see.
	/// A rock's transform is T·R·S with its scale on the rock object and the LOD children at identity, so
	/// the GPU path's shear-free 3×4 contract holds (TerrainTreeModel.RegisterGpu refuses anything else).
	/// </para>
	/// <para>
	/// Reflection-probe cameras take the CPU path at the lowest level (RenderMeshInstanced): tiny, per face,
	/// and never worth a GPU cull of their own.
	/// </para>
	/// </remarks>
	public static class CliffRockInstancing
	{
		/// <summary>Chunk side, metres: what a camera's frustum test accepts or rejects as one.</summary>
		public const float ChunkMetres = 64f;

		private sealed class Run
		{
			public TerrainTreeModel Model;
			public int Start;
			public int Count;
			public float MinSize, MaxSize;
		}

		private sealed class Chunk
		{
			public Bounds Bounds;
			public readonly List<Run> Runs = new List<Run>();
		}

		private sealed class Driven
		{
			public GeneratedCliffs Root;
			public readonly List<Chunk> Chunks = new List<Chunk>();
			public readonly List<Renderer> Hidden = new List<Renderer>();
			public NativeArray<FishInstance> Instances;
			public NativeArray<Matrix4x4> Matrices;
			public int GpuOffset = -1;
		}

		private static readonly List<Driven> drivenList = new List<Driven>();
		private static readonly HashSet<GeneratedCliffs> drivenRoots = new HashSet<GeneratedCliffs>();
		private static readonly Dictionary<string, TerrainTreeModel> models = new Dictionary<string, TerrainTreeModel>();
		private static readonly List<TerrainTreeModel> modelList = new List<TerrainTreeModel>();
		private static readonly HashSet<string> refused = new HashSet<string>();
		/// <summary>Rocks per model across every driven root: the CPU (reflection) buckets' capacity.</summary>
		private static readonly Dictionary<TerrainTreeModel, int> instancesOf = new Dictionary<TerrainTreeModel, int>();
		private static TerrainGpuRenderer gpu;
		private static bool gpuTried, hooked, scenesChanged = true;
		private static bool enabled = true, preferGpu = true;
		private static int syncedFrame = -1;

		/// <summary>Rocks drawn by this path, and the draw calls the last camera cost.</summary>
		public static int RockCount { get; private set; }
		public static int LastDrawCount { get; private set; }

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
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
			scenesChanged = true;
#if UNITY_EDITOR
			// The terrain A/B buttons' choices (TerrainTreeInstancing's keys) govern the cliffs too.
			enabled = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.EnabledKey, true);
			preferGpu = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.PreferGpuKey, true);
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;
			Application.quitting += Shutdown;
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

		private static void Shutdown()
		{
			ReleaseAll();
			Unhook();
		}

		private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => scenesChanged = true;

		private static void OnSceneUnloaded(Scene scene) => scenesChanged = true;

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras) => Sync();

		// ── Following the loaded cliffs ───────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			if (!enabled || !preferGpu)
			{
				return;
			}
			if (gpu == null && !gpuTried)
			{
				gpuTried = true;
				gpu = TerrainGpuRenderer.TryCreate();
			}
			if (gpu == null)
			{
				return;
			}

			// Let go of roots that were destroyed (their scene unloaded) before taking on new ones.
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				if (drivenList[i].Root == null)
				{
					Release(drivenList[i]);
				}
			}
			if (!scenesChanged)
			{
				return;
			}
			scenesChanged = false;
			int before = drivenList.Count;
			foreach (GeneratedCliffs root in Object.FindObjectsByType<GeneratedCliffs>())
			{
				if (root != null && !drivenRoots.Contains(root))
				{
					Drive(root);
				}
			}
			if (drivenList.Count != before)
			{
				Debug.Log($"[Cliff rocks] Drawing {RockCount} cliff rocks of {modelList.Count} distinct meshes on the GPU-driven path ({drivenList.Count} cliff root(s)); their own renderers are off for this play session.");
			}
		}

		/// <summary>Reads a cliff root's rocks into models, chunks and one GPU range, and switches their renderers off.</summary>
		private static void Drive(GeneratedCliffs root)
		{
			var d = new Driven { Root = root };
			drivenRoots.Add(root);
			var matrices = new List<Matrix4x4>();
			var byChunk = new Dictionary<Vector2Int, Dictionary<TerrainTreeModel, List<int>>>();
			var groups = root.GetComponentsInChildren<LODGroup>(true);
			foreach (LODGroup group in groups)
			{
				TerrainTreeModel model = ModelFor(group);
				if (model == null)
				{
					continue;   // left to Unity: drawn by its own renderers
				}
				Matrix4x4 m = group.transform.localToWorldMatrix;
				int index = matrices.Count;
				matrices.Add(m);
				Vector3 p = m.GetColumn(3);
				var key = new Vector2Int(Mathf.FloorToInt(p.x / ChunkMetres), Mathf.FloorToInt(p.z / ChunkMetres));
				if (!byChunk.TryGetValue(key, out var runs))
				{
					byChunk[key] = runs = new Dictionary<TerrainTreeModel, List<int>>();
				}
				if (!runs.TryGetValue(model, out var list))
				{
					runs[model] = list = new List<int>();
				}
				list.Add(index);
				foreach (LOD lod in group.GetLODs())
				{
					foreach (Renderer r in lod.renderers)
					{
						if (r != null && !r.forceRenderingOff)
						{
							r.forceRenderingOff = true;
							d.Hidden.Add(r);
						}
					}
				}
			}
			if (matrices.Count == 0)
			{
				drivenList.Add(d);
				return;
			}

			// Instances laid out chunk by chunk, model by model, so every (chunk, model) is one contiguous run.
			d.Instances = new NativeArray<FishInstance>(matrices.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			d.Matrices = new NativeArray<Matrix4x4>(matrices.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			int at = 0;
			foreach (var chunkPair in byChunk)
			{
				var chunk = new Chunk();
				bool any = false;
				foreach (var runPair in chunkPair.Value)
				{
					TerrainTreeModel model = runPair.Key;
					var run = new Run { Model = model, Start = at, MinSize = float.MaxValue, MaxSize = 0f };
					foreach (int i in runPair.Value)
					{
						Matrix4x4 m = matrices[i];
						d.Matrices[at] = m;
						d.Instances[at] = FishInstance.From(m);
						at++;
						float scale = Mathf.Max(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude);
						run.MinSize = Mathf.Min(run.MinSize, model.Size * scale);
						run.MaxSize = Mathf.Max(run.MaxSize, model.Size * scale);
						Bounds b = TerrainTreeMath.TransformBounds(m, model.LocalBounds);
						if (any)
						{
							chunk.Bounds.Encapsulate(b);
						}
						else
						{
							chunk.Bounds = b;
							any = true;
						}
					}
					run.Count = at - run.Start;
					chunk.Runs.Add(run);
					instancesOf.TryGetValue(model, out int total);
					instancesOf[model] = total + run.Count;
					model.EnsureCapacity(total + run.Count);
				}
				d.Chunks.Add(chunk);
			}
			d.GpuOffset = gpu.Upload(d.Instances, 0, d.Instances.Length, $"{root.gameObject.scene.name} cliff rocks");
			RockCount += d.Instances.Length;
			drivenList.Add(d);
		}

		/// <summary>The model for a rock's LOD group (shared by every rock of the same meshes, material and levels), or null when it cannot be drawn here.</summary>
		private static TerrainTreeModel ModelFor(LODGroup group)
		{
			string key = KeyOf(group);
			if (key == null || refused.Contains(key))
			{
				return null;
			}
			if (models.TryGetValue(key, out TerrainTreeModel model))
			{
				return model;
			}
			model = TerrainTreeModel.Build(group.gameObject, group.gameObject.layer, out string reason);
			if (model != null)
			{
				model.RegisterGpu(gpu);
				if (model.GpuId < 0)
				{
					reason = "its parts are not at the LOD group's origin, or its shader has no indirect variant";
					model.Dispose();
					model = null;
				}
			}
			if (model == null)
			{
				refused.Add(key);
				Debug.LogWarning($"[Cliff rocks] Leaving '{group.name}' ({key}) to Unity's own drawing: {reason}.");
				return null;
			}
			models[key] = model;
			modelList.Add(model);
			return model;
		}

		/// <summary>What makes two rocks the same model: every level's meshes and materials, the transitions and the layer.</summary>
		private static string KeyOf(LODGroup group)
		{
			var key = new System.Text.StringBuilder();
			key.Append(group.gameObject.layer);
			foreach (LOD lod in group.GetLODs())
			{
				key.Append('|').Append(lod.screenRelativeTransitionHeight.ToString("R"));
				foreach (Renderer r in lod.renderers)
				{
					MeshFilter f = r != null ? r.GetComponent<MeshFilter>() : null;
					if (f == null || f.sharedMesh == null)
					{
						return null;
					}
					key.Append(':').Append(f.sharedMesh.GetEntityId());
					foreach (Material m in r.sharedMaterials)
					{
						key.Append(',').Append(m != null ? m.GetEntityId().ToString() : "-");
					}
				}
			}
			return key.ToString();
		}

		private static void Release(Driven d)
		{
			foreach (Renderer r in d.Hidden)
			{
				if (r != null)
				{
					r.forceRenderingOff = false;
				}
			}
			if (gpu != null && d.GpuOffset >= 0)
			{
				gpu.Free(d.GpuOffset);
			}
			if (d.Instances.IsCreated)
			{
				RockCount -= d.Instances.Length;
				d.Instances.Dispose();
			}
			if (d.Matrices.IsCreated)
			{
				d.Matrices.Dispose();
			}
			drivenList.Remove(d);
			drivenRoots.Remove(d.Root);
		}

		/// <summary>Gives every rock back to Unity's own drawing and frees every buffer.</summary>
		public static void ReleaseAll()
		{
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Release(drivenList[i]);
			}
			drivenRoots.Clear();
			foreach (TerrainTreeModel model in modelList)
			{
				model.Dispose();
			}
			modelList.Clear();
			models.Clear();
			refused.Clear();
			instancesOf.Clear();
			gpu?.Dispose();
			gpu = null;
			gpuTried = false;
			RockCount = 0;
			scenesChanged = true;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!Application.isPlaying || !TerrainInstancingShared.Draws(camera))
			{
				return;
			}
			Sync();
			if (gpu == null || drivenList.Count == 0)
			{
				return;
			}

			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			bool reflection = camera.cameraType == CameraType.Reflection;
			float drawDistance = camera.farClipPlane;
			for (int i = 0; i < modelList.Count; i++)
			{
				modelList[i].Clear();
			}
			if (!reflection)
			{
				gpu.BeginCamera();
			}
			Vector3 eye = view.Position;
			foreach (Driven d in drivenList)
			{
				if (d.GpuOffset < 0)
				{
					continue;
				}
				foreach (Chunk chunk in d.Chunks)
				{
					float near = TerrainTreeMath.MinDistance(chunk.Bounds, eye);
					if (near > drawDistance)
					{
						continue;
					}
					bool inView = TerrainTreeMath.IntersectsFrustum(view.Planes, chunk.Bounds);
					bool shadowOnly = !inView && view.Shadows && near <= view.ShadowDistance
						&& TerrainTreeMath.IntersectsFrustum(view.Planes, TerrainTreeMath.ShadowSweep(chunk.Bounds, view.LightDirection, view.ShadowDistance));
					if (!inView && !shadowOnly)
					{
						continue;
					}
					float far = TerrainTreeMath.MaxDistance(chunk.Bounds, eye);
					foreach (Run run in chunk.Runs)
					{
						TerrainTreeModel model = run.Model;
						if (reflection)
						{
							// The lowest level of every rock in a visible chunk: a probe face needs the shapes, not the detail.
							if (inView)
							{
								int last = model.Transitions.Length - 1;
								for (int i = run.Start; i < run.Start + run.Count; i++)
								{
									model.Append(last, d.Matrices[i], 0f, in chunk.Bounds);
								}
							}
							continue;
						}
						float nearHeight = TerrainTreeMath.RelativeHeight(run.MaxSize, near, view.ScreenFactor, view.Orthographic);
						float farHeight = TerrainTreeMath.RelativeHeight(run.MinSize, far, view.ScreenFactor, view.Orthographic);
						int mask = TerrainGpuMath.LevelMask(nearHeight, farHeight, model.Transitions, model.FadeWidths, view.MaximumLodLevel);
						gpu.AddWork(d.GpuOffset + run.Start, run.Count, model.GpuId, drawDistance, 1f, mask, view.Shadows && near <= view.ShadowDistance);
					}
				}
			}
			int draws = 0;
			if (reflection)
			{
				for (int i = 0; i < modelList.Count; i++)
				{
					draws += modelList[i].Draw(camera, LightProbeUsage.Off);
				}
			}
			else
			{
				draws = gpu.Execute(camera, in view, drawDistance, context);
			}
			LastDrawCount = draws;
		}
	}
}
