using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU-driven path of the instanced terrain renderers (trees and details each own one): instances
	/// resident in one structured buffer, culled and LOD-picked per camera by FishTerrainInstancing.compute
	/// into per-(prototype, level, view) slots, and drawn with <see cref="Graphics.RenderMeshIndirect"/> —
	/// one main draw (shadows off) and one shadows-only draw per prototype, level and submesh.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>When.</b> <see cref="TryCreate"/> returns null where compute shaders or vertex-stage structured
	/// buffers are missing (WebGL2) or the compute asset is not in the build; the renderers then draw
	/// with <see cref="Graphics.RenderMeshInstanced"/> as before. A prototype the GPU path cannot take
	/// (a material whose shader has no indirect twin, a part offset from the prefab root, more than four
	/// levels) is drawn by the fallback alongside: the two mix per prototype.
	/// </para>
	/// <para>
	/// <b>Materials.</b> Each prototype material is cloned with its indirect twin
	/// ("FishMMO/Vegetation" → "FishMMO/Vegetation Indirect", "FishMMO/Weather Lit" →
	/// "FishMMO/Weather Lit Indirect"); the properties carry over 1:1. Each draw's property block binds
	/// <c>_FishInstances</c>, <c>_FishVisibleInstances</c> and its slot's <c>_FishVisibleBase</c>.
	/// </para>
	/// <para>
	/// <b>Slots never overflow</b> (the kernel drops an append past the capacity and the finaliser clamps the
	/// count). A slot's capacity is its prototype's resident instance count: an instance is appended at most
	/// once per level and view, even in a fade band.
	/// </para>
	/// <para>
	/// <b>Per camera</b>: the CPU culls chunks and lists the runs it keeps as 64-instance work items, with
	/// which levels each prototype can reach there; the GPU culls per instance; commands of levels no run
	/// can reach are not issued, so the draw count follows what is near, not every prototype. No readback.
	/// </para>
	/// </remarks>
	public sealed partial class TerrainGpuRenderer : IDisposable
	{
		/// <summary>Where the culling compute lives; the build reaches it through <see cref="WeatherRenderProfile.TerrainInstancingCompute"/>.</summary>
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishTerrainInstancing.compute";

		private static readonly Dictionary<string, string> IndirectShaders = new Dictionary<string, string>
		{
			{ "FishMMO/Vegetation", "FishMMO/Vegetation Indirect" },
			{ "FishMMO/Weather Lit", "FishMMO/Weather Lit Indirect" },
		};

		private static readonly int InstancesId = Shader.PropertyToID("_FishInstances");
		private static readonly int VisibleId = Shader.PropertyToID("_FishVisibleInstances"); // the indirect shaders' name (FishIndirectInstancing.hlsl)
		private static readonly int VisibleBaseId = Shader.PropertyToID("_FishVisibleBase");
		/// <summary>One uint per indirect command index: the visible-buffer base of the slot that command draws (read with GetCommandID(0)).</summary>
		public const string CommandBasesName = "_FishCommandBases";
		private static readonly int CommandBasesId = Shader.PropertyToID(CommandBasesName);
		private static readonly int ContactOnId = Shader.PropertyToID("_FishContactOn");
		private static readonly int ModelsId = Shader.PropertyToID("_FishModels");
		private static readonly int WorkId = Shader.PropertyToID("_FishWork");
		private static readonly int SlotsId = Shader.PropertyToID("_FishSlots");
		private static readonly int ArgSlotsId = Shader.PropertyToID("_FishArgSlots");
		private static readonly int CountsId = Shader.PropertyToID("_FishCounts");
		private static readonly int VisibleOutId = Shader.PropertyToID("_FishVisibleOut");
		private static readonly int ArgsId = Shader.PropertyToID("_FishArgs");
		private static readonly int ArgsFenceId = Shader.PropertyToID("_FishArgsFence");

		/// <summary>
		/// OpenGL only: Unity's GLES device issues GL_COMMAND_BARRIER_BIT before an indirect draw only when the args buffer
		/// was BOUND after its last write, never for the write itself (FishTerrainInstancing.compute FishArgsFence). Without
		/// the fence dispatch the draws read the args while FishFinalize still wrote them, every other frame: the flicker.
		/// </summary>
		private static bool NeedsArgsFence => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
		private static readonly int CameraPositionId = Shader.PropertyToID("_FishCameraPosition");
		private static readonly int PlanesId = Shader.PropertyToID("_FishPlanes");
		private static readonly int LightId = Shader.PropertyToID("_FishLight");
		private static readonly int LodId = Shader.PropertyToID("_FishLod");
		private static readonly int SlotCountId = Shader.PropertyToID("_FishSlotCount");
		private static readonly int WorkCountId = Shader.PropertyToID("_FishWorkCount");
		private static readonly int ArgCountId = Shader.PropertyToID("_FishArgCount");
		private static readonly int UploadId = Shader.PropertyToID("_FishUpload");
		private static readonly int InstancesOutId = Shader.PropertyToID("_FishInstancesOut");
		private static readonly int UploadSourceId = Shader.PropertyToID("_FishUploadSource");
		private static readonly int UploadDestinationId = Shader.PropertyToID("_FishUploadDestination");
		private static readonly int UploadCountId = Shader.PropertyToID("_FishUploadCount");

		/// <summary>One part of a level as a model source gives it.</summary>
		public struct PartSource
		{
			public Mesh Mesh;
			public int Submesh;
			public Material Material;
			/// <summary>Layer, receive shadows, reflection probes, rendering layer mask; shadow casting and camera are set per draw.</summary>
			public RenderParams Template;
			public bool CastsShadows;
		}

		private sealed class Model
		{
			public string Name;
			public FishModelData Data;
			public PartSource[][] Levels;
			public int Capacity;
			public int MainMask;
			public int ShadowMask;
			/// <summary>Instances this camera's work submits for the model: no slot of it can receive more.</summary>
			public int WorkInstances;
		}

		/// <summary>An upload buffer, released once the GPU is surely done with it.</summary>
		private struct Retired
		{
			public GraphicsBuffer Buffer;
			public int Frame;
		}

		/// <summary>Frames an upload buffer is kept after its copy was recorded (the GPU may run this far behind).</summary>
		private const int RetireFrames = 4;

		private readonly List<Retired> retired = new List<Retired>();

		private struct Command
		{
			public int Model;
			public int Level;
			public int View;
			public int Slot;
			public Mesh Mesh;
			public int Submesh;
			public RenderParams Params;
		}

		private struct Resident
		{
			public int Count;
			/// <summary>Who owns the range (a terrain, or a terrain's chunk), for the logs and the diagnostic.</summary>
			public string Owner;
		}

		/// <summary>Logs every upload, grow, free and flush (debug; the diagnostic button switches it on).</summary>
		public static bool LogUploads;

		/// <summary>
		/// The whole instance buffer's contents on the CPU: the source of truth. Uploads write here; the GPU
		/// buffer receives the changed span in the camera's own command buffer, before the culling reads it.
		/// </summary>
		private NativeArray<FishInstance> mirror;
		/// <summary>Changed spans of the mirror (x start, y end), near-adjacent ones merged; one upload each.</summary>
		private readonly List<Vector2Int> dirty = new List<Vector2Int>();

		/// <summary>Spans closer than this are uploaded together (one call beats two for a small gap).</summary>
		public const int DirtyMergeGap = 512;

		/// <summary>The GPU buffer is smaller than the mirror: recreated (once) before the next camera's flush.</summary>
		private bool instancesStale = true;

		private readonly ComputeShader compute;
		private readonly int clearKernel, cullKernel, finalizeKernel, copyKernel, argsFenceKernel;
		private readonly List<Model> models = new List<Model>();
		private readonly List<Command> commands = new List<Command>();
		private readonly Dictionary<int, Resident> residents = new Dictionary<int, Resident>();
		private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
		private readonly RangeAllocator allocator = new RangeAllocator(0);
		private readonly Vector4[] planeVectors = new Vector4[6];
		private MaterialPropertyBlock[] slotBlocks = Array.Empty<MaterialPropertyBlock>();
		private int slotCount;

		private GraphicsBuffer instances, modelBuffer, workBuffer, slotBuffer, argSlotBuffer, countBuffer, visibleBuffer, argsBuffer, commandBaseBuffer;
		private NativeArray<FishWork> work;
		private int workCount;
		private bool layoutDirty = true;

		/// <summary>Draws issued for the last camera.</summary>
		public int LastDrawCount { get; private set; }

		/// <summary>Instances resident in the instance buffer.</summary>
		public int ResidentInstances => allocator.Used;

		/// <summary>Bytes of GPU buffers held.</summary>
		public long GpuBytes =>
			Size(instances) + Size(modelBuffer) + Size(workBuffer) + Size(slotBuffer) + Size(argSlotBuffer) + Size(countBuffer) + Size(visibleBuffer) + Size(argsBuffer) + Size(commandBaseBuffer);

		private static long Size(GraphicsBuffer b) => b != null ? (long)b.count * b.stride : 0;

		private readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO terrain GPU culling" };

		/// <summary>
		/// Which contact blend with the ground this renderer's draws take (FishGroundColour.hlsl <c>_FishContactOn</c>):
		/// 0 none, 1 the small details' settings, 2 the trees' and rocks' (their bases take the terrain's colour and
		/// shading). Set by the owner right after <see cref="TryCreate"/>; written on its clones and per-draw blocks.
		/// </summary>
		public float ContactMode;

		private TerrainGpuRenderer(ComputeShader compute)
		{
			this.compute = compute;
			clearKernel = compute.FindKernel("FishClearCounts");
			cullKernel = compute.FindKernel("FishCull");
			finalizeKernel = compute.FindKernel("FishFinalize");
			copyKernel = compute.FindKernel("FishCopyInstances");
			argsFenceKernel = compute.FindKernel("FishArgsFence");
		}

		/// <summary>The GPU path, or null where it cannot run (the fallback draws).</summary>
		public static TerrainGpuRenderer TryCreate()
		{
			// From the client-only render profile, not Resources, so a server build never carries it.
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			ComputeShader cs = profile != null ? profile.TerrainInstancingCompute : null;
#if UNITY_EDITOR
			if (cs == null)
			{
				// No profile loaded (a scene played without the client boot): the asset itself.
				cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
			}
#endif
			if (!TerrainGpuMath.ChooseGpu(SystemInfo.supportsComputeShaders, SystemInfo.maxComputeBufferInputsVertex, cs != null))
			{
				return null;
			}
			// The indirect twins' variants, compiled now rather than as the first tree of each kind comes into view.
			if (profile != null && profile.IndirectShaderVariants != null && !profile.IndirectShaderVariants.isWarmedUp)
			{
				profile.IndirectShaderVariants.WarmUp();
			}
			// A private copy: the trees and the details each own one, so neither can see the other's
			// parameters or buffer bindings (a shared ComputeShader object is one parameter sheet).
			ComputeShader own = Object.Instantiate(cs);
			own.hideFlags = HideFlags.DontSave;
			own.name = cs.name + " (instance)";
			return new TerrainGpuRenderer(own);
		}

		/// <summary>The material's indirect twin, or null when its shader has none (the prototype then takes the fallback).</summary>
		private Material IndirectOf(Material material)
		{
			if (material == null)
			{
				return null;
			}
			if (clones.TryGetValue(material, out Material clone))
			{
				return clone;
			}
			Shader shader = IndirectShaderFor(material);
			if (shader == null)
			{
				return null;
			}
			clone = new Material(material) { shader = shader, name = material.name + " (indirect)", hideFlags = HideFlags.DontSave };
			clone.SetFloat(ContactOnId, ContactMode);
			clones.Add(material, clone);
			return clone;
		}

		/// <summary>
		/// The procedural-instancing twin of a material's shader ("FishMMO/Vegetation" → "FishMMO/Vegetation Indirect",
		/// "FishMMO/Weather Lit" → "FishMMO/Weather Lit Indirect"), or null when it has none or it is unsupported. Shared
		/// with the GPU detail scatter (<see cref="DetailScatterRenderer"/>), which draws through the same twins.
		/// </summary>
		public static Shader IndirectShaderFor(Material material)
		{
			if (material == null || material.shader == null || !IndirectShaders.TryGetValue(material.shader.name, out string name))
			{
				return null;
			}
			// The profile's references first: they are what puts the twins in a client build. Shader.Find only
			// finds a shader something in the build references, so it is the editor's fallback.
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile == null ? null
				: name == profile.VegetationIndirectShader?.name ? profile.VegetationIndirectShader
				: name == profile.WeatherLitIndirectShader?.name ? profile.WeatherLitIndirectShader
				: null;
			if (shader == null)
			{
				shader = Shader.Find(name);
			}
			return shader != null && shader.isSupported ? shader : null;
		}

		/// <summary>
		/// Registers a prototype; returns its id, or −1 when the GPU path cannot draw it (the caller keeps it
		/// on the fallback). Transition heights and fade bands as <see cref="TerrainTreeMath.SelectLodFaded"/>
		/// reads them; the sphere bounds every level in the root's space.
		/// </summary>
		public int AddModel(float[] transitions, float[] fadeWidths, float size, Vector3 localReference, Bounds localBounds, PartSource[][] levels, string name = null)
		{
			if (levels == null || levels.Length == 0 || levels.Length > FishModelData.MaxLevels || transitions.Length != levels.Length)
			{
				return -1;
			}
			var resolved = new PartSource[levels.Length][];
			uint shadowMask = 0;
			for (int l = 0; l < levels.Length; l++)
			{
				resolved[l] = new PartSource[levels[l].Length];
				for (int p = 0; p < levels[l].Length; p++)
				{
					PartSource part = levels[l][p];
					part.Material = IndirectOf(part.Material);
					if (part.Material == null || part.Mesh == null)
					{
						return -1;
					}
					resolved[l][p] = part;
					if (part.CastsShadows)
					{
						shadowMask |= 1u << l;
					}
				}
			}
			var data = new FishModelData
			{
				LocalReference = localReference,
				Size = size,
				SphereCentre = localBounds.center,
				SphereRadius = localBounds.extents.magnitude,
				LevelCount = (uint)levels.Length,
				ShadowMask = shadowMask,
			};
			for (int l = 0; l < levels.Length; l++)
			{
				data.Transitions[l] = transitions[l];
				data.FadeWidths[l] = fadeWidths != null && l < fadeWidths.Length ? fadeWidths[l] : 0f;
			}
			models.Add(new Model { Name = name ?? $"model {models.Count}", Data = data, Levels = resolved });
			layoutDirty = true;
			return models.Count - 1;
		}

		/// <summary>Sets how many instances a model's slots hold (its resident count); grows the visible buffer on the next camera.</summary>
		public void SetCapacity(int model, int capacity)
		{
			if (model < 0 || models[model].Capacity == capacity)
			{
				return;
			}
			if (TerrainInstancingProbe.On)
			{
				TerrainInstancingProbe.Reason($"{models[model].Name} capacity {models[model].Capacity}→{capacity}");
			}
			models[model].Capacity = Mathf.Max(0, capacity);
			layoutDirty = true;
		}

		public int CapacityOf(int model) => model >= 0 ? models[model].Capacity : 0;

		/// <summary>A slot's capacity: the model's, or none for the shadow view of a level that casts no shadow (the kernel never appends there).</summary>
		private static int SlotCapacity(Model m, int level, int view)
		{
			return view == 1 && (m.Data.ShadowMask & (1u << level)) == 0 ? 0 : m.Capacity;
		}

		/// <summary>Registered models that draw on this path (capacity above zero).</summary>
		public int LiveModelCount
		{
			get
			{
				int n = 0;
				foreach (Model m in models)
				{
					n += m.Capacity > 0 ? 1 : 0;
				}
				return n;
			}
		}

		/// <summary>
		/// Makes <paramref name="count"/> instances of <paramref name="source"/> (from <paramref name="start"/>)
		/// resident; returns their offset in <c>_FishInstances</c>. The source must stay alive while resident:
		/// it is uploaded again if the buffer has to grow.
		/// </summary>
		public int Upload(NativeArray<FishInstance> source, int start, int count, string owner = null)
		{
			if (count <= 0)
			{
				return -1;
			}
			int offset = allocator.Allocate(count);
			if (offset < 0)
			{
				GrowInstances(Mathf.Max(allocator.Capacity * 2, allocator.Used + count + 1024));
				offset = allocator.Allocate(count);
			}
			if (residents.ContainsKey(offset))
			{
				Debug.LogError($"[Terrain GPU] Range at {offset} handed out twice (held by {residents[offset].Owner}, now {owner}).");
			}
			residents[offset] = new Resident { Count = count, Owner = owner ?? "?" };
			NativeArray<FishInstance>.Copy(source, start, mirror, offset, count);
			MarkDirty(offset, count);
			TerrainInstancingProbe.UploadInstances += count;
			if (LogUploads)
			{
				Debug.Log($"[Terrain GPU] frame {Time.frameCount} upload {owner}: offset {offset}, count {count}, source length {source.Length} (from {start}), mirror capacity {mirror.Length}, GPU buffer {(instances != null ? instances.GetHashCode() : 0)}{(instancesStale ? " (stale: recreated with the next camera)" : "")}.");
			}
			return offset;
		}

		/// <summary>Releases a resident range.</summary>
		public void Free(int offset)
		{
			if (offset >= 0 && residents.TryGetValue(offset, out Resident r))
			{
				residents.Remove(offset);
				allocator.Free(offset, r.Count);
				if (LogUploads)
				{
					Debug.Log($"[Terrain GPU] frame {Time.frameCount} free {r.Owner}: offset {offset}, count {r.Count}.");
				}
			}
		}

		/// <summary>
		/// A bigger mirror and allocator. The GPU buffer is only recreated once, before the next camera's flush
		/// (<see cref="EnsureGpuBuffer"/>), however many grows a frame's adoption takes, and it is created and
		/// bound before anything is written into it: the whole mirror then goes up with that camera's commands.
		/// </summary>
		private void GrowInstances(int capacity)
		{
			capacity = Mathf.Max(capacity, 1024);
			var grown = new NativeArray<FishInstance>(capacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			if (mirror.IsCreated)
			{
				NativeArray<FishInstance>.Copy(mirror, grown, mirror.Length);
				mirror.Dispose();
			}
			mirror = grown;
			allocator.Grow(capacity);
			instancesStale = true;
			TerrainInstancingProbe.Grows++;
			if (TerrainInstancingProbe.On)
			{
				TerrainInstancingProbe.Reason($"instance buffer → {capacity}");
			}
			if (LogUploads)
			{
				Debug.Log($"[Terrain GPU] frame {Time.frameCount} grow: mirror capacity {capacity}, {residents.Count} resident ranges, {allocator.Used} instances; the GPU buffer is recreated and filled with the next camera.");
			}
		}

		/// <summary>Recreates the GPU instance buffer at the mirror's size when it is stale, binds it, and marks it all for upload.</summary>
		private void EnsureGpuBuffer()
		{
			if (!instancesStale && instances != null)
			{
				return;
			}
			if (!mirror.IsCreated)
			{
				GrowInstances(1024);
			}
			instancesStale = false;
			instances?.Release();
			instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, mirror.Length, FishInstance.Stride);
			BindBlocks();
			dirty.Clear();
			// Only the live ranges: free space is never referenced by work, and uploading the whole mirror
			// after a grow is the spike this buffer exists to avoid.
			foreach (KeyValuePair<int, Resident> r in residents)
			{
				MarkDirty(r.Key, r.Value.Count);
			}
		}

		/// <summary>Adds a changed span, merging it into any span it overlaps or nearly touches.</summary>
		private void MarkDirty(int offset, int count)
		{
			TerrainGpuMath.AddDirty(dirty, offset, offset + count, DirtyMergeGap);
		}

		/// <summary>Records each changed span of the mirror into the camera's command buffer, ahead of the culling.</summary>
		private void Flush(CommandBuffer into)
		{
			if (SkipUploads || dirty.Count == 0 || instances == null)
			{
				return;
			}
			ReleaseRetired(false);
			int total = 0;
			for (int i = 0; i < dirty.Count; i++)
			{
				total += Mathf.Max(0, Mathf.Min(dirty[i].y, instances.count) - Mathf.Clamp(dirty[i].x, 0, instances.count));
			}
			if (total == 0)
			{
				dirty.Clear();
				return;
			}
			// A buffer created now is in no frame's use, so writing it never waits for the GPU; the copy
			// into the instance buffer then runs on the GPU, in order, ahead of this camera's culling.
			var upload = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, FishInstance.Stride);
			retired.Add(new Retired { Buffer = upload, Frame = Time.frameCount });
			into.SetComputeBufferParam(compute, copyKernel, UploadId, upload);
			into.SetComputeBufferParam(compute, copyKernel, InstancesOutId, instances);
			int packed = 0;
			for (int i = 0; i < dirty.Count; i++)
			{
				int lo = Mathf.Clamp(dirty[i].x, 0, instances.count), hi = Mathf.Clamp(dirty[i].y, 0, instances.count);
				if (hi <= lo)
				{
					continue;
				}
				into.SetBufferData(upload, mirror, lo, packed, hi - lo);
				for (int done = 0; done < hi - lo; done += MaxCopyPerDispatch)
				{
					int n = Mathf.Min(MaxCopyPerDispatch, hi - lo - done);
					into.SetComputeIntParam(compute, UploadSourceId, packed + done);
					into.SetComputeIntParam(compute, UploadDestinationId, lo + done);
					into.SetComputeIntParam(compute, UploadCountId, n);
					into.DispatchCompute(compute, copyKernel, (n + 63) / 64, 1, 1);
				}
				packed += hi - lo;
				TerrainInstancingProbe.FlushRanges++;
				TerrainInstancingProbe.FlushInstances += hi - lo;
				if (LogUploads)
				{
					Debug.Log($"[Terrain GPU] frame {Time.frameCount} flush [{lo}, {hi}) through upload buffer {upload.GetHashCode()} into buffer {instances.GetHashCode()}.");
				}
			}
			dirty.Clear();
		}

		/// <summary>Instances one copy dispatch moves (65535 groups of 64 is the per-dimension ceiling).</summary>
		private const int MaxCopyPerDispatch = 65535 * 64;

		/// <summary>Releases the upload buffers the GPU is done with (all of them on dispose).</summary>
		private void ReleaseRetired(bool all)
		{
			int frame = Time.frameCount;
			for (int i = retired.Count - 1; i >= 0; i--)
			{
				if (all || frame - retired[i].Frame >= RetireFrames)
				{
					retired[i].Buffer.Release();
					retired.RemoveAt(i);
				}
			}
		}

		// ── Per camera ────────────────────────────────────────────────

		/// <summary>Starts a camera: no work, no levels reached.</summary>
		public void BeginCamera()
		{
			workCount = 0;
			foreach (Model m in models)
			{
				m.MainMask = 0;
				m.ShadowMask = 0;
				m.WorkInstances = 0;
			}
		}

		/// <summary>
		/// Adds a resident run to cull: <paramref name="levelMask"/> the levels it can reach (bit per level),
		/// <paramref name="shadowCandidate"/> whether it is within the shadow distance, and the distance
		/// thinning: full density to <paramref name="thinStart"/> metres, then a share falling linearly to
		/// <paramref name="thinKeep"/> at <paramref name="drawDistance"/> (1: no thinning).
		/// </summary>
		public void AddWork(int offset, int count, int model, float drawDistance, float lodBias, int levelMask, bool shadowCandidate, float thinStart = 0f, float thinKeep = 1f)
		{
			if (count <= 0 || model < 0 || levelMask == 0)
			{
				return;
			}
			Model m = models[model];
			m.MainMask |= levelMask;
			m.WorkInstances += count;
			if (shadowCandidate)
			{
				m.ShadowMask |= levelMask & (int)m.Data.ShadowMask;
			}
			int items = TerrainGpuMath.WorkItems(count);
			EnsureWork(workCount + items);
			for (int i = 0; i < items; i++)
			{
				int start = i * FishWork.MaxInstances;
				work[workCount++] = new FishWork
				{
					Start = (uint)(offset + start),
					Count = (uint)Mathf.Min(FishWork.MaxInstances, count - start),
					Model = (uint)model,
					DrawDistance = drawDistance,
					LodBiasMultiplier = lodBias,
					ThinStart = thinStart,
					ThinKeep = thinKeep,
				};
			}
		}

		private void EnsureWork(int needed)
		{
			if (work.IsCreated && work.Length >= needed)
			{
				return;
			}
			int capacity = Mathf.Max(needed, work.IsCreated ? work.Length * 2 : 1024);
			var grown = new NativeArray<FishWork>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			if (work.IsCreated)
			{
				NativeArray<FishWork>.Copy(work, grown, workCount);
				work.Dispose();
			}
			work = grown;
		}

		/// <summary>Culls the camera's work on the GPU and issues the indirect draws. Returns the draws issued.</summary>
		/// <summary>
		/// Culls the camera's work on the GPU and issues the indirect draws. Returns the draws issued.
		/// </summary>
		/// <remarks>
		/// Everything the GPU does for this camera — the work upload, the parameters, the three dispatches and
		/// a diagnostic readback — is recorded into a command buffer executed on the camera's own render
		/// context, so it runs in that camera's submission, just before its draws: another camera (a
		/// reflection-probe face, the scene view) can neither run between them nor change the parameters
		/// they were recorded with. Immediate-mode <c>ComputeShader.Set*</c> + <c>Dispatch</c> gave no such
		/// guarantee: the parameters live on the ComputeShader object, and the last writer won.
		/// </remarks>
		/// <summary>
		/// Debug: stop culling and keep drawing the last results (every renderer: trees, details, cliff rocks). A flicker
		/// that goes on while frozen is in the drawing; one that stops is in the culling.
		/// </summary>
		public static bool Freeze;
		private bool culled;

		/// <summary>
		/// Debug: keep culling, but stop copying changed instances into the instance buffer (chunks streaming in stay
		/// missing until it is switched off; the queue is kept). Splits a flicker between the uploads and the culling.
		/// </summary>
		public static bool SkipUploads;

		public int Execute(Camera camera, in TerrainTreeField.View view, float reach, ScriptableRenderContext context)
		{
			LastDrawCount = 0;
			// No slot may be smaller than the work submitted for its model: a camera's visible instances of
			// a model can never exceed it, so nothing is ever dropped. An estimate (resident counts, the
			// coverage disc bound) can fall short, and the culling then discards appends past the capacity;
			// work items run in chunk order, so the chunks lost were whole ones (grass missing from a chunk).
			for (int id = 0; id < models.Count; id++)
			{
				Model m = models[id];
				if (m.WorkInstances > m.Capacity)
				{
					int grown = Mathf.Max(m.WorkInstances, 2 * m.Capacity);
					if (TerrainInstancingProbe.On)
					{
						TerrainInstancingProbe.Reason($"{m.Name} capacity {m.Capacity}→{grown} (work)");
					}
					m.Capacity = grown;
					layoutDirty = true;
				}
			}
			if (layoutDirty)
			{
				Relayout();
			}
			EnsureGpuBuffer();
			if (workCount == 0 || commands.Count == 0 || instances == null)
			{
				return 0;
			}
			if (workBuffer == null || workBuffer.count < workCount)
			{
				workBuffer?.Release();
				workBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(workCount, work.Length), FishWork.Stride);
			}

			for (int i = 0; i < 6; i++)
			{
				Plane p = view.Planes[i];
				planeVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
			}
			if (!(Freeze && culled))
			{
				double recordStart = TerrainInstancingProbe.Now;
				cmd.Clear();
				Flush(cmd);
				cmd.SetBufferData(workBuffer, work, 0, 0, workCount);
				cmd.SetComputeVectorParam(compute, CameraPositionId, view.Position);
				cmd.SetComputeVectorArrayParam(compute, PlanesId, planeVectors);
				cmd.SetComputeVectorParam(compute, LightId, view.Shadows ? new Vector4(view.LightDirection.x, view.LightDirection.y, view.LightDirection.z, view.ShadowDistance) : Vector4.zero);
				cmd.SetComputeVectorParam(compute, LodId, new Vector4(view.ScreenFactor, view.Orthographic ? 1f : 0f, view.MaximumLodLevel, 0f));
				cmd.SetComputeIntParam(compute, SlotCountId, slotCount);
				cmd.SetComputeIntParam(compute, WorkCountId, workCount);
				cmd.SetComputeIntParam(compute, ArgCountId, commands.Count);

				cmd.SetComputeBufferParam(compute, clearKernel, CountsId, countBuffer);
				cmd.DispatchCompute(compute, clearKernel, (slotCount + 63) / 64, 1, 1);

				// Last frame's depth, to drop from the main view what was hidden behind what was drawn (not while a
				// diagnostic compares this culling with its CPU mirror, which knows nothing of depth).
				FishDepthPyramid.Bind(cmd, compute, cullKernel, camera, view.Position, !(diagnosticCamera == camera && diagnosticReport != null));
				cmd.SetComputeBufferParam(compute, cullKernel, InstancesId, instances);
				cmd.SetComputeBufferParam(compute, cullKernel, ModelsId, modelBuffer);
				cmd.SetComputeBufferParam(compute, cullKernel, WorkId, workBuffer);
				cmd.SetComputeBufferParam(compute, cullKernel, SlotsId, slotBuffer);
				cmd.SetComputeBufferParam(compute, cullKernel, CountsId, countBuffer);
				cmd.SetComputeBufferParam(compute, cullKernel, VisibleOutId, visibleBuffer);
				Vector2Int groups = TerrainGpuMath.CullGroups(workCount);
				cmd.DispatchCompute(compute, cullKernel, groups.x, groups.y, 1);

				cmd.SetComputeBufferParam(compute, finalizeKernel, ArgSlotsId, argSlotBuffer);
				cmd.SetComputeBufferParam(compute, finalizeKernel, CountsId, countBuffer);
				cmd.SetComputeBufferParam(compute, finalizeKernel, SlotsId, slotBuffer);
				cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, argsBuffer);
				cmd.DispatchCompute(compute, finalizeKernel, (commands.Count + 63) / 64, 1, 1);
				if (NeedsArgsFence)
				{
					cmd.SetComputeBufferParam(compute, argsFenceKernel, ArgsFenceId, argsBuffer);
					cmd.SetComputeBufferParam(compute, argsFenceKernel, CountsId, countBuffer);
					cmd.DispatchCompute(compute, argsFenceKernel, 1, 1, 1);
				}

				if (diagnosticCamera == camera && diagnosticReport != null)
				{
					Action<string> report = diagnosticReport;
					diagnosticCamera = null;
					diagnosticReport = null;
					StartDiagnostics(in view, report, cmd);
				}
				context.ExecuteCommandBuffer(cmd);
				cmd.Clear();
				TerrainInstancingProbe.RecordMs += TerrainInstancingProbe.Now - recordStart;
				culled = true;
			}
			double drawStart = TerrainInstancingProbe.Now;

			var bounds = new Bounds(view.Position, Vector3.one * (2f * reach));
			int draws = 0;
			for (int c = 0; c < commands.Count; c++)
			{
				Command command = commands[c];
				Model m = models[command.Model];
				int mask = command.View == 0 ? m.MainMask : m.ShadowMask;
				if ((mask & (1 << command.Level)) == 0)
				{
					continue;
				}
				RenderParams rp = command.Params;
				rp.camera = camera;
				rp.worldBounds = bounds;
				rp.matProps = slotBlocks[command.Slot];
				Graphics.RenderMeshIndirect(rp, command.Mesh, argsBuffer, 1, c);
				draws++;
			}
			LastDrawCount = draws;
			TerrainInstancingProbe.DrawMs += TerrainInstancingProbe.Now - drawStart;
			return draws;
		}

		// ── Layout ────────────────────────────────────────────────────

		private void Relayout()
		{
			layoutDirty = false;
			TerrainInstancingProbe.Relayouts++;
			slotCount = 0;
			var capacities = new List<int>();
			commands.Clear();
			var args = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
			var argSlots = new List<uint>();
			for (int id = 0; id < models.Count; id++)
			{
				Model m = models[id];
				m.Data.SlotBase = (uint)slotCount;
				for (int l = 0; l < m.Levels.Length; l++)
				{
					for (int v = 0; v < TerrainGpuMath.Views; v++)
					{
						int slot = TerrainGpuMath.Slot(slotCount, l, v);
						if (v == 1 && (m.Data.ShadowMask & (1u << l)) == 0)
						{
							continue;
						}
						foreach (PartSource part in m.Levels[l])
						{
							if (v == 1 && !part.CastsShadows)
							{
								continue;
							}
							RenderParams rp = part.Template;
							rp.material = part.Material;
							rp.shadowCastingMode = v == 0 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly;
							commands.Add(new Command { Model = id, Level = l, View = v, Slot = slot, Mesh = part.Mesh, Submesh = part.Submesh, Params = rp });
							args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
							{
								indexCountPerInstance = part.Mesh.GetIndexCount(part.Submesh),
								startIndex = part.Mesh.GetIndexStart(part.Submesh),
								baseVertexIndex = (uint)part.Mesh.GetBaseVertex(part.Submesh),
								instanceCount = 0,
								startInstance = 0,
							});
							argSlots.Add((uint)slot);
						}
					}
				}
				for (int l = 0; l < m.Levels.Length; l++)
				{
					for (int v = 0; v < TerrainGpuMath.Views; v++)
					{
						capacities.Add(SlotCapacity(m, l, v));
					}
				}
				slotCount += m.Levels.Length * TerrainGpuMath.Views;
			}

			var bases = new int[slotCount];
			int total = TerrainGpuMath.LayoutSlots(capacities, bases);
			var slots = new Vector2Int[slotCount];
			for (int s = 0; s < slotCount; s++)
			{
				slots[s] = new Vector2Int(bases[s], capacities[s]);
			}

			Recreate(ref modelBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, models.Count), FishModelData.Stride);
			var modelData = new FishModelData[models.Count];
			for (int i = 0; i < models.Count; i++)
			{
				modelData[i] = models[i].Data;
			}
			modelBuffer.SetData(modelData);
			Recreate(ref slotBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, slotCount), 8);
			slotBuffer.SetData(slots);
			Recreate(ref countBuffer, GraphicsBuffer.Target.Raw, Mathf.Max(1, slotCount), 4);
			if (visibleBuffer == null || visibleBuffer.count < total)
			{
				visibleBuffer?.Release();
				// Headroom, so a few more resident details do not reallocate at once.
				visibleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1024, total + total / 4), FishVisible.Stride);
			}
			Recreate(ref argsBuffer, GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, Mathf.Max(1, args.Count), GraphicsBuffer.IndirectDrawIndexedArgs.size);
			if (args.Count > 0)
			{
				argsBuffer.SetData(args);
			}
			Recreate(ref argSlotBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, argSlots.Count), 4);
			if (argSlots.Count > 0)
			{
				argSlotBuffer.SetData(argSlots);
			}
			// Command index (= the call's startCommand = unity_BaseCommandID) → its slot's base.
			uint[] commandBases = TerrainGpuMath.CommandBases(argSlots, bases);
			Recreate(ref commandBaseBuffer, GraphicsBuffer.Target.Structured, commandBases.Length, 4);
			commandBaseBuffer.SetData(commandBases);

			if (slotBlocks.Length < slotCount)
			{
				int old = slotBlocks.Length;
				Array.Resize(ref slotBlocks, slotCount);
				for (int s = old; s < slotCount; s++)
				{
					slotBlocks[s] = new MaterialPropertyBlock();
				}
			}
			for (int s = 0; s < slotCount; s++)
			{
				// Float-backed on purpose (exact to 2^24 entries). The shader declares `uint _FishVisibleBase`, a
				// loose uniform on GLCore; URP feeds its own uint uniforms (_MainLightLayerMask…) through the
				// float-backed SetGlobalInt, which Unity converts to the parameter's type when it applies it.
				// A real-int SetInteger value is not known to reach a uint parameter there: if it does not, every
				// draw reads slot 0's region, which is exactly "only one prototype's places, drawn by all".
				slotBlocks[s].SetFloat(VisibleBaseId, bases[s]);
			}
			EnsureGpuBuffer();
			BindBlocks();
		}

		/// <summary>Binds the instance, visible and command-base buffers on every per-draw block and on every clone material.</summary>
		private void BindBlocks()
		{
			for (int s = 0; s < slotCount && s < slotBlocks.Length; s++)
			{
				Bind(slotBlocks[s]);
			}
			foreach (Material clone in clones.Values)
			{
				if (clone == null)
				{
					continue;
				}
				if (instances != null)
				{
					clone.SetBuffer(InstancesId, instances);
				}
				if (visibleBuffer != null)
				{
					clone.SetBuffer(VisibleId, visibleBuffer);
				}
				if (commandBaseBuffer != null)
				{
					clone.SetBuffer(CommandBasesId, commandBaseBuffer);
				}
			}
		}

		private void Bind(MaterialPropertyBlock block)
		{
			block.SetFloat(ContactOnId, ContactMode);
			if (instances != null)
			{
				block.SetBuffer(InstancesId, instances);
			}
			if (visibleBuffer != null)
			{
				block.SetBuffer(VisibleId, visibleBuffer);
			}
			if (commandBaseBuffer != null)
			{
				block.SetBuffer(CommandBasesId, commandBaseBuffer);
			}
		}

		private static void Recreate(ref GraphicsBuffer buffer, GraphicsBuffer.Target target, int count, int stride)
		{
			if (buffer != null && buffer.count == count && buffer.stride == stride)
			{
				return;
			}
			buffer?.Release();
			buffer = new GraphicsBuffer(target, count, stride);
		}

		public void Dispose()
		{
			cmd.Release();
			ReleaseRetired(true);
			if (compute != null)
			{
				Object.Destroy(compute);
			}
			instances?.Release();
			modelBuffer?.Release();
			workBuffer?.Release();
			slotBuffer?.Release();
			argSlotBuffer?.Release();
			countBuffer?.Release();
			visibleBuffer?.Release();
			argsBuffer?.Release();
			commandBaseBuffer?.Release();
			instances = modelBuffer = workBuffer = slotBuffer = argSlotBuffer = countBuffer = visibleBuffer = argsBuffer = commandBaseBuffer = null;
			if (work.IsCreated)
			{
				work.Dispose();
			}
			if (mirror.IsCreated)
			{
				mirror.Dispose();
			}
			foreach (Material clone in clones.Values)
			{
				if (clone != null)
				{
					Object.Destroy(clone);
				}
			}
			clones.Clear();
			residents.Clear();
			models.Clear();
			commands.Clear();
		}
	}
}
