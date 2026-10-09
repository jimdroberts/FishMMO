using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Client
{
	/// <summary>
	/// Rock crevices: where one rock meets another, the seam darkens toward its bottom and fills with the surrounding
	/// ground's own material (FishRockContact.hlsl). Distance fields, measured in the world: the same from every angle
	/// and distance, unlike a screen-space blend.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One distance field per rock shape.</b> When a <see cref="TerrainGpuRenderer"/> registers a rock model
	/// (<see cref="RegisterShape"/>), its mesh (the level nearest <see cref="TargetTriangles"/> triangles) is read once and
	/// its unsigned distance field drawn into a 32³ brick of an R8 3D atlas by FishRockSdf.shader, one shape per frame.
	/// 32 KB a shape. Drawn, not computed, because WebGPU's compute stores only 32-bit floats.
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

		/// <summary>The level whose mesh comes nearest this many triangles (not over it) is the shape's field source.</summary>
		public const int TargetTriangles = 2500;

		private const int RockStride = 7 * 16;

		private sealed class Shape
		{
			public Mesh Mesh;
			public Bounds Bounds;
			public bool Built;
		}

		private static readonly List<Shape> shapes = new List<Shape>();
		private static readonly Dictionary<Mesh, int> shapeOf = new Dictionary<Mesh, int>();
		private static readonly List<TerrainGpuRenderer> renderers = new List<TerrainGpuRenderer>();

		private static ComputeShader compute;
		private static int clearKernel = -1, gatherKernel = -1;
		private static Material sdfMaterial;
		private static RenderTexture atlas;
		private static int atlasBricksZ;
		private static GraphicsBuffer bricks, list, listCount, slots, cellCounts;
		private static GraphicsBuffer dummyList, dummySlots;
		private static Texture3D dummyAtlas;
		private static bool bricksDirty;
		private static readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO rock crevices" };

		private static bool on = true;
		private static Vector4 crevice = new Vector4(0.35f, 0.45f, 0.85f, 1f);

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
			renderers.Clear();
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
				dummyAtlas = new Texture3D(1, 1, 1, TextureFormat.R8, false) { name = "Rock SDF placeholder", hideFlags = HideFlags.DontSave };
				dummyAtlas.SetPixel(0, 0, 0, Color.white);
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

		public static void RemoveRenderer(TerrainGpuRenderer renderer) => renderers.Remove(renderer);

		/// <summary>
		/// The shape's brick for a rock model's levels (its field is drawn on a later frame), or −1 when none of its
		/// meshes can be read. Shapes are shared by mesh across every renderer and model.
		/// </summary>
		public static int RegisterShape(TerrainGpuRenderer.PartSource[][] levels, Bounds localBounds)
		{
			Mesh mesh = Source(levels);
			if (mesh == null)
			{
				return -1;
			}
			if (shapeOf.TryGetValue(mesh, out int existing))
			{
				return existing;
			}
			// Padded so a point a crevice width off the surface is still inside the brick.
			float pad = Mathf.Max(0.1f, 0.1f * Mathf.Max(localBounds.size.x, Mathf.Max(localBounds.size.y, localBounds.size.z)));
			localBounds.Expand(pad * 2f);
			int index = shapes.Count;
			shapes.Add(new Shape { Mesh = mesh, Bounds = localBounds });
			shapeOf[mesh] = index;
			bricksDirty = true;
			return index;
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
					shader = Shader.Find(SdfShaderName);
				}
				if (shader == null || !shader.isSupported)
				{
					return false;
				}
				sdfMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
			}
			if (list == null)
			{
				int cells = GridCells.x * GridCells.y * GridCells.z;
				list = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ListCapacity, RockStride);
				listCount = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));
				slots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells * Slots, sizeof(uint));
				cellCounts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, sizeof(uint));
				EnsureBindings();
			}
			return true;
		}

		/// <summary>An atlas with room for every shape; a grown one starts empty, so every field is drawn again.</summary>
		private static void EnsureAtlas()
		{
			int bricksZ = Mathf.Max(1, (shapes.Count + BricksX - 1) / BricksX);
			if (atlas != null && atlasBricksZ >= bricksZ)
			{
				return;
			}
			if (atlas != null)
			{
				atlas.Release();
				Object.Destroy(atlas);
			}
			// Room for a few more without growing again.
			atlasBricksZ = bricksZ + 1;
			var desc = new RenderTextureDescriptor(BricksX * Brick, Brick, GraphicsFormat.R8_UNorm, 0)
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
			atlas.Create();
			foreach (Shape s in shapes)
			{
				s.Built = false;
			}
			bricksDirty = true;
			Shader.SetGlobalTexture(SdfId, atlas);
			Shader.SetGlobalVector(AtlasId, new Vector4(BricksX, 1f / atlas.width, 1f / atlas.volumeDepth, 0f));
		}

		/// <summary>The per-shape table the gather reads: min + range, size + built, flags (x rock).</summary>
		private static void UploadBricks()
		{
			if (!bricksDirty && bricks != null && bricks.count >= shapes.Count * 3)
			{
				return;
			}
			bricksDirty = false;
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
				float range = RangeOf(b);
				data[i * 3] = new Vector4(b.min.x, b.min.y, b.min.z, range);
				data[i * 3 + 1] = new Vector4(b.size.x, b.size.y, b.size.z, shapes[i].Built ? 1f : 0f);
				data[i * 3 + 2] = new Vector4(1f, 0f, 0f, 0f);
			}
			bricks.SetData(data);
		}

		/// <summary>What 1.0 in a brick means, in local units: six voxels of its largest side.</summary>
		private static float RangeOf(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / Brick * 6f;

		/// <summary>Draws the next unbuilt shape's field into its brick (one a frame).</summary>
		private static void BuildNext()
		{
			for (int i = 0; i < shapes.Count; i++)
			{
				if (shapes[i].Built)
				{
					continue;
				}
				Shape s = shapes[i];
				s.Built = true;
				bricksDirty = true;
				if (s.Mesh == null || !s.Mesh.isReadable)
				{
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
					continue;
				}
				var positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, positions.Length, 16);
				positionBuffer.SetData(positions);
				var indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, indices.Count, sizeof(uint));
				indexBuffer.SetData(indices);
				Bounds b = s.Bounds;
				int bx = i % BricksX, bz = i / BricksX;
				cmd.Clear();
				cmd.SetGlobalBuffer(SdfPositionsId, positionBuffer);
				cmd.SetGlobalBuffer(SdfIndicesId, indexBuffer);
				cmd.SetGlobalInt(SdfTrianglesId, indices.Count / 3);
				cmd.SetGlobalVector(SdfMinId, new Vector4(b.min.x, b.min.y, b.min.z, RangeOf(b)));
				cmd.SetGlobalVector(SdfSizeId, new Vector4(b.size.x, b.size.y, b.size.z, 0f));
				for (int z = 0; z < Brick; z++)
				{
					cmd.SetRenderTarget(new RenderTargetIdentifier(atlas, 0, CubemapFace.Unknown, bz * Brick + z));
					cmd.SetViewport(new Rect(bx * Brick, 0, Brick, Brick));
					cmd.SetGlobalVector(SdfSliceId, new Vector4(bx * Brick, z, 0f, 0f));
					cmd.DrawProcedural(Matrix4x4.identity, sdfMaterial, 0, MeshTopology.Triangles, 3);
				}
				Graphics.ExecuteCommandBuffer(cmd);
				cmd.Clear();
				// The draws are recorded with the buffers; releasing them now is safe (Unity defers the free).
				positionBuffer.Release();
				indexBuffer.Release();
				if (CountBuilt() == shapes.Count)
				{
					Debug.Log(Describe());
				}
				return;
			}
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			ReadProfile();
			Shader.SetGlobalVector(CreviceId, crevice);
			if (!on || shapes.Count == 0 || renderers.Count == 0 || !EnsureResources())
			{
				Shader.SetGlobalVector(GridDimsGlobalId, Vector4.zero);
				return;
			}
			EnsureAtlas();
			BuildNext();
			UploadBricks();

			Camera camera = Camera.main;
			if (camera == null)
			{
				Shader.SetGlobalVector(GridDimsGlobalId, Vector4.zero);
				return;
			}
			Vector3 eye = camera.transform.position;
			Vector3 extent = new Vector3(GridCells.x, GridCells.y, GridCells.z) * CellSize;
			Vector3 min = eye - extent * 0.5f;
			min = new Vector3(Mathf.Floor(min.x / CellSize), Mathf.Floor(min.y / CellSize), Mathf.Floor(min.z / CellSize)) * CellSize;
			int cells = GridCells.x * GridCells.y * GridCells.z;

			cmd.Clear();
			cmd.SetComputeVectorParam(compute, RockGridId, new Vector4(min.x, min.y, min.z, CellSize));
			cmd.SetComputeIntParams(compute, RockGridDimsId, GridCells.x, GridCells.y, GridCells.z, cells);
			cmd.SetComputeFloatParam(compute, RockReachId, crevice.x);
			cmd.SetComputeIntParam(compute, RockListCapacityId, ListCapacity);
			cmd.SetComputeBufferParam(compute, clearKernel, RockListCountId, listCount);
			cmd.SetComputeBufferParam(compute, clearKernel, RockSlotsId, slots);
			cmd.SetComputeBufferParam(compute, clearKernel, RockCellCountsId, cellCounts);
			cmd.DispatchCompute(compute, clearKernel, (cells + 63) / 64, 1, 1);
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
				cmd.DispatchCompute(compute, gatherKernel, (count + 63) / 64, 1, 1);
			}
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			Shader.SetGlobalBuffer(ListId, list);
			Shader.SetGlobalBuffer(SlotsGlobalId, slots);
			Shader.SetGlobalVector(GridGlobalId, new Vector4(min.x, min.y, min.z, CellSize));
			Shader.SetGlobalVector(GridDimsGlobalId, new Vector4(GridCells.x, GridCells.y, GridCells.z, 1f));
		}

		/// <summary>One line for the diagnostics.</summary>
		public static string Describe() =>
			$"[Rock crevices] {(on ? "on" : "off")}, {shapes.Count} shape(s) ({CountBuilt()} built), {renderers.Count} renderer(s), atlas {(atlas != null ? $"{atlas.width}×{atlas.height}×{atlas.volumeDepth}" : "none")}";

		private static int CountBuilt()
		{
			int n = 0;
			foreach (Shape s in shapes)
			{
				n += s.Built ? 1 : 0;
			}
			return n;
		}

		private static void Release()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			Application.quitting -= Release;
#if UNITY_EDITOR
			UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Release;
#endif
			hooked = false;
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
}
