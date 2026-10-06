using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws a scene's baked props — every <see cref="SceneProps"/> set (scatter trees and large props, cliff
	/// rocks, river boulders), and the LOD groups of cliffs generated before props were baked as data — instanced:
	/// on the GPU-driven path (<see cref="TerrainGpuRenderer"/>: culled per camera on the GPU, each prop's level
	/// picked with its prefab's own transitions and cross-fade, one indirect call per mesh, level and view), or,
	/// in edit mode and where compute is missing, on the CPU with <c>RenderMeshInstanced</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Props are data.</b> A set is prefab prototypes and transforms; nothing per prop is a scene object, so
	/// Unity culls and levels nothing per prop on the main thread. Each set is read once when its scene is first
	/// seen, grouped into <see cref="ChunkMetres"/> chunks, uploaded once, and only the chunks a camera (or its
	/// sun-swept shadow volume) can see are submitted. Collision is not drawn here: the bake merges it into chunk
	/// colliders that the server and client both keep.
	/// </para>
	/// <para>
	/// <b>Cliffs from before.</b> A <see cref="GeneratedCliffs"/> root still made of rock objects is taken over in
	/// play mode on the GPU path only: its renderers are switched off (<see cref="Renderer.forceRenderingOff"/>)
	/// while it is drawn here and switched back on when it is let go. Anywhere else Unity draws those rocks itself.
	/// </para>
	/// <para>
	/// <b>Edit mode.</b> Baked props have no renderers of their own, so the editor draws them here as well (the
	/// CPU path, a level picked per prop by distance), in the Scene and Game views, so a scene is authored with
	/// its trees and rocks in place.
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
			/// <summary>The largest prop in the chunk, metres: how far the chunk is drawn at all (<see cref="DrawDistanceFor"/>).</summary>
			public float MaxSize;
			public readonly List<Run> Runs = new List<Run>();
		}

		// ── Draw distance by size ─────────────────────────────────────

		/// <summary>Metres a prop is drawn to per metre of its size: a prop leaves when it is about half a degree across.</summary>
		public static float MetresPerMetreOfSize = 120f;

		/// <summary>The nearest any prop stops being drawn, and the furthest any prop is drawn, metres (before quality).</summary>
		public static float MinDrawDistance = 60f;
		public static float MaxDrawDistance = 3000f;

		/// <summary>The last share of a prop's draw distance over which a stand of them thins out (dithered), rather than vanishing at a line.</summary>
		public static float ThinShare = 0.15f;

		/// <summary>
		/// How far a prop of <paramref name="size"/> metres is drawn: in proportion to its size, so a boulder goes at a few
		/// hundred metres and a crag stays on the skyline for kilometres (Jim, 2026-10-06: larger objects keep larger
		/// distances, for realism). Scaled by the quality level's LOD bias (its square root: 0.6 Performant, 1 Balanced,
		/// 1.4 High Fidelity).
		/// </summary>
		/// <remarks>
		/// Before this the only limit was the camera's far plane, which the scene backdrop raises to 20 km or more, and
		/// the LOD's last transition: a 10 m tree was drawn to 4-9 km and a 30 m rock to 3-6 km, with every chunk tested
		/// against 20 km for every camera every frame.
		/// </remarks>
		public static float DrawDistanceFor(float size)
		{
			float bias = Mathf.Sqrt(Mathf.Max(0.1f, QualitySettings.lodBias));
			return Mathf.Clamp(size * MetresPerMetreOfSize, MinDrawDistance, MaxDrawDistance) * bias;
		}

		/// <summary>One drawn source: a baked prop set, or a cliff root of rock objects.</summary>
		private sealed class Driven
		{
			public Object Source;
			public readonly List<Chunk> Chunks = new List<Chunk>();
			public readonly List<Renderer> Hidden = new List<Renderer>();
			public NativeArray<FishInstance> Instances;
			public NativeArray<Matrix4x4> Matrices;
			public int GpuOffset = -1;
		}

		private static readonly List<Driven> drivenList = new List<Driven>();
		private static readonly HashSet<Object> drivenSources = new HashSet<Object>();
		private static readonly Dictionary<string, TerrainTreeModel> models = new Dictionary<string, TerrainTreeModel>();
		private static readonly List<TerrainTreeModel> modelList = new List<TerrainTreeModel>();
		private static readonly HashSet<string> refused = new HashSet<string>();
		/// <summary>Props per model across every driven source: the CPU buckets' capacity.</summary>
		private static readonly Dictionary<TerrainTreeModel, int> instancesOf = new Dictionary<TerrainTreeModel, int>();
		private static TerrainGpuRenderer gpu;
		private static bool gpuTried, hooked, scenesChanged = true;
		private static bool enabled = true, preferGpu = true;
		private static int syncedFrame = -1;

		/// <summary>Props drawn by this path, and the draw calls the last camera cost.</summary>
		public static int RockCount { get; private set; }
		public static int LastDrawCount { get; private set; }

		/// <summary>True while the GPU path draws (play mode, compute present, switched on).</summary>
		private static bool OnGpu => Application.isPlaying && gpu != null;

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void HookPlay()
		{
			if (!Application.isPlaying)
			{
				return;
			}
#if UNITY_EDITOR
			// The terrain A/B buttons' choices (TerrainTreeInstancing's keys) govern the props too.
			enabled = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.EnabledKey, true);
			preferGpu = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.PreferGpuKey, true);
