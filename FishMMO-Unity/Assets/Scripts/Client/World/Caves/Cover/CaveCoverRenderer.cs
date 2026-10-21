using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FishMMO.Shared;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU side of the cave cover (2026-10-20): per camera, the CPU lists the cave pieces (32 m cubes of a cave's rock
	/// surface) within the cover's reach that the camera can see, nearest first; FishCaveCover.compute walks each piece's
	/// triangles straight from the mesh's own GPU buffers and appends the instances into per-(type, level) slots; and one
	/// <see cref="Graphics.RenderMeshIndirect"/> per slot draws them from the cover art's one mesh through FishMMO/Cave Cover.
	/// Everything the GPU does for a camera is recorded into one command buffer executed on that camera's context, as
	/// <see cref="DetailScatterRenderer"/> does. Nothing per instance on the CPU; the slots' counts are read back
	/// asynchronously a few times a second to grow a slot that overflowed.
	/// </summary>
	public sealed class CaveCoverRenderer : IDisposable
	{
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishCaveCover.compute";
		public const string ShaderAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishCaveCover.shader";
		/// <summary>The art generator's payload mesh (ProceduralArtCatalogue.CaveCoverMeshPath).</summary>
		public const string MeshAssetPath = "Assets/Prefabs/Shared/Biomes/Generated/Payload/Meshes/CaveCover_Set.asset";

		public const int Levels = CaveCoverTypes.Levels;
		public const int Types = CaveCoverTypes.Count;
		public const int SlotCount = Types * Levels;
		/// <summary>The most caves whose palettes the shader holds at once.</summary>
		public const int MaxCaves = 64;

		/// <summary>The slot of a type at a level (FishCaveCover.compute: t * 3 + level).</summary>
		public static int SlotOf(int type, int level) => type * Levels + level;

		/// <summary>One piece of a cave as the compute reads it (FishCaveCover.compute CoverPiece): 128 bytes.</summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct PieceData
		{
			public const int Stride = 128;
			public Vector4 Row0, Row1, Row2;
			public uint PositionOffset, NormalOffset, ColourOffset, Uv3Offset;
			public uint PositionFormat, NormalFormat, ColourFormat, Uv3Format;
			public uint VertexStride, Triangles, Wide, FirstIndex;
			public uint BaseVertex, CaveSlot, TypeMask, Unused;
			public float Density, Glow, Pad0, Pad1;
		}

		/// <summary>One cover type as the compute reads it (FishCaveCover.compute CoverType): 80 bytes.</summary>
		[StructLayout(LayoutKind.Sequential)]
		private struct TypeData
		{
			public const int Stride = 80;
			public Vector4 ClassDensity, Gain, Rich, Shape;
			public uint Mount, Salt, Unused0, Unused1;
		}

		/// <summary>A piece the system has taken on: its mesh's buffers bound as raw bytes and its table row (but the cave's slot).</summary>
		public sealed class Piece
		{
			public int Id;
			public Bounds Bounds;
			public GraphicsBuffer Vertices, Indices;
			public PieceData Data;
			public int Layer;
		}

		private static readonly int PiecesId = Shader.PropertyToID("_CoverPieces");
		private static readonly int TypesId = Shader.PropertyToID("_CoverTypes");
		private static readonly int SlotsId = Shader.PropertyToID("_CoverSlots");
		private static readonly int CountsId = Shader.PropertyToID("_CoverCounts");
		private static readonly int InstancesOutId = Shader.PropertyToID("_CoverInstancesOut");
		private static readonly int VerticesId = Shader.PropertyToID("_CoverVertices");
		private static readonly int IndicesId = Shader.PropertyToID("_CoverIndices");
		private static readonly int CameraId = Shader.PropertyToID("_CoverCamera");
		private static readonly int PlanesId = Shader.PropertyToID("_CoverPlanes");
		private static readonly int RingsId = Shader.PropertyToID("_CoverRings");
		private static readonly int LevelsId = Shader.PropertyToID("_CoverLevels");
		private static readonly int PieceId = Shader.PropertyToID("_CoverPiece");
		private static readonly int SlotCountId = Shader.PropertyToID("_CoverSlotCount");
		private static readonly int ArgCountId = Shader.PropertyToID("_CoverArgCount");
		private static readonly int ArgSlotsId = Shader.PropertyToID("_CoverArgSlots");
		private static readonly int ArgsId = Shader.PropertyToID("_CoverArgs");
		private static readonly int ArgsFenceId = Shader.PropertyToID("_CoverArgsFence");
		private static readonly int InstancesId = Shader.PropertyToID("_CoverInstances");
		private static readonly int CommandBasesId = Shader.PropertyToID("_CoverCommandBases");
		private static readonly int PalettesId = Shader.PropertyToID("_CoverPalettes");
		private static readonly int LooksId = Shader.PropertyToID("_CoverLooks");
		private static readonly int GlowId = Shader.PropertyToID("_CoverGlow");
		private static readonly int DarknessId = Shader.PropertyToID("_CoverDarkness");

		/// <summary>OpenGL only: the args fence (FishCaveCover.compute FishCoverArgsFence).</summary>
		private static bool NeedsArgsFence => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;

		/// <summary>How far a camera may move (m) and turn (degrees) before its cover is generated again (DetailScatterRenderer's rule).</summary>
		public const float RegatherMetres = 0.5f, RegatherDegrees = 3f;

		/// <summary>Frames between reads of the slots' counts.</summary>
		public const int StatsInterval = 10;

		/// <summary>The cover's GPU time, as a marker the profiler's GPU module and ScenePerfProbe see.</summary>
		private static readonly CustomSampler GpuSample = CustomSampler.Create("FishMMO cave cover (compute)", true);

		private readonly ComputeShader compute;
		private readonly Material material;
		private readonly Mesh mesh;
		private readonly int clearKernel, generateKernel, finalizeKernel, argsFenceKernel;
		private readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO cave cover" };
		private readonly Vector4[] planeVectors = new Vector4[6];
		private readonly Vector4[] ringVectors = new Vector4[CaveCoverSettings.MaxRings];
		private readonly Plane[] widened = new Plane[6];
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retired = new List<(GraphicsBuffer, int)>();
		private readonly List<(Piece Piece, float Distance)> listed = new List<(Piece, float)>();
		private readonly int[] capacities = new int[SlotCount];
		private readonly int[] bases = new int[SlotCount];
		private readonly int[] lastCounts = new int[SlotCount];
		private readonly bool[] warned = new bool[SlotCount];
		private readonly Vector4[] palettes = new Vector4[MaxCaves * 3];
		private GraphicsBuffer pieceBuffer, typeBuffer, slotBuffer, countBuffer, instanceBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer, paletteBuffer, lookBuffer;
		private NativeArray<PieceData> pieceRows;
		private bool layoutDirty = true, palettesDirty = true;
		private int lastStatsFrame = -1000;
		private float longestReach = 1f;

		private Camera generatedFor;
		private Vector3 generatedEye, generatedForward;
		private float generatedFov, generatedAspect;
		private int generatedSignature, generatedSettings;
		private uint generatedTypes;

		/// <summary>The last game camera's pieces walked and the triangles in them.</summary>
		public int LastPieces { get; private set; }
		public long LastTriangles { get; private set; }

		/// <summary>Instances appended per slot at the last read (all builds: it sizes the slots).</summary>
		public IReadOnlyList<int> LastCounts => lastCounts;

		public int Capacity(int slot) => slot >= 0 && slot < SlotCount ? capacities[slot] : 0;

		/// <summary>Bytes of GPU buffers held.</summary>
		public long GpuBytes
		{
			get
			{
				long n = 0;
				foreach (GraphicsBuffer b in new[] { pieceBuffer, typeBuffer, slotBuffer, countBuffer, instanceBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer, paletteBuffer, lookBuffer })
				{
					n += b != null ? (long)b.count * b.stride : 0;
				}
				return n;
			}
		}

		private CaveCoverRenderer(ComputeShader compute, Material material, Mesh mesh)
		{
			this.compute = compute;
			this.material = material;
			this.mesh = mesh;
			clearKernel = compute.FindKernel("FishCoverClear");
			generateKernel = compute.FindKernel("FishCoverGenerate");
			finalizeKernel = compute.FindKernel("FishCoverFinalize");
			argsFenceKernel = compute.FindKernel("FishCoverArgsFence");
			foreach (CaveCoverTypeInfo t in CaveCoverTypes.All)
			{
				longestReach = Mathf.Max(longestReach, t.Reach);
			}
			UploadTables();
		}

		/// <summary>The renderer, or null where it cannot run (no compute, too few vertex-stage buffers, a missing asset) with why.</summary>
		public static CaveCoverRenderer TryCreate(out string reason)
		{
			reason = null;
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			ComputeShader cs = profile != null ? profile.CaveCoverCompute : null;
			Shader shader = profile != null ? profile.CaveCoverShader : null;
			Mesh art = profile != null ? profile.CaveCoverMesh : null;
#if UNITY_EDITOR
			if (cs == null)
			{
				cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
			}
			if (shader == null)
			{
				shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
			}
			if (art == null)
			{
				art = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
			}
#endif
			if (!TerrainGpuMath.ChooseGpu(SystemInfo.supportsComputeShaders, SystemInfo.maxComputeBufferInputsVertex, cs != null))
			{
				reason = cs == null ? $"the compute ({ComputeAssetPath}) is not loaded: Weather Render Profile → Cave cover" : "no compute shaders or too few vertex-stage buffers on this device";
				return null;
			}
			if (shader == null || !shader.isSupported)
			{
				reason = $"FishMMO/Cave Cover ({ShaderAssetPath}) is not loaded or not supported here: Weather Render Profile → Cave cover";
				return null;
			}
			if (art == null)
			{
				reason = $"the cover art ({MeshAssetPath}) is missing: run Generate Biome Art (the structures), then set it on the Weather Render Profile → Cave cover";
				return null;
			}
			if (art.subMeshCount != SlotCount)
			{
				reason = $"the cover art '{art.name}' has {art.subMeshCount} submeshes, {SlotCount} expected: regenerate the biome art (the structures)";
				return null;
			}
			ComputeShader own = Object.Instantiate(cs);
			own.hideFlags = HideFlags.DontSave;
			own.name = cs.name + " (instance)";
			var mat = new Material(shader) { name = "Cave Cover (instance)", hideFlags = HideFlags.DontSave, enableInstancing = false };
			return new CaveCoverRenderer(own, mat, art);
		}

		/// <summary>A cave's palette (its slot's three glow colours), uploaded with the next draw.</summary>
		public void SetPalette(int slot, Color fungus, Color accent, Color crystal)
		{
			if (slot < 0 || slot >= MaxCaves)
			{
				return;
			}
			palettes[slot * 3] = fungus;
			palettes[slot * 3 + 1] = accent;
			palettes[slot * 3 + 2] = crystal;
			palettesDirty = true;
		}

		// ── Per camera ───────────────────────────────────────────────

		/// <summary>Lists the camera's pieces, records the generation into its context and issues the draws. Returns the draws.</summary>
		public int Execute(Camera camera, ScriptableRenderContext context, in TerrainTreeField.View view, IReadOnlyList<Piece> pieces, CaveCoverSettings s)
		{
			ReleaseRetired(false);
			GrowFromCounts(s);
			if (layoutDirty)
			{
				Relayout();
			}
			if (palettesDirty)
			{
				paletteBuffer.SetData(palettes);
				palettesDirty = false;
			}
			ApplyMaterial(s);

			Vector3 eye = view.Position;
			float reach = s.Reach(longestReach);
			float margin = RegatherMetres + reach * RegatherDegrees * Mathf.Deg2Rad;
			for (int i = 0; i < 6; i++)
			{
				widened[i] = new Plane(view.Planes[i].normal, view.Planes[i].distance + margin);
			}

			// The pieces within reach the widened frustum sees, nearest first, capped.
			listed.Clear();
			for (int i = 0; i < pieces.Count; i++)
			{
				Piece p = pieces[i];
				if (p == null || p.Vertices == null || p.Indices == null)
				{
					continue;
				}
				float near = TerrainTreeMath.MinDistance(p.Bounds, eye);
				if (near > reach)
				{
					continue;
				}
				Bounds padded = p.Bounds;
				padded.Expand(2f);
				if (!TerrainTreeMath.IntersectsFrustum(widened, padded))
				{
					continue;
				}
				listed.Add((p, near));
			}
			listed.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			if (listed.Count > s.MaxPieces)
			{
				listed.RemoveRange(s.MaxPieces, listed.Count - s.MaxPieces);
			}
			uint types = 0u;
			int signature = 17;
			long triangles = 0;
			foreach ((Piece p, _) in listed)
			{
				types |= p.Data.TypeMask;
				signature = unchecked(signature * 31 + p.Id);
				triangles += p.Data.Triangles;
			}
			if (camera.cameraType == CameraType.Game)
			{
				LastPieces = listed.Count;
				LastTriangles = triangles;
			}
			if (listed.Count == 0)
			{
				generatedFor = null;
				return 0;
			}
			int settings = SettingsSignature(s);
			if (StillGenerated(camera, eye, signature, settings))
			{
				return Draw(camera, eye, reach, types, listed[0].Piece.Layer);
			}

			EnsurePieceRows(listed.Count);
			for (int i = 0; i < listed.Count; i++)
			{
				pieceRows[i] = listed[i].Piece.Data;
			}
			if (pieceBuffer == null || pieceBuffer.count < listed.Count)
			{
				Retire(pieceBuffer);
				pieceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(64, Mathf.NextPowerOfTwo(listed.Count)), PieceData.Stride) { name = "Cave cover pieces" };
			}
			for (int i = 0; i < 6; i++)
			{
				Plane p = widened[i];
				planeVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
			}
			int rings = s.Rings != null ? Mathf.Min(s.Rings.Length, CaveCoverSettings.MaxRings) : 0;
			for (int i = 0; i < CaveCoverSettings.MaxRings; i++)
			{
				ringVectors[i] = i < rings ? new Vector4(s.Rings[i].x, s.Rings[i].y, 0f, 0f) : Vector4.zero;
			}

			double recordStart = TerrainInstancingProbe.Now;
			cmd.Clear();
			cmd.BeginSample(GpuSample);
			cmd.SetBufferData(pieceBuffer, pieceRows, 0, 0, listed.Count);
			cmd.SetComputeVectorParam(compute, CameraId, new Vector4(eye.x, eye.y, eye.z, reach));
			cmd.SetComputeVectorArrayParam(compute, PlanesId, planeVectors);
			cmd.SetComputeVectorArrayParam(compute, RingsId, ringVectors);
			cmd.SetComputeVectorParam(compute, LevelsId, new Vector4(Mathf.Max(0.5f, s.Level1Metres), Mathf.Max(s.Level1Metres + 0.5f, s.Level2Metres), Mathf.Max(0f, s.Density), rings));
			cmd.SetComputeIntParam(compute, SlotCountId, SlotCount);
			cmd.SetComputeIntParam(compute, ArgCountId, SlotCount);

			cmd.SetComputeBufferParam(compute, clearKernel, CountsId, countBuffer);
			cmd.DispatchCompute(compute, clearKernel, (SlotCount + 63) / 64, 1, 1);

			cmd.SetComputeBufferParam(compute, generateKernel, PiecesId, pieceBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, TypesId, typeBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, SlotsId, slotBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, CountsId, countBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, InstancesOutId, instanceBuffer);
			for (int i = 0; i < listed.Count; i++)
			{
				Piece p = listed[i].Piece;
				int groups = ((int)p.Data.Triangles + 63) / 64;
				if (groups <= 0)
				{
					continue;
				}
				cmd.SetComputeBufferParam(compute, generateKernel, VerticesId, p.Vertices);
				cmd.SetComputeBufferParam(compute, generateKernel, IndicesId, p.Indices);
				cmd.SetComputeIntParam(compute, PieceId, i);
				cmd.DispatchCompute(compute, generateKernel, Mathf.Min(groups, 65535), (groups + 65534) / 65535, 1);
			}

			cmd.SetComputeBufferParam(compute, finalizeKernel, ArgSlotsId, argSlotBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, CountsId, countBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, SlotsId, slotBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, argsBuffer);
			cmd.DispatchCompute(compute, finalizeKernel, (SlotCount + 63) / 64, 1, 1);
			if (NeedsArgsFence)
			{
				cmd.SetComputeBufferParam(compute, argsFenceKernel, ArgsFenceId, argsBuffer);
				cmd.SetComputeBufferParam(compute, argsFenceKernel, CountsId, countBuffer);
				cmd.DispatchCompute(compute, argsFenceKernel, 1, 1, 1);
			}
			if (camera.cameraType == CameraType.Game && Time.frameCount - lastStatsFrame >= StatsInterval)
			{
				lastStatsFrame = Time.frameCount;
				RequestCounts();
			}
			cmd.EndSample(GpuSample);
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			TerrainInstancingProbe.RecordMs += TerrainInstancingProbe.Now - recordStart;

			generatedFor = camera;
			generatedEye = eye;
			generatedForward = camera.transform.forward;
			generatedFov = camera.fieldOfView;
			generatedAspect = camera.aspect;
			generatedSignature = signature;
			generatedSettings = settings;
			generatedTypes = types;
			return Draw(camera, eye, reach, types, listed[0].Piece.Layer);
		}

		/// <summary>One indirect draw per slot of a type the listed caves grow.</summary>
		private int Draw(Camera camera, Vector3 eye, float reach, uint types, int layer)
		{
			double drawStart = TerrainInstancingProbe.Now;
			var rp = new RenderParams(material)
			{
				camera = camera,
				layer = layer,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = true,
				worldBounds = new Bounds(eye, Vector3.one * (2f * reach + 20f)),
				lightProbeUsage = LightProbeUsage.Off,
				reflectionProbeUsage = ReflectionProbeUsage.Off,
			};
			int draws = 0;
			for (int t = 0; t < Types; t++)
			{
				if ((types & (1u << t)) == 0u)
				{
					continue;
				}
				for (int level = 0; level < Levels; level++)
				{
					int slot = SlotOf(t, level);
					Graphics.RenderMeshIndirect(rp, mesh, argsBuffer, 1, slot);
					draws++;
				}
			}
			TerrainInstancingProbe.DrawMs += TerrainInstancingProbe.Now - drawStart;
			return draws;
		}

		private bool StillGenerated(Camera camera, Vector3 eye, int signature, int settings)
		{
			if (generatedFor != camera || layoutDirty || generatedSignature != signature || generatedSettings != settings
				|| generatedFov != camera.fieldOfView || generatedAspect != camera.aspect)
			{
				return false;
			}
			if ((eye - generatedEye).sqrMagnitude > RegatherMetres * RegatherMetres)
			{
				return false;
			}
			return Vector3.Dot(camera.transform.forward, generatedForward) >= Mathf.Cos(RegatherDegrees * Mathf.Deg2Rad);
		}

		/// <summary>Every setting the generation reads, folded together, so an edit in the profile regenerates at once.</summary>
		private static int SettingsSignature(CaveCoverSettings s)
		{
			unchecked
			{
				int h = s.Level1Metres.GetHashCode();
				h = h * 31 + s.Level2Metres.GetHashCode();
				h = h * 31 + s.Density.GetHashCode();
				h = h * 31 + s.MaxPieces;
				if (s.Rings != null)
				{
					foreach (Vector2 r in s.Rings)
					{
						h = h * 31 + r.GetHashCode();
					}
				}
				return h;
			}
		}

		private void ApplyMaterial(CaveCoverSettings s)
		{
			material.SetVector(GlowId, new Vector4(Mathf.Max(0f, s.GlowStrength), Mathf.Clamp01(s.ShimmerAmount), Mathf.Max(0f, s.ShimmerSpeed), Mathf.Clamp01(s.FootShade)));
			material.SetVector(DarknessId, new Vector4(Mathf.Clamp01(s.Underground), s.Highlight ? 1f : 0f, 0f, 0f));
		}

		// ── Slots ─────────────────────────────────────────────────────

		/// <summary>Grows every slot the last read found past its capacity (to 1.25 × what it needed, a power of two, under the cap).</summary>
		private void GrowFromCounts(CaveCoverSettings s)
		{
			int cap = Mathf.Max(1024, s.MaxInstancesPerSlot);
			int start = Mathf.Max(256, s.InitialSlotCapacity);
			for (int slot = 0; slot < SlotCount; slot++)
			{
				if (capacities[slot] < start && capacities[slot] < cap)
				{
					capacities[slot] = Mathf.Min(cap, start);
					layoutDirty = true;
				}
				int needed = lastCounts[slot];
				if (needed <= capacities[slot] || capacities[slot] >= cap)
				{
					continue;
				}
				if (needed > cap && !warned[slot])
				{
					warned[slot] = true;
					Debug.LogWarning($"[Cave cover] '{(CaveCoverType)(slot / Levels)}' level {slot % Levels} needed {needed} instances, over the cap of {cap} (Cave cover > Max Instances Per Slot): the instances past it are dropped.");
				}
				capacities[slot] = Mathf.Min(cap, Mathf.NextPowerOfTwo(Mathf.CeilToInt(needed * 1.25f)));
				layoutDirty = true;
			}
		}

		/// <summary>Lays the slots out in the instance buffer and rebuilds the commands and the bindings.</summary>
		private void Relayout()
		{
			layoutDirty = false;
			int total = 0;
			for (int slot = 0; slot < SlotCount; slot++)
			{
				bases[slot] = total;
				total += capacities[slot];
			}
			var slots = new Vector2Int[SlotCount];
			for (int slot = 0; slot < SlotCount; slot++)
			{
				slots[slot] = new Vector2Int(bases[slot], capacities[slot]);
			}
			Recreate(ref slotBuffer, GraphicsBuffer.Target.Structured, SlotCount, 8);
			slotBuffer.SetData(slots);
			Recreate(ref countBuffer, GraphicsBuffer.Target.Raw, SlotCount, 4);
			if (instanceBuffer == null || instanceBuffer.count < total)
			{
				Retire(instanceBuffer);
				instanceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1024, total + total / 4), 32) { name = "Cave cover instances" };
			}
			var args = new GraphicsBuffer.IndirectDrawIndexedArgs[SlotCount];
			var argSlots = new uint[SlotCount];
			var commandBases = new uint[SlotCount];
			for (int slot = 0; slot < SlotCount; slot++)
			{
				SubMeshDescriptor sub = mesh.GetSubMesh(slot);
				args[slot] = new GraphicsBuffer.IndirectDrawIndexedArgs
				{
					indexCountPerInstance = (uint)sub.indexCount,
					startIndex = (uint)sub.indexStart,
					baseVertexIndex = (uint)sub.baseVertex,
					instanceCount = 0,
					// The slot's start where the instance index carries it (FishCaveCover.hlsl's Vulkan branch).
					startInstance = TerrainGpuRenderer.InstanceIdCarriesBase ? (uint)bases[slot] : 0u,
				};
				argSlots[slot] = (uint)slot;
				commandBases[slot] = (uint)bases[slot];
			}
			Recreate(ref argsBuffer, GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, SlotCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
			argsBuffer.SetData(args);
			Recreate(ref argSlotBuffer, GraphicsBuffer.Target.Structured, SlotCount, 4);
			argSlotBuffer.SetData(argSlots);
			Recreate(ref commandBaseBuffer, GraphicsBuffer.Target.Structured, SlotCount, 4);
			commandBaseBuffer.SetData(commandBases);
			material.SetBuffer(InstancesId, instanceBuffer);
			material.SetBuffer(CommandBasesId, commandBaseBuffer);
			material.SetBuffer(PalettesId, paletteBuffer);
			material.SetBuffer(LooksId, lookBuffer);
			generatedFor = null;
		}

		/// <summary>The type table for the compute and the looks for the shader (fixed: the shared table's).</summary>
		private void UploadTables()
		{
			var rows = new TypeData[Types];
			var looks = new Vector4[Types * 2];
			for (int t = 0; t < Types; t++)
			{
				ref readonly CaveCoverTypeInfo info = ref CaveCoverTypes.All[t];
				rows[t] = new TypeData
				{
					ClassDensity = new Vector4(info.Class.x, info.Class.y, info.Class.z, info.Density),
					Gain = info.Gain,
					Rich = new Vector4(info.Rich.x, info.Rich.y, info.Bias, info.TroddenKeep),
					Shape = new Vector4(info.Scale.x, info.Scale.y, info.Sink, info.Reach),
					Mount = (uint)info.Mount,
					Salt = CaveCoverMath.Salt(info.Type),
				};
				looks[t * 2] = new Vector4(info.Smoothness, info.WetGloss, info.Jitter, info.Shimmer);
				looks[t * 2 + 1] = new Vector4((float)info.Glow, 0f, 0f, 0f);
			}
			typeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Types, TypeData.Stride) { name = "Cave cover types" };
			typeBuffer.SetData(rows);
			lookBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, looks.Length, 16) { name = "Cave cover looks" };
			lookBuffer.SetData(looks);
			paletteBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, palettes.Length, 16) { name = "Cave cover palettes" };
			CaveCoverSite.Choice d = CaveCoverSite.Default;
			for (int slot = 0; slot < MaxCaves; slot++)
			{
				palettes[slot * 3] = d.Fungus;
				palettes[slot * 3 + 1] = d.Accent;
				palettes[slot * 3 + 2] = d.Crystal;
			}
			paletteBuffer.SetData(palettes);
			palettesDirty = false;
		}

		private void RequestCounts()
		{
			cmd.RequestAsyncReadback(countBuffer, request =>
			{
				if (request.hasError)
				{
					return;
				}
				NativeArray<uint> counts = request.GetData<uint>();
				int n = Mathf.Min(SlotCount, counts.Length);
				for (int i = 0; i < n; i++)
				{
					lastCounts[i] = (int)Math.Min(counts[i], int.MaxValue);
				}
			});
		}

		private void EnsurePieceRows(int needed)
		{
			if (pieceRows.IsCreated && pieceRows.Length >= needed)
			{
				return;
			}
			if (pieceRows.IsCreated)
			{
				pieceRows.Dispose();
			}
			pieceRows = new NativeArray<PieceData>(Mathf.Max(64, Mathf.NextPowerOfTwo(needed)), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
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
			foreach (GraphicsBuffer b in new[] { pieceBuffer, typeBuffer, slotBuffer, countBuffer, instanceBuffer, argsBuffer, argSlotBuffer, commandBaseBuffer, paletteBuffer, lookBuffer })
			{
				b?.Release();
			}
			pieceBuffer = typeBuffer = slotBuffer = countBuffer = instanceBuffer = argsBuffer = argSlotBuffer = commandBaseBuffer = paletteBuffer = lookBuffer = null;
			if (pieceRows.IsCreated)
			{
				pieceRows.Dispose();
			}
			if (compute != null)
			{
				Object.Destroy(compute);
			}
			if (material != null)
			{
				Object.Destroy(material);
			}
		}
	}
}
