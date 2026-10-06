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
	/// The GPU side of the procedural blade grass: per camera, the CPU picks the world-aligned tiles within
	/// the grass distance that the camera (or the sun's shadow of it) can see, gives each tile a lattice
	/// level from its nearest distance (<see cref="GrassMath.LevelForShare"/>) and lists it as 8 × 8-candidate
	/// work items; FishGrassBlades.compute turns those into blades in five slot buffers (camera LOD0..2,
	/// shadow LOD0..1), and five <see cref="Graphics.RenderMeshIndirect"/> calls draw them. Everything the
	/// GPU does for a camera is recorded into one command buffer executed on that camera's context, as
	/// <see cref="TerrainGpuRenderer"/> does. No readback in the frame (stats are read asynchronously).
	/// </summary>
	public sealed class GrassBladeRenderer : IDisposable
	{
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishGrassBlades.compute";
		public const string ShaderName = "FishMMO/Grass Blades";
		public const int MaxTypes = 16;
		public const int MaxRings = 6;
		public const int Slots = 5;

		/// <summary>The slot's level of detail (camera 0..2, shadow 0..1).</summary>
		private static readonly int[] SlotLod = { 0, 1, 2, 0, 1 };
		/// <summary>The slot's share of the blade cap.</summary>
		private static readonly float[] SlotShare = { 0.3f, 0.5f, 0.5f, 0.3f, 0.3f };

		[StructLayout(LayoutKind.Sequential)]
		private struct Item
		{
			public const int Stride = 16;
			public int CellX, CellZ;
			public uint Packed, Terrain;
		}

		private struct GroupRange
		{
			public GrassTerrainAtlas.Group Group;
			public int Offset, Count;
		}

		private static readonly int ItemsId = Shader.PropertyToID("_GrassItems");
		private static readonly int CountsId = Shader.PropertyToID("_GrassCounts");
		private static readonly int ArgsId = Shader.PropertyToID("_GrassArgs");
		private static readonly int[] OutIds =
		{
			Shader.PropertyToID("_GrassOut0"), Shader.PropertyToID("_GrassOut1"), Shader.PropertyToID("_GrassOut2"),
			Shader.PropertyToID("_GrassOut3"), Shader.PropertyToID("_GrassOut4"),
		};
		private static readonly int HeightmapId = Shader.PropertyToID("_GrassHeightmap");
		private static readonly int Density0Id = Shader.PropertyToID("_GrassDensity0");
		private static readonly int SeaId = Shader.PropertyToID("_GrassSea");
		private static readonly int FishSeaId = Shader.PropertyToID("_FishSea");
		private static readonly int Density1Id = Shader.PropertyToID("_GrassDensity1");
		private static readonly int CameraId = Shader.PropertyToID("_GrassCamera");
		private static readonly int PlanesId = Shader.PropertyToID("_GrassPlanes");
		private static readonly int LightId = Shader.PropertyToID("_GrassLight");
		private static readonly int LatticeId = Shader.PropertyToID("_GrassLattice");
		private static readonly int ShapeParamsId = Shader.PropertyToID("_GrassShapeParams");
		private static readonly int RingDistanceId = Shader.PropertyToID("_GrassRingDistance");
		private static readonly int RingDensityId = Shader.PropertyToID("_GrassRingDensity");
		private static readonly int RingCountId = Shader.PropertyToID("_GrassRingCount");
		private static readonly int TypeShapeId = Shader.PropertyToID("_GrassTypeShape");
		private static readonly int TypeShape2Id = Shader.PropertyToID("_GrassTypeShape2");
		private static readonly int TypeHeadId = Shader.PropertyToID("_GrassTypeHead");
		private static readonly int TypeHeadColourId = Shader.PropertyToID("_GrassTypeHeadColour");
		private static readonly int Capacity0Id = Shader.PropertyToID("_GrassCapacity0");
		private static readonly int Capacity1Id = Shader.PropertyToID("_GrassCapacity1");
		private static readonly int ItemOffsetId = Shader.PropertyToID("_GrassItemOffset");
		private static readonly int ItemCountId = Shader.PropertyToID("_GrassItemCount");
		private static readonly int SurfaceLayersId = Shader.PropertyToID("_GrassSurfaceLayers");
		private static readonly int SharedId = Shader.PropertyToID("_GrassShared");
		private static readonly int ColourId = Shader.PropertyToID("_GrassColour");
		private static readonly int ProbeTerrainId = Shader.PropertyToID("_GrassProbeTerrain");
		private static readonly int ProbePointsId = Shader.PropertyToID("_GrassProbePoints");
		private static readonly int ProbeHeightsId = Shader.PropertyToID("_GrassProbeHeights");
		private static readonly int ProbeCountId = Shader.PropertyToID("_GrassProbeCount");

		private static readonly int BladesId = Shader.PropertyToID("_GrassBlades");
		private static readonly int TypeRootId = Shader.PropertyToID("_GrassTypeRoot");
		private static readonly int TypeTipId = Shader.PropertyToID("_GrassTypeTip");
		private static readonly int TypeHealthyId = Shader.PropertyToID("_GrassTypeHealthy");
		private static readonly int TypeDryId = Shader.PropertyToID("_GrassTypeDry");
		private static readonly int TypeParamsId = Shader.PropertyToID("_GrassTypeParams");
		private static readonly int Params0Id = Shader.PropertyToID("_GrassParams0");
		private static readonly int Params1Id = Shader.PropertyToID("_GrassParams1");
		private static readonly int Params2Id = Shader.PropertyToID("_GrassParams2");
		private static readonly int Params3Id = Shader.PropertyToID("_GrassParams3");
		private static readonly int Params4Id = Shader.PropertyToID("_GrassParams4");
		private static readonly int Params5Id = Shader.PropertyToID("_GrassParams5");
		private static readonly int Params6Id = Shader.PropertyToID("_GrassParams6");
		private static readonly int Params7Id = Shader.PropertyToID("_GrassParams7");

		private readonly ComputeShader compute;
		private readonly int clearKernel, generateKernel, finalizeKernel, probeKernel, argsFenceKernel;
		private static readonly int ArgsFenceId = Shader.PropertyToID("_GrassArgsFence");

		/// <summary>
		/// OpenGL only: Unity's GLES device issues GL_COMMAND_BARRIER_BIT before an indirect draw only when the args buffer
		/// was BOUND after its last write, never for the write itself (FishGrassBlades.compute FishGrassArgsFence). Without
		/// the fence dispatch the draws read the args while Finalize still wrote them, every other frame: the flicker.
		/// </summary>
		private static bool NeedsArgsFence => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore || SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
		private readonly Material material;
		/* One material per slot, each holding its slot's blade buffer; copied from `material` (the parameters) every
		 * Publish. The per-draw MaterialPropertyBlock alone was not enough: a pass drew without it (Vulkan: "Shader
		 * requires a compute buffer _GrassBlades, but none provided. Skipping draw calls"), and on OpenGL that draw
		 * silently read whatever buffer was last bound. TerrainGpuRenderer sets its buffers on its material for the
		 * same reason. */
		private readonly Material[] slotMaterials = new Material[Slots];
		private readonly Mesh[] lodMeshes = new Mesh[3];
		private readonly GraphicsBuffer[] outBuffers = new GraphicsBuffer[Slots];
		private readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[Slots];
		private readonly int[] capacities = new int[Slots];
		private GraphicsBuffer itemBuffer, countBuffer, argsBuffer;
		private NativeArray<Item> items;
		private int itemCount;
		private readonly List<GroupRange> ranges = new List<GroupRange>();
		/// <summary>All terrains' textures as array slices and their parameters in one table: one dispatch for all of them.</summary>
		private readonly GrassTerrainAtlas atlas = new GrassTerrainAtlas();
		private readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO procedural grass" };
		private readonly Vector4[] planeVectors = new Vector4[6];
		private readonly Vector4[] ringDistance = new Vector4[MaxRings];
		private readonly Vector4[] ringDensity = new Vector4[MaxRings];
		private readonly float[] ringDistanceF = new float[MaxRings];
		/// <summary>The rings as one number, so a work list gathered under other rings is not reused.</summary>
		private float ringSignature;

		/// <summary>
		/// How far a camera may move, metres, and turn, degrees, before the work list is gathered again. Gathered
		/// against a view widened by as much (the frustum's sides pushed out by what that move and turn sweep at the
		/// grass's far edge), it still holds every tile the camera can see until then, and the compute culls each blade
		/// against the camera's own frustum every frame. The gather was 4 ms of every camera's render in the editor
		/// (ScenePerfProbe, 2026-10-07: 3 500 tiles, 67 000 items) for a list that changes only as the camera moves.
		/// </summary>
		public const float RegatherMetres = 1.5f, RegatherDegrees = 3f;

		private Camera gatheredFor;
		private Vector3 gatheredEye, gatheredForward, gatheredLight;
		private float gatheredFov, gatheredAspect, gatheredDistance, gatheredShadow, gatheredCell, gatheredRings;
		private int gatheredAtlas = -1, gatheredTiles;
		private long gatheredCandidates;
		private readonly Plane[] widened = new Plane[6];

		/// <summary>Whether the last work list still holds for this camera's view (see <see cref="RegatherMetres"/>).</summary>
		/// <summary>Debug and profiling: gather on every render, as a camera turning all the time would.</summary>
		public static bool AlwaysRegather;

		private bool StillGathered(Camera camera, in TerrainTreeField.View view, float cell, float grassDistance, float shadowDistance)
		{
			if (AlwaysRegather || gatheredFor != camera || gatheredAtlas != atlas.Version || gatheredCell != cell || gatheredDistance != grassDistance
				|| gatheredShadow != shadowDistance || gatheredRings != ringSignature || gatheredFov != camera.fieldOfView || gatheredAspect != camera.aspect)
			{
				return false;
			}
			if ((view.Position - gatheredEye).sqrMagnitude > RegatherMetres * RegatherMetres)
			{
				return false;
			}
			float turn = Mathf.Cos(RegatherDegrees * Mathf.Deg2Rad);
			if (Vector3.Dot(camera.transform.forward, gatheredForward) < turn)
			{
				return false;
			}
			return shadowDistance <= 0f || Vector3.Dot(view.LightDirection, gatheredLight) >= turn;
		}

		private void RememberGather(Camera camera, in TerrainTreeField.View view, float cell, float grassDistance, float shadowDistance, int tiles, long candidates)
		{
			gatheredFor = camera;
			gatheredAtlas = atlas.Version;
			gatheredEye = view.Position;
			gatheredForward = camera.transform.forward;
			gatheredLight = view.LightDirection;
			gatheredFov = camera.fieldOfView;
			gatheredAspect = camera.aspect;
			gatheredDistance = grassDistance;
			gatheredShadow = shadowDistance;
			gatheredCell = cell;
			gatheredRings = ringSignature;
			gatheredTiles = tiles;
			gatheredCandidates = candidates;
		}

		/// <summary>The view's planes with every one pushed outward by <paramref name="margin"/> metres.</summary>
		private Plane[] Widen(Plane[] planes, float margin)
		{
			for (int i = 0; i < 6; i++)
			{
				widened[i] = new Plane(planes[i].normal, planes[i].distance + margin);
			}
			return widened;
		}
		private readonly float[] ringDensityF = new float[MaxRings];
		private int ringCount;
		private readonly Vector4[] typeShape = new Vector4[MaxTypes];
		/// <summary>Per type for the compute: x the sprinkle chance per candidate (0: a tussock species), y the head, z its size (m).</summary>
		private readonly Vector4[] typeShape2 = new Vector4[MaxTypes];
		/// <summary>Per type for the blades: x the head, y its size (m), z how many head colours, w 1 = blunt strap tip.</summary>
		private readonly Vector4[] typeHead = new Vector4[MaxTypes];
		private readonly Vector4[] typeHeadColour = new Vector4[MaxTypes * GrassType.MaxHeadColours];
		private readonly Vector4[] typeRoot = new Vector4[MaxTypes];
		private readonly Vector4[] typeTip = new Vector4[MaxTypes];
		private readonly Vector4[] typeHealthy = new Vector4[MaxTypes];
		private readonly Vector4[] typeDry = new Vector4[MaxTypes];
		private readonly Vector4[] typeParams = new Vector4[MaxTypes];
		/// <summary>Each array set's layer albedo as the grass's own buffer (<see cref="GrassAlbedoTexels"/>), built in <see cref="Publish"/>.</summary>
		private readonly Dictionary<FishMMO.Shared.TerrainArraySet, GrassAlbedoTexels> albedoTexels = new Dictionary<FishMMO.Shared.TerrainArraySet, GrassAlbedoTexels>();
		private readonly HashSet<FishMMO.Shared.TerrainArraySet> albedoWanted = new HashSet<FishMMO.Shared.TerrainArraySet>();
		private float colourFootprint = 0.25f;
		private readonly List<(GraphicsBuffer Buffer, int Frame)> retired = new List<(GraphicsBuffer, int)>();

		/// <summary>Work items, candidate threads and tiles of the last camera.</summary>
		public int LastItems { get; private set; }

		/// <summary>
		/// Where <see cref="Execute"/>'s time goes, milliseconds summed over calls since the last reset, and the calls:
		/// the atlas check, the tile gather, the compute recording, the draws. Read by ScenePerfProbe; cheap enough to
		/// leave on (four timestamps a camera).
		/// </summary>
		public static double TimeAtlas, TimeGather, TimeRecord, TimeDraws;
		/// <summary>Calls to <see cref="Execute"/> counted with the times, and the items and tiles of the last.</summary>
		public static int TimedCalls, TimedItems, TimedTiles;

		/// <summary>Zeroes the <see cref="TimeAtlas"/> family.</summary>
		public static void ResetTimes()
		{
			TimeAtlas = TimeGather = TimeRecord = TimeDraws = 0;
			TimedCalls = 0;
		}
		public long LastCandidates { get; private set; }
		public int LastTiles { get; private set; }
		/// <summary>The last game camera's name, position, grass distance and lattice cell (Describe).</summary>
		public string LastCamera { get; private set; } = "none yet";

		/// <summary>Debug: every blade drawn bright magenta, so procedural blades can be told from mesh grass at a glance.</summary>
		public static bool Highlight;

		/// <summary>
		/// Debug: stop regenerating and keep drawing the last blades generated (whichever camera made them).
		/// A flicker that goes on while frozen is in the drawing or lighting; one that stops is in the generation or culling.
		/// </summary>
		public static bool Freeze;

		/// <summary>
		/// Debug A/B: the generate kernel's GRASS_NO_TERRAIN_COLOUR variant, which declares and binds none of the terrain-colour
		/// textures (8 control maps, the albedo array): exactly the bindings it had before the tint. The blades take the ground
		/// colour map's fallback.
		/// </summary>
		public static bool SkipTerrainColour;

		/// <summary>Debug A/B: the full kernel and bindings, but a constant valid colour and no colour data read (_GrassArrayInfo.y = 1).</summary>
		public static bool ConstantTerrainColour;

		/// <summary>Debug A/B: dispatch the terrains last to first (the terrain underfoot, built first, is normally the first dispatch).</summary>
		public static bool ReverseDispatchOrder;
		private readonly LocalKeyword noTerrainColour;
		private bool generated;
		private static readonly int HighlightId = Shader.PropertyToID("_GrassHighlight");

		/// <summary>Blades appended per slot, read back asynchronously (camera LOD0..2, shadow LOD0..1); −1 before the first read.</summary>
		public readonly int[] LastBlades = { -1, -1, -1, -1, -1 };

		/// <summary>Reads the slot counters back every <see cref="StatsInterval"/> frames (the probe and the dashboard turn it on).</summary>
		public static bool StatsEnabled;
		public const int StatsInterval = 30;
		private int lastStatsFrame = -1000;
		private readonly bool[] overflowWarned = new bool[Slots];
		private static readonly string[] SlotNames = { "camera LOD0", "camera LOD1", "camera LOD2", "shadow LOD0", "shadow LOD1" };

		public int Capacity(int slot) => capacities[slot];

		/// <summary>Bytes of GPU memory this renderer holds (buffers; the density maps are the terrains').</summary>
		public long GpuBytes
		{
			get
			{
				long n = Size(itemBuffer) + Size(countBuffer) + Size(argsBuffer);
				foreach (GraphicsBuffer b in outBuffers)
				{
					n += Size(b);
				}
				return n;
			}
		}

		private static long Size(GraphicsBuffer b) => b != null ? (long)b.count * b.stride : 0;

		private GrassBladeRenderer(ComputeShader compute, Shader shader)
		{
			this.compute = compute;
			clearKernel = compute.FindKernel("FishGrassClear");
			generateKernel = compute.FindKernel("FishGrassGenerate");
			finalizeKernel = compute.FindKernel("FishGrassFinalize");
			probeKernel = compute.FindKernel("FishGrassProbeHeights");
			argsFenceKernel = compute.FindKernel("FishGrassArgsFence");
			noTerrainColour = new LocalKeyword(compute, "GRASS_NO_TERRAIN_COLOUR");
			material = new Material(shader) { name = "Grass Blades (runtime)", hideFlags = HideFlags.DontSave };
			for (int s = 0; s < Slots; s++)
			{
				slotMaterials[s] = new Material(material) { name = $"Grass Blades (runtime, slot {s})", hideFlags = HideFlags.DontSave };
			}
			lodMeshes[0] = BuildStrip(7, "Grass blade LOD0");
			lodMeshes[1] = BuildStrip(3, "Grass blade LOD1");
			lodMeshes[2] = BuildStrip(1, "Grass blade LOD2");
			for (int s = 0; s < Slots; s++)
			{
				blocks[s] = new MaterialPropertyBlock();
			}
			countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 8, 4);
			argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, Slots, GraphicsBuffer.IndirectDrawIndexedArgs.size);
			var args = new GraphicsBuffer.IndirectDrawIndexedArgs[Slots];
			for (int s = 0; s < Slots; s++)
			{
				Mesh mesh = lodMeshes[SlotLod[s]];
				args[s] = new GraphicsBuffer.IndirectDrawIndexedArgs
				{
					indexCountPerInstance = mesh.GetIndexCount(0),
					instanceCount = 0,
					startIndex = 0,
					baseVertexIndex = 0,
					startInstance = 0,
				};
			}
			argsBuffer.SetData(args);

		}

		/// <summary>The renderer, or null where it cannot run (no compute, no vertex-stage buffers, assets missing or unsupported).</summary>
		public static GrassBladeRenderer TryCreate(out string reason)
		{
			reason = null;
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			ComputeShader cs = profile != null ? profile.GrassBladesCompute : null;
			Shader shader = profile != null ? profile.GrassBladesShader : null;
#if UNITY_EDITOR
			if (cs == null)
			{
				cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
			}
#endif
			if (shader == null)
			{
				shader = Shader.Find(ShaderName);
			}
			if (!SystemInfo.supportsComputeShaders || SystemInfo.maxComputeBufferInputsVertex < 1)
			{
				reason = "no compute shaders or no vertex-stage buffers on this device (the mesh grass draws)";
				return null;
			}
			if (cs == null || shader == null)
			{
				reason = $"the {(cs == null ? "compute (" + ComputeAssetPath + ")" : "shader (" + ShaderName + ")")} is not loaded: Weather Render Profile → Procedural grass";
				return null;
			}
			if (!shader.isSupported)
			{
				reason = $"{ShaderName} is not supported on this device";
				return null;
			}
			ComputeShader own = Object.Instantiate(cs);
			own.hideFlags = HideFlags.DontSave;
			own.name = cs.name + " (instance)";
			return new GrassBladeRenderer(own, shader);
		}

		/// <summary>A blade strip: two vertices a row (x −1 and +1, y how far along), the last row a single tip point.</summary>
		public static Mesh BuildStrip(int segments, string name)
		{
			var vertices = new List<Vector3>();
			var indices = new List<int>();
			for (int r = 0; r < segments; r++)
			{
				float t = GrassMath.RowT(r, segments);
				vertices.Add(new Vector3(-1f, t, 0f));
				vertices.Add(new Vector3(1f, t, 0f));
			}
			vertices.Add(new Vector3(0f, 1f, 0f));
			// Wound so the front face (Unity: the side cross(b − a, c − a) points to) is the side the shader's
			// face normal cross(across, along) points to: SV_IsFrontFace then says which side is seen.
			for (int r = 0; r < segments - 1; r++)
			{
				int a = r * 2;
				indices.Add(a); indices.Add(a + 1); indices.Add(a + 2);
				indices.Add(a + 1); indices.Add(a + 3); indices.Add(a + 2);
			}
			int last = (segments - 1) * 2;
			indices.Add(last); indices.Add(last + 1); indices.Add(vertices.Count - 1);
			var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
			mesh.SetVertices(vertices);
			mesh.SetIndices(indices, MeshTopology.Triangles, 0);
			mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
			return mesh;
		}

		// ── Settings and types ───────────────────────────────────────

		/// <summary>Takes the profile's settings (every frame: live in the inspector) and the types into the material and the compute's arrays.</summary>
		public void Publish(GrassBladeSettings s, IReadOnlyList<GrassType> types, float grassDistance)
		{
			ringCount = 0;
			if (s.Rings != null)
			{
				for (int r = 0; r < s.Rings.Length && ringCount < MaxRings; r++)
				{
					ringDistanceF[ringCount] = Mathf.Max(0.1f, s.Rings[r].x);
					ringDensityF[ringCount] = Mathf.Max(1e-3f, s.Rings[r].y);
					ringCount++;
				}
			}
			ringSignature = ringCount;
			for (int r = 0; r < ringCount; r++)
			{
				ringSignature = ringSignature * 31f + ringDistanceF[r] * 7f + ringDensityF[r];
			}
			for (int r = 0; r < MaxRings; r++)
			{
				ringDistance[r] = new Vector4(r < ringCount ? ringDistanceF[r] : 0f, 0f, 0f, 0f);
				// Ring 0's y carries the fullness (FishGrassBlades.compute: spawn = summed density / fullness).
				ringDensity[r] = new Vector4(r < ringCount ? ringDensityF[r] : 0f, r == 0 ? Mathf.Clamp(s.Fullness, 0.05f, 1f) : 0f, 0f, 0f);
			}
			for (int t = 0; t < MaxTypes; t++)
			{
				GrassType type = t < types.Count ? types[t] : null;
				if (type == null)
				{
					typeShape[t] = new Vector4(0.3f, 1f, 0f, 0f);
					typeShape2[t] = typeHead[t] = Vector4.zero;
					typeRoot[t] = typeTip[t] = typeHealthy[t] = typeDry[t] = Vector4.one;
					typeParams[t] = new Vector4(1f, 0f, 0.4f, 1f);
					for (int k = 0; k < GrassType.MaxHeadColours; k++)
					{
						typeHeadColour[t * GrassType.MaxHeadColours + k] = Vector4.one;
					}
					continue;
				}
				GrassTypeTuning tuning = s.TuningFor(type.Name) ?? type.Tuning;
				typeShape[t] = new Vector4(type.Height * (tuning?.HeightScale ?? 1f) / Mathf.Max(0.05f, type.Tuning?.HeightScale ?? 1f), Mathf.Clamp(tuning?.Density ?? 1f, 0.01f, 1f), Mathf.Clamp(tuning?.ClumpPull ?? 0.3f, 0f, 0.95f), 0f);
				typeRoot[t] = new Vector4(type.Root.r, type.Root.g, type.Root.b, 0f);
				typeTip[t] = new Vector4(type.Tip.r, type.Tip.g, type.Tip.b, s.BladeWidth * (tuning?.Width ?? 1f));
				typeHealthy[t] = new Vector4(type.Healthy.r, type.Healthy.g, type.Healthy.b, type.TintSpread);
				typeDry[t] = new Vector4(type.Dry.r, type.Dry.g, type.Dry.b, type.PatchMetres);
				typeParams[t] = new Vector4(tuning?.Stiffness ?? 1f, type.SnowBury, tuning?.Bend ?? 0.4f, 1f);
				// Sprinkled species: stems per square metre at full paint, as a chance per candidate of the near lattice.
				float sprinkle = tuning != null && tuning.Sprinkle > 0f ? Mathf.Clamp01(tuning.Sprinkle / s.NearDensity) : 0f;
				GrassHead head = tuning?.Head ?? GrassHead.None;
				float headSize = Mathf.Max(0.002f, tuning?.HeadSize ?? 0.035f);
				typeShape2[t] = new Vector4(sprinkle, (float)head, head != GrassHead.None ? headSize : 0f, 0f);
				int colours = Mathf.Clamp(type.HeadColours.Count, 0, GrassType.MaxHeadColours);
				Color fallback = (tuning?.HeadColour ?? new Color(0.36f, 0.25f, 0.14f)).linear;
				for (int k = 0; k < GrassType.MaxHeadColours; k++)
				{
					Color c = k < colours ? type.HeadColours[k] : fallback;
					typeHeadColour[t * GrassType.MaxHeadColours + k] = new Vector4(c.r, c.g, c.b, 1f);
				}
				typeHead[t] = new Vector4((float)head, headSize, Mathf.Max(1, colours), tuning != null && tuning.BluntTip ? 1f : 0f);
			}
			material.SetVectorArray(TypeRootId, typeRoot);
			material.SetVectorArray(TypeTipId, typeTip);
			material.SetVectorArray(TypeHealthyId, typeHealthy);
			material.SetVectorArray(TypeDryId, typeDry);
			material.SetVectorArray(TypeParamsId, typeParams);
			material.SetVectorArray(TypeHeadId, typeHead);
			material.SetVectorArray(TypeHeadColourId, typeHeadColour);
			material.SetVectorArray(RingDistanceId, ringDistance);
			material.SetVectorArray(RingDensityId, ringDensity);
			material.SetFloat(RingCountId, ringCount);
			material.SetVector(Params0Id, new Vector4(s.WidenExponent, Mathf.Max(1f, s.MaxWiden), grassDistance, s.DistanceFadeBand));
			material.SetVector(Params1Id, new Vector4(s.NormalRounding * Mathf.Deg2Rad, s.RootOcclusion, s.Translucency, s.Smoothness));
			material.SetVector(Params2Id, new Vector4(s.WindStrength, s.GustWaveMetres, s.Flutter, s.IdleBreeze));
			material.SetVector(Params3Id, new Vector4(s.RootGroundBlend, s.RootGroundHeight, s.GroundHueTint, s.Specular));
			material.SetVector(Params4Id, new Vector4(s.MaxWindBendDegrees * Mathf.Deg2Rad, s.GustSharpness, s.GustSpeed, s.GustSheen));
			material.SetVector(Params5Id, new Vector4(s.FarBlendStart, s.FarBlendEnd, s.FarGroundPull, s.FarNormalFlatten));
			material.SetVector(Params6Id, new Vector4(s.TerrainTextureTint, s.TerrainTextureBrightness, s.TerrainTextureRoot, 0f));
			material.SetVector(Params7Id, new Vector4(s.WindCalmStart, Mathf.Max(s.WindCalmStart + 1f, s.WindCalmEnd), s.FarWind, 0f));
			colourFootprint = Mathf.Max(0.01f, s.TerrainTextureFootprint);
			EnsureCapacity(s.BladeCap);
			// One set's albedo copy a frame (a blit and a readback per layer), outside any camera's rendering.
			FishMMO.Shared.TerrainArraySet wanted = null;
			foreach (FishMMO.Shared.TerrainArraySet set in albedoWanted)
			{
				wanted = set;
				break;
			}
			if (wanted != null || albedoWanted.Count > 0)
			{
				albedoWanted.Remove(wanted);
				if (wanted != null && wanted.IsUsable)
				{
					albedoTexels[wanted] = GrassAlbedoTexels.Build(wanted.Albedo, wanted.LayerCount, wanted.LayerST, wanted.LayerTint);
				}
			}
			for (int slot = 0; slot < Slots; slot++)
			{
				slotMaterials[slot].CopyPropertiesFromMaterial(material);
				slotMaterials[slot].SetBuffer(BladesId, outBuffers[slot]);
			}
		}

		private void EnsureCapacity(int cap)
		{
			for (int s = 0; s < Slots; s++)
			{
				int capacity = Mathf.Max(1024, Mathf.CeilToInt(cap * SlotShare[s]));
				if (outBuffers[s] != null && capacities[s] == capacity)
				{
					continue;
				}
				// The old buffer may still be read by a frame in flight: released a few frames later.
				if (outBuffers[s] != null)
				{
					retired.Add((outBuffers[s], Time.frameCount));
				}
				outBuffers[s] = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, GrassMath.BladeStride);
				capacities[s] = capacity;
				blocks[s].SetBuffer(BladesId, outBuffers[s]);
			}
		}

		// ── Per camera ───────────────────────────────────────────────

		/// <summary>Picks the tiles, records the generation into the camera's context and issues the draws. Returns the draws.</summary>
		public int Execute(Camera camera, ScriptableRenderContext context, in TerrainTreeField.View view, IReadOnlyList<GrassTerrain> terrains, GrassBladeSettings s, float grassDistance)
		{
			double t0 = TerrainInstancingProbe.Now;
			TimedCalls++;
			ReleaseRetired(false);
			Shader.SetGlobalFloat(HighlightId, Highlight ? 1f : 0f);
			float cell = s.CellMetres;
			float tile = GrassMath.TileCells * cell;
			float shadowDistance = view.Shadows ? Mathf.Min(s.ShadowDistance, view.ShadowDistance) : 0f;
			if (Freeze && generated)
			{
				return IssueDraws(camera, terrains, view.Position, grassDistance, shadowDistance);
			}
			bool synced = atlas.Sync(terrains, AlbedoFor);
			double t1 = TerrainInstancingProbe.Now;
			TimeAtlas += t1 - t0;
			if (!synced)
			{
				return 0;
			}
			Vector3 eye = view.Position;
			// Kept from the last gather while the camera has not moved or turned past what it was widened for.
			bool reuse = StillGathered(camera, in view, cell, grassDistance, shadowDistance);
			int tiles = gatheredTiles;
			long candidates = gatheredCandidates;
			if (!reuse)
			{
				itemCount = 0;
				ranges.Clear();
				tiles = 0;
				candidates = 0;
				Plane[] gatherPlanes = Widen(view.Planes, RegatherMetres + grassDistance * RegatherDegrees * Mathf.Deg2Rad);
				// One work list for all terrains, grouped by the atlas's resolution groups (normally one): each item names
				// its terrain's row in the table, so one dispatch per group generates them all.
				foreach (GrassTerrainAtlas.Group group in atlas.Groups)
				{
					int groupStart = itemCount;
					foreach (GrassTerrain gt in group.Terrains)
					{
						uint terrainIndex = (uint)atlas.IndexOf(gt);
						if (gt.Terrain == null || !gt.Terrain.drawTreesAndFoliage)
						{
							continue;
						}
						float rx0 = gt.Origin.x, rz0 = gt.Origin.z, rx1 = rx0 + gt.Size.x, rz1 = rz0 + gt.Size.z;
						float x0 = Mathf.Max(rx0, eye.x - grassDistance), x1 = Mathf.Min(rx1, eye.x + grassDistance);
						float z0 = Mathf.Max(rz0, eye.z - grassDistance), z1 = Mathf.Min(rz1, eye.z + grassDistance);
						if (x0 >= x1 || z0 >= z1)
						{
							continue;
						}
						int tx0 = Mathf.FloorToInt(x0 / tile), tx1 = Mathf.FloorToInt(x1 / tile);
						int tz0 = Mathf.FloorToInt(z0 / tile), tz1 = Mathf.FloorToInt(z1 / tile);
						for (int tz = tz0; tz <= tz1; tz++)
						{
							for (int tx = tx0; tx <= tx1; tx++)
							{
								float bx0 = Mathf.Max(tx * tile, rx0), bx1 = Mathf.Min((tx + 1) * tile, rx1);
								float bz0 = Mathf.Max(tz * tile, rz0), bz1 = Mathf.Min((tz + 1) * tile, rz1);
								if (bx0 >= bx1 || bz0 >= bz1 || !gt.HasGrass(bx0, bz0, bx1, bz1))
								{
									continue;
								}
								Vector2 heights = gt.HeightRange(bx0, bz0, bx1, bz1);
								var bounds = new Bounds();
								// Room for the clump pull (blades leave their cell toward the clump's centre) and the wind: a blade
								// laid over reaches its full length sideways (up to 1.6 x the type height with the clump and own
								// variation, as the top). With 0.5 m the edge tiles were culled while their bent blades showed.
								float reach = gt.MaxGrassHeight * 1.6f + 0.5f;
								float pad = s.ClumpMetres + reach;
								bounds.SetMinMax(new Vector3(bx0 - pad, heights.x - 0.2f, bz0 - pad), new Vector3(bx1 + pad, heights.y + reach, bz1 + pad));
								float near = TerrainTreeMath.MinDistance(bounds, eye);
								if (near > grassDistance)
								{
									continue;
								}
								bool main = TerrainTreeMath.IntersectsFrustum(gatherPlanes, bounds);
								bool shadow = shadowDistance > 0f && near <= shadowDistance
									&& TerrainTreeMath.IntersectsFrustum(gatherPlanes, TerrainTreeMath.ShadowSweep(bounds, view.LightDirection, shadowDistance));
								if (!main && !shadow)
								{
									continue;
								}
								float share = GrassMath.Share(near, ringDistanceF, ringDensityF, ringCount);
								int level = GrassMath.LevelForShare(share);
								int side = GrassMath.TileCells >> level;
								int perSide = GrassMath.ItemsPerSide(level);
								int step = 1 << level;
								EnsureItems(itemCount + perSide * perSide);
								for (int iz = 0; iz < perSide; iz++)
								{
									for (int ix = 0; ix < perSide; ix++)
									{
										int nx = Mathf.Min(GrassMath.ItemSide, side - ix * GrassMath.ItemSide);
										int nz = Mathf.Min(GrassMath.ItemSide, side - iz * GrassMath.ItemSide);
										items[itemCount++] = new Item
										{
											CellX = tx * GrassMath.TileCells + ix * GrassMath.ItemSide * step,
											CellZ = tz * GrassMath.TileCells + iz * GrassMath.ItemSide * step,
											Packed = (uint)level | (main ? 8u : 0u) | (shadow ? 16u : 0u) | (uint)nx << 5 | (uint)nz << 9,
										Terrain = terrainIndex,
										};
										candidates += nx * nz;
									}
								}
								tiles++;
							}
						}
					}
					if (itemCount > groupStart)
					{
						ranges.Add(new GroupRange { Group = group, Offset = groupStart, Count = itemCount - groupStart });
					}
				}
				RememberGather(camera, in view, cell, grassDistance, shadowDistance, tiles, candidates);
			}
			// Only the game camera's figures: a scene view or capture camera rendering last used to overwrite them
			// ("0 tiles" for a camera nowhere near the grass).
			if (camera.cameraType == CameraType.Game)
			{
				LastItems = itemCount;
				LastCandidates = candidates;
				LastTiles = tiles;
				LastCamera = $"'{camera.name}' at ({eye.x:F0}, {eye.y:F0}, {eye.z:F0}), grass distance {grassDistance:F0} m, cell {cell * 100f:F1} cm";
			}
			TimeGather += TerrainInstancingProbe.Now - t1;
			TimedItems = itemCount;
			TimedTiles = tiles;
			if (itemCount == 0)
			{
				return 0;
			}
			if (itemBuffer == null || itemBuffer.count < itemCount)
			{
				if (itemBuffer != null)
				{
					retired.Add((itemBuffer, Time.frameCount));
				}
				itemBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(itemCount, items.Length), Item.Stride);
			}

			for (int i = 0; i < 6; i++)
			{
				Plane p = view.Planes[i];
				planeVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
			}
			double recordStart = TerrainInstancingProbe.Now;
			cmd.Clear();
			// A reused work list is already in the buffer (only a new gather can grow it, which reallocates).
			if (!reuse)
			{
				cmd.SetBufferData(itemBuffer, items, 0, 0, itemCount);
			}
			cmd.SetComputeVectorParam(compute, CameraId, new Vector4(eye.x, eye.y, eye.z, grassDistance));
			// The sea (SeaLifeSystem's _FishSea, which a compute shader never sees as a global): no land grass below its lowest tide.
			Vector4 sea = Shader.GetGlobalVector(FishSeaId);
			cmd.SetComputeVectorParam(compute, SeaId, new Vector4(sea.y, sea.z, 0f, 0f));
			cmd.SetComputeVectorArrayParam(compute, PlanesId, planeVectors);
			cmd.SetComputeVectorParam(compute, LightId, shadowDistance > 0f ? new Vector4(view.LightDirection.x, view.LightDirection.y, view.LightDirection.z, shadowDistance) : Vector4.zero);
			cmd.SetComputeVectorParam(compute, LatticeId, new Vector4(cell, Mathf.Max(0.1f, s.ClumpMetres), s.ClumpFacing, s.ClumpHeightVariation));
			cmd.SetComputeVectorParam(compute, ShapeParamsId, new Vector4(s.MaxLean, s.Lod0Distance, Mathf.Max(s.Lod0Distance, s.Lod1Distance), GrassMath.MaxPackedHeight));
			cmd.SetComputeVectorArrayParam(compute, RingDistanceId, ringDistance);
			cmd.SetComputeVectorArrayParam(compute, RingDensityId, ringDensity);
			cmd.SetComputeIntParam(compute, RingCountId, ringCount);
			cmd.SetComputeVectorArrayParam(compute, TypeShapeId, typeShape);
			cmd.SetComputeVectorArrayParam(compute, TypeShape2Id, typeShape2);
			cmd.SetComputeVectorParam(compute, Capacity0Id, new Vector4(capacities[0], capacities[1], capacities[2], capacities[3]));
			cmd.SetComputeVectorParam(compute, Capacity1Id, new Vector4(capacities[4], 0f, 0f, 0f));

			cmd.SetComputeBufferParam(compute, clearKernel, CountsId, countBuffer);
			cmd.DispatchCompute(compute, clearKernel, 1, 1, 1);

			compute.SetKeyword(noTerrainColour, SkipTerrainColour);
			cmd.SetComputeBufferParam(compute, generateKernel, ItemsId, itemBuffer);
			cmd.SetComputeBufferParam(compute, generateKernel, CountsId, countBuffer);
			for (int slot = 0; slot < Slots; slot++)
			{
				cmd.SetComputeBufferParam(compute, generateKernel, OutIds[slot], outBuffers[slot]);
			}
			cmd.SetComputeBufferParam(compute, generateKernel, SharedId, atlas.Shared);
			cmd.SetComputeVectorParam(compute, ColourId, new Vector4(colourFootprint, cell, ConstantTerrainColour ? 1f : 0f, 0f));
			for (int r = 0; r < ranges.Count; r++)
			{
				GroupRange range = ranges[ReverseDispatchOrder ? ranges.Count - 1 - r : r];
				GrassTerrainAtlas.Group group = range.Group;
				cmd.SetComputeTextureParam(compute, generateKernel, HeightmapId, group.Heights);
				cmd.SetComputeTextureParam(compute, generateKernel, Density0Id, group.Density0);
				cmd.SetComputeTextureParam(compute, generateKernel, Density1Id, group.Density1);
				if (!SkipTerrainColour)
				{
					cmd.SetComputeTextureParam(compute, generateKernel, SurfaceLayersId, group.Surface);
				}
				cmd.SetComputeIntParam(compute, ItemOffsetId, range.Offset);
				cmd.SetComputeIntParam(compute, ItemCountId, range.Count);
				int gx = Mathf.Min(range.Count, 65535), gy = (range.Count + 65534) / 65535;
				cmd.DispatchCompute(compute, generateKernel, gx, gy, 1);
			}
			if (camera.cameraType == CameraType.Game)
			{
				foreach (GrassTerrainAtlas.Group group in atlas.Groups)
				{
					foreach (GrassTerrain gt in group.Terrains)
					{
						if (!gt.HeightChecked)
						{
							gt.HeightChecked = true;
							RecordHeightProbe(gt, group);
						}
					}
				}
			}

			cmd.SetComputeBufferParam(compute, finalizeKernel, CountsId, countBuffer);
			cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, argsBuffer);
			cmd.DispatchCompute(compute, finalizeKernel, 1, 1, 1);
			if (NeedsArgsFence)
			{
				cmd.SetComputeBufferParam(compute, argsFenceKernel, ArgsFenceId, argsBuffer);
				cmd.SetComputeBufferParam(compute, argsFenceKernel, CountsId, countBuffer);
				cmd.DispatchCompute(compute, argsFenceKernel, 1, 1, 1);
			}
			// Always read in the editor: an overflowing slot is otherwise invisible but for its symptom.
			if ((StatsEnabled || Application.isEditor) && camera.cameraType == CameraType.Game && Time.frameCount - lastStatsFrame >= StatsInterval)
			{
				lastStatsFrame = Time.frameCount;
				cmd.RequestAsyncReadback(countBuffer, request =>
				{
					if (request.hasError)
					{
						return;
					}
					NativeArray<uint> counts = request.GetData<uint>();
					for (int i = 0; i < Slots && i < counts.Length; i++)
					{
						LastBlades[i] = (int)counts[i];
						if (counts[i] > capacities[i] && !overflowWarned[i])
						{
							overflowWarned[i] = true;
							Debug.LogWarning($"[Grass] Slot {SlotNames[i]} overflowed: {counts[i]} blades appended, capacity {capacities[i]}. "
								+ "The blades past the capacity are dropped by whole work groups in a different order every frame, so squares of grass flicker. "
								+ "Raise Grass > BladeCap on the Weather Render Profile, or thin the rings.");
						}
					}
				});
			}
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			generated = true;
			TerrainInstancingProbe.RecordMs += TerrainInstancingProbe.Now - recordStart;
			TimeRecord += TerrainInstancingProbe.Now - recordStart;
			double drawsAt = TerrainInstancingProbe.Now;
			int issued = IssueDraws(camera, terrains, eye, grassDistance, shadowDistance);
			TimeDraws += TerrainInstancingProbe.Now - drawsAt;
			return issued;
		}

		/// <summary>The five indirect draws of whatever the slots last received.</summary>
		private int IssueDraws(Camera camera, IReadOnlyList<GrassTerrain> terrains, Vector3 eye, float grassDistance, float shadowDistance)
		{
			double drawStart = TerrainInstancingProbe.Now;
			var worldBounds = new Bounds(eye, Vector3.one * (2f * grassDistance + 20f));
			int draws = 0;
			for (int slot = 0; slot < Slots; slot++)
			{
				bool shadowSlot = slot >= 3;
				if (shadowSlot && shadowDistance <= 0f)
				{
					continue;
				}
				var rp = new RenderParams(slotMaterials[slot])
				{
					camera = camera,
					worldBounds = worldBounds,
					matProps = blocks[slot],
					layer = LayerOf(terrains),
					shadowCastingMode = shadowSlot ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off,
					receiveShadows = true,
					lightProbeUsage = LightProbeUsage.Off,
					reflectionProbeUsage = ReflectionProbeUsage.Off,
					motionVectorMode = MotionVectorGenerationMode.Camera,
				};
				Graphics.RenderMeshIndirect(rp, lodMeshes[SlotLod[slot]], argsBuffer, 1, slot);
				draws++;
			}
			TerrainInstancingProbe.DrawMs += TerrainInstancingProbe.Now - drawStart;
			return draws;
		}

		/// <summary>
		/// A terrain array set's albedo copy for the atlas, or null until it is built: asking for one queues it for the
		/// next <see cref="Publish"/> (a blit and a readback per layer, outside any camera's rendering).
		/// </summary>
		private GrassAlbedoTexels AlbedoFor(FishMMO.Shared.TerrainArraySet set)
		{
			if (SkipTerrainColour || set == null || !set.IsUsable)
			{
				return null;
			}
			if (albedoTexels.TryGetValue(set, out GrassAlbedoTexels texels) && texels != null && texels.Source == set.Albedo)
			{
				return texels;
			}
			albedoWanted.Add(set);
			return null;
		}

		/// <summary>The layer the blades draw on: the first terrain's (the detail renderer's choice too).</summary>
		private static int LayerOf(IReadOnlyList<GrassTerrain> terrains)
		{
			for (int i = 0; i < terrains.Count; i++)
			{
				if (terrains[i].Terrain != null)
				{
					return terrains[i].Terrain.gameObject.layer;
				}
			}
			return 0;
		}

		/// <summary>
		/// Reads the heightmap back at a few points through the same kernel code the blades use, and logs how
		/// far it is from <see cref="TerrainData.GetInterpolatedHeight"/>: proves the texture's normalisation
		/// (<see cref="GrassTerrain.Heightmap"/>, the grass's own copy of the heights) on this platform, once per terrain.
		/// </summary>
		private void RecordHeightProbe(GrassTerrain gt, GrassTerrainAtlas.Group group)
		{
			const int n = 8;
			var points = new Vector2[n];
			for (int i = 0; i < n; i++)
			{
				points[i] = new Vector2(gt.Size.x * (0.1f + 0.8f * ((i * 0.618034f) % 1f)), gt.Size.z * (0.1f + 0.8f * ((i * 0.381966f + 0.5f) % 1f)));
			}
			var pointBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 8);
			var heightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 4);
			retired.Add((pointBuffer, Time.frameCount + 60));
			retired.Add((heightBuffer, Time.frameCount + 60));
			cmd.SetBufferData(pointBuffer, points);
			cmd.SetComputeBufferParam(compute, probeKernel, ProbePointsId, pointBuffer);
			cmd.SetComputeBufferParam(compute, probeKernel, ProbeHeightsId, heightBuffer);
			cmd.SetComputeTextureParam(compute, probeKernel, HeightmapId, group.Heights);
			cmd.SetComputeBufferParam(compute, probeKernel, SharedId, atlas.Shared);
			cmd.SetComputeIntParam(compute, ProbeTerrainId, atlas.IndexOf(gt));
			cmd.SetComputeIntParam(compute, ProbeCountId, n);
			cmd.DispatchCompute(compute, probeKernel, 1, 1, 1);
			string name = gt.Terrain != null ? gt.Terrain.name : "terrain";
			TerrainData data = gt.Data;
			Vector3 origin = gt.Origin, size = gt.Size;
			cmd.RequestAsyncReadback(heightBuffer, request =>
			{
				if (request.hasError || data == null)
				{
					return;
				}
				NativeArray<float> gpu = request.GetData<float>();
				float worst = 0f;
				for (int i = 0; i < n; i++)
				{
					float cpu = origin.y + data.GetInterpolatedHeight(points[i].x / size.x, points[i].y / size.z);
					worst = Mathf.Max(worst, Mathf.Abs(cpu - gpu[i]));
				}
				string line = $"[Grass] Heightmap check on '{name}': GPU roots vs GetInterpolatedHeight, worst {worst:F3} m over {n} points.";
				if (worst > 0.25f)
				{
					Debug.LogWarning(line + " The blades float or sink: GrassTerrain.Heightmap (the grass's own height texture, 0..1 of the terrain's height) does not read back as written on this platform.");
				}
				else if (!heightCheckReported)
				{
					// Once a session: every play and every scene load rebuilds the terrains and probes each
					// one again, and a line per terrain per load buried the console. A failure always warns.
					heightCheckReported = true;
					Debug.Log(line + " (Further checks that pass are not logged this session.)");
				}
			});
		}

		/// <summary>Whether a passing height check has been logged this session (RecordHeightProbe).</summary>
		private static bool heightCheckReported;

		private void EnsureItems(int needed)
		{
			if (items.IsCreated && items.Length >= needed)
			{
				return;
			}
			var grown = new NativeArray<Item>(Mathf.Max(needed, items.IsCreated ? items.Length * 2 : 4096), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			if (items.IsCreated)
			{
				NativeArray<Item>.Copy(items, grown, itemCount);
				items.Dispose();
			}
			items = grown;
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
			itemBuffer?.Release();
			countBuffer?.Release();
			argsBuffer?.Release();
			itemBuffer = countBuffer = argsBuffer = null;
			for (int s = 0; s < Slots; s++)
			{
				outBuffers[s]?.Release();
				outBuffers[s] = null;
			}
			if (items.IsCreated)
			{
				items.Dispose();
			}
			if (compute != null)
			{
				Object.Destroy(compute);
			}
			foreach (Material m in slotMaterials)
			{
				if (m != null)
				{
					Object.Destroy(m);
				}
			}
			if (material != null)
			{
				Object.Destroy(material);
			}
			foreach (Mesh m in lodMeshes)
			{
				if (m != null)
				{
					Object.Destroy(m);
				}
			}
			atlas.Dispose();
			albedoTexels.Clear();
		}
	}
}
