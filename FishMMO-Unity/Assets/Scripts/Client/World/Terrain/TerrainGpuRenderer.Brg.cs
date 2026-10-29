using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The BatchRendererGroup path: the same GPU culling, LOD pick and cross-fade, the same visible lists and indirect args,
	/// submitted as <see cref="BatchDrawCommandIndirect"/>s of one <see cref="BatchRendererGroup"/> per renderer instead of
	/// one <see cref="Graphics.RenderMeshIndirect"/> call each. The draws then render through the SRP Batcher (the indirect
	/// twins' DOTS_INSTANCING_ON variants), where a material change within one shader variant is a constant-buffer bind
	/// (SRPBRender.ApplyShader) rather than a full material set-up (StdRender.ApplyShader, ~6-7 µs a draw: render-pass
	/// attribution 2026-10-27). Off (<see cref="UseBrg"/>, probe variant `nobrg`) is today's path, unchanged.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Data</b> (TerrainBrgMath): one raw batch buffer per renderer holding every resident instance's DOTS matrices
	/// (unity_ObjectToWorld, unity_WorldToObject; the previous-frame pair aliases them), written on the GPU by
	/// FishTerrainInstancing.compute FishDotsInstances from each upload, beside the 48-byte rows the culling, the rock
	/// crevice field and the RenderMeshIndirect path keep reading; and the renderer's two per-batch constants
	/// (<see cref="ContactMode"/>, <see cref="FloraMode"/>), which a draw took from its property block before. The culling
	/// writes each visible entry a second time as DOTS reads it (resident index | fade byte, TerrainBrgMath.PackVisible).
	/// </para>
	/// <para>
	/// <b>Commands</b>: one per laid-out command (model, level, view, part) with its material (its own clone, or its look
	/// family's with the family's pooled mesh, TerrainGpuRenderer.Looks.cs — the table row rides the pooled uv3 as on the
	/// RenderMeshIndirect path; measured 2026-10-28, the SRP Batcher still pays a pipeline change a material, so the families
	/// matter here as much), its mesh, the args entry FishFinalize fills
	/// (startInstance 0 on every API: DOTS adds the command's visibleOffset itself), visibleOffset = its slot's base. A
	/// primed lit command also gets its depth prime. The camera's culling callback emits the camera view's wanted
	/// commands; every shadow-casting light's emits the shadow view's, visible in every split (the per-cascade caster
	/// sphere was a per-draw block value, which a batch draw has none of — as the batched Vulkan calls today).
	/// </para>
	/// <para>
	/// <b>Which camera</b>: <see cref="Execute"/> arms the path for its camera after recording that camera's culling; the
	/// culling callbacks emit only while it is armed for the camera being culled, and endCameraRendering disarms it. A
	/// camera the owner never executes for (a preview, a reflection probe) draws none of it, as RenderParams.camera kept it.
	/// </para>
	/// <para>
	/// <b>Where</b>: compute and a raw-buffer BatchRendererGroup (<see cref="BatchRendererGroup.BufferTarget"/>, the GPU
	/// Resident Drawer's own test): Vulkan, D3D11/12, Metal, and WebGPU where Unity gives it raw buffers. Elsewhere, and
	/// with the switch off, the RenderMeshIndirect path draws.
	/// </para>
	/// </remarks>
	public sealed partial class TerrainGpuRenderer
	{
		/// <summary>Whether the GPU-driven draws go through a BatchRendererGroup where it can (probe variant `nobrg`: today's calls).</summary>
		public static bool UseBrg = true;

		/// <summary>Whether this platform can: compute, and a raw-buffer BatchRendererGroup (the GPU Resident Drawer's test).</summary>
		public static bool BrgSupported => SystemInfo.supportsComputeShaders && BatchRendererGroup.BufferTarget == BatchBufferTarget.RawBuffer;

		/// <summary>Whether this renderer's next layout submits through the batch group (not where it was refused: <see cref="SyncDots"/>).</summary>
		private bool BrgWanted => UseBrg && !brgRefused && BrgSupported;
		private bool brgRefused;

		private static readonly int ObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld");
		private static readonly int WorldToObjectId = Shader.PropertyToID("unity_WorldToObject");
		private static readonly int PreviousObjectToWorldId = Shader.PropertyToID("unity_MatrixPreviousM");
		private static readonly int PreviousWorldToObjectId = Shader.PropertyToID("unity_MatrixPreviousMI");
		private static readonly int BatchContactOnId = Shader.PropertyToID("_FishBatchContactOn");
		private static readonly int BatchFloraOnId = Shader.PropertyToID("_FishBatchFloraOn");
		private static readonly int VisiblePackedId = Shader.PropertyToID("_FishVisiblePacked");
		private static readonly int PackedOnId = Shader.PropertyToID("_FishPackedOn");
		private static readonly int DotsOutId = Shader.PropertyToID("_FishDotsOut");
		private static readonly int DotsObjectToWorldId = Shader.PropertyToID("_FishDotsObjectToWorld");
		private static readonly int DotsWorldToObjectId = Shader.PropertyToID("_FishDotsWorldToObject");

		private readonly int dotsKernel;

		/// <summary>The layout's: commands are submitted through <see cref="brg"/>.</summary>
		private bool brgState;
		private BatchRendererGroup brg;
		private BatchRendererGroup.OnPerformCulling brgCulling;
		private BatchID brgBatch;
		private bool brgHasBatch;
		private bool brgHooked;
		/// <summary>The batch buffer (TerrainBrgMath layout) and the instances it was laid out for.</summary>
		private GraphicsBuffer dotsBuffer;
		private int dotsCapacity;
		private float dotsContact = float.NaN, dotsFlora = float.NaN;
		/// <summary>The packed visible entries (the BatchRendererGroup's visible list), and the one word bound in their place when off.</summary>
		private GraphicsBuffer packedVisibleBuffer, packedDummy;

		private readonly Dictionary<Mesh, BatchMeshID> brgMeshes = new Dictionary<Mesh, BatchMeshID>();
		private readonly Dictionary<Material, BatchMaterialID> brgMaterials = new Dictionary<Material, BatchMaterialID>();

		/// <summary>One laid-out command as the culling callback submits it.</summary>
		private struct BrgDraw
		{
			public BatchMeshID Mesh;
			public BatchMaterialID Material;
			public BatchMaterialID Prime;
			public bool HasPrime;
			public MeshTopology Topology;
			public uint ArgsOffset;
			public uint VisibleOffset;
			public int View;
			/// <summary>Its render parameters' filter (<see cref="brgFilters"/>).</summary>
			public int Filter;
		}

		/// <summary>What a draw range fixes besides the shadow casting mode (which the view decides).</summary>
		private readonly struct BrgFilter : IEquatable<BrgFilter>
		{
			public readonly int Layer;
			public readonly uint RenderingLayerMask;
			public readonly bool ReceiveShadows;
			public readonly MotionVectorGenerationMode Motion;

			public BrgFilter(in RenderParams p)
			{
				Layer = p.layer;
				RenderingLayerMask = p.renderingLayerMask;
				ReceiveShadows = p.receiveShadows;
				Motion = p.motionVectorMode;
			}

			public bool Equals(BrgFilter o) => Layer == o.Layer && RenderingLayerMask == o.RenderingLayerMask && ReceiveShadows == o.ReceiveShadows && Motion == o.Motion;
			public override bool Equals(object obj) => obj is BrgFilter f && Equals(f);
			public override int GetHashCode() => ((Layer * 397) ^ (int)RenderingLayerMask) * 397 ^ (ReceiveShadows ? 1 : 0) ^ ((int)Motion << 1);
		}

		private BrgDraw[] brgDraws = Array.Empty<BrgDraw>();
		/// <summary>Where the armed camera was: every command's sorting position (see <see cref="OnPerformCulling"/>).</summary>
		private Vector3 brgEye;
		private readonly List<BrgFilter> brgFilters = new List<BrgFilter>();
		private int[] brgFilterCounts = Array.Empty<int>();
		private int[] brgFilterNext = Array.Empty<int>();

		/// <summary>The camera the path is armed for (<see cref="ArmBrg"/>), until its endCameraRendering.</summary>
		private EntityId brgCamera;
		private bool brgArmed;

		/// <summary>Whether this renderer submits through a BatchRendererGroup now (for probes).</summary>
		public bool BrgActive => brgState;

		/// <summary>Meshes and materials registered with the BatchRendererGroup, and the batch buffer's bytes.</summary>
		public int BrgMeshCount => brgMeshes.Count;
		public int BrgMaterialCount => brgMaterials.Count;
		public long BrgBytes => Size(dotsBuffer) + Size(packedVisibleBuffer) + Size(packedDummy);

		/// <summary>The commands the last camera's and its lights' culling callbacks submitted (shadow commands once a light).</summary>
		public int LastBrgCameraCommands { get; private set; }
		public int LastBrgLightCommands { get; private set; }

		/// <summary>
		/// The end of a layout: the batch group, its batch buffer, the packed visible list and one submission record a command,
		/// or everything released when the layout is not for it.
		/// </summary>
		private void BrgLayout(int[] bases)
		{
			if (!brgState)
			{
				DisposeBrg();
				return;
			}
			EnsureBrg();
			SyncDots();
			int visibleCount = visibleBuffer != null ? visibleBuffer.count : 1;
			if (packedVisibleBuffer == null || packedVisibleBuffer.count != visibleCount)
			{
				packedVisibleBuffer?.Release();
				packedVisibleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, visibleCount, sizeof(uint));
			}
			if (brgDraws.Length < commands.Count)
			{
				brgDraws = new BrgDraw[commands.Count];
			}
			brgFilters.Clear();
			for (int c = 0; c < commands.Count; c++)
			{
				Command command = commands[c];
				var filter = new BrgFilter(command.Params);
				int f = brgFilters.IndexOf(filter);
				if (f < 0)
				{
					f = brgFilters.Count;
					brgFilters.Add(filter);
				}
				bool primed = command.Prime != null;
				brgDraws[c] = new BrgDraw
				{
					Mesh = MeshId(command.Mesh),
					Material = MaterialId(command.Params.material),
					Prime = primed ? MaterialId(command.Prime) : default,
					HasPrime = primed,
					Topology = command.Mesh.GetTopology(command.Submesh),
					ArgsOffset = (uint)(c * GraphicsBuffer.IndirectDrawIndexedArgs.size),
					VisibleOffset = (uint)bases[command.Slot],
					View = command.View,
					Filter = f,
				};
			}
			if (brgFilterCounts.Length < brgFilters.Count)
			{
				brgFilterCounts = new int[brgFilters.Count];
				brgFilterNext = new int[brgFilters.Count];
			}
		}

		private BatchMeshID MeshId(Mesh mesh)
		{
			if (!brgMeshes.TryGetValue(mesh, out BatchMeshID id))
			{
				id = brg.RegisterMesh(mesh);
				brgMeshes.Add(mesh, id);
			}
			return id;
		}

		private BatchMaterialID MaterialId(Material material)
		{
			if (!brgMaterials.TryGetValue(material, out BatchMaterialID id))
			{
				id = brg.RegisterMaterial(material);
				brgMaterials.Add(material, id);
			}
			return id;
		}

		private void EnsureBrg()
		{
			if (brg == null)
			{
				brgCulling = OnPerformCulling;
				brg = new BatchRendererGroup(brgCulling, IntPtr.Zero);
				brg.SetEnabledViewTypes(new[] { BatchCullingViewType.Camera, BatchCullingViewType.Light });
				brgHasBatch = false;
				dotsCapacity = -1;
			}
			if (!brgHooked)
			{
				brgHooked = true;
				RenderPipelineManager.endCameraRendering += OnBrgEndCamera;
			}
		}

		/// <summary>
		/// The batch buffer at the instance buffer's capacity, its header (the constants) current, and the batch over it; a new
		/// buffer has every resident range marked for upload, so the next flush writes its matrices (FishDotsInstances).
		/// </summary>
		private void SyncDots()
		{
			if (!brgState || brg == null || instances == null)
			{
				return;
			}
			int capacity = instances.count;
			if (capacity >= TerrainBrgMath.MaxInstances)
			{
				// A packed visible index names 2^24 instances at most: the path cannot draw this renderer.
				Debug.LogWarning($"[Terrain GPU] {capacity} resident instances: past what a BatchRendererGroup visible index names ({TerrainBrgMath.MaxInstances}); drawing with RenderMeshIndirect.");
				brgRefused = true;
				layoutDirty = true;
				return;
			}
			if (dotsBuffer == null || dotsCapacity != capacity)
			{
				dotsBuffer?.Release();
				dotsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, TerrainBrgMath.BufferWords(capacity), sizeof(uint));
				dotsCapacity = capacity;
				dotsContact = dotsFlora = float.NaN;
				if (brgHasBatch)
				{
					brg.RemoveBatch(brgBatch);
					brgHasBatch = false;
				}
				foreach (KeyValuePair<int, Resident> r in residents)
				{
					MarkDirty(r.Key, r.Value.Count);
				}
			}
			if (dotsContact != ContactMode || dotsFlora != FloraMode)
			{
				dotsContact = ContactMode;
				dotsFlora = FloraMode;
				dotsBuffer.SetData(TerrainBrgMath.Header(ContactMode, FloraMode));
			}
			if (!brgHasBatch)
			{
				var metadata = new NativeArray<MetadataValue>(6, Allocator.Temp);
				uint objectToWorld = TerrainBrgMath.PerInstance(TerrainBrgMath.ObjectToWorldAddress);
				uint worldToObject = TerrainBrgMath.PerInstance(TerrainBrgMath.WorldToObjectAddress(capacity));
				metadata[0] = new MetadataValue { NameID = ObjectToWorldId, Value = objectToWorld };
				metadata[1] = new MetadataValue { NameID = WorldToObjectId, Value = worldToObject };
				metadata[2] = new MetadataValue { NameID = PreviousObjectToWorldId, Value = objectToWorld };
				metadata[3] = new MetadataValue { NameID = PreviousWorldToObjectId, Value = worldToObject };
				metadata[4] = new MetadataValue { NameID = BatchContactOnId, Value = TerrainBrgMath.ContactOnAddress };
				metadata[5] = new MetadataValue { NameID = BatchFloraOnId, Value = TerrainBrgMath.FloraOnAddress };
				brgBatch = brg.AddBatch(metadata, dotsBuffer.bufferHandle);
				metadata.Dispose();
				brgHasBatch = true;
			}
		}

		/// <summary>Binds the cull kernel's packed output: the real list while submitting through the batch group, else one word and off.</summary>
		private void BindPacked(CommandBuffer into)
		{
			bool on = brgState && packedVisibleBuffer != null;
			if (!on && packedDummy == null)
			{
				packedDummy = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
			}
			into.SetComputeBufferParam(compute, cullKernel, VisiblePackedId, on ? packedVisibleBuffer : packedDummy);
			into.SetComputeIntParam(compute, PackedOnId, on ? 1 : 0);
		}

		/// <summary>After a copy dispatch: the same range's DOTS matrices into the batch buffer (parameters already set for the copy).</summary>
		private void DispatchDots(CommandBuffer into, GraphicsBuffer upload, int count)
		{
			if (!brgState || dotsBuffer == null)
			{
				return;
			}
			into.SetComputeBufferParam(compute, dotsKernel, UploadId, upload);
			into.SetComputeBufferParam(compute, dotsKernel, DotsOutId, dotsBuffer);
			into.SetComputeIntParam(compute, DotsObjectToWorldId, TerrainBrgMath.ObjectToWorldAddress);
			into.SetComputeIntParam(compute, DotsWorldToObjectId, TerrainBrgMath.WorldToObjectAddress(dotsCapacity));
			into.DispatchCompute(compute, dotsKernel, (count + 63) / 64, 1, 1);
		}

		/// <summary>
		/// The camera's draws: its wanted commands, submitted by its culling callbacks from now until its endCameraRendering.
		/// Returns the draws they make (a shadow command counted once, though drawn into every cascade).
		/// </summary>
		private int ArmBrg(Camera camera)
		{
			CollectWanted();
			CameraLightCallbacks = CameraLightFacesBeforeCull = CameraLightFacesAfterCull = 0;
			lightWorkPrepared = false;
			brgCamera = camera.GetEntityId();
			brgArmed = true;
			brgEye = camera.transform.position;
			int main = 0, primes = 0, shadows = 0;
			LastFamilies.Clear();
			for (int w = 0; w < wantedCommands.Count; w++)
			{
				int c = wantedCommands[w];
				bool primed = primedState && brgDraws[c].HasPrime;
				if (brgDraws[c].View == 0)
				{
					main++;
					primes += primed ? 1 : 0;
				}
				else
				{
					shadows++;
				}
				if (CountFamilies)
				{
					Tally(c, 1, brgDraws[c].View, primed);
				}
			}
			LastMainCommands = main;
			LastPrimeCommands = primes;
			LastShadowCommands = shadows;
			LastCommandCount = wantedCommands.Count;
			LastDrawCount = main + primes + shadows;
			return LastDrawCount;
		}

		private void OnBrgEndCamera(ScriptableRenderContext context, Camera camera)
		{
			if (brgArmed && camera != null && camera.GetEntityId() == brgCamera)
			{
				brgArmed = false;
			}
		}

		/// <summary>
		/// The culling callback: the armed camera's view gets its camera-view commands (and their primes), a light its
		/// shadow-view ones in every split. One draw range per render-parameter filter; the memory is Unity's to free
		/// (Allocator.TempJob). Nothing for any other view, or while not armed.
		/// </summary>
		private unsafe JobHandle OnPerformCulling(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
		{
			if (!brgArmed || !brgState || !brgHasBatch || argsBuffer == null || packedVisibleBuffer == null || brgFilters.Count == 0)
			{
				return default;
			}
			int view;
			if (context.viewType == BatchCullingViewType.Camera)
			{
				if (context.viewID.GetEntityId() != brgCamera)
				{
					return default;
				}
				view = 0;
			}
			else if (context.viewType == BatchCullingViewType.Light)
			{
				view = 1;
			}
			else
			{
				return default;
			}

			ushort splits = view == 0 ? (ushort)1 : TerrainBrgMath.SplitMask(context.cullingSplits.Length);
			// The sun retains the existing cascade path and CPU cost. Point and spot shadows provide perspective splits.
			bool cullLight = view == 1 && CullLightCommands && context.projectionType == BatchCullingProjectionType.Perspective;
			if (view == 1)
			{
				if (cullLight) CullLight(in context);
				LastBrgLightCommandsBeforeCull = LastBrgLightFacesBeforeCull = LastBrgLightFacesAfterCull = 0;
			}
			int filters = brgFilters.Count;
			Array.Clear(brgFilterCounts, 0, filters);
			int total = 0;
			for (int w = 0; w < wantedCommands.Count; w++)
			{
				int c = wantedCommands[w];
				if (c >= brgDraws.Length || brgDraws[c].View != view)
				{
					continue;
				}
				if (view == 1)
				{
					LastBrgLightCommandsBeforeCull++;
					LastBrgLightFacesBeforeCull += FaceCount(splits);
					ushort mask = CommandLightMask(c, splits, cullLight);
					LastBrgLightFacesAfterCull += FaceCount(mask);
					if (mask == 0) continue;
				}
				int n = view == 0 && primedState && brgDraws[c].HasPrime ? 2 : 1;
				brgFilterCounts[brgDraws[c].Filter] += n;
				total += n;
			}
			if (view == 0)
			{
				LastBrgCameraCommands = total;
			}
			else
			{
				LastBrgLightCommands = total;
				CameraLightCallbacks++;
				CameraLightFacesBeforeCull += LastBrgLightFacesBeforeCull;
				CameraLightFacesAfterCull += LastBrgLightFacesAfterCull;
			}
			if (total == 0)
			{
				return default;
			}
			int ranges = 0;
			int start = 0;
			for (int f = 0; f < filters; f++)
			{
				brgFilterNext[f] = start;
				start += brgFilterCounts[f];
				ranges += brgFilterCounts[f] > 0 ? 1 : 0;
			}

			var drawCommands = new BatchCullingOutputDrawCommands
			{
				indirectDrawCommands = (BatchDrawCommandIndirect*)UnsafeUtility.Malloc(
					UnsafeUtility.SizeOf<BatchDrawCommandIndirect>() * total, UnsafeUtility.AlignOf<BatchDrawCommandIndirect>(), Allocator.TempJob),
				indirectDrawCommandCount = total,
				drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(
					UnsafeUtility.SizeOf<BatchDrawRange>() * ranges, UnsafeUtility.AlignOf<BatchDrawRange>(), Allocator.TempJob),
				drawRangeCount = ranges,
				// One sorting position a command, all the armed camera's own: the RenderMeshIndirect calls had their bounds
				// centred on the camera, so the opaque sort's depth tied for all of them and state (shader, material) decided
				// their order. Without positions the batch group's draws sort by some depth of their own and interleave variants.
				instanceSortingPositions = (float*)UnsafeUtility.Malloc(sizeof(float) * 3 * total, UnsafeUtility.AlignOf<float>(), Allocator.TempJob),
				instanceSortingPositionFloatCount = 3 * total,
			};
			for (int i = 0; i < total; i++)
			{
				drawCommands.instanceSortingPositions[3 * i] = brgEye.x;
				drawCommands.instanceSortingPositions[3 * i + 1] = brgEye.y;
				drawCommands.instanceSortingPositions[3 * i + 2] = brgEye.z;
			}

			GraphicsBufferHandle visibleHandle = packedVisibleBuffer.bufferHandle;
			GraphicsBufferHandle argsHandle = argsBuffer.bufferHandle;
			for (int w = 0; w < wantedCommands.Count; w++)
			{
				int c = wantedCommands[w];
				if (c >= brgDraws.Length || brgDraws[c].View != view)
				{
					continue;
				}
				ushort mask = view == 0 ? splits : CommandLightMask(c, splits, cullLight);
				if (mask == 0) continue;
				BrgDraw d = brgDraws[c];
				var command = new BatchDrawCommandIndirect
				{
					flags = BatchDrawCommandFlags.LODCrossFadeValuePacked | BatchDrawCommandFlags.HasSortingPosition,
					batchID = brgBatch,
					materialID = d.Material,
					splitVisibilityMask = mask,
					lightmapIndex = ushort.MaxValue,
					sortingPosition = 0,
					visibleOffset = d.VisibleOffset,
					meshID = d.Mesh,
					topology = d.Topology,
					visibleInstancesBufferHandle = visibleHandle,
					indirectArgsBufferHandle = argsHandle,
					indirectArgsBufferOffset = d.ArgsOffset,
				};
				command.sortingPosition = 3 * brgFilterNext[d.Filter];
				drawCommands.indirectDrawCommands[brgFilterNext[d.Filter]++] = command;
			}
			for (int w = 0; view == 0 && primedState && w < wantedCommands.Count; w++)
			{
				int c = wantedCommands[w];
				if (c >= brgDraws.Length || brgDraws[c].View != 0 || !brgDraws[c].HasPrime)
				{
					continue;
				}
				BrgDraw d = brgDraws[c];
				// The same instances (its args and its slot's run) through the depth prime, two queues ahead of the lit draw.
				int at = brgFilterNext[d.Filter]++;
				drawCommands.indirectDrawCommands[at] = new BatchDrawCommandIndirect
				{
					flags = BatchDrawCommandFlags.LODCrossFadeValuePacked | BatchDrawCommandFlags.HasSortingPosition,
					batchID = brgBatch,
					materialID = d.Prime,
					splitVisibilityMask = splits,
					lightmapIndex = ushort.MaxValue,
					sortingPosition = 3 * at,
					visibleOffset = d.VisibleOffset,
					meshID = d.Mesh,
					topology = d.Topology,
					visibleInstancesBufferHandle = visibleHandle,
					indirectArgsBufferHandle = argsHandle,
					indirectArgsBufferOffset = d.ArgsOffset,
				};
			}

			int range = 0;
			start = 0;
			for (int f = 0; f < filters; f++)
			{
				int count = brgFilterCounts[f];
				if (count == 0)
				{
					continue;
				}
				BrgFilter filter = brgFilters[f];
				drawCommands.drawRanges[range++] = new BatchDrawRange
				{
					drawCommandsType = BatchDrawCommandType.Indirect,
					drawCommandsBegin = (uint)start,
					drawCommandsCount = (uint)count,
					filterSettings = new BatchFilterSettings
					{
						renderingLayerMask = filter.RenderingLayerMask,
						layer = (byte)filter.Layer,
						motionMode = filter.Motion,
						shadowCastingMode = view == 0 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly,
						receiveShadows = filter.ReceiveShadows,
						staticShadowCaster = false,
						allDepthSorted = true,
					},
				};
				start += count;
			}
			output.drawCommands[0] = drawCommands;
			return default;
		}

		/// <summary>
		/// Releases the batch group, its registrations and its buffers (the RenderMeshIndirect path needs none of them); with
		/// <paramref name="all"/> (the renderer's own dispose) the one-word stand-in too.
		/// </summary>
		private void DisposeBrg(bool all = false)
		{
			if (all)
			{
				shadowRanges.Clear();
				lightSplitCaches.Clear();
				lightWorkSnapshot = Array.Empty<FishWork>();
				lightWorkModelMasks = Array.Empty<int>();
				lightWorkCount = lightWorkModels = -1;
				lightModelMasks = Array.Empty<ushort>();
				packedDummy?.Release();
				packedDummy = null;
			}
			if (brgHooked)
			{
				brgHooked = false;
				RenderPipelineManager.endCameraRendering -= OnBrgEndCamera;
			}
			brgArmed = false;
			if (brg != null)
			{
				// Disposing the group frees its batches and its mesh and material registrations.
				brg.Dispose();
				brg = null;
			}
			brgCulling = null;
			brgHasBatch = false;
			brgMeshes.Clear();
			brgMaterials.Clear();

			dotsBuffer?.Release();
			dotsBuffer = null;
			dotsCapacity = -1;
			packedVisibleBuffer?.Release();
			packedVisibleBuffer = null;
		}
	}
}
