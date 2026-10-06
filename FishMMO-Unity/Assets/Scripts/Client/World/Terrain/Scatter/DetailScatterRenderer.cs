using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU side of the detail scatter: per camera, the CPU picks the work items (8 × 8 blocks of 2 m) within each
	/// terrain's draw distance that the camera, or the sun's shadow of it, can see, and sizes every type's slots from the
	/// items' exact upper bounds; FishDetailScatter.compute turns the items into instances in per-(type, view) regions;
	/// and one <see cref="Graphics.RenderMeshIndirect"/> per type and view draws them through the material's
	/// procedural-instancing twin (FishIndirectInstancing.hlsl), the same twin the chunk renderer draws with. Everything
	/// the GPU does for a camera is recorded into one command buffer executed on that camera's context, as
	/// <see cref="GrassBladeRenderer"/> and <see cref="TerrainGpuRenderer"/> do. No readback in the frame.
	/// </summary>
	public sealed class DetailScatterRenderer : IDisposable
	{
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishDetailScatter.compute";

		/// <summary>Views per type: the camera (0) and the shadow casters (1).</summary>
		public const int Views = 2;

		[StructLayout(LayoutKind.Sequential)]
		private struct Item
		{
			public const int Stride = 16;
			public int BlockX, BlockZ;
			public uint Packed, Terrain;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct ModelData
		{
			public const int Stride = 32;
			/// <summary>The mesh's bounding sphere in its own space: centre, radius.</summary>
			public Vector4 Sphere;
			/// <summary>The compute's <c>extra</c>: x foot radius (m), y the salt (its raw bits, read with asuint), z casts shadows (0/1).</summary>
			public float FootRadius;
			public uint Salt;
			public float CastsShadows;
			public float Padding;
		}

		private struct Command
		{
			public int Slot;
			public Mesh Mesh;
			public RenderParams Params;
		}

		private struct GroupRange
		{
			public DetailScatterAtlas.Group Group;
			public int Offset, Count;
		}

		private static readonly int ItemsId = Shader.PropertyToID("_ScatterItems");
		private static readonly int TableId = Shader.PropertyToID("_ScatterTable");
		private static readonly int ModelsId = Shader.PropertyToID("_ScatterModels");
		private static readonly int SlotsId = Shader.PropertyToID("_ScatterSlots");
		private static readonly int CountsId = Shader.PropertyToID("_ScatterCounts");
		private static readonly int InstancesOutId = Shader.PropertyToID("_ScatterInstancesOut");
		private static readonly int VisibleOutId = Shader.PropertyToID("_ScatterVisibleOut");
		private static readonly int HeightsId = Shader.PropertyToID("_ScatterHeights");
		private static readonly int DensityId = Shader.PropertyToID("_ScatterDensity");
		private static readonly int CameraId = Shader.PropertyToID("_ScatterCamera");
		private static readonly int PlanesId = Shader.PropertyToID("_ScatterPlanes");
		private static readonly int LightId = Shader.PropertyToID("_ScatterLight");
		private static readonly int ThinId = Shader.PropertyToID("_ScatterThin");
		private static readonly int ItemOffsetId = Shader.PropertyToID("_ScatterItemOffset");
		private static readonly int ItemCountId = Shader.PropertyToID("_ScatterItemCount");
		private static readonly int SlotCountId = Shader.PropertyToID("_ScatterSlotCount");
		private static readonly int ArgCountId = Shader.PropertyToID("_ScatterArgCount");
		private static readonly int ArgSlotsId = Shader.PropertyToID("_ScatterArgSlots");
		private static readonly int ArgsId = Shader.PropertyToID("_ScatterArgs");
		private static readonly int ArgsFenceId = Shader.PropertyToID("_ScatterArgsFence");
		// The procedural-instancing twins' contract (FishIndirectInstancing.hlsl).
		private static readonly int InstancesId = Shader.PropertyToID("_FishInstances");
		private static readonly int VisibleId = Shader.PropertyToID("_FishVisibleInstances");
		private static readonly int CommandBasesId = Shader.PropertyToID(TerrainGpuRenderer.CommandBasesName);

		/// <summary>OpenGL only: the args fence (see FishDetailScatter.compute FishScatterArgsFence).</summary>
		private static bool NeedsArgsFence => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;

		private readonly ComputeShader compute;
		private readonly int clearKernel, generateKernel, finalizeKernel, argsFenceKernel;
		private readonly DetailScatterAtlas atlas = new DetailScatterAtlas();
		private readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO detail scatter" };
		private readonly Vector4[] planeVectors = new Vector4[6];
		private readonly List<GroupRange> ranges = new List<GroupRange>();
		private readonly List<Command> commands = new List<Command>();
		private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retired = new List<(GraphicsBuffer, int)>();
		private MaterialPropertyBlock[] blocks = Array.Empty<MaterialPropertyBlock>();
		private GraphicsBuffer itemBuffer, modelBuffer, slotBuffer, countBuffer, instanceBuffer, visibleBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer;
		private NativeArray<Item> items;
		private int itemCount;
		/// <summary>Per slot (type × 2 + view): the instances it holds, and its first entry in the output buffers.</summary>
		private int[] capacities = Array.Empty<int>(), bases = Array.Empty<int>();
		/// <summary>Per slot, the exact upper bound of this camera's work (the items it listed).</summary>
		private long[] need = Array.Empty<long>();
		private int slotCount;
		private int layoutTypes = -1;
		private bool layoutDirty = true;
		private bool capWarned;

		/// <summary>The last game camera's work items and its total upper bound (instances).</summary>
		public int LastItems { get; private set; }
		public long LastBound { get; private set; }

		/// <summary>Instances appended per slot, read back asynchronously in the editor (−1 before the first read).</summary>
		public int[] LastCounts { get; private set; } = Array.Empty<int>();

		public const int StatsInterval = 30;
		private int lastStatsFrame = -1000;
		private bool[] overflowWarned = Array.Empty<bool>();

		/// <summary>Bytes of GPU buffers held (the textures are the atlas's).</summary>
		public long GpuBytes
		{
			get
			{
				long n = 0;
				foreach (GraphicsBuffer b in new[] { itemBuffer, modelBuffer, slotBuffer, countBuffer, instanceBuffer, visibleBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer })
				{
					n += b != null ? (long)b.count * b.stride : 0;
				}
				return n;
			}
		}

		public int Capacity(int slot) => slot >= 0 && slot < capacities.Length ? capacities[slot] : 0;

		private DetailScatterRenderer(ComputeShader compute)
		{
			this.compute = compute;
			clearKernel = compute.FindKernel("FishScatterClear");
			generateKernel = compute.FindKernel("FishScatterGenerate");
			finalizeKernel = compute.FindKernel("FishScatterFinalize");
			argsFenceKernel = compute.FindKernel("FishScatterArgsFence");
		}

		/// <summary>The renderer, or null where it cannot run (no compute, too few vertex-stage buffers, the compute missing).</summary>
		public static DetailScatterRenderer TryCreate(out string reason)
		{
			reason = null;
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			ComputeShader cs = profile != null ? profile.DetailScatterCompute : null;
#if UNITY_EDITOR
			if (cs == null)
			{
				cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
			}
#endif
			if (!TerrainGpuMath.ChooseGpu(SystemInfo.supportsComputeShaders, SystemInfo.maxComputeBufferInputsVertex, cs != null))
			{
				reason = cs == null ? $"the compute ({ComputeAssetPath}) is not loaded: Weather Render Profile → GPU detail scatter" : "no compute shaders or too few vertex-stage buffers on this device (the chunk renderer draws every detail)";
				return null;
			}
			ComputeShader own = Object.Instantiate(cs);
			own.hideFlags = HideFlags.DontSave;
			own.name = cs.name + " (instance)";
			return new DetailScatterRenderer(own);
		}

		// ── Per camera ───────────────────────────────────────────────

		/// <summary>Lists the camera's work, records the generation into its context and issues the draws. Returns the draws.</summary>
		public int Execute(Camera camera, ScriptableRenderContext context, in TerrainTreeField.View view, IReadOnlyList<DetailScatterTerrain> terrains,
			IReadOnlyList<DetailScatterType> types, DetailScatterSettings s, Func<DetailScatterTerrain, float> distanceOf)
		{
			ReleaseRetired(false);
			if (types.Count == 0 || !atlas.Sync(terrains, distanceOf))
			{
				return 0;
			}
			EnsureSlots(types.Count);
			Array.Clear(need, 0, need.Length);
			itemCount = 0;
			ranges.Clear();
			Vector3 eye = view.Position;
			float reach = 0f;
			foreach (DetailScatterAtlas.Group group in atlas.Groups)
			{
				int groupStart = itemCount;
				foreach (DetailScatterTerrain st in group.Terrains)
				{
					if (st.Terrain == null || !st.Terrain.drawTreesAndFoliage)
					{
						continue;
					}
					float distance = distanceOf(st);
					if (distance <= 0f)
					{
						continue;
					}
					reach = Mathf.Max(reach, distance);
					ListItems(st, (uint)atlas.IndexOf(st), distance, in view, types);
				}
				if (itemCount > groupStart)
				{
					ranges.Add(new GroupRange { Group = group, Offset = groupStart, Count = itemCount - groupStart });
				}
			}
			if (camera.cameraType == CameraType.Game)
			{
				long bound = 0;
				foreach (long n in need)
				{
					bound += n;
				}
				LastItems = itemCount;
				LastBound = bound;
			}
			if (itemCount == 0)
			{
				return 0;
			}

			// Every slot at least this camera's exact upper bound (grow-only, doubling), never past the cap.
			int cap = Mathf.Max(1024, s.MaxInstancesPerSlot);
			for (int slot = 0; slot < slotCount; slot++)
			{
				if (need[slot] <= capacities[slot] || capacities[slot] >= cap)
				{
					continue;
				}
				if (need[slot] > cap && !capWarned)
				{
					capWarned = true;
					Debug.LogWarning($"[Detail scatter] '{types[slot / Views].Name}' {(slot % Views == 0 ? "camera" : "shadow")} slot needs up to {need[slot]} instances, over the cap of {cap} (GPU detail scatter > Max Instances Per Slot): the instances past it are dropped.");
				}
				capacities[slot] = (int)Math.Min(cap, Math.Max(need[slot], Math.Max(2L * capacities[slot], 1024L)));
				layoutDirty = true;
			}
			if (layoutDirty || layoutTypes != types.Count)
			{
				Relayout(types);
			}
			if (itemBuffer == null || itemBuffer.count < itemCount)
			{
				Retire(itemBuffer);
				itemBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(itemCount, items.Length), Item.Stride);
			}
			for (int i = 0; i < 6; i++)
			{
				Plane p = view.Planes[i];
				planeVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
			}

			double recordStart = TerrainInstancingProbe.Now;
			cmd.Clear();
			cmd.SetBufferData(itemBuffer, items, 0, 0, itemCount);
			cmd.SetComputeVectorParam(compute, CameraId, eye);
			cmd.SetComputeVectorArrayParam(compute, PlanesId, planeVectors);
			cmd.SetComputeVectorParam(compute, LightId, view.Shadows ? new Vector4(view.LightDirection.x, view.LightDirection.y, view.LightDirection.z, view.ShadowDistance) : Vector4.zero);
			cmd.SetComputeVectorParam(compute, ThinId, new Vector4(Mathf.Max(0f, s.ThinStartMetres), Mathf.Clamp(s.ThinKeepAtDistance, 0.02f, 1f), Mathf.Clamp(s.EdgeFadeBand, 0.02f, 0.5f), 0f));
			cmd.SetComputeIntParam(compute, SlotCountId, slotCount);
			cmd.SetComputeIntParam(compute, ArgCountId, commands.Count);

			cmd.SetComputeBufferParam(compute, clearKernel, CountsId, countBuffer);
			cmd.DispatchCompute(compute, clearKernel, (slotCount + 63) / 64, 1, 1);

			cmd.SetComputeBufferParam(compute, generateKernel, ItemsId, itemBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, TableId, atlas.Table);
			cmd.SetComputeBufferParam(compute, generateKernel, ModelsId, modelBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, SlotsId, slotBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, CountsId, countBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, InstancesOutId, instanceBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, VisibleOutId, visibleBuffer);
			foreach (GroupRange range in ranges)
			{
				cmd.SetComputeTextureParam(compute, generateKernel, HeightsId, range.Group.Heights);
				cmd.SetComputeTextureParam(compute, generateKernel, DensityId, range.Group.Density);
				cmd.SetComputeIntParam(compute, ItemOffsetId, range.Offset);
				cmd.SetComputeIntParam(compute, ItemCountId, range.Count);
				cmd.DispatchCompute(compute, generateKernel, Mathf.Min(range.Count, 65535), (range.Count + 65534) / 65535, 1);
			}

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
			// Always read in the editor: an overflowing slot is otherwise invisible but for its symptom.
			if (Application.isEditor && camera.cameraType == CameraType.Game && Time.frameCount - lastStatsFrame >= StatsInterval)
			{
				lastStatsFrame = Time.frameCount;
				RequestStats(types);
			}
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			TerrainInstancingProbe.RecordMs += TerrainInstancingProbe.Now - recordStart;

			double drawStart = TerrainInstancingProbe.Now;
			var worldBounds = new Bounds(eye, Vector3.one * (2f * reach + 20f));
			int draws = 0;
			for (int c = 0; c < commands.Count; c++)
			{
				Command command = commands[c];
				if (need[command.Slot] <= 0)
				{
					continue;
				}
				RenderParams rp = command.Params;
				rp.camera = camera;
				rp.worldBounds = worldBounds;
				rp.matProps = blocks[c];
				Graphics.RenderMeshIndirect(rp, command.Mesh, argsBuffer, 1, c);
				draws++;
			}
			TerrainInstancingProbe.DrawMs += TerrainInstancingProbe.Now - drawStart;
			return draws;
		}

		/// <summary>Lists one terrain's work items within <paramref name="distance"/> that the view (or the sun's shadow of it) can see, adding their bounds to the slots' needs.</summary>
		private void ListItems(DetailScatterTerrain st, uint terrainIndex, float distance, in TerrainTreeField.View view, IReadOnlyList<DetailScatterType> types)
		{
			Vector3 eye = view.Position;
			float itemX = DetailScatterMath.ItemSide * st.BlockX, itemZ = DetailScatterMath.ItemSide * st.BlockZ;
			int side = st.ItemsSide;
			int ix0 = Mathf.Max(0, Mathf.FloorToInt((eye.x - distance - st.Origin.x) / itemX)), ix1 = Mathf.Min(side - 1, Mathf.FloorToInt((eye.x + distance - st.Origin.x) / itemX));
			int iz0 = Mathf.Max(0, Mathf.FloorToInt((eye.z - distance - st.Origin.z) / itemZ)), iz1 = Mathf.Min(side - 1, Mathf.FloorToInt((eye.z + distance - st.Origin.z) / itemZ));
			float pad = st.MaxInstanceReach + 0.5f;
			for (int iz = iz0; iz <= iz1; iz++)
			{
				for (int ix = ix0; ix <= ix1; ix++)
				{
					float x0 = st.Origin.x + ix * itemX, x1 = Mathf.Min(x0 + itemX, st.Origin.x + st.Size.x);
					float z0 = st.Origin.z + iz * itemZ, z1 = Mathf.Min(z0 + itemZ, st.Origin.z + st.Size.z);
					int boundsAt = (iz * side + ix) * DetailScatterMath.MaxChannels;
					bool anything = false;
					for (int c = 0; c < st.Channels && !anything; c++)
					{
						anything = st.ItemBounds[boundsAt + c] > 0;
					}
					if (!anything)
					{
						continue;
					}
					Vector2 heights = st.ItemHeightRange(ix, iz);
					var bounds = new Bounds();
					bounds.SetMinMax(new Vector3(x0 - pad, heights.x - 0.5f, z0 - pad), new Vector3(x1 + pad, heights.y + st.MaxInstanceHeight + 0.5f, z1 + pad));
					float near = TerrainTreeMath.MinDistance(bounds, eye);
					if (near > distance)
					{
						continue;
					}
					bool main = TerrainTreeMath.IntersectsFrustum(view.Planes, bounds);
					bool shadow = view.Shadows && st.CastsShadows && near <= view.ShadowDistance
						&& TerrainTreeMath.IntersectsFrustum(view.Planes, TerrainTreeMath.ShadowSweep(bounds, view.LightDirection, view.ShadowDistance));
					if (!main && !shadow)
					{
						continue;
					}
					for (int c = 0; c < st.Channels; c++)
					{
						int bound = st.ItemBounds[boundsAt + c];
						int type = st.ChannelSettings[c].Type;
						if (bound <= 0 || type < 0 || type >= types.Count)
						{
							continue;
						}
						if (main)
						{
							need[type * Views] += bound;
						}
						if (shadow && types[type].CastsShadows)
						{
							need[type * Views + 1] += bound;
						}
					}
					int nx = Mathf.Min(DetailScatterMath.ItemSide, st.Blocks - ix * DetailScatterMath.ItemSide);
					int nz = Mathf.Min(DetailScatterMath.ItemSide, st.Blocks - iz * DetailScatterMath.ItemSide);
					EnsureItems(itemCount + 1);
					items[itemCount++] = new Item
					{
						BlockX = ix * DetailScatterMath.ItemSide,
						BlockZ = iz * DetailScatterMath.ItemSide,
						Packed = (main ? 1u : 0u) | (shadow ? 2u : 0u) | (uint)nx << 2 | (uint)nz << 6,
						Terrain = terrainIndex,
					};
				}
			}
		}

		// ── Layout ────────────────────────────────────────────────────

		/// <summary>Grows the per-slot arrays to the types' count (a new type starts with no capacity).</summary>
		private void EnsureSlots(int typeCount)
		{
			int wanted = typeCount * Views;
			if (slotCount == wanted)
			{
				return;
			}
			Array.Resize(ref capacities, wanted);
			Array.Resize(ref bases, wanted);
			Array.Resize(ref need, wanted);
			Array.Resize(ref overflowWarned, wanted);
			slotCount = wanted;
			layoutDirty = true;
		}

		/// <summary>Lays the slots out in the output buffers and rebuilds the commands, the models and the bindings.</summary>
		private void Relayout(IReadOnlyList<DetailScatterType> types)
		{
			layoutDirty = false;
			layoutTypes = types.Count;
			int total = 0;
			for (int slot = 0; slot < slotCount; slot++)
			{
				bases[slot] = total;
				total += capacities[slot];
			}
			var slots = new Vector2Int[Mathf.Max(1, slotCount)];
			for (int slot = 0; slot < slotCount; slot++)
			{
				slots[slot] = new Vector2Int(bases[slot], capacities[slot]);
			}
			Recreate(ref slotBuffer, GraphicsBuffer.Target.Structured, slots.Length, 8);
			slotBuffer.SetData(slots);
			Recreate(ref countBuffer, GraphicsBuffer.Target.Raw, Mathf.Max(1, slotCount), 4);
			if (instanceBuffer == null || instanceBuffer.count < total)
			{
				// Headroom, so the next few growths do not reallocate at once.
				int count = Mathf.Max(1024, total + total / 4);
				Retire(instanceBuffer);
				Retire(visibleBuffer);
				instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, FishInstance.Stride) { name = "Detail scatter instances" };
				visibleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, FishVisible.Stride) { name = "Detail scatter visible" };
			}

			var models = new ModelData[Mathf.Max(1, types.Count)];
			commands.Clear();
			var args = new List<GraphicsBuffer.IndirectDrawIndexedArgs>();
			var argSlots = new List<uint>();
			var commandBases = new List<uint>();
			for (int t = 0; t < types.Count; t++)
			{
				DetailScatterType type = types[t];
				Bounds b = type.MeshBounds;
				models[t] = new ModelData
				{
					Sphere = new Vector4(b.center.x, b.center.y, b.center.z, b.extents.magnitude),
					FootRadius = type.FootRadius,
					Salt = type.Salt,
					CastsShadows = type.CastsShadows ? 1f : 0f,
				};
				Material clone = CloneOf(type.Material);
				if (clone == null)
				{
					continue;
				}
				for (int v = 0; v < Views; v++)
				{
					if (v == 1 && !type.CastsShadows)
					{
						continue;
					}
					int slot = t * Views + v;
					RenderParams rp = type.Template;
					rp.material = clone;
					rp.shadowCastingMode = v == 0 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly;
					commands.Add(new Command { Slot = slot, Mesh = type.Mesh, Params = rp });
					args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
					{
						indexCountPerInstance = type.Mesh.GetIndexCount(0),
						startIndex = type.Mesh.GetIndexStart(0),
						baseVertexIndex = (uint)type.Mesh.GetBaseVertex(0),
						instanceCount = 0,
						startInstance = 0,
					});
					argSlots.Add((uint)slot);
					commandBases.Add((uint)bases[slot]);
				}
			}
			Recreate(ref modelBuffer, GraphicsBuffer.Target.Structured, models.Length, ModelData.Stride);
			modelBuffer.SetData(models);
			Recreate(ref argsBuffer, GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, Mathf.Max(1, args.Count), GraphicsBuffer.IndirectDrawIndexedArgs.size);
			if (args.Count > 0)
			{
				argsBuffer.SetData(args);
			}
			Recreate(ref argSlotBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, argSlots.Count), 4);
			Recreate(ref commandBaseBuffer, GraphicsBuffer.Target.Structured, Mathf.Max(1, commandBases.Count), 4);
			if (argSlots.Count > 0)
			{
				argSlotBuffer.SetData(argSlots);
				commandBaseBuffer.SetData(commandBases);
			}
			if (blocks.Length < commands.Count)
			{
				int old = blocks.Length;
				Array.Resize(ref blocks, commands.Count);
				for (int c = old; c < commands.Count; c++)
				{
					blocks[c] = new MaterialPropertyBlock();
				}
			}
			Bind();
		}

		/// <summary>The material's procedural-instancing twin, cloned once per source material (its buffers are this renderer's).</summary>
		private Material CloneOf(Material material)
		{
			if (material == null)
			{
				return null;
			}
			if (clones.TryGetValue(material, out Material clone))
			{
				return clone;
			}
			Shader shader = TerrainGpuRenderer.IndirectShaderFor(material);
			if (shader == null)
			{
				return null;
			}
			clone = new Material(material) { shader = shader, name = material.name + " (scatter)", hideFlags = HideFlags.DontSave };
			clones.Add(material, clone);
			return clone;
		}

		/// <summary>Binds the output and command-base buffers on every clone material and every command's block.</summary>
		private void Bind()
		{
			foreach (Material clone in clones.Values)
			{
				if (clone != null)
				{
					clone.SetBuffer(InstancesId, instanceBuffer);
					clone.SetBuffer(VisibleId, visibleBuffer);
					clone.SetBuffer(CommandBasesId, commandBaseBuffer);
				}
			}
			for (int c = 0; c < commands.Count; c++)
			{
				blocks[c].SetBuffer(InstancesId, instanceBuffer);
				blocks[c].SetBuffer(VisibleId, visibleBuffer);
				blocks[c].SetBuffer(CommandBasesId, commandBaseBuffer);
			}
		}

		private void RequestStats(IReadOnlyList<DetailScatterType> types)
		{
			int slotsNow = slotCount;
			int[] capacitiesNow = (int[])capacities.Clone();
			cmd.RequestAsyncReadback(countBuffer, request =>
			{
				if (request.hasError)
				{
					return;
				}
				NativeArray<uint> counts = request.GetData<uint>();
				int n = Mathf.Min(slotsNow, counts.Length);
				var last = new int[n];
				for (int i = 0; i < n; i++)
				{
					last[i] = (int)counts[i];
					if (counts[i] > capacitiesNow[i] && i < overflowWarned.Length && !overflowWarned[i] && i / Views < types.Count)
					{
						overflowWarned[i] = true;
						Debug.LogWarning($"[Detail scatter] '{types[i / Views].Name}' {(i % Views == 0 ? "camera" : "shadow")} slot overflowed: {counts[i]} appended, capacity {capacitiesNow[i]}; the rest were dropped (GPU detail scatter > Max Instances Per Slot).");
					}
				}
				LastCounts = last;
			});
		}

		private void EnsureItems(int needed)
		{
			if (items.IsCreated && items.Length >= needed)
			{
				return;
			}
			var grown = new NativeArray<Item>(Mathf.Max(needed, items.IsCreated ? items.Length * 2 : 1024), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			if (items.IsCreated)
			{
				NativeArray<Item>.Copy(items, grown, itemCount);
				items.Dispose();
			}
			items = grown;
		}

		private void Recreate(ref GraphicsBuffer buffer, GraphicsBuffer.Target target, int count, int stride)
		{
			if (buffer != null && buffer.count == count && buffer.stride == stride)
			{
				return;
			}
			Retire(buffer);
			buffer = new GraphicsBuffer(target, count, stride);
		}

		/// <summary>A buffer a frame in flight may still read: released a few frames later.</summary>
		private void Retire(GraphicsBuffer buffer)
		{
			if (buffer != null)
			{
				retired.Add((buffer, Time.frameCount));
			}
		}

		private void ReleaseRetired(bool all)
		{
			for (int i = retired.Count - 1; i >= 0; i--)
			{
				if (all || Time.frameCount - retired[i].Frame >= 4)
				{
					retired[i].Buffer.Release();
					retired.RemoveAt(i);
				}
			}
		}

		public void Dispose()
		{
			cmd.Release();
			ReleaseRetired(true);
			foreach (GraphicsBuffer b in new[] { itemBuffer, modelBuffer, slotBuffer, countBuffer, instanceBuffer, visibleBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer })
			{
				b?.Release();
			}
			itemBuffer = modelBuffer = slotBuffer = countBuffer = instanceBuffer = visibleBuffer = argsBuffer = argSlotBuffer = commandBaseBuffer = null;
			if (items.IsCreated)
			{
				items.Dispose();
			}
			if (compute != null)
			{
				Object.Destroy(compute);
			}
			foreach (Material clone in clones.Values)
			{
				if (clone != null)
				{
					Object.Destroy(clone);
				}
			}
			clones.Clear();
			commands.Clear();
			atlas.Dispose();
		}
	}
}
