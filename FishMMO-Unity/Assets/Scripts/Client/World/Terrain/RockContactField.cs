using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Rock crevices: where one rock meets another, the seam darkens toward its bottom and fills with the surrounding
	/// ground's own material (FishRockContact.hlsl). Distance fields, measured in the world: the same from every angle
	/// and distance, unlike a screen-space blend. The same fields and grid feed the seam fillets (<see cref="RockFillets"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One distance field per rock shape.</b> When a <see cref="TerrainGpuRenderer"/> registers a rock model
	/// (<see cref="RegisterShape"/>), its mesh (the finest level up to <see cref="TargetTriangles"/> triangles) is read once
	/// and its SIGNED distance field (local units, negative inside) drawn into a 32³ brick of a 16-bit float 3D atlas by
	/// FishRockSdf.shader, as many slices a frame as <see cref="BakeBudget"/> allows. 64 KB a shape. Drawn, not computed,
	/// because WebGPU's compute stores only 32-bit floats. Each rock model also names its LOOK (<see cref="RegisterLook"/>:
	/// its material), carried in its instances' tags, so a fillet knows which rock's material to wear.
	/// </para>
	/// <para>
	/// <b>A grid around the camera, every frame.</b> FishRockField.compute lists the rocks whose box reaches a
	/// <see cref="GridCells"/> grid of <see cref="CellSize"/> m cells around the main camera, from every registered
	/// renderer's GPU instance buffer and its per-instance shape tags (<see cref="TerrainGpuRenderer.TagRun"/>), and
	/// records up to <see cref="Slots"/> per cell. A rock pixel then measures its distance to the OTHER rocks in its cell through
	/// their own fields. Rocks only: trees never register a shape.
	/// </para>
	/// <para>
	/// Client, play mode. Every input the rock shader reads stays bound (placeholders before anything exists), so no
	/// draw ever meets an unbound buffer.
	/// </para>
	/// </remarks>
	public static class RockContactField
	{
		/// <summary>The gather's GPU time, as a marker the profiler's GPU module and ScenePerfProbe see (a string sample has no GPU timing).</summary>
		private static readonly CustomSampler GatherSample = CustomSampler.Create("FishMMO rock crevices (gather)", true);

		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishRockField.compute";
		public const string SdfShaderName = "Hidden/FishMMO/RockSdf";


		/// <summary>Voxels along each side of a shape's brick (FISH_ROCK_BRICK in the shaders).</summary>
		public const int Brick = 32;

		/// <summary>Bricks along the atlas's x (its width is this × <see cref="Brick"/>); it grows in z.</summary>
		public const int BricksX = 16;

		/// <summary>
		/// List slots per grid cell (FISH_ROCK_SLOTS). Rocks enter a cell by an atomic counter, in a different order every
		/// frame: a cell with more rocks than slots would drop a different one each frame and its crevices flicker, so
		/// this is generous for dense cliff stacks (a rock's own entry takes one).
		/// </summary>
		public const int Slots = 8;

		/// <summary>The grid around the camera, in cells: 128 × 64 × 128 m at <see cref="CellSize"/>.</summary>
		public static readonly Vector3Int GridCells = new Vector3Int(64, 32, 64);

		public const float CellSize = 2f;

		/// <summary>Rocks the list holds; more near the camera are dropped (the grid then misses them).</summary>
		public const int ListCapacity = 8192;

		/// <summary>
		/// The finest level at or under this many triangles is the shape's field source (the coarsest when none is). The
		/// fillets meet the rock as drawn near the camera, its finest level: a crag's field from its 972-triangle level stood
		/// up to 0.9 m off its finest surface (p95 0.5 m), from that level itself p95 0.1–0.2 m (2026-10-21, offline).
		/// </summary>
		public const int TargetTriangles = 16000;

		/// <summary>
		/// Voxel–triangle tests drawn per frame while a field the grid can already read is built (its nearest rock within
		/// <see cref="NearDistance"/>): the slices of a shape a frame takes are as many as fit (at least one). 48 M cost 5.7 ms
		/// of GPU a frame in the browser (2026-11-04), so this is about 1.4 ms, and a shape (some 300 M tests) takes a third of
		/// a second.
		/// </summary>
		public const long BakeBudget = 12L * 1000 * 1000;

		/// <summary>
		/// The same while every field wanted near the grid is drawn and those left are only in the margin
		/// (<see cref="DemandMarginXZ"/>): about 0.5 ms. Of 127 shapes wanted round the settlement test camera
		/// (2026-11-05) the grid itself held none.
		/// </summary>
		public const long MarginBakeBudget = 4L * 1000 * 1000;

		/// <summary>Metres from the camera to a shape's nearest rock box within which its field takes <see cref="BakeBudget"/>: half the grid's height, so inside the grid.</summary>
		public static float NearDistance => GridCells.y * CellSize * 0.5f;

		/// <summary>
		/// Metres past the grid (each side, across; <see cref="DemandMarginY"/> up and down) within which a rock's shape is
		/// wanted: only those fields are drawn, nearest the camera first. The gather never reads a field of a rock whose box
		/// misses the grid, and a scene registers hundreds of shapes (500 in the settlement test scene, 2026-11-04) of which
		/// the grid meets a few, so drawing every field cost 5.7 ms of GPU a frame for some 40 s after a scene loaded in the
		/// browser. The wanted set is measured again once the camera has moved half a margin along any axis (the grid then
		/// still lies inside the region it was measured for, so every field the gather can read is wanted), or the rocks
		/// change (<see cref="DemandInterval"/>). A measurement reads a few columns of <see cref="RockBoxes"/> (0.03–0.5 ms
		/// in the editor), so the margin is the lead a field gets to be drawn before the grid reaches it: 16 m of walking.
		/// </summary>
		public const float DemandMarginXZ = 32f;

		/// <summary>The same up and down: the grid is half as tall, and the camera seldom climbs fast.</summary>
		public const float DemandMarginY = 16f;

		/// <summary>The margins as a vector (x, y, z).</summary>
		public static Vector3 DemandMargin => new Vector3(DemandMarginXZ, DemandMarginY, DemandMarginXZ);

		/// <summary>The fewest frames between two measurements of the wanted set taken because the rocks changed (streaming uploads).</summary>
		public const int DemandInterval = 30;

		/// <summary>The most looks (rock materials) a tag can carry: 12 bits over the shape's 20 (<see cref="TagOf"/>).</summary>
		public const int MaxLooks = 4095;

		private const int RockStride = 7 * 16;

		private sealed class Shape
		{
			public Mesh Mesh;
			public Bounds Bounds;
			public bool Built;
			/// <summary>The renderers whose rocks wear this shape; the slot is freed when the last one goes.</summary>
			public readonly HashSet<TerrainGpuRenderer> Users = new HashSet<TerrainGpuRenderer>();
			/// <summary>A freed slot: no rock references it, and the next new shape takes it (<see cref="freeShapes"/>).</summary>
			public bool Free => Mesh == null;
		}

		private static readonly List<Shape> shapes = new List<Shape>();
		private static readonly List<Material> looks = new List<Material>();
		private static readonly Dictionary<Material, int> lookOf = new Dictionary<Material, int>();

		/*
		 * Ownership. Shapes and looks are keyed by mesh and material OBJECT, and a world's are new objects every time it
		 * loads (bundles reloaded), so a slot kept after its world left is never matched again: kept, the registries grew by
		 * a full set per load until the atlas passed the device's 3D texture limit (2048 deep) on the third login and every
		 * frame failed. Each slot therefore records the renderers using it and is freed when the last of them is disposed,
		 * and a freed slot is reused before the lists grow. Indices are stable while in use (a renderer's rock tags carry
		 * them), so freed slots are never compacted away; they are only ever reused.
		 */
		private static readonly Dictionary<TerrainGpuRenderer, List<int>> shapesOf = new Dictionary<TerrainGpuRenderer, List<int>>();
		private static readonly Dictionary<TerrainGpuRenderer, List<int>> looksOf = new Dictionary<TerrainGpuRenderer, List<int>>();
		private static readonly Dictionary<int, HashSet<TerrainGpuRenderer>> lookUsers = new Dictionary<int, HashSet<TerrainGpuRenderer>>();
		private static readonly SortedSet<int> freeShapes = new SortedSet<int>();
		private static readonly SortedSet<int> freeLooks = new SortedSet<int>();

		/// <summary>The shape whose field is being drawn (−1 none), its next slice and its mesh's buffers, held until its last slice.</summary>
		private static int bakeShape = -1, bakeSlice;
		private static GraphicsBuffer bakePositions, bakeIndices;
		private static int bakeTriangles;
		private static readonly Dictionary<Mesh, int> shapeOf = new Dictionary<Mesh, int>();
		private static readonly List<TerrainGpuRenderer> renderers = new List<TerrainGpuRenderer>();

		/// <summary>
		/// Per shape, the distance from <see cref="demandEye"/> to its nearest rock whose box meets the demand region
		/// (+∞: none, its field is not wanted); what it was measured at, and the measurement's own cost for the diagnostics.
		/// </summary>
		private static float[] demand = new float[0];
		private static Vector3 demandEye = new Vector3(float.NaN, float.NaN, float.NaN);
		private static float demandReach = float.NaN, demandMs, boxesMs;
		private static int demandWanted, boxesKey, boxesFrame = NeverGathered;

		/// <summary>Far enough back that the first frame counts as an interval later (int.MinValue would overflow the subtraction).</summary>
		private const int NeverGathered = -1000000;
		private static readonly RockBoxes boxes = new RockBoxes();
		private static bool loadLogged;

		/// <summary>
		/// Only the wanted fields are drawn (the default). Off, every shape's field is drawn as before, the wanted ones
		/// first: the harness switch "rockDemand", for A/B runs and <see cref="CheckDemand"/>.
		/// </summary>
		public static bool DemandOnly = true;

		private static ComputeShader compute;
		private static int clearKernel = -1, gatherKernel = -1;
		private static Material sdfMaterial;
		private static RenderTexture atlas;
		/// <summary>The atlas could not be created on this device; not tried again until the shapes are forgotten.</summary>
		private static bool atlasFailed;
		private static int atlasBricksZ;
		private static GraphicsBuffer bricks, list, listCount, slots, cellCounts;
		private static GraphicsBuffer dummyList, dummySlots;
		private static Texture3D dummyAtlas;
		private static bool bricksDirty;
		private static readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO rock crevices" };

		private static bool on = true;
		private static bool filletsOn;
		private static Vector4 crevice = new Vector4(0.35f, 0.45f, 0.85f, 1f);

		/// <summary>
		/// What the grid was last gathered for (its corner, and a key of everything else the gather read), and how many more
		/// frames to gather it: the rocks never move, so the grid is the same until the camera crosses a cell, a renderer
		/// uploads rocks or tags, a shape's field is built or the crevice width changes. Gathered twice on a change, so an
		/// upload recorded later in the frame it was seen is read the frame after. Every frame it cost up to 2.3 ms of GPU
		/// beside cliffs (ScenePerfProbe, 2026-10-13); a grid a frame never changes is the same list in the same cells.
		/// </summary>
		private static Vector3 gatheredMin = new Vector3(float.NaN, float.NaN, float.NaN);
		private static int gatheredKey, regathers;

		private static readonly int ListId = Shader.PropertyToID("_FishRockList");
		private static readonly int SlotsGlobalId = Shader.PropertyToID("_FishRockSlots");
		private static readonly int SdfId = Shader.PropertyToID("_FishRockSdf");
		private static readonly int GridGlobalId = Shader.PropertyToID("_FishRockGrid");
		private static readonly int GridDimsGlobalId = Shader.PropertyToID("_FishRockGridDims");
		private static readonly int AtlasId = Shader.PropertyToID("_FishRockAtlas");
		private static readonly int CreviceId = Shader.PropertyToID("_FishRockCrevice");

		private static readonly int RockListId = Shader.PropertyToID("_RockList");
		private static readonly int RockListCountId = Shader.PropertyToID("_RockListCount");
		private static readonly int RockSlotsId = Shader.PropertyToID("_RockSlots");
		private static readonly int RockCellCountsId = Shader.PropertyToID("_RockCellCounts");
		private static readonly int RockInstancesId = Shader.PropertyToID("_RockInstances");
		private static readonly int RockTagsId = Shader.PropertyToID("_RockTags");
		private static readonly int RockBricksId = Shader.PropertyToID("_RockBricks");
		private static readonly int RockInstanceCountId = Shader.PropertyToID("_RockInstanceCount");
		private static readonly int RockListCapacityId = Shader.PropertyToID("_RockListCapacity");
		private static readonly int RockGridId = Shader.PropertyToID("_RockGrid");
		private static readonly int RockGridDimsId = Shader.PropertyToID("_RockGridDims");
		private static readonly int RockReachId = Shader.PropertyToID("_RockReach");

		private static readonly int SdfPositionsId = Shader.PropertyToID("_SdfPositions");
		private static readonly int SdfIndicesId = Shader.PropertyToID("_SdfIndices");
		private static readonly int SdfTrianglesId = Shader.PropertyToID("_SdfTriangles");
		private static readonly int SdfMinId = Shader.PropertyToID("_SdfMin");
		private static readonly int SdfSizeId = Shader.PropertyToID("_SdfSize");
		private static readonly int SdfSliceId = Shader.PropertyToID("_SdfSlice");

		private static bool hooked;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			hooked = false;
			shapes.Clear();
			shapeOf.Clear();
			looks.Clear();
			lookOf.Clear();
			ClearOwnership();
			renderers.Clear();
			bakeShape = -1;
			demand = new float[0];
			demandEye = new Vector3(float.NaN, float.NaN, float.NaN);
			demandReach = float.NaN;
			boxesKey = 0;
			boxesFrame = NeverGathered;
			boxes.Clear();
			loadLogged = false;
			atlasFailed = false;
			Forget();
		}

		private static void ClearOwnership()
		{
			shapesOf.Clear();
			looksOf.Clear();
			lookUsers.Clear();
			freeShapes.Clear();
			freeLooks.Clear();
		}

		/// <summary>Gathers the grid again next time (its buffers new, or the field switched off and its globals cleared).</summary>
		private static void Forget()
		{
			gatheredMin = new Vector3(float.NaN, float.NaN, float.NaN);
			regathers = 0;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			EnsureBindings();
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			Application.quitting += Release;
#if UNITY_EDITOR
			// Scripts recompiled while playing reload in place: the GPU buffers and atlas are let go first.
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;
#endif
		}

		/// <summary>
		/// Binds every input the rock shader reads, with placeholders until the real ones exist. Called by the renderers
		/// that draw FishMMO/Weather Lit Indirect before their first draw: an unbound structured buffer is not legal everywhere.
		/// </summary>
		public static void EnsureBindings()
		{
			if (dummyList == null)
			{
				dummyList = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, RockStride);
				dummySlots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Slots, sizeof(uint));
				var empty = new uint[Slots];
				for (int i = 0; i < Slots; i++)
				{
					empty[i] = uint.MaxValue;
				}
				dummySlots.SetData(empty);
				dummyAtlas = new Texture3D(1, 1, 1, TextureFormat.RHalf, false) { name = "Rock SDF placeholder", hideFlags = HideFlags.DontSave };
				// Far outside every rock.
				dummyAtlas.SetPixel(0, 0, 0, new Color(1000f, 0f, 0f, 0f));
				dummyAtlas.Apply(false, true);
			}
			Shader.SetGlobalBuffer(ListId, list ?? dummyList);
			Shader.SetGlobalBuffer(SlotsGlobalId, slots ?? dummySlots);
			Shader.SetGlobalTexture(SdfId, atlas != null ? atlas : (Texture)dummyAtlas);
			if (list == null)
			{
				Shader.SetGlobalVector(GridDimsGlobalId, Vector4.zero);
			}
		}

		/// <summary>A renderer whose rock instances take part (it calls this when it registers its first rock).</summary>
		public static void AddRenderer(TerrainGpuRenderer renderer)
		{
			if (renderer != null && !renderers.Contains(renderer))
			{
				renderers.Add(renderer);
			}
		}

		/// <summary>
		/// A renderer that is going (its scene unloading): every shape and look only it used is freed for reuse. With the last
		/// renderer gone everything is dropped, the atlas too, so the next world starts from empty lists.
		/// </summary>
		public static void RemoveRenderer(TerrainGpuRenderer renderer)
		{
			if (renderer == null)
			{
				return;
			}
			renderers.Remove(renderer);
			if (renderers.Count == 0)
			{
				if (shapes.Count > 0 || looks.Count > 0)
				{
					ForgetShapes();
				}
				return;
			}
			bool freedAny = false;
			if (shapesOf.TryGetValue(renderer, out List<int> ownShapes))
			{
				shapesOf.Remove(renderer);
				foreach (int index in ownShapes)
				{
					if (index < 0 || index >= shapes.Count)
					{
						continue;
					}
					Shape shape = shapes[index];
					if (shape.Users.Remove(renderer) && shape.Users.Count == 0 && !shape.Free)
					{
						FreeShape(index);
						freedAny = true;
					}
				}
			}
			if (looksOf.TryGetValue(renderer, out List<int> ownLooks))
			{
				looksOf.Remove(renderer);
				foreach (int index in ownLooks)
				{
					if (lookUsers.TryGetValue(index, out HashSet<TerrainGpuRenderer> users) && users.Remove(renderer) && users.Count == 0)
					{
						FreeLook(index);
						freedAny = true;
					}
				}
			}
			if (freedAny)
			{
				// What is drawn names freed slots: built again from the renderers that are left.
				bricksDirty = true;
				RockFillets.Hide();
				Forget();
			}
		}

		/// <summary>Frees a shape slot: its mesh forgotten, its brick unused until a new shape takes the slot.</summary>
		private static void FreeShape(int index)
		{
			Shape shape = shapes[index];
			if (shape.Mesh != null && shapeOf.TryGetValue(shape.Mesh, out int at) && at == index)
			{
				shapeOf.Remove(shape.Mesh);
			}
			if (bakeShape == index)
			{
				EndBake();
				bakeShape = -1;
			}
			shape.Mesh = null;
			shape.Bounds = default;
			// Nothing to draw: the bake passes over it, and the gather never meets it (no rock carries its tag).
			shape.Built = true;
			freeShapes.Add(index);
		}

		/// <summary>Frees a look slot: its material forgotten, and the fillets' copy of it destroyed.</summary>
		private static void FreeLook(int index)
		{
			lookUsers.Remove(index);
			Material material = looks[index];
			if (material != null && lookOf.TryGetValue(material, out int at) && at == index)
			{
				lookOf.Remove(material);
			}
			looks[index] = null;
			RockFillets.ForgetLook(index);
			freeLooks.Add(index);
		}

		private static void Own(Dictionary<TerrainGpuRenderer, List<int>> owned, TerrainGpuRenderer owner, int index)
		{
			if (!owned.TryGetValue(owner, out List<int> list))
			{
				owned[owner] = list = new List<int>();
			}
			list.Add(index);
		}

		/// <summary>Drops every shape and look and the atlas holding their fields; the next world registers its own.</summary>
		private static void ForgetShapes()
		{
			EndBake();
			shapes.Clear();
			shapeOf.Clear();
			looks.Clear();
			lookOf.Clear();
			ClearOwnership();
			bakeShape = -1;
			demand = new float[0];
			demandEye = new Vector3(float.NaN, float.NaN, float.NaN);
			demandReach = float.NaN;
			boxesKey = 0;
			boxesFrame = NeverGathered;
			boxes.Clear();
			bricksDirty = true;
			RockFillets.ForgetLooks();
			if (atlas != null)
			{
				atlas.Release();
				Object.Destroy(atlas);
				atlas = null;
				atlasBricksZ = 0;
			}
			atlasFailed = false;
			if (dummyAtlas != null)
			{
				Shader.SetGlobalTexture(SdfId, dummyAtlas);
			}
			Forget();
		}

		/// <summary>
		/// The shape's brick for a rock model's levels (its field is drawn on a later frame), or −1 when none of its
		/// meshes can be read. Shapes are shared by mesh across every renderer and model.
		/// </summary>
		public static int RegisterShape(TerrainGpuRenderer owner, TerrainGpuRenderer.PartSource[][] levels, Bounds localBounds)
		{
			Mesh mesh = Source(levels);
			if (mesh == null)
			{
				return -1;
			}
			if (shapeOf.TryGetValue(mesh, out int existing))
			{
				if (owner != null && shapes[existing].Users.Add(owner))
				{
					Own(shapesOf, owner, existing);
				}
				return existing;
			}
			// Padded so a point a crevice width off the surface is still inside the brick.
			float pad = Mathf.Max(0.1f, 0.1f * Mathf.Max(localBounds.size.x, Mathf.Max(localBounds.size.y, localBounds.size.z)));
			localBounds.Expand(pad * 2f);
			if (shapes.Count == 0 && levels[0].Length > 0)
			{
				// The layer the rocks draw on, which the fillets draw on too (camera culling masks).
				RockLayer = levels[0][0].Template.layer;
			}
			int index;
			Shape shape;
			if (freeShapes.Count > 0)
			{
				// A freed slot first: the atlas only grows for shapes that are all in use at once.
				index = freeShapes.Min;
				freeShapes.Remove(index);
				shape = shapes[index];
				shape.Mesh = mesh;
				shape.Bounds = localBounds;
				shape.Built = false;
				shape.Users.Clear();
			}
			else
			{
				index = shapes.Count;
				shape = new Shape { Mesh = mesh, Bounds = localBounds };
				shapes.Add(shape);
			}
			shapeOf[mesh] = index;
			if (owner != null)
			{
				shape.Users.Add(owner);
				Own(shapesOf, owner, index);
			}
			bricksDirty = true;
			return index;
		}

		/// <summary>
		/// A rock model's look: its material (the plain one, before the renderer's indirect clone), shared by every model
		/// that wears it; −1 when it has none. The fillets copy it (<see cref="RockFillets"/>).
		/// </summary>
		public static int RegisterLook(TerrainGpuRenderer owner, Material material)
		{
			if (material == null)
			{
				return -1;
			}
			int index;
			if (!lookOf.TryGetValue(material, out index))
			{
				if (freeLooks.Count > 0)
				{
					index = freeLooks.Min;
					freeLooks.Remove(index);
					looks[index] = material;
				}
				else if (looks.Count >= MaxLooks)
				{
					return -1;
				}
				else
				{
					index = looks.Count;
					looks.Add(material);
				}
				lookOf[material] = index;
			}
			if (owner != null)
			{
				if (!lookUsers.TryGetValue(index, out HashSet<TerrainGpuRenderer> users))
				{
					lookUsers[index] = users = new HashSet<TerrainGpuRenderer>();
				}
				if (users.Add(owner))
				{
					Own(looksOf, owner, index);
				}
			}
			return index;
		}

		/// <summary>The looks registered so far, by index.</summary>
		public static IReadOnlyList<Material> Looks => looks;

		/// <summary>
		/// An instance's tag for the gather (FishRockField.compute FishRockGather): its shape + 1 in the low 20 bits (0: not a
		/// rock), its look + 1 above them (0: none).
		/// </summary>
		public static uint TagOf(int shape, int look)
		{
			if (shape < 0)
			{
				return 0u;
			}
			uint l = look >= 0 && look < MaxLooks ? (uint)(look + 1) : 0u;
			return ((uint)(shape + 1) & 0xFFFFFu) | (l << 20);
		}

		/// <summary>The readable mesh of the finest level at or under <see cref="TargetTriangles"/> (else the coarsest).</summary>
		private static Mesh Source(TerrainGpuRenderer.PartSource[][] levels)
		{
			Mesh chosen = null;
			for (int l = 0; l < levels.Length; l++)
			{
				Mesh m = levels[l].Length > 0 ? levels[l][0].Mesh : null;
				if (m == null || !m.isReadable)
				{
					continue;
				}
				chosen = m;
				if (Triangles(m) <= TargetTriangles)
				{
					break;
				}
			}
			return chosen;
		}

		private static long Triangles(Mesh m)
		{
			long n = 0;
			for (int s = 0; s < m.subMeshCount; s++)
			{
				n += m.GetIndexCount(s) / 3;
			}
			return n;
		}

		/// <summary>Takes the profile's settings ("Rock crevices"), live.</summary>
		private static void ReadProfile()
		{
			WeatherRenderProfile p = WeatherRenderProfile.Active;
			if (p == null)
			{
				return;
			}
			on = p.RockCrevices;
			filletsOn = RockFillets.Wanted(p);
			crevice = new Vector4(Mathf.Max(0.01f, p.RockCreviceWidth), Mathf.Clamp01(p.RockCreviceShadow), Mathf.Clamp01(p.RockCreviceSoil), on ? 1f : 0f);
		}

		private static bool EnsureResources()
		{
			if (compute == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				ComputeShader cs = profile != null ? profile.RockFieldCompute : null;
#if UNITY_EDITOR
				if (cs == null)
				{
					cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
				}
#endif
				if (cs == null || !SystemInfo.supportsComputeShaders)
				{
					return false;
				}
				compute = Object.Instantiate(cs);
				compute.hideFlags = HideFlags.DontSave;
				clearKernel = compute.FindKernel("FishRockClear");
				gatherKernel = compute.FindKernel("FishRockGather");
			}
			if (sdfMaterial == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				Shader shader = profile != null ? profile.RockSdfShader : null;
				if (shader == null)
				{
					shader = ShaderLookup.Find(SdfShaderName);
				}
				if (shader == null || !shader.isSupported)
				{
					return false;
				}
				sdfMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
			}
			if (list == null)
			{
				Forget();
				int cells = GridCells.x * GridCells.y * GridCells.z;
				list = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ListCapacity, RockStride);
				listCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
				slots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells * Slots, sizeof(uint));
				cellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, sizeof(uint));
				EnsureBindings();
			}
			return true;
		}

		/// <summary>
		/// An atlas with room for every shape (up to the device's limit); a grown one starts empty, so every field is drawn
		/// again. False when there is none.
		/// </summary>
		private static bool EnsureAtlas()
		{
			if (atlasFailed)
			{
				return false;
			}
			// The device's 3D texture depth caps the atlas: shapes past it keep no field (they stay unbuilt) rather than the
			// atlas failing to create, which failed again every frame and broke every pipeline it was bound to.
			int maxBricksZ = Mathf.Max(1, SystemInfo.maxTexture3DSize / Brick);
			int bricksZ = Mathf.Clamp((shapes.Count + BricksX - 1) / BricksX, 1, maxBricksZ);
			if (atlas != null && atlasBricksZ >= bricksZ)
			{
				return true;
			}
			if (atlas != null)
			{
				atlas.Release();
				Object.Destroy(atlas);
			}
			// Room for a few more without growing again.
			atlasBricksZ = Mathf.Min(bricksZ + 1, maxBricksZ);
			var desc = new RenderTextureDescriptor(BricksX * Brick, Brick, GraphicsFormat.R16_SFloat, 0)
			{
				dimension = TextureDimension.Tex3D,
				volumeDepth = atlasBricksZ * Brick,
				useMipMap = false,
				msaaSamples = 1,
			};
			atlas = new RenderTexture(desc)
			{
				name = "Rock SDF atlas",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				hideFlags = HideFlags.DontSave,
			};
			if (!atlas.Create())
			{
				Debug.LogError($"[Rock contact] The SDF atlas ({atlas.width}×{atlas.height}×{atlas.volumeDepth}) could not be created; rock contact is off.");
				Object.Destroy(atlas);
				atlas = null;
				atlasBricksZ = 0;
				atlasFailed = true;
				Shader.SetGlobalTexture(SdfId, dummyAtlas);
				return false;
			}
			foreach (Shape s in shapes)
			{
				s.Built = false;
			}
			EndBake();
			bricksDirty = true;
			Shader.SetGlobalTexture(SdfId, atlas);
			Shader.SetGlobalVector(AtlasId, new Vector4(BricksX, 1f / atlas.width, 1f / atlas.volumeDepth, 0f));
			return true;
		}

		/// <summary>How many shapes the atlas has bricks for: the first this many get fields, any past them none.</summary>
		private static int AtlasCapacity => atlas != null ? atlasBricksZ * BricksX : 0;

		/// <summary>The per-shape table the gather reads: min + unit (1: the field is in local units), size + built, flags (x rock).</summary>
		private static int bricksVersion;

		private static void UploadBricks()
		{
			if (!bricksDirty && bricks != null && bricks.count >= shapes.Count * 3)
			{
				return;
			}
			bricksDirty = false;
			bricksVersion++;
			int count = Mathf.Max(3, shapes.Count * 3);
			if (bricks == null || bricks.count < count)
			{
				bricks?.Release();
				bricks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(count, 64), 16);
			}
			var data = new Vector4[Mathf.Max(count, 3)];
			for (int i = 0; i < shapes.Count; i++)
			{
				Bounds b = shapes[i].Bounds;
				data[i * 3] = new Vector4(b.min.x, b.min.y, b.min.z, 1f);
				data[i * 3 + 1] = new Vector4(b.size.x, b.size.y, b.size.z, shapes[i].Built || listEveryShape ? 1f : 0f);
				data[i * 3 + 2] = new Vector4(1f, 0f, 0f, 0f);
			}
			bricks.SetData(data);
		}

		/// <summary>
		/// Draws the next slices of the shape being built (starting the next unbuilt shape when none is): as many as
		/// <see cref="BakeBudget"/> allows this frame, at least one. A shape counts as built (and joins the gather) after
		/// its last slice.
		/// </summary>
		private static void BuildNext()
		{
			if (bakeShape < 0 && !StartBake())
			{
				return;
			}
			Shape s = shapes[bakeShape];
			Bounds b = s.Bounds;
			int bx = bakeShape % BricksX, bz = bakeShape / BricksX;
			long perSlice = (long)Brick * Brick * Mathf.Max(1, bakeTriangles);
			// Full budget for a field the grid can read now, a smaller one for those only in the margin.
			bool near = bakeShape < demand.Length && demand[bakeShape] <= NearDistance;
			int count = (int)Mathf.Clamp((near ? BakeBudget : MarginBakeBudget) / perSlice, 1, Brick - bakeSlice);
			cmd.Clear();
			cmd.SetGlobalBuffer(SdfPositionsId, bakePositions);
			cmd.SetGlobalBuffer(SdfIndicesId, bakeIndices);
			cmd.SetGlobalInt(SdfTrianglesId, bakeTriangles);
			cmd.SetGlobalVector(SdfMinId, new Vector4(b.min.x, b.min.y, b.min.z, 1f));
			cmd.SetGlobalVector(SdfSizeId, new Vector4(b.size.x, b.size.y, b.size.z, 0f));
			for (int z = bakeSlice; z < bakeSlice + count; z++)
			{
				cmd.SetRenderTarget(new RenderTargetIdentifier(atlas, 0, CubemapFace.Unknown, bz * Brick + z));
				cmd.SetViewport(new Rect(bx * Brick, 0, Brick, Brick));
				cmd.SetGlobalVector(SdfSliceId, new Vector4(bx * Brick, z, 0f, 0f));
				cmd.DrawProcedural(Matrix4x4.identity, sdfMaterial, 0, MeshTopology.Triangles, 3);
			}
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			bakeSlice += count;
			if (bakeSlice < Brick)
			{
				return;
			}
			s.Built = true;
			bricksDirty = true;
			EndBake();
		}

		/// <summary>
		/// Measures again which shapes' fields are wanted (<see cref="demand"/>) when the camera has moved half the
		/// <see cref="DemandMargin"/> since, or the crevice width changed, from the rocks' boxes (<see cref="boxes"/>).
		/// Those are gathered again when the rocks or shapes change, at most every <see cref="DemandInterval"/> frames
		/// (uploads come in bursts as a scene's sets register).
		/// </summary>
		private static void RefreshDemand(Vector3 eye)
		{
			int rocksKey;
			unchecked
			{
				rocksKey = 17 * 31 + shapes.Count;
				foreach (TerrainGpuRenderer r in renderers)
				{
					rocksKey = rocksKey * 31 + (r != null ? r.RockVersion : 0);
				}
				rocksKey = rocksKey == 0 ? 1 : rocksKey;
			}
			bool regathered = false;
			if (rocksKey != boxesKey && Time.frameCount - boxesFrame >= DemandInterval)
			{
				long gatherStart = System.Diagnostics.Stopwatch.GetTimestamp();
				boxes.Clear();
				foreach (TerrainGpuRenderer r in renderers)
				{
					r?.CollectRockBoxes(boxes);
				}
				boxesKey = rocksKey;
				boxesFrame = Time.frameCount;
				boxesMs = Milliseconds(gatherStart);
				regathered = true;
			}
			Vector3 half = DemandMargin * 0.5f;
			// A NaN eye (never measured) fails every comparison, so counts as moved.
			bool kept = Mathf.Abs(eye.x - demandEye.x) <= half.x && Mathf.Abs(eye.y - demandEye.y) <= half.y && Mathf.Abs(eye.z - demandEye.z) <= half.z;
			if (!regathered && kept && demand.Length >= shapes.Count && crevice.x == demandReach)
			{
				return;
			}
			long start = System.Diagnostics.Stopwatch.GetTimestamp();
			if (demand.Length < shapes.Count)
			{
				demand = new float[Mathf.Max(shapes.Count, demand.Length * 2)];
			}
			for (int i = 0; i < demand.Length; i++)
			{
				demand[i] = float.PositiveInfinity;
			}
			boxes.Fold(RockFieldDemand.Region(eye, new Vector3(GridCells.x, GridCells.y, GridCells.z) * CellSize, CellSize, DemandMargin), eye, crevice.x, demand);
			demandWanted = 0;
			for (int i = 0; i < shapes.Count; i++)
			{
				demandWanted += float.IsPositiveInfinity(demand[i]) ? 0 : 1;
			}
			demandEye = eye;
			demandReach = crevice.x;
			demandMs = Milliseconds(start);
		}

		private static float Milliseconds(long since) =>
			(float)((System.Diagnostics.Stopwatch.GetTimestamp() - since) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

		/// <summary>A registered shape's local box (its field's brick), false for none.</summary>
		public static bool ShapeBox(int shape, out Vector3 centre, out Vector3 half)
		{
			if (shape < 0 || shape >= shapes.Count)
			{
				centre = half = Vector3.zero;
				return false;
			}
			Bounds b = shapes[shape].Bounds;
			centre = b.center;
			half = b.extents;
			return true;
		}

		/// <summary>
		/// Reads the mesh of the nearest wanted shape not yet built (<see cref="demand"/>) into the bake's buffers; false
		/// when every wanted shape is built.
		/// </summary>
		private static bool StartBake()
		{
			while (true)
			{
				int i = -1;
				float nearest = float.PositiveInfinity;
				int capacity = AtlasCapacity;
				for (int k = 0, n = Mathf.Min(Mathf.Min(shapes.Count, demand.Length), capacity); k < n; k++)
				{
					if (!shapes[k].Built && demand[k] < nearest)
					{
						nearest = demand[k];
						i = k;
					}
				}
				if (i < 0 && !DemandOnly)
				{
					// Every field, as before demand: the rest in registration order.
					for (int k = 0; k < shapes.Count && k < capacity && i < 0; k++)
					{
						i = shapes[k].Built ? -1 : k;
					}
				}
				if (i < 0)
				{
					if (!loadLogged && demandWanted > 0)
					{
						// The first time everything wanted is drawn: how long a joining player waits for the crevices.
						loadLogged = true;
						Debug.Log($"{Describe()}; every wanted field drawn by frame {Time.frameCount}, {Time.realtimeSinceStartup:F1} s");
					}
					return false;
				}
				Shape s = shapes[i];
				if (s.Mesh == null || !s.Mesh.isReadable)
				{
					s.Built = true;
					bricksDirty = true;
					continue;
				}
				Vector3[] v = s.Mesh.vertices;
				var positions = new Vector4[v.Length];
				for (int k = 0; k < v.Length; k++)
				{
					positions[k] = v[k];
				}
				var indices = new List<int>();
				for (int sub = 0; sub < s.Mesh.subMeshCount; sub++)
				{
					if (s.Mesh.GetTopology(sub) == MeshTopology.Triangles)
					{
						indices.AddRange(s.Mesh.GetTriangles(sub));
					}
				}
				if (indices.Count < 3 || positions.Length == 0)
				{
					s.Built = true;
					bricksDirty = true;
					continue;
				}
				bakePositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, positions.Length, 16);
				bakePositions.SetData(positions);
				bakeIndices = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Count, sizeof(uint));
				bakeIndices.SetData(indices);
				bakeTriangles = indices.Count / 3;
				bakeShape = i;
				bakeSlice = 0;
				return true;
			}
		}

		/// <summary>Lets the bake's mesh go (Unity defers the free until the draws recorded with it are done).</summary>
		private static void EndBake()
		{
			bakePositions?.Release();
			bakeIndices?.Release();
			bakePositions = bakeIndices = null;
			bakeShape = -1;
			bakeSlice = 0;
		}

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("RockContactField.BeginContext");
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			ReadProfile();
			Shader.SetGlobalVector(CreviceId, crevice);
			// The grid serves the crevices and the fillets: kept while either is on (the crevices' own w turns their shading off).
			if ((!on && !filletsOn) || shapes.Count == 0 || renderers.Count == 0 || !EnsureResources())
			{
				Shader.SetGlobalVector(GridDimsGlobalId, Vector4.zero);
				Forget();
				RockFillets.Hide();
				return;
			}
			if (!EnsureAtlas())
			{
				Shader.SetGlobalVector(GridDimsGlobalId, Vector4.zero);
				Forget();
				RockFillets.Hide();
				return;
			}
			Camera camera = Camera.main;
			if (camera != null)
			{
				// Only the fields the grid round this camera can read, nearest first.
				RefreshDemand(camera.transform.position);
				BuildNext();
			}
			UploadBricks();

			if (camera == null)
			{
				Shader.SetGlobalVector(GridDimsGlobalId, Vector4.zero);
				Forget();
				RockFillets.Hide();
				return;
			}
			Vector3 eye = camera.transform.position;
			Vector3 extent = new Vector3(GridCells.x, GridCells.y, GridCells.z) * CellSize;
			Vector3 min = eye - extent * 0.5f;
			min = new Vector3(Mathf.Floor(min.x / CellSize), Mathf.Floor(min.y / CellSize), Mathf.Floor(min.z / CellSize)) * CellSize;
			int cells = GridCells.x * GridCells.y * GridCells.z;
			int key;
			unchecked
			{
				key = (17 * 31 + bricksVersion) * 31 + crevice.x.GetHashCode();
				foreach (TerrainGpuRenderer r in renderers)
				{
					key = key * 31 + (r != null ? r.RockVersion : 0);
				}
			}
			if (min != gatheredMin || key != gatheredKey)
			{
				gatheredMin = min;
				gatheredKey = key;
				regathers = 2;
			}
			// The fillets start a build only from a grid no gather is pending on: the one in the buffers is then this min's.
			bool settled = regathers <= 0;
			if (!settled)
			{
				regathers--;
				Gather(min, cells);
			}
			if (filletsOn)
			{
				RockFillets.Frame(camera, min, key, settled && list != null);
			}
			else
			{
				RockFillets.Hide();
			}
		}

		/// <summary>Lists the rocks round the grid at <paramref name="min"/> into its cells (FishRockClear, FishRockGather).</summary>
		private static void Gather(Vector3 min, int cells)
		{
			cmd.Clear();
			cmd.BeginSample(GatherSample);
			cmd.SetComputeVectorParam(compute, RockGridId, new Vector4(min.x, min.y, min.z, CellSize));
			cmd.SetComputeIntParams(compute, RockGridDimsId, GridCells.x, GridCells.y, GridCells.z, cells);
			cmd.SetComputeFloatParam(compute, RockReachId, crevice.x);
			cmd.SetComputeIntParam(compute, RockListCapacityId, ListCapacity);
			cmd.SetComputeBufferParam(compute, clearKernel, RockListCountId, listCount);
			cmd.SetComputeBufferParam(compute, clearKernel, RockSlotsId, slots);
			cmd.SetComputeBufferParam(compute, clearKernel, RockCellCountsId, cellCounts);
			cmd.DispatchComputeChecked(compute, clearKernel, (cells + 63) / 64, 1, 1);
			foreach (TerrainGpuRenderer r in renderers)
			{
				if (!r.RockSource(out GraphicsBuffer instances, out GraphicsBuffer tags, out int count) || count == 0)
				{
					continue;
				}
				cmd.SetComputeBufferParam(compute, gatherKernel, RockListId, list);
				cmd.SetComputeBufferParam(compute, gatherKernel, RockListCountId, listCount);
				cmd.SetComputeBufferParam(compute, gatherKernel, RockSlotsId, slots);
				cmd.SetComputeBufferParam(compute, gatherKernel, RockCellCountsId, cellCounts);
				cmd.SetComputeBufferParam(compute, gatherKernel, RockInstancesId, instances);
				cmd.SetComputeBufferParam(compute, gatherKernel, RockTagsId, tags);
				cmd.SetComputeBufferParam(compute, gatherKernel, RockBricksId, bricks);
				cmd.SetComputeIntParam(compute, RockInstanceCountId, count);
				cmd.DispatchComputeChecked(compute, gatherKernel, (count + 63) / 64, 1, 1);
			}
			cmd.EndSample(GatherSample);
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			Shader.SetGlobalBuffer(ListId, list);
			Shader.SetGlobalBuffer(SlotsGlobalId, slots);
			Shader.SetGlobalVector(GridGlobalId, new Vector4(min.x, min.y, min.z, CellSize));
			Shader.SetGlobalVector(GridDimsGlobalId, new Vector4(GridCells.x, GridCells.y, GridCells.z, 1f));
		}

		// What the fillets read of the grid (RockFillets): the listed rocks, their cells, and the atlas of their fields.
		internal static GraphicsBuffer ListBuffer => list;
		internal static GraphicsBuffer SlotsBuffer => slots;
		internal static Texture Atlas => atlas;
		internal static Vector4 AtlasInfo => atlas != null ? new Vector4(BricksX, 1f / atlas.width, 1f / atlas.volumeDepth, 0f) : Vector4.zero;
		internal static float Reach => crevice.x;
		internal static int RockLayer { get; private set; }

		/// <summary>
		/// For the harness's demand check: every shape counts as built in the gather's table, drawn or not, so the grid lists
		/// every rock that reaches it (the gather reads a field's built flag, never its texels, to list a rock). The crevices
		/// and fillets read unbuilt fields meanwhile: a probe's frame or two only.
		/// </summary>
		public static bool ListEveryShape
		{
			get => listEveryShape;
			set
			{
				if (listEveryShape != value)
				{
					listEveryShape = value;
					bricksDirty = true;
				}
			}
		}

		private static bool listEveryShape;

		/// <summary>A listed rock as FishRockField.compute writes it (FishRock: 7 float4); brickSize.w is its shape.</summary>
		private struct ListedRock
		{
			public Vector4 Row0, Row1, Row2, Origin, BrickMin, BrickSize, Flags;
		}

		/// <summary>
		/// For the harness (ScenePerfProbe FISHMMO_PERF_ROCK_DEMAND_CHECK): the shapes of the rocks the gather last listed
		/// into the grid, read back (a stall), against the wanted set. Meaningful with <see cref="ListEveryShape"/> on: the
		/// gather lists a rock only once its field is built.
		/// </summary>
		public static string CheckDemand()
		{
			if (list == null || listCount == null)
			{
				return "no grid";
			}
			var count = new uint[1];
			listCount.GetData(count);
			int n = (int)Mathf.Min(count[0], ListCapacity);
			var rocks = new ListedRock[Mathf.Max(1, n)];
			list.GetData(rocks, 0, 0, n);
			var listed = new HashSet<int>();
			for (int i = 0; i < n; i++)
			{
				listed.Add(Mathf.RoundToInt(rocks[i].BrickSize.w));
			}
			var missing = new List<int>();
			foreach (int shape in listed)
			{
				if (shape < 0 || shape >= demand.Length || float.IsPositiveInfinity(demand[shape]))
				{
					missing.Add(shape);
				}
			}
			missing.Sort();
			// How many more shapes the 64 m columns want than the rocks themselves (1 m columns: as good as rock by rock).
			var fine = new RockBoxes(1f);
			foreach (TerrainGpuRenderer r in renderers)
			{
				r?.CollectRockBoxes(fine);
			}
			var fineNear = new float[Mathf.Max(1, shapes.Count)];
			for (int i = 0; i < fineNear.Length; i++)
			{
				fineNear[i] = float.PositiveInfinity;
			}
			fine.Fold(RockFieldDemand.Region(demandEye, new Vector3(GridCells.x, GridCells.y, GridCells.z) * CellSize, CellSize, DemandMargin), demandEye, crevice.x, fineNear);
			int fineWanted = 0;
			for (int i = 0; i < shapes.Count; i++)
			{
				fineWanted += float.IsPositiveInfinity(fineNear[i]) ? 0 : 1;
			}
			return $"rock by rock {fineWanted} wanted; listed {count[0]} rocks{(count[0] > ListCapacity ? " (over capacity)" : "")} of {listed.Count} shapes; {demandWanted} wanted at {demandEye}; " +
				$"listed but NOT wanted: {missing.Count}{(missing.Count > 0 ? " (" + string.Join(", ", missing) + ")" : "")}; fields built {CountBuilt()}/{shapes.Count}";
		}

		/// <summary>One line for the diagnostics.</summary>
		public static string Describe() =>
			$"[Rock crevices] {(on ? "on" : "off")}, {shapes.Count} shape(s) ({CountBuilt()} built, {demandWanted} wanted round the camera{(DemandOnly ? "" : ", every field drawn")}; {boxes.Rocks} rocks in {boxes.Count} boxes gathered in {boxesMs:F2} ms, wanted measured in {demandMs:F3} ms), {renderers.Count} renderer(s), atlas {(atlas != null ? $"{atlas.width}×{atlas.height}×{atlas.volumeDepth}" : "none")}";

		private static int CountBuilt()
		{
			int n = 0;
			foreach (Shape s in shapes)
			{
				n += s.Built && !s.Free ? 1 : 0;
			}
			return n;
		}

		/// <summary>Shapes in use (slots not freed), for diagnostics and tests.</summary>
		public static int LiveShapes => shapes.Count - freeShapes.Count;

		/// <summary>Shape slots, freed ones included: what the atlas is sized for.</summary>
		public static int ShapeSlots => shapes.Count;

		/// <summary>Looks in use (slots not freed).</summary>
		public static int LiveLooks => looks.Count - freeLooks.Count;

		private static void Release()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			Application.quitting -= Release;