#endif
			Hook();
		}

		private static void Hook()
		{
			if (hooked)
			{
				return;
			}
			hooked = true;
			syncedFrame = -1;
			scenesChanged = true;
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
		}

#if UNITY_EDITOR
		/// <summary>Edit mode: draws the open scenes' baked props, and follows the editor into and out of play mode.</summary>
		[UnityEditor.InitializeOnLoadMethod]
		private static void HookEditor()
		{
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
			UnityEditor.EditorApplication.hierarchyChanged -= MarkChanged;
			UnityEditor.EditorApplication.hierarchyChanged += MarkChanged;
			UnityEditor.EditorApplication.projectChanged -= MarkChanged;
			UnityEditor.EditorApplication.projectChanged += MarkChanged;
			if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
			{
				Hook();
			}
		}

		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			switch (change)
			{
				case UnityEditor.PlayModeStateChange.ExitingEditMode:
				case UnityEditor.PlayModeStateChange.ExitingPlayMode:
					Shutdown();
					break;
				case UnityEditor.PlayModeStateChange.EnteredEditMode:
					enabled = true;
					Hook();
					break;
			}
		}

		/// <summary>The hierarchy or project changed: a set may have been baked again, a cliff root replaced, a scene opened.</summary>
		private static void MarkChanged()
		{
			if (Application.isPlaying)
			{
				return;
			}
			// Drop and re-read everything: in edit mode sets are rebaked in place, so their contents change under the same object.
			ReleaseSources();
			scenesChanged = true;
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

		// ── Following the loaded props ────────────────────────────────

		private static void Sync()
		{
			if (Application.isPlaying)
			{
				if (syncedFrame == Time.frameCount)
				{
					return;
				}
				syncedFrame = Time.frameCount;
			}
			if (!enabled)
			{
				return;
			}
			if (Application.isPlaying && preferGpu && gpu == null && !gpuTried)
			{
				gpuTried = true;
				gpu = TerrainGpuRenderer.TryCreate();
			}

			// Let go of sources that were destroyed (their scene unloaded) before taking on new ones.
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				if (drivenList[i].Source == null)
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
			foreach (SceneProps props in Object.FindObjectsByType<SceneProps>())
			{
				if (props == null)
				{
					continue;
				}
				foreach (ScenePropSet set in props.Sets)
				{
					if (set != null && !drivenSources.Contains(set))
					{
						DriveSet(set);
					}
				}
			}
			// Cliffs made of rock objects are taken over only on the GPU path; elsewhere Unity draws them.
			if (OnGpu)
			{
				foreach (GeneratedCliffs root in Object.FindObjectsByType<GeneratedCliffs>())
				{
					if (root != null && !drivenSources.Contains(root))
					{
						DriveRoot(root);
					}
				}
			}
			if (drivenList.Count != before && Application.isPlaying)
			{
				Debug.Log($"[Props] Drawing {RockCount:N0} props of {modelList.Count} distinct prefabs on the {(OnGpu ? "GPU-driven" : "CPU instanced")} path ({drivenList.Count} source(s)).");
			}
		}

		/// <summary>Reads a baked prop set into models, chunks and (on the GPU path) one GPU range.</summary>
		private static void DriveSet(ScenePropSet set)
		{
			var placed = new List<(TerrainTreeModel model, Matrix4x4 matrix)>(set.Props.Length);
			var modelOf = new TerrainTreeModel[set.Prototypes.Length];
			for (int p = 0; p < set.Prototypes.Length; p++)
			{
				modelOf[p] = ModelFor(set.Prototypes[p].Prefab, set.Prototypes[p].Layer);
			}
			foreach (ScenePropSet.Prop prop in set.Props)
			{
				if (prop.Prototype < 0 || prop.Prototype >= modelOf.Length || modelOf[prop.Prototype] == null)
				{
					continue;
				}
				placed.Add((modelOf[prop.Prototype], prop.Matrix));
			}
			Drive(new Driven { Source = set }, placed, $"{set.name}");
		}

		/// <summary>Reads a cliff root's rock objects into models, chunks and one GPU range, and switches their renderers off.</summary>
		private static void DriveRoot(GeneratedCliffs root)
		{
			var d = new Driven { Source = root };
			var placed = new List<(TerrainTreeModel model, Matrix4x4 matrix)>();
			foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
			{
				TerrainTreeModel model = ModelFor(group.gameObject, group.gameObject.layer, KeyOf(group));
				if (model == null)
				{
					continue;   // left to Unity: drawn by its own renderers
				}
				placed.Add((model, group.transform.localToWorldMatrix));
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
			Drive(d, placed, $"{root.gameObject.scene.name} cliff rocks");
		}

		/// <summary>Lays a source's props out chunk by chunk, model by model, so every (chunk, model) is one contiguous run.</summary>
		private static void Drive(Driven d, List<(TerrainTreeModel model, Matrix4x4 matrix)> placed, string owner)
		{
			drivenSources.Add(d.Source);
			if (placed.Count == 0)
			{
				drivenList.Add(d);
				return;
			}
			var byChunk = new Dictionary<Vector2Int, Dictionary<TerrainTreeModel, List<int>>>();
			for (int i = 0; i < placed.Count; i++)
			{
				Vector3 p = placed[i].matrix.GetColumn(3);
				var key = new Vector2Int(Mathf.FloorToInt(p.x / ChunkMetres), Mathf.FloorToInt(p.z / ChunkMetres));
				if (!byChunk.TryGetValue(key, out var runs))
				{
					byChunk[key] = runs = new Dictionary<TerrainTreeModel, List<int>>();
				}
				if (!runs.TryGetValue(placed[i].model, out var list))
				{
					runs[placed[i].model] = list = new List<int>();
				}
				list.Add(i);
			}

			d.Instances = new NativeArray<FishInstance>(placed.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			d.Matrices = new NativeArray<Matrix4x4>(placed.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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
						Matrix4x4 m = placed[i].matrix;
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
					chunk.MaxSize = Mathf.Max(chunk.MaxSize, run.MaxSize);
					instancesOf.TryGetValue(model, out int total);
					instancesOf[model] = total + run.Count;
					model.EnsureCapacity(total + run.Count);
				}
				d.Chunks.Add(chunk);
			}
			if (OnGpu)
			{
				d.GpuOffset = gpu.Upload(d.Instances, 0, d.Instances.Length, owner);
			}
			RockCount += d.Instances.Length;
			drivenList.Add(d);
		}

		/// <summary>The model for a prefab on a layer (shared by every prop of it), or null when it cannot be drawn here.</summary>
		private static TerrainTreeModel ModelFor(GameObject prefab, int layer, string key = null)
		{
			if (prefab == null)
			{
				return null;
			}
			key ??= $"{prefab.GetEntityId()}|{layer}";
			if (refused.Contains(key))
			{
				return null;
			}
			if (models.TryGetValue(key, out TerrainTreeModel model))
			{
				return model;
			}
			model = TerrainTreeModel.Build(prefab, layer, out string reason);
			if (model != null && OnGpu)
			{
				model.RegisterGpu(gpu);
				if (model.GpuId < 0)
				{
					reason = "its parts are not at the prefab's origin, or its shader has no indirect variant";
					model.Dispose();
					model = null;
				}
			}
			if (model == null)
			{
				refused.Add(key);
				Debug.LogWarning($"[Props] Not drawing '{prefab.name}': {reason}.");
				return null;
			}
			models[key] = model;
			modelList.Add(model);
			return model;
		}

		/// <summary>What makes two cliff rock objects the same model: every level's meshes and materials, the transitions and the layer.</summary>
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
			drivenSources.Remove(d.Source);
		}

		/// <summary>Lets every source go (renderers back on, buffers freed) and forgets the models.</summary>
		private static void ReleaseSources()
		{
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Release(drivenList[i]);
			}
			drivenSources.Clear();
			foreach (TerrainTreeModel model in modelList)
			{
				model.Dispose();
			}
			modelList.Clear();
			models.Clear();
			refused.Clear();
			instancesOf.Clear();
		}

		/// <summary>Gives every rock object back to Unity's own drawing and frees every buffer.</summary>
		public static void ReleaseAll()
		{
			ReleaseSources();
			gpu?.Dispose();
			gpu = null;
			gpuTried = false;
			RockCount = 0;
			scenesChanged = true;
		}

		// ── Drawing, per camera ───────────────────────────────────────

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (!TerrainInstancingShared.Draws(camera))
			{
				return;
			}
			Sync();
			if (drivenList.Count == 0)
			{
				return;
			}

			bool reflection = camera.cameraType == CameraType.Reflection;
			if (camera.cullingMask == 0)
			{
				// It renders no layers at all (the sky's probe): gathering props for it was work thrown away.
				return;
			}
			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			bool onGpu = OnGpu && !reflection;
			float drawDistance = camera.farClipPlane;
			for (int i = 0; i < modelList.Count; i++)
			{
				modelList[i].Clear();
			}
			if (onGpu)
			{
				gpu.BeginCamera();
			}
			Vector3 eye = view.Position;
			foreach (Driven d in drivenList)
			{
				if (!d.Matrices.IsCreated)
				{
					continue;
				}
				foreach (Chunk chunk in d.Chunks)
				{
					float near = TerrainTreeMath.MinDistance(chunk.Bounds, eye);
					if (near > Mathf.Min(drawDistance, DrawDistanceFor(chunk.MaxSize)))
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
						// Each run as far as its largest prop is drawn: the chunk's reach is its biggest.
						float reach = Mathf.Min(drawDistance, DrawDistanceFor(run.MaxSize));
						if (near > reach)
						{
							continue;
						}
						if (reflection)
						{
							// The lowest level of every prop in a visible chunk: a probe face needs the shapes, not the detail.
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
						if (onGpu && d.GpuOffset >= 0)
						{
							float nearHeight = TerrainTreeMath.RelativeHeight(run.MaxSize, near, view.ScreenFactor, view.Orthographic);
							float farHeight = TerrainTreeMath.RelativeHeight(run.MinSize, far, view.ScreenFactor, view.Orthographic);
							int mask = TerrainGpuMath.LevelMask(nearHeight, farHeight, model.Transitions, model.FadeWidths, view.MaximumLodLevel);
							// Thinned out (dithered, by position) over the last share of its reach: a stand thins away rather than ending at a line.
							gpu.AddWork(d.GpuOffset + run.Start, run.Count, model.GpuId, reach, 1f, mask, view.Shadows && near <= view.ShadowDistance,
								reach * (1f - ThinShare), 0f);
							continue;
						}
						AppendByDistance(model, d.Matrices, run, chunk, in view, reach);
					}
				}
			}
			int draws = 0;
			for (int i = 0; i < modelList.Count; i++)
			{
				draws += modelList[i].Draw(camera, LightProbeUsage.Off);
			}
			if (onGpu)
			{
				draws += gpu.Execute(camera, in view, drawDistance, context);
			}
			LastDrawCount = draws;
		}

		/// <summary>
		/// The CPU path: each prop of a run into the bucket of the level its size on screen picks (the prefab's own
		/// transitions, as its LOD group would), none past the last transition or the draw distance.
		/// </summary>
		private static void AppendByDistance(TerrainTreeModel model, NativeArray<Matrix4x4> matrices, Run run, Chunk chunk, in TerrainTreeField.View view,
			float drawDistance)
		{
			float[] transitions = model.Transitions;
			int levels = transitions.Length;
			for (int i = run.Start; i < run.Start + run.Count; i++)
			{
				Matrix4x4 m = matrices[i];
				Vector3 at = m.GetColumn(3);
				float distance = Vector3.Distance(at, view.Position);
				if (distance > drawDistance)
				{
					continue;
				}
				float scale = Mathf.Max(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude);
				float height = TerrainTreeMath.RelativeHeight(model.Size * scale, distance, view.ScreenFactor, view.Orthographic);
				int level = -1;
				for (int l = 0; l < levels; l++)
				{
					if (height >= transitions[l])
					{
						level = l;
						break;
					}
				}
				if (level < 0)
				{
					continue;
				}
				model.Append(Mathf.Max(level, Mathf.Min(view.MaximumLodLevel, levels - 1)), m, 0f, in chunk.Bounds);
			}
		}
	}
}
