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

		/// <summary>Levels of detail per type (most types draw only level 0; a bush draws three).</summary>
		public const int Levels = DetailScatterSettings.MaxLevels;

		/// <summary>Slots per type: one per level and view.</summary>
		public const int SlotsPerType = Levels * Views;

		/// <summary>The slot of a type's level in a view (FishDetailScatter.compute ScatterSlot).</summary>
		public static int SlotOf(int type, int level, int view) => (type * Levels + level) * Views + view;

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
			public const int Stride = 48;
			/// <summary>The mesh's bounding sphere in its own space: centre, radius.</summary>
			public Vector4 Sphere;
			/// <summary>The compute's <c>extra</c>: x foot radius (m), y the salt (its raw bits, read with asuint), z casts shadows (0/1).</summary>
			public float FootRadius;
			public uint Salt;
			public float CastsShadows;
			public float Padding;
			/// <summary>The compute's <c>lod</c>: x levels (1..3), y bush (0/1), z the mesh's top at scale 1 (m).</summary>
			public Vector4 Lod;
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
		private static readonly int BushLodId = Shader.PropertyToID("_ScatterBushLod");
		private static readonly int BushReachId = Shader.PropertyToID("_ScatterBushReach");
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
		private const string GpuSample = "FishMMO detail scatter (compute)";
		private readonly Vector4[] planeVectors = new Vector4[6];
		private readonly List<GroupRange> ranges = new List<GroupRange>();
		private readonly List<Command> commands = new List<Command>();
		private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retired = new List<(GraphicsBuffer, int)>();
		private MaterialPropertyBlock[] blocks = Array.Empty<MaterialPropertyBlock>();
		private GraphicsBuffer itemBuffer, modelBuffer, slotBuffer, countBuffer, instanceBuffer, visibleBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer;
		private NativeArray<Item> items;
		private int itemCount;
		/// <summary>Per slot (<see cref="SlotOf"/>): the instances it holds, and its first entry in the output buffers.</summary>
		private int[] capacities = Array.Empty<int>(), bases = Array.Empty<int>();
		/// <summary>Per slot, the exact upper bound of this camera's work (the items it listed).</summary>
		private long[] need = Array.Empty<long>();
		private int slotCount;
		private int layoutTypes = -1;
		private bool layoutDirty = true;
		private bool capWarned;

		/// <summary>
		/// How far a camera may move, metres, and turn, degrees, before its instances are generated again. Between, the last
		/// generation is drawn as it stands: nothing in it moves with time (the wind is the vertex stage's), so a camera
		/// standing still or creeping regenerated the same instances every frame — about 0.8 ms of GPU and 0.4 ms of the main
		/// thread at Flo Monolith's meadow (ScenePerfProbe, 2026-10-08). Generated against a frustum widened by what that
		/// move and turn sweep at the farthest reach, so nothing the camera can turn to is missing. The move is kept short,
		/// shorter than the grass's and the props' (RegatherMetres 1.5), because unlike theirs this reuse skips the per-instance
		/// work too: the distance fades, the thinning and a bush's level are as from where the camera stood, and a shorter
		/// step keeps each refresh's change in them below anything seen.
		/// </summary>
		public const float RegatherMetres = 0.5f, RegatherDegrees = 3f;

		/// <summary>Debug and profiling: generate on every render, as before the reuse.</summary>
		public static bool AlwaysRegenerate;

		private Camera generatedFor;
		private Vector3 generatedEye, generatedForward, generatedLight;
		private float generatedFov, generatedAspect, generatedShadow, generatedReach;
		private int generatedAtlas = -1, generatedTypes = -1, generatedSettings, generatedDraws;
		private readonly Plane[] widened = new Plane[6];

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
			// Its pebbles draw through the rock shader, which declares the crevice inputs: bound before the first draw.
			RockContactField.EnsureBindings();
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
				generatedFor = null;
				return 0;
			}
			int settings = SettingsSignature(s);
			if (StillGenerated(camera, in view, types.Count, settings))
			{
				// The buffers still hold this camera's instances (no other camera has generated since): only the draws.
				return generatedDraws > 0 ? Draw(camera, generatedEye, generatedReach) : 0;
			}
			generatedFor = null;
			EnsureSlots(types.Count);
			Array.Clear(need, 0, need.Length);
			itemCount = 0;
			ranges.Clear();
			Vector3 eye = view.Position;
			float reach = 0f;
			float bushReach = Mathf.Max(s.BushDrawMin, s.BushDrawMax);
			foreach (DetailScatterAtlas.Group group in atlas.Groups)
			{
				foreach (DetailScatterTerrain st in group.Terrains)
				{
					float distance = st.Terrain != null && st.Terrain.drawTreesAndFoliage ? distanceOf(st) : 0f;
					if (distance > 0f)
					{
						reach = Mathf.Max(reach, HasBush(st, types) ? Mathf.Max(distance, bushReach) : distance);
					}
				}
			}
			// Listed and generated against the frustum widened by what the reuse's move and turn sweep at the farthest
			// reach (RegatherMetres): the blocks just off screen are there when the camera turns to them.
			float margin = RegatherMetres + reach * RegatherDegrees * Mathf.Deg2Rad;
			for (int i = 0; i < 6; i++)
			{
				widened[i] = new Plane(view.Planes[i].normal, view.Planes[i].distance + margin);
			}
			TerrainTreeField.View listView = view;
			listView.Planes = widened;
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
					ListItems(st, (uint)atlas.IndexOf(st), distance, in listView, types, s);
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
				Remember(camera, in view, types.Count, settings, reach, 0);
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
					Debug.LogWarning($"[Detail scatter] '{types[slot / SlotsPerType].Name}' level {slot % SlotsPerType / Views} {(slot % Views == 0 ? "camera" : "shadow")} slot needs up to {need[slot]} instances, over the cap of {cap} (GPU detail scatter > Max Instances Per Slot): the instances past it are dropped.");
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
				Plane p = widened[i];
				planeVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
			}

			double recordStart = TerrainInstancingProbe.Now;
			cmd.Clear();
			// Named on the GPU, so the profiler (and ScenePerfProbe's GPU table) can price the scatter's compute.
			cmd.BeginSample(GpuSample);
			cmd.SetBufferData(itemBuffer, items, 0, 0, itemCount);
			cmd.SetComputeVectorParam(compute, CameraId, eye);
			cmd.SetComputeVectorArrayParam(compute, PlanesId, planeVectors);
			cmd.SetComputeVectorParam(compute, LightId, view.Shadows ? new Vector4(view.LightDirection.x, view.LightDirection.y, view.LightDirection.z, view.ShadowDistance) : Vector4.zero);
			// w: the farthest any bush reaches, for the compute's per-block early out (a terrain's own distance is the detail distance).
			cmd.SetComputeVectorParam(compute, ThinId, new Vector4(Mathf.Max(0f, s.ThinStartMetres), Mathf.Clamp(s.ThinKeepAtDistance, 0.02f, 1f), Mathf.Clamp(s.EdgeFadeBand, 0.02f, 0.5f), bushReach));
			cmd.SetComputeVectorParam(compute, BushLodId, new Vector4(Mathf.Max(0f, s.BushLodMetresPerMetre.x), Mathf.Max(0f, s.BushLodMetresPerMetre.y), Mathf.Clamp(s.BushLodFadeBand, 0.02f, 0.5f), 0f));
			cmd.SetComputeVectorParam(compute, BushReachId, new Vector4(Mathf.Max(1f, s.BushDrawMetresPerMetre), Mathf.Max(10f, s.BushDrawMin), bushReach, 0f));
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
			// The scene's baked paths: nothing stands on a worn way (FishGroundPaths.hlsl).
			FishMMO.Shared.ScenePathSurfaceBinder.BindCompute(cmd, compute, generateKernel);
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
			cmd.EndSample(GpuSample);
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			TerrainInstancingProbe.RecordMs += TerrainInstancingProbe.Now - recordStart;

			int draws = Draw(camera, eye, reach);
			Remember(camera, in view, types.Count, settings, reach, draws);
			return draws;
		}

		/// <summary>Issues the indirect draws of every slot this camera's generation filled. Returns the draws.</summary>
		private int Draw(Camera camera, Vector3 eye, float reach)
		{
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

		/// <summary>Whether the last generation still serves this camera (see <see cref="RegatherMetres"/>).</summary>
		private bool StillGenerated(Camera camera, in TerrainTreeField.View view, int typeCount, int settings)
		{
			if (AlwaysRegenerate || generatedFor != camera || generatedAtlas != atlas.Version || generatedTypes != typeCount || generatedSettings != settings
				|| layoutDirty || generatedShadow != (view.Shadows ? view.ShadowDistance : 0f) || generatedFov != camera.fieldOfView || generatedAspect != camera.aspect)
			{
				return false;
			}
			if ((view.Position - generatedEye).sqrMagnitude > RegatherMetres * RegatherMetres)
			{
				return false;
			}
			float turn = Mathf.Cos(RegatherDegrees * Mathf.Deg2Rad);
			return Vector3.Dot(camera.transform.forward, generatedForward) >= turn
				&& (!view.Shadows || Vector3.Dot(view.LightDirection, generatedLight) >= turn);
		}

		private void Remember(Camera camera, in TerrainTreeField.View view, int typeCount, int settings, float reach, int draws)
		{
			generatedFor = camera;
			generatedAtlas = atlas.Version;
			generatedTypes = typeCount;
			generatedSettings = settings;
			generatedEye = view.Position;
			generatedForward = camera.transform.forward;
			generatedLight = view.LightDirection;
			generatedShadow = view.Shadows ? view.ShadowDistance : 0f;
			generatedFov = camera.fieldOfView;
			generatedAspect = camera.aspect;
			generatedReach = reach;
			generatedDraws = draws;
		}

		/// <summary>Every setting the generation reads, folded together, so an edit in the profile regenerates at once.</summary>
		private static int SettingsSignature(DetailScatterSettings s)
		{
			unchecked
			{
				int h = s.ThinStartMetres.GetHashCode();
				h = h * 31 + s.ThinKeepAtDistance.GetHashCode();
				h = h * 31 + s.EdgeFadeBand.GetHashCode();
				h = h * 31 + s.BushDrawMetresPerMetre.GetHashCode();
				h = h * 31 + s.BushDrawMin.GetHashCode();
				h = h * 31 + s.BushDrawMax.GetHashCode();
				h = h * 31 + s.BushLodMetresPerMetre.GetHashCode();
				h = h * 31 + s.BushLodFadeBand.GetHashCode();
				h = h * 31 + s.MaxInstancesPerSlot;
				return h;
			}
		}

		/// <summary>True when any of a terrain's channels is a bush.</summary>
		private static bool HasBush(DetailScatterTerrain st, IReadOnlyList<DetailScatterType> types)
		{
			for (int c = 0; c < st.Channels; c++)
			{
				int type = st.ChannelSettings[c].Type;
				if (type >= 0 && type < types.Count && types[type].Bush)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The farthest point of a box from a point.</summary>
		private static float MaxDistance(Bounds b, Vector3 p)
		{
			Vector3 d = Vector3.Max(b.max - p, p - b.min);
			return d.magnitude;
		}

		/// <summary>
		/// Lists one terrain's work items within <paramref name="distance"/> (the detail distance; a bush's own reach for
		/// its channels) that the view (or the sun's shadow of it) can see, adding their bounds to the slots' needs: to
		/// level 0 for most types, to every level a bush's instances there can draw (<see cref="DetailScatterMath.LevelsReached"/>).
		/// </summary>
		private void ListItems(DetailScatterTerrain st, uint terrainIndex, float distance, in TerrainTreeField.View view, IReadOnlyList<DetailScatterType> types, DetailScatterSettings s)
		{
			Vector3 eye = view.Position;
			bool bushes = HasBush(st, types);
			float listed = bushes ? Mathf.Max(distance, Mathf.Max(s.BushDrawMin, s.BushDrawMax)) : distance;
			float itemX = DetailScatterMath.ItemSide * st.BlockX, itemZ = DetailScatterMath.ItemSide * st.BlockZ;
			int side = st.ItemsSide;
			int ix0 = Mathf.Max(0, Mathf.FloorToInt((eye.x - listed - st.Origin.x) / itemX)), ix1 = Mathf.Min(side - 1, Mathf.FloorToInt((eye.x + listed - st.Origin.x) / itemX));
			int iz0 = Mathf.Max(0, Mathf.FloorToInt((eye.z - listed - st.Origin.z) / itemZ)), iz1 = Mathf.Min(side - 1, Mathf.FloorToInt((eye.z + listed - st.Origin.z) / itemZ));
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
					if (near > listed)
					{
						continue;
					}
					float far = MaxDistance(bounds, eye);
					bool main = TerrainTreeMath.IntersectsFrustum(view.Planes, bounds);
					bool shadow = view.Shadows && st.CastsShadows && near <= view.ShadowDistance
						&& TerrainTreeMath.IntersectsFrustum(view.Planes, TerrainTreeMath.ShadowSweep(bounds, view.LightDirection, view.ShadowDistance));
					if (!main && !shadow)
					{
						continue;
					}
					bool any = false;
					for (int c = 0; c < st.Channels; c++)
					{
						int bound = st.ItemBounds[boundsAt + c];
						int type = st.ChannelSettings[c].Type;
						if (bound <= 0 || type < 0 || type >= types.Count)
						{
							continue;
						}
						DetailScatterType t = types[type];
						int levels = 1;
						if (t.Bush)
						{
							ref DetailScatterTerrain.Channel ch = ref st.ChannelSettings[c];
							float tallest = t.MeshTop * Mathf.Max(ch.MinHeight, ch.MaxHeight);
							if (near > s.BushDrawDistance(tallest))
							{
								continue;
							}
							levels = DetailScatterMath.LevelsReached(near, far, t.MeshTop * Mathf.Min(ch.MinHeight, ch.MaxHeight), tallest, t.Levels.Length, s.BushLodMetresPerMetre, s.BushLodFadeBand);
						}
						else if (near > distance)
						{
							continue;
						}
						any = true;
						for (int level = 0; level < Levels; level++)
						{
							if ((levels & (1 << level)) == 0)
							{
								continue;
							}
							if (main)
							{
								need[SlotOf(type, level, 0)] += bound;
							}
							if (shadow && t.CastsShadows)
							{
								need[SlotOf(type, level, 1)] += bound;
							}
						}
					}
					if (!any)
					{
						continue;
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
			int wanted = typeCount * SlotsPerType;
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
					Lod = new Vector4(type.Levels.Length, type.Bush ? 1f : 0f, type.MeshTop, 0f),
				};
				Material clone = CloneOf(type.Material);
				if (clone == null)
				{
					continue;
				}
				for (int level = 0; level < type.Levels.Length && level < Levels; level++)
				{
					Mesh mesh = type.Levels[level];
					for (int v = 0; v < Views; v++)
					{
						if (v == 1 && !type.CastsShadows)
						{
							continue;
						}
						int slot = SlotOf(t, level, v);
						RenderParams rp = type.Template;
						rp.material = clone;
						rp.shadowCastingMode = v == 0 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly;
						commands.Add(new Command { Slot = slot, Mesh = mesh, Params = rp });
						args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
						{
							indexCountPerInstance = mesh.GetIndexCount(0),
							startIndex = mesh.GetIndexStart(0),
							baseVertexIndex = (uint)mesh.GetBaseVertex(0),
							instanceCount = 0,
							startInstance = 0,
						});
						argSlots.Add((uint)slot);
						commandBases.Add((uint)bases[slot]);
					}
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
					if (counts[i] > capacitiesNow[i] && i < overflowWarned.Length && !overflowWarned[i] && i / SlotsPerType < types.Count)
					{
						overflowWarned[i] = true;
						Debug.LogWarning($"[Detail scatter] '{types[i / SlotsPerType].Name}' level {i % SlotsPerType / Views} {(i % Views == 0 ? "camera" : "shadow")} slot overflowed: {counts[i]} appended, capacity {capacitiesNow[i]}; the rest were dropped (GPU detail scatter > Max Instances Per Slot).");
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