#if UNITY_EDITOR
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
#endif
			hooked = false;
			RockFillets.Release();
			EndBake();
			list?.Release();
			listCount?.Release();
			slots?.Release();
			cellCounts?.Release();
			bricks?.Release();
			dummyList?.Release();
			dummySlots?.Release();
			list = listCount = slots = cellCounts = bricks = dummyList = dummySlots = null;
			if (atlas != null)
			{
				atlas.Release();
				Object.Destroy(atlas);
				atlas = null;
			}
		}
	}

	/// <summary>
	/// The pure parts of <see cref="RockContactField"/>'s demand (which fields its grid can read), apart from its engine
	/// objects so tests outside Unity reach them.
	/// </summary>
	public static class RockFieldDemand
	{
		/// <summary>
		/// The box a rock must meet for its field to be wanted, measured at <paramref name="eye"/>: every grid of
		/// <paramref name="gridExtent"/> metres the gather can place round a camera within half the
		/// <paramref name="margin"/> of it along each axis (its corner snapped down to a <paramref name="cellSize"/> cell),
		/// grown by that half again.
		/// </summary>
		public static Bounds Region(Vector3 eye, Vector3 gridExtent, float cellSize, Vector3 margin)
		{
			var region = new Bounds();
			region.SetMinMax(eye - gridExtent * 0.5f - Vector3.one * cellSize - margin, eye + gridExtent * 0.5f + margin);
			return region;
		}

		/// <summary>
		/// A rock's world box as the gather builds it (FishRockField.compute FishRockGather), before the crevice reach: its
		/// shape's local box through the instance, the centre moved and the half-extents through |M| (exactly the box of
		/// the moved corners).
		/// </summary>
		public static void WorldBox(in FishInstance instance, Vector3 localCentre, Vector3 localHalf, out Vector3 lo, out Vector3 hi)
		{
			Vector4 r0 = instance.Row0, r1 = instance.Row1, r2 = instance.Row2;
			var centre = new Vector3(
				r0.x * localCentre.x + r0.y * localCentre.y + r0.z * localCentre.z + r0.w,
				r1.x * localCentre.x + r1.y * localCentre.y + r1.z * localCentre.z + r1.w,
				r2.x * localCentre.x + r2.y * localCentre.y + r2.z * localCentre.z + r2.w);
			var extent = new Vector3(
				Mathf.Abs(r0.x) * localHalf.x + Mathf.Abs(r0.y) * localHalf.y + Mathf.Abs(r0.z) * localHalf.z,
				Mathf.Abs(r1.x) * localHalf.x + Mathf.Abs(r1.y) * localHalf.y + Mathf.Abs(r1.z) * localHalf.z,
				Mathf.Abs(r2.x) * localHalf.x + Mathf.Abs(r2.y) * localHalf.y + Mathf.Abs(r2.z) * localHalf.z);
			lo = centre - extent;
			hi = centre + extent;
		}

		/// <summary>
		/// The distance from <paramref name="eye"/> to the box [<paramref name="lo"/>, <paramref name="hi"/>] grown by
		/// <paramref name="reach"/>, or +∞ when that box misses <paramref name="region"/>.
		/// </summary>
		public static float BoxDistance(Vector3 lo, Vector3 hi, float reach, Bounds region, Vector3 eye)
		{
			Vector3 grow = new Vector3(reach, reach, reach), min = region.min, max = region.max;
			lo -= grow;
			hi += grow;
			if (hi.x < min.x || hi.y < min.y || hi.z < min.z || lo.x > max.x || lo.y > max.y || lo.z > max.z)
			{
				return float.PositiveInfinity;
			}
			float dx = Mathf.Max(0f, Mathf.Max(lo.x - eye.x, eye.x - hi.x));
			float dy = Mathf.Max(0f, Mathf.Max(lo.y - eye.y, eye.y - hi.y));
			float dz = Mathf.Max(0f, Mathf.Max(lo.z - eye.z, eye.z - hi.z));
			return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
		}
	}

	/// <summary>
	/// The rocks' world boxes, gathered once when the rocks change and kept by shape in <see cref="Cell"/> m columns (by
	/// each box's centre): one box per shape and column, the union of its rocks' boxes. Measuring which shapes a region
	/// wants then reads the few columns round it, not every rock. A union box meets the region whenever one of its rocks
	/// does, and lies no farther from the eye, so the shapes wanted are every one the rocks themselves want (and a few
	/// more), nearest first.
	/// </summary>
	public sealed class RockBoxes
	{
		/// <summary>The columns' side, metres, unless a <see cref="RockBoxes(float)"/> says otherwise.</summary>
		public const float DefaultCell = 64f;

		/// <summary>This set's column side, metres.</summary>
		public readonly float Cell;

		public RockBoxes(float cell = DefaultCell)
		{
			Cell = Mathf.Max(0.01f, cell);
		}

		private struct Entry
		{
			public int Shape;
			public Vector3 Lo, Hi;
		}

		private readonly List<Entry> entries = new List<Entry>();
		private readonly Dictionary<(long Column, int Shape), int> entryOf = new Dictionary<(long Column, int Shape), int>();
		private readonly Dictionary<long, List<int>> columns = new Dictionary<long, List<int>>();
		private readonly Stack<List<int>> spare = new Stack<List<int>>();

		/// <summary>The largest half-width (x or z) of any box added: a union keyed to a column lies within it grown by this.</summary>
		private float maxHalf;

		/// <summary>Rocks added since the last <see cref="Clear"/>.</summary>
		public int Rocks { get; private set; }

		/// <summary>Union boxes held (shape × column).</summary>
		public int Count => entries.Count;

		public void Clear()
		{
			entries.Clear();
			entryOf.Clear();
			foreach (List<int> list in columns.Values)
			{
				list.Clear();
				spare.Push(list);
			}
			columns.Clear();
			maxHalf = 0f;
			Rocks = 0;
		}

		private static long ColumnKey(int x, int z) => ((long)x << 32) | (uint)z;

		public void Add(int shape, Vector3 lo, Vector3 hi)
		{
			Rocks++;
			Vector3 centre = (lo + hi) * 0.5f;
			maxHalf = Mathf.Max(maxHalf, Mathf.Max(hi.x - centre.x, hi.z - centre.z));
			long column = ColumnKey(Mathf.FloorToInt(centre.x / Cell), Mathf.FloorToInt(centre.z / Cell));
			if (entryOf.TryGetValue((column, shape), out int at))
			{
				Entry e = entries[at];
				e.Lo = Vector3.Min(e.Lo, lo);
				e.Hi = Vector3.Max(e.Hi, hi);
				entries[at] = e;
				return;
			}
			if (!columns.TryGetValue(column, out List<int> list))
			{
				list = spare.Count > 0 ? spare.Pop() : new List<int>();
				columns[column] = list;
			}
			entryOf[(column, shape)] = entries.Count;
			list.Add(entries.Count);
			entries.Add(new Entry { Shape = shape, Lo = lo, Hi = hi });
		}

		/// <summary>
		/// Folds into <paramref name="near"/>, per shape, the least <see cref="RockFieldDemand.BoxDistance"/> of its union
		/// boxes from <paramref name="eye"/> among those (grown by <paramref name="reach"/>) that meet <paramref name="region"/>.
		/// </summary>
		public void Fold(Bounds region, Vector3 eye, float reach, float[] near)
		{
			float grow = maxHalf + reach;
			Vector3 min = region.min, max = region.max;
			int x0 = Mathf.FloorToInt((min.x - grow) / Cell), x1 = Mathf.FloorToInt((max.x + grow) / Cell);
			int z0 = Mathf.FloorToInt((min.z - grow) / Cell), z1 = Mathf.FloorToInt((max.z + grow) / Cell);
			for (int x = x0; x <= x1; x++)
			{
				for (int z = z0; z <= z1; z++)
				{
					if (!columns.TryGetValue(ColumnKey(x, z), out List<int> list))
					{
						continue;
					}
					foreach (int at in list)
					{
						Entry e = entries[at];
						if (e.Shape < 0 || e.Shape >= near.Length)
						{
							continue;
						}
						float d = RockFieldDemand.BoxDistance(e.Lo, e.Hi, reach, region, eye);
						if (d < near[e.Shape])
						{
							near[e.Shape] = d;
						}
					}
				}
			}
		}
	}
}
