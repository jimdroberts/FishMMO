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
	/// The detail scatter's BatchRendererGroup path (2026-11-04, the harness setting `scatterBrg`), as the props' (TerrainGpuRenderer.Brg.cs):
	/// the same generated instances, culled lists and indirect args, submitted as <see cref="BatchDrawCommandIndirect"/>s of one batch
	/// group instead of a <see cref="Graphics.RenderMeshIndirect"/> call each. The draws then render through the SRP Batcher (the
	/// twins' DOTS_INSTANCING_ON variants): a material change within a variant is a constant-buffer bind, not a full material
	/// set-up, and on WebGPU a RenderMeshIndirect submission re-applies its whole pass (Docs/Rendering/2026-11-01/webgpu-constants).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Data</b> (TerrainBrgMath's layout): the batch buffer holds every generated instance's DOTS matrices at its instance
	/// index, written by FishDetailScatter.compute as it emits the instance (ScatterEmitDots), and the header's two constants:
	/// contact 0 (the scatter never takes the contact blend) and flora 1. A bush's colours-only look, which its RenderMeshIndirect
	/// draws say with _FishFloraOn 2, is its clone's _FishFloraColoursOnly (CloneOf). The visible lists are packed as DOTS reads
	/// them (resident index | fade byte): the generated ones by FishScatterPack after each generation, the camera's culled ones by
	/// FishScatterCull as it refreshes them.
	/// </para>
	/// <para>
	/// <b>Commands</b>: one per laid-out command with work for the camera (<see cref="ArmBrg"/>): its material (its clone or its
	/// vegetation family's), its mesh (a family's from its pool), the args entry FishScatterFinalize fills (startInstance 0: DOTS
	/// adds the command's visibleOffset itself), visibleOffset its slot's base. The camera's culling callback emits the camera
	/// view's, a light's the shadow view's in every split; a point or spot light only the non-vegetation casters (vegetation drops
	/// those in its shader and DirectionalShadowDraws keeps them off the other path). There is no per-draw caster sphere: FishCasterCulling.hlsl culls
	/// nothing for a batch draw, as for the props'.
	/// </para>
	/// </remarks>
	public sealed partial class DetailScatterRenderer
	{
		/// <summary>Whether the scatter submits through a BatchRendererGroup where it can (TerrainGpuRenderer.BrgSupported).</summary>
		public static bool UseBrg = true;

		private bool BrgWanted => UseBrg && !brgRefused && TerrainGpuRenderer.BrgSupported;
		private bool brgRefused;

		/// <summary>The layout's: commands are submitted through <see cref="brg"/>.</summary>
		private bool brgState;
		private readonly int packKernel;
		private BatchRendererGroup brg;
		private BatchRendererGroup.OnPerformCulling brgCulling;
		private BatchID brgBatch;
		private bool brgHasBatch, brgHooked;
		/// <summary>The batch buffer at the instance buffer's capacity, the packed generated and culled visible lists, and the one word bound in their place when off.</summary>
		private GraphicsBuffer dotsBuffer, packedBuffer, culledPackedBuffer, brgDummy;
		private int dotsCapacity = -1;

		private readonly Dictionary<Mesh, BatchMeshID> brgMeshes = new Dictionary<Mesh, BatchMeshID>();
		private readonly Dictionary<Material, BatchMaterialID> brgMaterials = new Dictionary<Material, BatchMaterialID>();

		private struct BrgDraw
		{
			public BatchMeshID Mesh;
			public BatchMaterialID Material;
			public MeshTopology Topology;
			public uint ArgsOffset;
			public uint VisibleOffset;
			public int View;
			public int Filter;
			/// <summary>A shadow command that also casts into a point or spot light's shadow (not vegetation, whose shader drops those: DirectionalShadowDraws).</summary>
			public bool Punctual;
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
		private readonly List<BrgFilter> brgFilters = new List<BrgFilter>();
		private int[] brgFilterCounts = Array.Empty<int>(), brgFilterNext = Array.Empty<int>();
		/// <summary>The armed camera's commands with work (ascending).</summary>
		private readonly List<int> brgWanted = new List<int>();
		private EntityId brgCamera;
		private bool brgArmed;
		/// <summary>Whether the armed camera's view reads the culled list (<see cref="CullInstances"/> as it was armed).</summary>
		private bool brgCulled;
		private Vector3 brgEye;

		private static readonly int ObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld");
		private static readonly int WorldToObjectId = Shader.PropertyToID("unity_WorldToObject");
		private static readonly int PreviousObjectToWorldId = Shader.PropertyToID("unity_MatrixPreviousM");
		private static readonly int PreviousWorldToObjectId = Shader.PropertyToID("unity_MatrixPreviousMI");
		private static readonly int BatchContactOnId = Shader.PropertyToID("_FishBatchContactOn");
		private static readonly int BatchFloraOnId = Shader.PropertyToID("_FishBatchFloraOn");
		private static readonly int DotsOutId = Shader.PropertyToID("_ScatterDotsOut");
		private static readonly int DotsOnId = Shader.PropertyToID("_ScatterDotsOn");
		private static readonly int DotsObjectToWorldId = Shader.PropertyToID("_ScatterDotsObjectToWorld");
		private static readonly int DotsWorldToObjectId = Shader.PropertyToID("_ScatterDotsWorldToObject");
		private static readonly int PackedOutId = Shader.PropertyToID("_ScatterPackedOut");
		private static readonly int CulledPackedId = Shader.PropertyToID("_ScatterCulledPacked");

		/// <summary>Whether the scatter submits through a BatchRendererGroup now (probes).</summary>
		public bool BrgActive => brgState;

		/// <summary>The commands the last camera's and its directional light's culling callbacks submitted.</summary>
		public int LastBrgCameraCommands { get; private set; }
		public int LastBrgLightCommands { get; private set; }

		/// <summary>Whether this layout may submit through the batch group: wanted, and every instance nameable by a packed visible index.</summary>
		private bool BrgFor(int instanceCapacity) => BrgWanted && instanceCapacity < TerrainBrgMath.MaxInstances;

		/// <summary>The end of a layout: the batch group, its batch buffer, the packed lists and one submission record a command, or everything released.</summary>
		private void BrgLayout()
		{
			if (!brgState)
			{
				DisposeBrg();
				return;
			}
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
			int capacity = instanceBuffer.count;
			if (dotsBuffer == null || dotsCapacity != capacity)
			{
				// A new buffer is written whole by the generation every relayout is followed by (Execute: relayout, then generate).
				dotsBuffer?.Release();
				dotsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, TerrainBrgMath.BufferWords(capacity), sizeof(uint)) { name = "Detail scatter batch" };
				dotsBuffer.SetData(TerrainBrgMath.Header(0f, 1f));
				dotsCapacity = capacity;
				if (brgHasBatch)
				{
					brg.RemoveBatch(brgBatch);
					brgHasBatch = false;
				}
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
			int visibleCount = visibleBuffer.count;
			if (packedBuffer == null || packedBuffer.count != visibleCount)
			{
				packedBuffer?.Release();
				culledPackedBuffer?.Release();
				packedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, visibleCount, sizeof(uint)) { name = "Detail scatter packed visible" };
				culledPackedBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, visibleCount, sizeof(uint)) { name = "Detail scatter packed culled" };
			}
			PruneRegistrations();
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
				brgDraws[c] = new BrgDraw
				{
					Mesh = MeshId(command.Mesh),
					Material = MaterialId(command.Params.material),
					Topology = command.Mesh.GetTopology(0),
					ArgsOffset = (uint)(c * GraphicsBuffer.IndirectDrawIndexedArgs.size),
					VisibleOffset = (uint)bases[command.Slot],
					View = command.Slot % Views,
					Filter = f,
					Punctual = command.Params.material.shader.name != TerrainGpuRenderer.VegetationIndirectName,
				};
			}
			if (brgFilterCounts.Length < brgFilters.Count)
			{
				brgFilterCounts = new int[brgFilters.Count];
				brgFilterNext = new int[brgFilters.Count];
			}
		}

		private readonly List<KeyValuePair<Mesh, BatchMeshID>> liveMeshes = new List<KeyValuePair<Mesh, BatchMeshID>>();
		private readonly List<KeyValuePair<Material, BatchMaterialID>> liveMaterials = new List<KeyValuePair<Material, BatchMaterialID>>();

		/// <summary>
		/// Unregisters the meshes and materials destroyed since (a pool merged again, the clones made again after a switch flipped).
		/// The maps are rebuilt from their live entries: a destroyed object's Equals matches any other destroyed one.
		/// </summary>
		private void PruneRegistrations()
		{
			liveMeshes.Clear();
			foreach (KeyValuePair<Mesh, BatchMeshID> m in brgMeshes)
			{
				if (m.Key == null)
				{
					brg.UnregisterMesh(m.Value);
				}
				else
				{
					liveMeshes.Add(m);
				}
			}
			if (liveMeshes.Count != brgMeshes.Count)
			{
				brgMeshes.Clear();
				foreach (KeyValuePair<Mesh, BatchMeshID> m in liveMeshes)
				{
					brgMeshes.Add(m.Key, m.Value);
				}
			}
			liveMaterials.Clear();
			foreach (KeyValuePair<Material, BatchMaterialID> m in brgMaterials)
			{
				if (m.Key == null)
				{
					brg.UnregisterMaterial(m.Value);
				}
				else
				{
					liveMaterials.Add(m);
				}
			}
			if (liveMaterials.Count != brgMaterials.Count)
			{
				brgMaterials.Clear();
				foreach (KeyValuePair<Material, BatchMaterialID> m in liveMaterials)
				{
					brgMaterials.Add(m.Key, m.Value);
				}
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

		/// <summary>The generate kernel's batch-buffer output: the batch buffer while submitting through the batch group, else one word and off.</summary>
		private void BindDots(CommandBuffer into)
		{
			bool on = brgState && dotsBuffer != null;
			brgDummy ??= new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
			into.SetComputeIntParam(compute, DotsOnId, on ? 1 : 0);
			into.SetComputeBufferParam(compute, generateKernel, DotsOutId, on ? dotsBuffer : brgDummy);
			into.SetComputeIntParam(compute, DotsObjectToWorldId, TerrainBrgMath.ObjectToWorldAddress);
			into.SetComputeIntParam(compute, DotsWorldToObjectId, TerrainBrgMath.WorldToObjectAddress(Mathf.Max(0, dotsCapacity)));
		}

		/// <summary>After a generation: every slot's generated list packed for the batch group (FishScatterPack).</summary>
		private void DispatchPack(CommandBuffer into)
		{
			if (!brgState || packedBuffer == null)
			{
				return;
			}
			into.SetComputeIntParam(compute, SlotCountId, slotCount);
			into.SetComputeBufferParam(compute, packKernel, SlotsId, slotBuffer);
			into.SetComputeBufferParam(compute, packKernel, CountsId, countBuffer);
			into.SetComputeBufferParam(compute, packKernel, VisibleOutId, visibleBuffer);
			into.SetComputeBufferParam(compute, packKernel, PackedOutId, packedBuffer);
			into.DispatchCompute(compute, packKernel, Mathf.Max(1, slotCount), 1, 1);
		}

		/// <summary>The cull kernel's packed output: the culled list's own while submitting through the batch group, else one word.</summary>
		private void BindCulledPacked(CommandBuffer into)
		{
			bool on = brgState && culledPackedBuffer != null;
			brgDummy ??= new GraphicsBuffer(GraphicsBuffer.Target.Raw, 1, sizeof(uint));
			into.SetComputeIntParam(compute, DotsOnId, on ? 1 : 0);
			into.SetComputeBufferParam(compute, cullKernel, CulledPackedId, on ? culledPackedBuffer : brgDummy);
		}

		/// <summary>
		/// The camera's draws: its commands with work, submitted by its culling callbacks from now until its endCameraRendering.
		/// Returns the draws they make (a shadow command counted once, though drawn into every cascade).
		/// </summary>
		private int ArmBrg(Camera camera, out int shadowDraws)
		{
			brgWanted.Clear();
			shadowDraws = 0;
			for (int c = 0; c < commands.Count; c++)
			{
				if (need[commands[c].Slot] > 0)
				{
					brgWanted.Add(c);
					shadowDraws += commands[c].Slot % Views == 1 ? 1 : 0;
				}
			}
			brgCamera = camera.GetEntityId();
			brgArmed = true;
			brgCulled = CullInstances;
			brgEye = camera.transform.position;
			return brgWanted.Count;
		}

		private void OnBrgEndCamera(ScriptableRenderContext context, Camera camera)
		{
			if (brgArmed && camera != null && camera.GetEntityId() == brgCamera)
			{
				brgArmed = false;
			}
		}

		/// <summary>
		/// The culling callback: the armed camera's view gets its camera-view commands, a directional light its shadow-view ones in
		/// every split. One draw range per render-parameter filter; the memory is Unity's to free (Allocator.TempJob).
		/// </summary>
		private unsafe JobHandle OnPerformCulling(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
		{
			if (!brgArmed || !brgState || !brgHasBatch || argsBuffer == null || packedBuffer == null || brgFilters.Count == 0)
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
			// A point or spot light (a site's indoor lights) takes only the casters the other path gives it: not vegetation.
			bool punctual = view == 1 && context.projectionType == BatchCullingProjectionType.Perspective;
			int filters = brgFilters.Count;
			Array.Clear(brgFilterCounts, 0, filters);
			int total = 0;
			foreach (int c in brgWanted)
			{
				if (c < brgDraws.Length && brgDraws[c].View == view && (!punctual || brgDraws[c].Punctual))
				{
					brgFilterCounts[brgDraws[c].Filter]++;
					total++;
				}
			}
			if (view == 0)
			{
				LastBrgCameraCommands = total;
			}
			else
			{
				LastBrgLightCommands = total;
			}
			if (total == 0)
			{
				return default;
			}
			int ranges = 0, start = 0;
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
				// One sorting position a command, the armed camera's own, as the props': the RenderMeshIndirect calls' bounds were
				// centred on the camera, so the opaque sort tied for them all and state (shader, material) decided their order.
				instanceSortingPositions = (float*)UnsafeUtility.Malloc(sizeof(float) * 3 * total, UnsafeUtility.AlignOf<float>(), Allocator.TempJob),
				instanceSortingPositionFloatCount = 3 * total,
			};
			for (int i = 0; i < total; i++)
			{
				drawCommands.instanceSortingPositions[3 * i] = brgEye.x;
				drawCommands.instanceSortingPositions[3 * i + 1] = brgEye.y;
				drawCommands.instanceSortingPositions[3 * i + 2] = brgEye.z;
			}
			// The camera view reads the culled list where the visibility refresh keeps one, the shadow view the generated list.
			GraphicsBufferHandle visibleHandle = (view == 0 && brgCulled ? culledPackedBuffer : packedBuffer).bufferHandle;
			GraphicsBufferHandle argsHandle = argsBuffer.bufferHandle;
			foreach (int c in brgWanted)
			{
				if (c >= brgDraws.Length || brgDraws[c].View != view || (punctual && !brgDraws[c].Punctual))
				{
					continue;
				}
				BrgDraw d = brgDraws[c];
				int at = brgFilterNext[d.Filter]++;
				drawCommands.indirectDrawCommands[at] = new BatchDrawCommandIndirect
				{
					flags = BatchDrawCommandFlags.LODCrossFadeValuePacked | BatchDrawCommandFlags.HasSortingPosition,
					batchID = brgBatch,
					materialID = d.Material,
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

		/// <summary>Releases the batch group, its registrations and its buffers; with <paramref name="all"/> (the renderer's dispose) the stand-in word too.</summary>
		private void DisposeBrg(bool all = false)
		{
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
			packedBuffer?.Release();
			packedBuffer = null;
			culledPackedBuffer?.Release();
			culledPackedBuffer = null;
			if (all)
			{
				brgDummy?.Release();
				brgDummy = null;
			}
		}
	}
}
