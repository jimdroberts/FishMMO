using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// The GPU-driven path of the instanced terrain renderers (trees and details each own one): instances
	/// resident in one structured buffer, culled and LOD-picked per camera by FishTerrainInstancing.compute
	/// into per-(prototype, level, view) slots, and drawn with <see cref="Graphics.RenderMeshIndirect"/> —
	/// one main command (shadows off) and one shadows-only command per prototype, level and submesh. On Vulkan the
	/// commands sharing a material are drawn by one call over a merged mesh (<see cref="BatchDraws"/>); elsewhere one
	/// call a command.
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
		/// <summary>
		/// The per-draw switch for the biomes' looks (FishFlora.hlsl; FishVegetationPasses.hlsl VegFloraLook, FishWeatherLitForwardPass.hlsl):
		/// 1 on the terrain's detail draws, read 0 everywhere else. Set like <c>_FishContactOn</c>, on the clone material and on
		/// every draw's block, by this renderer (<see cref="FloraMode"/>), the detail scatter and the detail fallback.
		/// </summary>
		public static readonly int FloraOnId = Shader.PropertyToID("_FishFloraOn");
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
			/// <summary>Its shape in the rock crevice field (<see cref="RockContactField"/>), −1 when it is not a rock taking part.</summary>
			public int RockShape = -1;
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
			/// <summary>The mesh the command draws from and its submesh there: the part's own, or its pool's (<see cref="MeshPool"/>).</summary>
			public Mesh Mesh;
			public int Submesh;
			/// <summary>The part's own mesh and submesh, whatever <see cref="Mesh"/> is (names and the pools' contents).</summary>
			public Mesh SourceMesh;
			public int SourceSubmesh;
			public RenderParams Params;
			/// <summary>The depth-prime twin drawn ahead of this lit draw (<see cref="PrimeOf"/>), or null: the camera view's alpha-tested vegetation only.</summary>
			public Material Prime;
		}

		/// <summary>
		/// A run of consecutive commands drawn by one <see cref="Graphics.RenderMeshIndirect"/> call (<see cref="BatchDraws"/>):
		/// one material, one view, one set of render parameters and one mesh (a pool's), each command a submesh of it.
		/// </summary>
		private struct Batch
		{
			public int First, Count;
			public Mesh Mesh;
			public RenderParams Params;
			public Material Prime;
			public int View;
		}

		/// <summary>
		/// Every part drawn with one material and one vertex layout, merged into one mesh, a submesh a part, so their commands
		/// can share a call. Kept across relayouts and rebuilt only when a part joins (models are only ever added).
		/// </summary>
		private sealed class MeshPool
		{
			public string Name;
			public Mesh Mesh;
			public readonly List<(Mesh Mesh, int Submesh)> Parts = new List<(Mesh, int)>();
			public readonly Dictionary<(Mesh, int), int> Index = new Dictionary<(Mesh, int), int>();
			public int Built;
		}

		/// <summary>
		/// Vulkan's SV_InstanceID includes the args' startInstance (D3D's, GL's and WebGPU's here do not, or may not use it):
		/// there every command's startInstance is its run's start, and FishIndirectSetup (FishIndirectInstancing.hlsl) reads
		/// the visible entry by the instance index alone, so no command needs to know which command it is. That is what lets
		/// one call draw many commands. Must match the shader's SHADER_API_VULKAN branch; the detail scatter follows it too.
		/// </summary>
		public static bool InstanceIdCarriesBase => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan;

		/// <summary>
		/// Where the instance index carries the run's start (<see cref="InstanceIdCarriesBase"/>): the commands that share a
		/// material, a view and their render parameters are drawn by one call over their pooled mesh (<see cref="MeshPool"/>),
		/// so Unity sets the material up once for the lot instead of once a prototype and level (2026-10-13: ~275 calls a
		/// camera at Flo Monolith's meadow, each its own SetPass, ~8 ms of the render's CPU). Off is one call a command
		/// (probe variant `nobatch`). Elsewhere it is always one call a command.
		/// </summary>
		public static bool BatchDraws = true;
		private bool batchedState;
		private readonly List<Batch> batches = new List<Batch>();
		private readonly Dictionary<(Material, string), MeshPool> pools = new Dictionary<(Material, string), MeshPool>();
		private readonly Dictionary<Mesh, string> layouts = new Dictionary<Mesh, string>();
		/// <summary>The one block every batched call draws with: the buffers, and no caster sphere (it differs by part).</summary>
		private readonly MaterialPropertyBlock batchBlock = new MaterialPropertyBlock();

		/// <summary>Commands the last camera's calls covered (with <see cref="BatchDraws"/>, several a call).</summary>
		public int LastCommandCount { get; private set; }

		/// <summary>The last camera's commands by view: the lit draws, their depth primes, and the shadow casters (each drawn into every cascade).</summary>
		public int LastMainCommands { get; private set; }
		public int LastPrimeCommands { get; private set; }
		public int LastShadowCommands { get; private set; }

		/// <summary>Diagnostics (ScenePerfProbe): count the last camera's commands by art family too (<see cref="LastFamilies"/>).</summary>
		public static bool CountFamilies;
		/// <summary>Per family (the prefab name's first word: Tree, Vine, Rock…), the last camera's main, prime and shadow commands.</summary>
		public readonly Dictionary<string, Vector3Int> LastFamilies = new Dictionary<string, Vector3Int>();

		/// <summary>Whether this frame draws batched (and the layout must have been made for it).</summary>
		private static bool Batching => BatchDraws && InstanceIdCarriesBase;

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
		/// <summary>Lit clone → its depth-prime twin (<see cref="PrimeOf"/>).</summary>
		private readonly Dictionary<Material, Material> primes = new Dictionary<Material, Material>();

		/// <summary>
		/// Whether the camera view's alpha-tested vegetation is drawn twice: its depth first (FishMMO/Vegetation Prime, one
		/// render queue earlier, the lit pass's exact coverage), then its lit pass with depth writes off, which then shades
		/// only the leaf in front at each pixel. A discarding shader that writes depth gets no early depth rejection, so
		/// every layer of every crown ran the whole lit shader: ~2 ms of the trees' 2.9 ms lit pass at Flo Monolith's meadow
		/// was pixels (ScenePerfProbe, 2026-10-08, from the pass's cost at half resolution). URP's own depth priming is off
		/// under MSAA, which the leaves' alpha-to-coverage edges need. Off is the old single draw (probe variant `noprime`).
		/// </summary>
		public static bool PrimeAlphaTested = true;
		private bool primedState = true;

		private static readonly int VegZWriteId = Shader.PropertyToID("_VegZWrite");
		/// <summary>Per slot: its level's bounding sphere in object space, grown, for the shadow caster's per-cascade culling (ShadowCascadeCulling).</summary>
		public static readonly int CasterSphereId = Shader.PropertyToID("_FishCasterSphere");
		/// <summary>
		/// How much a level's mesh sphere is grown for what the vegetation shader does to each plant: VegVary widens a crown
		/// by up to 60 % and heightens it by up to 40 %, leans it and swings its limbs, and the wind sways it. Too big only
		/// culls less; too small would drop a shadow.
		/// </summary>
		public const float CasterSphereGrowth = 1.6f, CasterSphereMetres = 1f;

		/// <summary>
		/// A drawn mesh's caster sphere (<see cref="CasterSphereId"/>): its bounds' centre, and their half diagonal grown by
		/// <see cref="CasterSphereGrowth"/> and <see cref="CasterSphereMetres"/>. Shared with the GPU detail scatter, whose
		/// bushes and pebbles draw through the same twins.
		/// </summary>
		public static Vector4 CasterSphere(Bounds b) => new Vector4(b.center.x, b.center.y, b.center.z, b.extents.magnitude * CasterSphereGrowth + CasterSphereMetres);
		private const string VegetationIndirectName = "FishMMO/Vegetation Indirect";
		private const string VegetationPrimeName = "FishMMO/Vegetation Prime";
		private readonly RangeAllocator allocator = new RangeAllocator(0);
		private readonly Vector4[] planeVectors = new Vector4[6];
		private MaterialPropertyBlock[] slotBlocks = Array.Empty<MaterialPropertyBlock>();
		private int slotCount;

		private GraphicsBuffer instances, modelBuffer, workBuffer, slotBuffer, argSlotBuffer, countBuffer, visibleBuffer, argsBuffer, commandBaseBuffer;
		private NativeArray<FishWork> work;
		private int workCount;
		/// <summary>
		/// Counts every change to the work list (<see cref="BeginCamera"/>, <see cref="AddWork"/>), and the count the GPU's
		/// copy was last written at: a list kept across frames (a reused gather) is not uploaded again.
		/// </summary>
		private int workVersion, uploadedWorkVersion = -1;
		private bool layoutDirty = true;

		/// <summary>
		/// Per instance, the shape (+1) of the rock it is in the crevice field (0: not a rock), alongside the mirror;
		/// the GPU copy (<see cref="RockSource"/>) is replaced whole when it changes, a frame late, so the instances it
		/// tags have been uploaded by then. Only renderers that registered a rock keep one.
		/// </summary>
		private NativeArray<uint> tagMirror;
		private GraphicsBuffer tagBuffer;
		private bool tagsDirty;
		private int tagsDirtyFrame;
		private bool anyRock;

		/// <summary>
		/// Counts every change to what the rock crevice field reads of this renderer (its instances uploaded, its rock tags):
		/// the field's gather runs again only when it moves (RockContactField).
		/// </summary>
		public int RockVersion { get; private set; }

		/// <summary>Draws issued for the last camera.</summary>
		public int LastDrawCount { get; private set; }

		/// <summary>Instances resident in the instance buffer.</summary>
		public int ResidentInstances => allocator.Used;

		/// <summary>Bytes of GPU buffers held.</summary>
		public long GpuBytes =>
			Size(instances) + Size(modelBuffer) + Size(workBuffer) + Size(slotBuffer) + Size(argSlotBuffer) + Size(countBuffer) + Size(visibleBuffer) + Size(argsBuffer) + Size(commandBaseBuffer);

		private static long Size(GraphicsBuffer b) => b != null ? (long)b.count * b.stride : 0;

		private readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO terrain GPU culling" };
		/// <summary>The culling's GPU time, as a marker the profiler and ScenePerfProbe see: it runs before the camera's own render, outside every URP marker.</summary>
		private static readonly CustomSampler CullingSampler = CustomSampler.Create("FishMMO terrain culling (GPU)", true);

		/// <summary>
		/// Whether this renderer's draws take the contact blend with the ground (FishGroundColour.hlsl <c>_FishContactOn</c>):
		/// 1 for trees and rocks (their bases draw the terrain and flare into it), 0 none. Set by the owner right after
		/// <see cref="TryCreate"/>; written on its clones and per-draw blocks.
		/// </summary>
		public float ContactMode;

		/// <summary>
		/// Whether this renderer's draws take the look of the patch each instance stands in (<see cref="FloraOnId"/>): 1 for
		/// the terrain's details (ferns, flowers, fungi, pebbles: TerrainDetailInstancing), 0 for trees and cliff rocks, whose
		/// materials it must never touch. Set by the owner right after <see cref="TryCreate"/>, before any model is added;
		/// written on its clones, their depth primes and every per-draw block.
		/// </summary>
		public float FloraMode;

		/// <summary>The most a detail's look scales it about its root (FishVegetationPasses.hlsl FISH_VEG_FLORA_SCALE_MAX).</summary>
		public const float FloraMaxScale = 1.4f;

		/// <summary>
		/// A culling radius about <paramref name="b"/>'s own centre that holds the mesh at any size its look draws it (scaled
		/// about the root, the mesh's origin, by 0.7 to <see cref="FloraMaxScale"/>): the scaled sphere's centre moves by
		/// |centre|·(s − 1) and its radius grows by s. The caster sphere (<see cref="CasterSphere"/>) is already wider than this.
		/// </summary>
		public static float FloraSphereRadius(Bounds b) => b.extents.magnitude * FloraMaxScale + b.center.magnitude * (FloraMaxScale - 1f);

		private TerrainGpuRenderer(ComputeShader compute)
		{
			this.compute = compute;
			// The rock shader's crevice inputs, bound before this renderer's first draw.
			RockContactField.EnsureBindings();
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
			// The indirect twins' variants, compiled now rather than as the first tree of each kind comes into view. A
			// player only: there the variants are prebuilt and warming creates their GPU programs, but the editor COMPILES
			// every variant of each listed keyword set (crossed with every global multi_compile) synchronously, which
			// held the first frame 5.7 s (StartupTimeline, 2026-10-08). The editor compiles them in the background as
			// they are first drawn instead.
			if (!Application.isEditor && profile != null && profile.IndirectShaderVariants != null && !profile.IndirectShaderVariants.isWarmedUp)
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
			clone.SetFloat(FloraOnId, FloraMode);
			clones.Add(material, clone);
			return clone;
		}

		/// <summary>
		/// The depth-prime twin of a lit vegetation clone (<see cref="PrimeAlphaTested"/>), or null where there is nothing to
		/// gain or no prime shader: bark and rocks do not discard, so their own depth test already rejects what is hidden.
		/// Setting it up turns the clone's lit-pass depth writes off: from then on the clone is only ever drawn after it.
		/// </summary>
		private Material PrimeOf(Material clone)
		{
			if (clone == null || clone.shader == null || clone.shader.name != VegetationIndirectName || !clone.IsKeywordEnabled("_ALPHATEST_ON"))
			{
				return null;
			}
			if (primes.TryGetValue(clone, out Material prime))
			{
				return prime;
			}
			Shader shader = PrimeShader();
			if (shader == null)
			{
				primes.Add(clone, null);
				return null;
			}
			prime = new Material(clone) { shader = shader, name = clone.name + " (prime)", hideFlags = HideFlags.DontSave };
			// The keyword by name: a local keyword's state does not survive the change of shader.
			prime.EnableKeyword("_ALPHATEST_ON");
			// Ahead of every lit draw in the opaque pass, which sorts by render queue first.
			prime.renderQueue = clone.renderQueue - 1;
			clone.SetFloat(VegZWriteId, PrimeAlphaTested ? 0f : 1f);
			primes.Add(clone, prime);
			return prime;
		}

		private static Shader PrimeShader()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			Shader shader = profile != null && profile.VegetationPrimeShader != null ? profile.VegetationPrimeShader : Shader.Find(VegetationPrimeName);
			return shader != null && shader.isSupported ? shader : null;
		}

		/// <summary>Follows <see cref="PrimeAlphaTested"/> (a probe switch): the lit clones write depth again while it is off.</summary>
		private void SyncPrimeState()
		{
			if (primedState == PrimeAlphaTested)
			{
				return;
			}
			primedState = PrimeAlphaTested;
			foreach (KeyValuePair<Material, Material> pair in primes)
			{
				if (pair.Key != null && pair.Value != null)
				{
					pair.Key.SetFloat(VegZWriteId, primedState ? 0f : 1f);
				}
			}
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
				// A detail's look may draw it larger than its mesh (FloraSphereRadius): culled by the largest it can be.
				SphereRadius = FloraMode > 0.5f ? FloraSphereRadius(localBounds) : localBounds.extents.magnitude,
				LevelCount = (uint)levels.Length,
				ShadowMask = shadowMask,
			};
			for (int l = 0; l < levels.Length; l++)
			{
				data.Transitions[l] = transitions[l];
				data.FadeWidths[l] = fadeWidths != null && l < fadeWidths.Length ? fadeWidths[l] : 0f;
			}
			var model = new Model { Name = name ?? $"model {models.Count}", Data = data, Levels = resolved };
			// A rock (drawn by the Weather Lit twin) of a renderer that takes the contact blend joins the crevice field;
			// trees (the vegetation twin) never do.
			if (ContactMode > 0.5f && IsRock(resolved))
			{
				model.RockShape = RockContactField.RegisterShape(levels, localBounds);
				if (model.RockShape >= 0 && !anyRock)
				{
					anyRock = true;
					RockContactField.AddRenderer(this);
					// Only now: a renderer with no shape in the contact field (the details) never carries the tags.
					if (mirror.IsCreated)
					{
						EnsureTags(mirror.Length);
					}
				}
			}
			models.Add(model);
			layoutDirty = true;
			return models.Count - 1;
		}

		private static bool IsRock(PartSource[][] levels)
		{
			Material m = levels[0].Length > 0 ? levels[0][0].Material : null;
			return m != null && m.shader != null && m.shader.name.EndsWith("Weather Lit Indirect");
		}

		/// <summary>
		/// Marks a resident run as instances of <paramref name="model"/> for the rock crevice field (a no-op unless the
		/// model is a rock taking part). Owners call it after <see cref="Upload"/>, once per run of one model.
		/// </summary>
		public void TagRun(int offset, int count, int model)
		{
			if (!anyRock || offset < 0 || count <= 0 || model < 0 || model >= models.Count || models[model].RockShape < 0 || !tagMirror.IsCreated)
			{
				return;
			}
			uint tag = (uint)models[model].RockShape + 1u;
			int end = Mathf.Min(offset + count, tagMirror.Length);
			for (int i = offset; i < end; i++)
			{
				tagMirror[i] = tag;
			}
			tagsDirty = true;
			tagsDirtyFrame = Time.frameCount;
		}

		/// <summary>The tag mirror at the instance mirror's capacity, keeping what it held.</summary>
		private void EnsureTags(int capacity)
		{
			if (tagMirror.IsCreated && tagMirror.Length >= capacity)
			{
				return;
			}
			var grownTags = new NativeArray<uint>(capacity, Allocator.Persistent, NativeArrayOptions.ClearMemory);
			if (tagMirror.IsCreated)
			{
				NativeArray<uint>.Copy(tagMirror, grownTags, tagMirror.Length);
				tagMirror.Dispose();
			}
			tagMirror = grownTags;
			tagsDirty = true;
			tagsDirtyFrame = Time.frameCount;
		}

		/// <summary>
		/// This renderer's GPU instances and their rock tags for the crevice field's gather, or false while there is
		/// nothing to read (no rock, the GPU buffer about to be recreated).
		/// </summary>
		public bool RockSource(out GraphicsBuffer instanceBuffer, out GraphicsBuffer tags, out int count)
		{
			instanceBuffer = instances;
			tags = null;
			count = 0;
			if (!anyRock || instances == null || instancesStale || !tagMirror.IsCreated)
			{
				return false;
			}
			if (tagBuffer == null || tagBuffer.count != tagMirror.Length)
			{
				tagBuffer?.Release();
				tagBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tagMirror.Length, sizeof(uint));
				tagsDirty = true;
				tagsDirtyFrame = Time.frameCount - 1;
			}
			// A frame late: the instances tagged this frame reach the GPU with this frame's camera.
			if (tagsDirty && Time.frameCount > tagsDirtyFrame)
			{
				tagBuffer.SetData(tagMirror);
				tagsDirty = false;
				RockVersion++;
			}
			tags = tagBuffer;
			count = Mathf.Min(instances.count, tagMirror.Length);
			return true;
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
				if (anyRock && tagMirror.IsCreated)
				{
					for (int i = offset, end = Mathf.Min(offset + r.Count, tagMirror.Length); i < end; i++)
					{
						tagMirror[i] = 0u;
					}
					tagsDirty = true;
					tagsDirtyFrame = Time.frameCount;
				}
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
			if (anyRock)
			{
				EnsureTags(capacity);
			}
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
			RockVersion++;
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
			workVersion++;
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
			workVersion++;
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
			if (batchedState != Batching)
			{
				layoutDirty = true;
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
				uploadedWorkVersion = -1;
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
				cmd.BeginSample(CullingSampler);
				Flush(cmd);
				// Only a changed list: one kept from an earlier frame's gather is in the buffer already.
				if (uploadedWorkVersion != workVersion)
				{
					cmd.SetBufferData(workBuffer, work, 0, 0, workCount);
					uploadedWorkVersion = workVersion;
				}
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
				cmd.EndSample(CullingSampler);
				context.ExecuteCommandBuffer(cmd);
				cmd.Clear();
				TerrainInstancingProbe.RecordMs += TerrainInstancingProbe.Now - recordStart;
				culled = true;
			}
			double drawStart = TerrainInstancingProbe.Now;

			var bounds = new Bounds(view.Position, Vector3.one * (2f * reach));
			SyncPrimeState();
			int draws = 0;
			if (batchedState)
			{
				draws = DrawBatches(camera, bounds);
				TerrainInstancingProbe.DrawMs += TerrainInstancingProbe.Now - drawStart;
				return draws;
			}
			int drawn = 0;
			for (int c = 0; c < commands.Count; c++)
			{
				Command command = commands[c];
				if (!Wanted(command))
				{
					continue;
				}
				drawn++;
				RenderParams rp = command.Params;
				rp.camera = camera;
				rp.worldBounds = bounds;
				rp.matProps = slotBlocks[command.Slot];
				Graphics.RenderMeshIndirect(rp, command.Mesh, argsBuffer, 1, c);
				draws++;
				if (primedState && command.Prime != null)
				{
					// The same command, so the same visible instances (its args and its slot's base), through the prime.
					rp.material = command.Prime;
					Graphics.RenderMeshIndirect(rp, command.Mesh, argsBuffer, 1, c);
					draws++;
				}
			}
			LastDrawCount = draws;
			LastCommandCount = drawn;
			TerrainInstancingProbe.DrawMs += TerrainInstancingProbe.Now - drawStart;
			return draws;
		}

		/// <summary>Whether a command's level is one this camera's work can reach, in its view.</summary>
		private bool Wanted(in Command command)
		{
			Model m = models[command.Model];
			int mask = command.View == 0 ? m.MainMask : m.ShadowMask;
			return (mask & (1 << command.Level)) != 0;
		}

		/// <summary>
		/// Wanted commands of a batch at most this many unwanted ones apart are drawn by the same call. Each command a call
		/// covers is a GPU draw (an empty one where no work reached its level, still counted in the frame's draw calls),
		/// while a further call of the same batch costs no material set-up: so none (2026-10-13, Flo Monolith meadow: a
		/// gap of 2 covered 383 commands for the 235 wanted, at no CPU gain).
		/// </summary>
		public static int BatchGap = 0;

		/// <summary>
		/// A batch's wanted commands, in calls over runs of them (<see cref="BatchGap"/>), each with its prime ahead of it.
		/// The calls of a batch share their material and block, so only the first sets the material up. Returns the calls.
		/// </summary>
		private int DrawBatches(Camera camera, Bounds bounds)
		{
			int draws = 0, covered = 0, main = 0, primes = 0, shadows = 0;
			LastFamilies.Clear();
			for (int b = 0; b < batches.Count; b++)
			{
				Batch batch = batches[b];
				RenderParams rp = batch.Params;
				rp.camera = camera;
				rp.worldBounds = bounds;
				rp.matProps = batchBlock;
				int end = batch.First + batch.Count;
				int c = batch.First;
				while (c < end)
				{
					if (!Wanted(commands[c]))
					{
						c++;
						continue;
					}
					int first = c, last = c;
					for (int next = c + 1; next < end && next - last <= BatchGap + 1; next++)
					{
						if (Wanted(commands[next]))
						{
							last = next;
						}
					}
					int count = last - first + 1;
					rp.material = batch.Params.material;
					Graphics.RenderMeshIndirect(rp, batch.Mesh, argsBuffer, count, first);
					draws++;
					covered += count;
					bool primed = primedState && batch.Prime != null;
					if (batch.View == 0)
					{
						main += count;
						primes += primed ? count : 0;
					}
					else
					{
						shadows += count;
					}
					if (CountFamilies)
					{
						Tally(first, count, batch.View, primed);
					}
					if (primed)
					{
						rp.material = batch.Prime;
						Graphics.RenderMeshIndirect(rp, batch.Mesh, argsBuffer, count, first);
						draws++;
					}
					c = last + 1;
				}
			}
			LastDrawCount = draws;
			LastCommandCount = covered;
			LastMainCommands = main;
			LastPrimeCommands = primes;
			LastShadowCommands = shadows;
			return draws;
		}

		/// <summary>Adds a call's commands to their models' families (<see cref="LastFamilies"/>).</summary>
		private void Tally(int first, int count, int view, bool primed)
		{
			for (int c = first; c < first + count; c++)
			{
				string name = models[commands[c].Model].Name ?? "?";
				int cut = name.IndexOf('_');
				string family = cut > 0 ? name.Substring(0, cut) : name;
				LastFamilies.TryGetValue(family, out Vector3Int n);
				if (view == 0)
				{
					n.x++;
					n.y += primed ? 1 : 0;
				}
				else
				{
					n.z++;
				}
				LastFamilies[family] = n;
			}
		}

		// ── Layout ────────────────────────────────────────────────────

		private void Relayout()
		{
			layoutDirty = false;
			TerrainInstancingProbe.Relayouts++;
			slotCount = 0;
			var capacities = new List<int>();
			var bounded = new Dictionary<int, Bounds>();
			commands.Clear();
			batches.Clear();
			batchedState = Batching;
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
							commands.Add(new Command
							{
								Model = id, Level = l, View = v, Slot = slot,
								Mesh = part.Mesh, Submesh = part.Submesh, SourceMesh = part.Mesh, SourceSubmesh = part.Submesh,
								Params = rp, Prime = v == 0 ? PrimeOf(part.Material) : null,
							});
							Bounds partBounds = part.Mesh.bounds;
							if (bounded.TryGetValue(slot, out Bounds sofar))
							{
								sofar.Encapsulate(partBounds);
								bounded[slot] = sofar;
							}
							else
							{
								bounded[slot] = partBounds;
							}
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
			// Until every pool is merged (a few a frame: PoolBudgetMilliseconds), a call a command from the parts' own meshes; the
			// next camera's layout, seeing it unbatched, goes on with the merging.
			if (batchedState && !PoolsReady())
			{
				batchedState = false;
			}
			if (batchedState)
			{
				GroupCommands();
			}
			// Where the instance index carries it, each command's startInstance is its run's start (InstanceIdCarriesBase).
			bool startsCarried = InstanceIdCarriesBase;
			var args = new List<GraphicsBuffer.IndirectDrawIndexedArgs>(commands.Count);
			var argSlots = new List<uint>(commands.Count);
			foreach (Command command in commands)
			{
				args.Add(new GraphicsBuffer.IndirectDrawIndexedArgs
				{
					indexCountPerInstance = command.Mesh.GetIndexCount(command.Submesh),
					startIndex = command.Mesh.GetIndexStart(command.Submesh),
					baseVertexIndex = (uint)command.Mesh.GetBaseVertex(command.Submesh),
					instanceCount = 0,
					startInstance = startsCarried ? (uint)bases[command.Slot] : 0u,
				});
				argSlots.Add((uint)command.Slot);
			}
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
				// No bounds (a slot nothing draws from): w 0, and the caster culls nothing for it.
				Vector4 sphere = Vector4.zero;
				if (bounded.TryGetValue(s, out Bounds b))
				{
					sphere = CasterSphere(b);
				}
				slotBlocks[s].SetVector(CasterSphereId, sphere);
			}
			EnsureGpuBuffer();
			BindBlocks();
		}

		/// <summary>
		/// Reorders <see cref="commands"/> into <see cref="batches"/>: those of one material, view and set of render
		/// parameters, whose parts share a pool (<see cref="PoolOf"/>), become one run drawn from the pool's mesh. A part no
		/// pool can take (an unreadable mesh, not triangles) is a batch of its own, on its own mesh, as before.
		/// </summary>
		private void GroupCommands()
		{
			// What a call fixes besides its commands: the material, the mesh (a pool, or a lone part's own mesh and
			// submesh), the view (shadow casting) and the rest of the part's render parameters.
			var keyed = new Dictionary<((Material, MeshPool, Mesh, int), (int, int, bool, ReflectionProbeUsage, LightProbeUsage, uint, MotionVectorGenerationMode)), int>();
			var members = new List<List<(Command Command, MeshPool Pool)>>();
			var heads = new List<Batch>();
			foreach (Command command in commands)
			{
				MeshPool pool = PoolOf(command.Params.material, command.SourceMesh, command.SourceSubmesh);
				RenderParams p = command.Params;
				var key = ((p.material, pool, pool == null ? command.SourceMesh : null, pool == null ? command.SourceSubmesh : -1),
					(command.View, p.layer, p.receiveShadows, p.reflectionProbeUsage, p.lightProbeUsage, p.renderingLayerMask, p.motionVectorMode));
				if (!keyed.TryGetValue(key, out int b))
				{
					b = heads.Count;
					keyed.Add(key, b);
					heads.Add(new Batch { Params = p, Prime = command.Prime, View = command.View });
					members.Add(new List<(Command, MeshPool)>());
				}
				members[b].Add((command, pool));
			}
			// Every pool a new part joined is merged again, before any command reads its submeshes (PoolsReady has, normally).
			foreach (MeshPool pool in pools.Values)
			{
				if (pool.Built != pool.Parts.Count)
				{
					BuildPool(pool);
				}
			}
			int rebuilt = mergedSinceLog;
			mergedSinceLog = 0;
			commands.Clear();
			for (int b = 0; b < heads.Count; b++)
			{
				Batch batch = heads[b];
				batch.First = commands.Count;
				batch.Count = members[b].Count;
				foreach ((Command member, MeshPool pool) in members[b])
				{
					Command c = member;
					if (pool != null)
					{
						c.Mesh = pool.Mesh;
						c.Submesh = pool.Index[(c.SourceMesh, c.SourceSubmesh)];
					}
					commands.Add(c);
				}
				batch.Mesh = commands[batch.First].Mesh;
				batches.Add(batch);
			}
			if (rebuilt > 0)
			{
				long vertices = 0;
				foreach (MeshPool pool in pools.Values)
				{
					vertices += pool.Mesh != null ? pool.Mesh.vertexCount : 0;
				}
				Debug.Log($"[Terrain GPU] {commands.Count} draw commands in {batches.Count} batched calls over {pools.Count} pooled meshes ({rebuilt} merged now, {vertices:N0} vertices in all).");
			}
		}

		/// <summary>
		/// Main-thread milliseconds a layout may spend merging pools (each a CombineMeshes over its parts): all of them at once
		/// was a 0.5 s frame as a scene's props first came in (1.26 M vertices, 2026-10-13).
		/// </summary>
		public const double PoolBudgetMilliseconds = 4.0;
		private int mergedSinceLog;

		/// <summary>
		/// Every command's part in its pool, and the pools a part joined merged for up to <see cref="PoolBudgetMilliseconds"/>
		/// (one at least); true once none is left to merge.
		/// </summary>
		private bool PoolsReady()
		{
			foreach (Command command in commands)
			{
				PoolOf(command.Params.material, command.SourceMesh, command.SourceSubmesh);
			}
			var watch = Stopwatch.StartNew();
			bool ready = true;
			foreach (MeshPool pool in pools.Values)
			{
				if (pool.Built == pool.Parts.Count)
				{
					continue;
				}
				if (mergedThisLayout > 0 && watch.Elapsed.TotalMilliseconds >= PoolBudgetMilliseconds)
				{
					ready = false;
					break;
				}
				BuildPool(pool);
				mergedThisLayout++;
			}
			mergedThisLayout = 0;
			return ready;
		}

		private int mergedThisLayout;

		/// <summary>
		/// The pool a part's commands draw from, joining it if new; null where the part cannot be merged (its mesh is not
		/// readable, or the submesh is not triangles) or the material is gone.
		/// </summary>
		private MeshPool PoolOf(Material material, Mesh mesh, int submesh)
		{
			if (material == null || mesh == null || !mesh.isReadable || submesh < 0 || submesh >= mesh.subMeshCount
				|| mesh.GetTopology(submesh) != MeshTopology.Triangles || mesh.blendShapeCount > 0)
			{
				return null;
			}
			var key = (material, LayoutOf(mesh));
			if (!pools.TryGetValue(key, out MeshPool pool))
			{
				pool = new MeshPool { Name = $"{material.name} (pool {pools.Count})" };
				pools.Add(key, pool);
			}
			if (!pool.Index.ContainsKey((mesh, submesh)))
			{
				pool.Index.Add((mesh, submesh), pool.Parts.Count);
				pool.Parts.Add((mesh, submesh));
			}
			return pool;
		}

		/// <summary>A mesh's vertex layout (attribute, format, dimension and stream of each), the pools' second key: only like meshes merge.</summary>
		private string LayoutOf(Mesh mesh)
		{
			if (layouts.TryGetValue(mesh, out string layout))
			{
				return layout;
			}
			var sb = new System.Text.StringBuilder();
			foreach (VertexAttributeDescriptor d in mesh.GetVertexAttributes())
			{
				sb.Append((int)d.attribute).Append(':').Append((int)d.format).Append('x').Append(d.dimension).Append('@').Append(d.stream).Append(';');
			}
			layout = sb.ToString();
			layouts.Add(mesh, layout);
			return layout;
		}

		/// <summary>
		/// Merges a pool's parts into a new mesh, part i its submesh i (32-bit indices, the parts' own vertices untouched),
		/// and releases the old one. A rebuild merges from the parts again.
		/// </summary>
		private void BuildPool(MeshPool pool)
		{
			var combine = new CombineInstance[pool.Parts.Count];
			for (int i = 0; i < combine.Length; i++)
			{
				combine[i] = new CombineInstance { mesh = pool.Parts[i].Mesh, subMeshIndex = pool.Parts[i].Submesh, transform = Matrix4x4.identity };
			}
			var mesh = new Mesh { name = pool.Name, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
			mesh.CombineMeshes(combine, false, false, false);
			if (pool.Mesh != null)
			{
				Object.Destroy(pool.Mesh);
			}
			pool.Mesh = mesh;
			pool.Built = pool.Parts.Count;
			mergedSinceLog++;
			if (TerrainInstancingProbe.On)
			{
				TerrainInstancingProbe.Reason($"pool {pool.Name} merged: {pool.Parts.Count} parts");
			}
		}

		/// <summary>Binds the instance, visible and command-base buffers on every per-draw block and on every clone material.</summary>
		private void BindBlocks()
		{
			Bind(batchBlock);
			// The batched calls' parts differ in size: no caster sphere, so the casters' per-cascade culling passes them all.
			batchBlock.SetVector(CasterSphereId, Vector4.zero);
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
			foreach (Material prime in primes.Values)
			{
				if (prime == null)
				{
					continue;
				}
				prime.SetFloat(ContactOnId, ContactMode);
				prime.SetFloat(FloraOnId, FloraMode);
				if (instances != null)
				{
					prime.SetBuffer(InstancesId, instances);
				}
				if (visibleBuffer != null)
				{
					prime.SetBuffer(VisibleId, visibleBuffer);
				}
				if (commandBaseBuffer != null)
				{
					prime.SetBuffer(CommandBasesId, commandBaseBuffer);
				}
			}
		}

		private void Bind(MaterialPropertyBlock block)
		{
			block.SetFloat(ContactOnId, ContactMode);
			block.SetFloat(FloraOnId, FloraMode);
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

		/// <summary>
		/// What a gather whose GPU work list may be kept across frames (<see cref="WorkReuse"/>) widens its tests by: chunks
		/// <see cref="Slack"/> metres nearer (and farther, for the levels a run can reach) count, against the view's planes
		/// pushed out for what the camera sees (<see cref="ViewPlanes"/>) and for the shadows' sweep (<see cref="ShadowPlanes"/>).
		/// Default (no planes): the exact gather.
		/// </summary>
		public readonly struct ReuseBounds
		{
			public readonly float Slack;
			public readonly Plane[] ViewPlanes, ShadowPlanes;

			public ReuseBounds(float slack, Plane[] viewPlanes, Plane[] shadowPlanes)
			{
				Slack = slack;
				ViewPlanes = viewPlanes;
				ShadowPlanes = shadowPlanes;
			}
		}

		/// <summary>
		/// Keeps a camera's GPU work list across frames while the camera stays within <see cref="Metres"/> of where it was
		/// gathered and turns (and the sun turns) less than <see cref="Degrees"/>, as the props' (CliffRockInstancing) and
		/// the grass's lists are kept. The list is gathered against what that move and turn can bring into reach: chunks
		/// <see cref="Metres"/> nearer, the frustum widened by <see cref="ViewMargin"/> and, for the shadows' sweep, by
		/// <see cref="ShadowMargin"/>, and each run's levels and shadow candidacy from its distances widened the same way.
		/// It only ever lists more than an exact gather would; the compute still culls every instance against the camera's
		/// real frustum, distances and shadow reach each frame, so the same instances are drawn.
		/// </summary>
		public sealed class WorkReuse
		{
			public const float Metres = 1.5f, Degrees = 3f;

			private Camera camera;
			private Vector3 eye, light;
			private Quaternion rotation;
			private Matrix4x4 projection;
			private bool shadows;
			private float shadowDistance;
			private int signature;
			private bool valid;

			/// <summary>How far the frustum's planes are pushed out for what the camera can see within the move and turn.</summary>
			public static float ViewMargin(float reach) => Metres + reach * Degrees * Mathf.Deg2Rad;

			/// <summary>
			/// How far they are pushed out for the shadows' sweep: a sweep can end up to twice the shadow distance (and a
			/// chunk's span, <paramref name="chunkMetres"/>) from the camera, and the sun's own turn moves its far end by up to
			/// the shadow distance times the angle.
			/// </summary>
			public static float ShadowMargin(float reach, float shadowDistance, float chunkMetres) =>
				Metres + (Mathf.Max(reach, 2f * shadowDistance + chunkMetres) + shadowDistance) * Degrees * Mathf.Deg2Rad;

			/// <summary>The planes pushed out by <paramref name="margin"/> metres, into <paramref name="into"/> (six).</summary>
			public static Plane[] Widen(Plane[] planes, float margin, Plane[] into)
			{
				for (int i = 0; i < 6; i++)
				{
					into[i] = new Plane(planes[i].normal, planes[i].distance + margin);
				}
				return into;
			}

			/// <summary>Whether the list gathered last still serves this camera; <paramref name="signature"/> folds whatever else the gather read.</summary>
			public bool Still(Camera camera, in TerrainTreeField.View view, int signature)
			{
				if (!valid || camera != this.camera || signature != this.signature || view.Shadows != shadows || view.ShadowDistance != shadowDistance
					|| !camera.projectionMatrix.Equals(projection))
				{
					return false;
				}
				if ((view.Position - eye).sqrMagnitude > Metres * Metres || Quaternion.Angle(camera.transform.rotation, rotation) > Degrees)
				{
					return false;
				}
				return !view.Shadows || Vector3.Angle(view.LightDirection, light) <= Degrees;
			}

			public void Remember(Camera camera, in TerrainTreeField.View view, int signature)
			{
				this.camera = camera;
				this.signature = signature;
				eye = view.Position;
				light = view.LightDirection;
				rotation = camera.transform.rotation;
				projection = camera.projectionMatrix;
				shadows = view.Shadows;
				shadowDistance = view.ShadowDistance;
				valid = true;
			}

			public void Forget()
			{
				valid = false;
				camera = null;
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
			if (tagMirror.IsCreated)
			{
				tagMirror.Dispose();
			}
			tagBuffer?.Release();
			tagBuffer = null;
			RockContactField.RemoveRenderer(this);
			foreach (Material clone in clones.Values)
			{
				if (clone != null)
				{
					Object.Destroy(clone);
				}
			}
			clones.Clear();
			foreach (Material prime in primes.Values)
			{
				if (prime != null)
				{
					Object.Destroy(prime);
				}
			}
			primes.Clear();
			foreach (MeshPool pool in pools.Values)
			{
				if (pool.Mesh != null)
				{
					Object.Destroy(pool.Mesh);
				}
			}
			pools.Clear();
			layouts.Clear();
			batches.Clear();
			residents.Clear();
			models.Clear();
			commands.Clear();
		}
	}
}
