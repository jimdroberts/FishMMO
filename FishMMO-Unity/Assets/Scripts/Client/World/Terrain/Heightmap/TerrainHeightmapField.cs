using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// One scene's GPU patch selection. LOD fields and node bounds are shared across tiles; each tile's indirect
	/// draws sample its existing heightmap, normal map, holes, index map and snow fade without texture copies.
	/// Solid and holed patches have separate lists so solid ground retains early depth testing.
	/// </summary>
	internal sealed class TerrainHeightmapField
	{
		/// <summary>One tile as the shaders read it (FishTerrainHeightmap.hlsl FishTerrainTile, 96 bytes).</summary>
		internal struct TileRecord
		{
			public Vector4 Origin;
			public Vector4 Grid;
			public Vector4 IndexTexel;
			public Vector4 FadeTexel;
			public int NeighbourLeft, NeighbourRight, NeighbourDown, NeighbourUp;
			public int HoleSlice, FadeSlice, R16, IndexBound;

			public const int Stride = 96;
		}

		private static readonly int TilesId = Shader.PropertyToID("_FishTerrainTiles");
		private static readonly int HeightsId = Shader.PropertyToID("_FishTerrainHeights");
		private static readonly int FieldId = Shader.PropertyToID("_FishTerrainLodField");
		private static readonly int NodesId = Shader.PropertyToID("_FishTerrainNodes");
		private static readonly int PatchesId = Shader.PropertyToID("_FishTerrainPatches");
		private static readonly int GridId = Shader.PropertyToID("_FishTerrainLodGrid");
		private static readonly int ScaleId = Shader.PropertyToID("_FishTerrainLodScale");
		private static readonly int LayoutId = Shader.PropertyToID("_FishTerrainLayout");
		private static readonly int ViewId = Shader.PropertyToID("_FishTerrainLodView");
		private static readonly int OrthoId = Shader.PropertyToID("_FishTerrainLodOrtho");
		private static readonly int BuildId = Shader.PropertyToID("_FishTerrainBuild");
		private static readonly int RoughCellsId = Shader.PropertyToID("_FishTerrainRoughCells");
		private static readonly int RoughReadId = Shader.PropertyToID("_FishTerrainRoughRead");
		private static readonly int FieldInId = Shader.PropertyToID("_FishTerrainFieldIn");
		private static readonly int FieldOutId = Shader.PropertyToID("_FishTerrainFieldOut");
		private static readonly int NodesOutId = Shader.PropertyToID("_FishTerrainNodesOut");
		private static readonly int HolesReadId = Shader.PropertyToID("_FishTerrainHolesRead");
		private static readonly int ArgsId = Shader.PropertyToID("_FishTerrainArgs");
		private static readonly int PatchesOutId = Shader.PropertyToID("_FishTerrainPatchesOut");
		private static readonly int ShownId = Shader.PropertyToID("_FishTerrainTileShown");
		private static readonly int PlanesId = Shader.PropertyToID("_FishTerrainPlanes");
		private static readonly int ShadowPlanesId = Shader.PropertyToID("_FishTerrainShadowPlanes");
		private static readonly int SweepId = Shader.PropertyToID("_FishTerrainShadowSweep");
		private static readonly int FenceId = Shader.PropertyToID("_FishTerrainArgsFence");
		private static readonly int FenceSinkId = Shader.PropertyToID("_FishTerrainFenceSink");

		private static readonly int NormalsId = Shader.PropertyToID("_FishTerrainNormals");
		private static readonly int IndexArrayId = Shader.PropertyToID("_FishIndexMapArray");
		private static readonly int HolesArrayId = Shader.PropertyToID("_FishHolesArray");
		private static readonly int FadeArrayId = Shader.PropertyToID("_FishSnowHoleFadeArray");
		private static readonly int GpuArrayInfoId = Shader.PropertyToID("_FishGpuArrayInfo");
		private static readonly int GpuPathLayersId = Shader.PropertyToID("_FishGpuPathLayers");
		private static readonly int GpuHeightBlendId = Shader.PropertyToID("_FishGpuHeightBlend");
		private static readonly int GpuShadowCullId = Shader.PropertyToID("_FishGpuShadowCull");

		// What TerrainArrayBinder pushes into a tile's splat block.
		private static readonly int BlockIndexMapId = Shader.PropertyToID("_FishIndexMap");
		private static readonly int BlockIndexTexelId = Shader.PropertyToID("_FishIndexMap_TexelSize");
		private static readonly int BlockFadeId = Shader.PropertyToID("_FishSnowHoleFade");
		private static readonly int BlockFadeTexelId = Shader.PropertyToID("_FishSnowHoleFade_TexelSize");
		private static readonly int BlockInfoId = Shader.PropertyToID("_FishArrayInfo");
		private static readonly int BlockPathLayersId = Shader.PropertyToID("_FishPathLayers");
		private static readonly int[] BlockSceneTextureIds =
		{
			Shader.PropertyToID("_FishAlbedoArray"), Shader.PropertyToID("_FishNormalArray"), Shader.PropertyToID("_FishMaskArray"),
			Shader.PropertyToID("_FishLayerTable"),
		};

		// The tiles' material's own numbers the shaders read (FishWeatherTerrainArrayInput.hlsl UnityPerMaterial).
		private static readonly int[] LookFloatIds =
		{
			Shader.PropertyToID("_HeightTransition"), Shader.PropertyToID("_FishWeatherAmount"), Shader.PropertyToID("_FishSnowDepth"),
			Shader.PropertyToID("_Cutoff"),
		};

		private const string AlphaTestKeyword = "_ALPHATEST_ON";
		private const string HeightBlendKeyword = "_TERRAIN_BLEND_HEIGHT";

		/// <summary>The scene the tiles are in.</summary>
		public readonly UnityEngine.SceneManagement.Scene Scene;

		/// <summary>The tiles drawn, in record order.</summary>
		public readonly List<Terrain> Tiles;

		/// <summary>The binder's push count the tiles' bindings were last read at.</summary>
		public int BoundPushes { get; private set; } = -1;

		/// <summary>True once the bindings, the field and the nodes are built and the patches can be chosen.</summary>
		public bool Built { get; private set; }

		/// <summary>Why the scene cannot be drawn so (set when <see cref="Built"/> stays false), for one log line.</summary>
		public string Refusal { get; private set; }

		/// <summary>The tiles' heights or holes changed: build everything again before the next draw.</summary>
		public bool Stale;

		private readonly bool[] drewHeightmap;
		// Whether the tiles' own heightmap drawing is ours (and drewHeightmap what they had before).
		private bool taken;
		private readonly bool[] shown;
		private readonly uint[] shownWords;
		private readonly TileRecord[] records;
		private bool[] tileHoled;
		private bool holesKnown;

		private int quads, levels, nodesPerTile, capacity, cellsX, cellsZ;
		private Bounds bounds;
		private Material solid, holed;
		private readonly Material[] both = new Material[2];
		private Material template;
		private GraphicsBuffer tileBuffer, shownBuffer, nodeBuffer, fieldBuffer, fieldScratch, roughCells, patches, args, fenceSink;
		private MaterialPropertyBlock[] tileBlocks;
		private Mesh mesh;
		private readonly MaterialPropertyBlock read = new MaterialPropertyBlock();
		private SphericalHarmonicsL2 ambient;
		private bool ambientSet;
		private static readonly SphericalHarmonicsL2[] ambientOne = new SphericalHarmonicsL2[1];
		private static readonly Vector4[] occlusionOne = { Vector4.one };
		private static readonly Plane[] frustum = new Plane[6];
		private static readonly Vector4[] planeVectors = new Vector4[6];
		private static readonly Vector4[] shadowPlaneVectors = new Vector4[6];
		private static readonly int BuildTileId = Shader.PropertyToID("_FishTerrainBuildTile");

		public TerrainHeightmapField(UnityEngine.SceneManagement.Scene scene, List<Terrain> tiles)
		{
			Scene = scene;
			Tiles = tiles;
			drewHeightmap = new bool[tiles.Count];
			shown = new bool[tiles.Count];
			shownWords = new uint[tiles.Count];
			records = new TileRecord[tiles.Count];
			tileHoled = new bool[tiles.Count];
		}

		/// <summary>True while every tile is still there (an unloaded scene's are destroyed).</summary>
		public bool Alive()
		{
			for (int i = 0; i < Tiles.Count; i++)
			{
				if (Tiles[i] == null || Tiles[i].terrainData == null)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Whether this field draws <paramref name="terrain"/>.</summary>
		public bool Holds(Terrain terrain) => Tiles.Contains(terrain);

		// ── Build ───────────────────────────────────────────────────────────

		/// <summary>
		/// Builds the field, nodes and materials and turns the tiles' own heightmap drawing off. False (with
		/// <see cref="Refusal"/>) when these tiles cannot be drawn so; nothing is changed on them then.
		/// </summary>
		public bool Build(ComputeShader compute, Shader shader, Mesh patchMesh)
		{
			Built = false;
			Refusal = null;
			if (!Measure())
			{
				ReleaseGpu();
				return false;
			}
			mesh = patchMesh;
			ReleaseGpu();
			CreateResources(compute, shader);
			if (!BindHeightsAndHoles())
			{
				Refusal = Refusal ?? "this device cannot read the tiles' heightmaps or normals";
				ReleaseGpu();
				return false;
			}
			if (!Rebind())
			{
				ReleaseGpu();
				return false;
			}
			RunBuild(compute);
			for (int i = 0; i < Tiles.Count; i++)
			{
				if (!taken)
				{
					drewHeightmap[i] = Tiles[i].drawHeightmap;
				}
				Tiles[i].drawHeightmap = false;
			}
			taken = true;
			Built = true;
			Stale = false;
			return true;
		}

		/// <summary>The tiles' layout: one size and resolution, square texels, a heightmap the quadtree divides.</summary>
		private bool Measure()
		{
			Terrain first = Tiles[0];
			TerrainData data = first.terrainData;
			int res = data.heightmapResolution;
			Vector3 size = data.size;
			quads = res - 1;
			levels = TerrainHeightmapMath.LevelCount(quads);
			if (levels == 0)
			{
				Refusal = $"heightmap resolution {res} is not 16·2^k + 1";
				return false;
			}
			float texelX = size.x / quads, texelZ = size.z / quads;
			if (Mathf.Abs(texelX - texelZ) > 1e-4f * texelX)
			{
				Refusal = $"texels are not square ({texelX} x {texelZ} m)";
				return false;
			}
			template = first.materialTemplate;
			Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			foreach (Terrain t in Tiles)
			{
				TerrainData d = t.terrainData;
				if (d.heightmapResolution != res || (d.size - size).sqrMagnitude > 1e-6f || d.heightmapTexture == null)
				{
					Refusal = $"tile '{t.name}' differs from '{first.name}' in heightmap size or resolution";
					return false;
				}
				Vector3 p = t.GetPosition();
				min = Vector3.Min(min, p);
				max = Vector3.Max(max, p + d.size);
			}
			bounds = new Bounds();
			bounds.SetMinMax(min - Vector3.up * 1f, max + Vector3.up * TerrainHeightmapMath.LiftPadMetres);
			int columns = Mathf.RoundToInt((max.x - min.x) / size.x);
			int rows = Mathf.RoundToInt((max.z - min.z) / size.z);
			cellsX = columns * quads / TerrainHeightmapMath.CellQuads;
			cellsZ = rows * quads / TerrainHeightmapMath.CellQuads;
			nodesPerTile = TerrainHeightmapMath.NodesPerTile(quads);
			int across = TerrainHeightmapMath.NodesAcross(quads, 0);
			capacity = across * across;

			for (int i = 0; i < Tiles.Count; i++)
			{
				Vector3 p = Tiles[i].GetPosition();
				var r = new TileRecord
				{
					Origin = new Vector4(p.x, p.y, p.z, size.y * (65535f / 32766f)),
					Grid = new Vector4(texelX, texelZ, quads, levels),
					NeighbourLeft = Find(p - new Vector3(size.x, 0f, 0f)),
					NeighbourRight = Find(p + new Vector3(size.x, 0f, 0f)),
					NeighbourDown = Find(p - new Vector3(0f, 0f, size.z)),
					NeighbourUp = Find(p + new Vector3(0f, 0f, size.z)),
					HoleSlice = -1,
					FadeSlice = -1,
					R16 = Tiles[i].terrainData.heightmapTexture.graphicsFormat == GraphicsFormat.R16_UNorm ? 1 : 0,
				};
				records[i] = r;
			}
			return true;

			int Find(Vector3 at)
			{
				for (int j = 0; j < Tiles.Count; j++)
				{
					Vector3 q = Tiles[j].GetPosition();
					if (Mathf.Abs(q.x - at.x) < 0.01f && Mathf.Abs(q.z - at.z) < 0.01f)
					{
						return j;
					}
				}
				return -1;
			}
		}

		private void CreateResources(ComputeShader compute, Shader shader)
		{
			int n = Tiles.Count;
			int res = quads + 1;
			tileBlocks = new MaterialPropertyBlock[n];
			for (int i = 0; i < n; i++) tileBlocks[i] = new MaterialPropertyBlock();

			int cells = cellsX * cellsZ * Fields;
			tileBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, TileRecord.Stride) { name = "Terrain heightmap tiles" };
			shownBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, sizeof(uint)) { name = "Terrain heightmap tiles shown" };
			nodeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n * nodesPerTile, 16) { name = "Terrain heightmap nodes" };
			roughCells = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, sizeof(uint)) { name = "Terrain heightmap roughness" };
			fieldBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, sizeof(float)) { name = "Terrain heightmap level field" };
			fieldScratch = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cells, sizeof(float)) { name = "Terrain heightmap level field scratch" };
			patches = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n * TerrainHeightmapMath.Lists * capacity, 8) { name = "Terrain heightmap patches" };
			args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, n * TerrainHeightmapMath.Lists, GraphicsBuffer.IndirectDrawIndexedArgs.size) { name = "Terrain heightmap args" };
			fenceSink = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint)) { name = "Terrain heightmap fence" };
			var commands = new GraphicsBuffer.IndirectDrawIndexedArgs[n * TerrainHeightmapMath.Lists];
			for (int i = 0; i < commands.Length; i++)
			{
				commands[i].indexCountPerInstance = mesh.GetIndexCount(0);
			}
			args.SetData(commands);
			for (int i = 0; i < n; i++)
			{
				shown[i] = Shown(Tiles[i]);
				shownWords[i] = shown[i] ? 1u : 0u;
			}
			shownBuffer.SetData(shownWords);

			solid = new Material(shader) { name = $"{Scene.name} terrain (GPU)", hideFlags = HideFlags.HideAndDontSave };
			holed = new Material(shader) { name = $"{Scene.name} terrain with holes (GPU)", hideFlags = HideFlags.HideAndDontSave };
			holed.EnableKeyword(AlphaTestKeyword);
			both[0] = solid;
			both[1] = holed;
			CopyLook();
			foreach (Material m in both)
			{
				m.SetBuffer(TilesId, tileBuffer);
				m.SetBuffer(FieldId, fieldBuffer);
				m.SetBuffer(NodesId, nodeBuffer);
				m.SetBuffer(PatchesId, patches);
				m.SetVector(GridId, GridVector());
				m.SetVector(ScaleId, ScaleVector());
				m.SetVector(LayoutId, LayoutVector());
			}
		}

		/// <summary>The tiles' own material's numbers (UnityPerMaterial), its height blend and its shadow casting, on both materials.</summary>
		public void CopyLook()
		{
			if (solid == null)
			{
				return;
			}
			ShadowCastingMode casting = Tiles[0] != null ? Tiles[0].shadowCastingMode : ShadowCastingMode.TwoSided;
			foreach (Material m in both)
			{
				foreach (int id in LookFloatIds)
				{
					if (template != null && template.HasFloat(id))
					{
						m.SetFloat(id, template.GetFloat(id));
					}
				}
				m.SetFloat(GpuHeightBlendId, template != null && template.IsKeywordEnabled(HeightBlendKeyword) ? 1f : 0f);
				m.SetFloat(GpuShadowCullId, casting == ShadowCastingMode.TwoSided ? (float)CullMode.Off : (float)CullMode.Back);
			}
		}

		private bool BindHeightsAndHoles()
		{
			for (int i = 0; i < Tiles.Count; i++)
			{
				Terrain t = Tiles[i];
				if (t.normalmapTexture == null)
				{
					Refusal = $"tile '{t.name}' has no instanced terrain normal map";
					return false;
				}
				Texture h = t.terrainData.holesTexture;
				bool usable = h != null && h.width == quads && h.height == quads;
				records[i].HoleSlice = usable ? 0 : -1;
				tileHoled[i] = usable;
				tileBlocks[i].SetTexture(HeightsId, t.terrainData.heightmapTexture);
				tileBlocks[i].SetTexture(NormalsId, t.normalmapTexture);
				tileBlocks[i].SetTexture(HolesArrayId, usable ? h : White());
			}
			holesKnown = false;
			return true;
		}

		/// <summary>
		/// Takes the binder's per-tile bindings again (index maps, snow hole fades and their sizes) and the scene's arrays, layer
		/// table and numbers, as the tiles' splat blocks hold them now. Textures are borrowed, never copied.
		/// </summary>
		public bool Rebind()
		{
			BoundPushes = TerrainArrayBinder.TilePushes;
			if (solid == null)
			{
				return true;
			}
			int n = Tiles.Count;
			for (int i = 0; i < n; i++)
			{
				Tiles[i].GetSplatMaterialPropertyBlock(read);
				Texture index = read.GetTexture(BlockIndexMapId);
				Texture fade = read.GetTexture(BlockFadeId);
				records[i].IndexTexel = read.GetVector(BlockIndexTexelId);
				records[i].FadeTexel = read.GetVector(BlockFadeTexelId);
				bool indexBound = index != null && index.width > 1;
				records[i].IndexBound = indexBound ? 1 : 0;
				bool fadeBound = fade != null && fade.width == quads + 1 && fade.height == quads + 1;
				records[i].FadeSlice = fadeBound ? 0 : -1;
				tileBlocks[i].SetTexture(IndexArrayId, indexBound ? index : BlankIndex());
				tileBlocks[i].SetTexture(FadeArrayId, fadeBound ? fade : White());
				if (i == 0)
				{
					foreach (int id in BlockSceneTextureIds)
					{
						Texture t = read.GetTexture(id);
						foreach (Material m in both)
						{
							m.SetTexture(id, t);
						}
					}
					Vector4 info = read.GetVector(BlockInfoId);
					Vector4 paths = read.GetVector(BlockPathLayersId);
					foreach (Material m in both)
					{
						m.SetVector(GpuArrayInfoId, new Vector4(info.x, 0f, info.z, info.w));
						m.SetVector(GpuPathLayersId, paths);
					}
				}
			}

			read.Clear();
			tileBuffer.SetData(records);
			return true;
		}

		private static RenderTexture blankIndex;
		private static RenderTexture BlankIndex()
		{
			if (blankIndex == null)
			{
				blankIndex = new RenderTexture(new RenderTextureDescriptor(1, 1, TerrainIndexMapBaker.Format, GraphicsFormat.None));
				blankIndex.hideFlags = HideFlags.HideAndDontSave;
				blankIndex.Create();
			}
			return blankIndex;
		}

		/// <summary>The field and node bounds on the GPU (FishTerrainHeightmap.compute, the build kernels).</summary>
		private void RunBuild(ComputeShader compute)
		{
			int n = Tiles.Count;
			int res = quads + 1;
			int cells = cellsX * cellsZ;
			var cmd = new CommandBuffer { name = "Terrain heightmap build" };
			cmd.SetComputeVectorParam(compute, GridId, GridVector());
			cmd.SetComputeVectorParam(compute, ScaleId, ScaleVector());
			cmd.SetComputeVectorParam(compute, LayoutId, LayoutVector());
			cmd.SetComputeVectorParam(compute, BuildId, new Vector4(0f, TerrainHeightmapMath.FieldSlope, TerrainHeightmapMath.Log2(TerrainHeightmapMath.FieldFloorMetres), TerrainHeightmapMath.DilationReach));

			int k = compute.FindKernel("FishTerrainRoughClear");
			cmd.SetComputeBufferParam(compute, k, RoughCellsId, roughCells);
			cmd.DispatchCompute(compute, k, Groups(cells * Fields, 64), 1, 1);

			k = compute.FindKernel("FishTerrainRough");
			BindTiles(cmd, compute, k);
			cmd.SetComputeBufferParam(compute, k, RoughCellsId, roughCells);
			DispatchTiles(cmd, compute, k, Groups(res, 8), Groups(res, 8));

			k = compute.FindKernel("FishTerrainRoughLog");
			cmd.SetComputeBufferParam(compute, k, RoughReadId, roughCells);
			cmd.SetComputeBufferParam(compute, k, FieldOutId, fieldBuffer);
			cmd.DispatchCompute(compute, k, Groups(cells * Fields, 64), 1, 1);

			k = compute.FindKernel("FishTerrainDilateX");
			cmd.SetComputeBufferParam(compute, k, FieldInId, fieldBuffer);
			cmd.SetComputeBufferParam(compute, k, FieldOutId, fieldScratch);
			cmd.DispatchCompute(compute, k, Groups(cellsX, 8), Groups(cellsZ, 8), Fields);

			k = compute.FindKernel("FishTerrainDilateZ");
			cmd.SetComputeBufferParam(compute, k, FieldInId, fieldScratch);
			cmd.SetComputeBufferParam(compute, k, FieldOutId, fieldBuffer);
			cmd.DispatchCompute(compute, k, Groups(cellsX, 8), Groups(cellsZ, 8), Fields);

			k = compute.FindKernel("FishTerrainFieldRaise");
			cmd.SetComputeBufferParam(compute, k, FieldOutId, fieldBuffer);
			cmd.DispatchCompute(compute, k, Groups(cells, 64), 1, 1);

			k = compute.FindKernel("FishTerrainNodeLeaf");
			BindTiles(cmd, compute, k);
			cmd.SetComputeBufferParam(compute, k, NodesOutId, nodeBuffer);
			
			int across = TerrainHeightmapMath.NodesAcross(quads, 0);
			DispatchTiles(cmd, compute, k, Groups(across, 8), Groups(across, 8));

			k = compute.FindKernel("FishTerrainNodeParent");
			BindTiles(cmd, compute, k);
			cmd.SetComputeBufferParam(compute, k, FieldId, fieldBuffer);
			cmd.SetComputeBufferParam(compute, k, NodesOutId, nodeBuffer);
			for (int level = 1; level < levels; level++)
			{
				cmd.SetComputeVectorParam(compute, BuildId, new Vector4(level, TerrainHeightmapMath.FieldSlope, TerrainHeightmapMath.Log2(TerrainHeightmapMath.FieldFloorMetres), TerrainHeightmapMath.DilationReach));
				int a = TerrainHeightmapMath.NodesAcross(quads, level);
				cmd.DispatchCompute(compute, k, Groups(a, 8), Groups(a, 8), n);
			}

			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Release();

			// Which tiles' holes are real (a tile may carry a holes texture with none in it): their roots' flags, once.
			if (Tiles.Count > 0)
			{
				int rootOffset = TerrainHeightmapMath.LevelOffset(quads, levels - 1);
				int perTile = nodesPerTile;
				GraphicsBuffer source = nodeBuffer;
				AsyncGPUReadback.Request(source, request =>
				{
					if (request.hasError || source != nodeBuffer || nodeBuffer == null)
					{
						return;
					}
					var data = request.GetData<Vector4>();
					for (int i = 0; i < tileHoled.Length && (i * perTile + rootOffset) < data.Length; i++)
					{
						tileHoled[i] = data[i * perTile + rootOffset].w > 0.5f;
					}
					holesKnown = true;
				});
			}
		}

		private void BindTiles(CommandBuffer cmd, ComputeShader compute, int kernel)
		{
			cmd.SetComputeBufferParam(compute, kernel, TilesId, tileBuffer);
		}

		private void DispatchTiles(CommandBuffer cmd, ComputeShader compute, int kernel, int x, int y)
		{
			for (int i = 0; i < Tiles.Count; i++)
			{
				cmd.SetComputeIntParam(compute, BuildTileId, i);
				cmd.SetComputeTextureParam(compute, kernel, HeightsId, Tiles[i].terrainData.heightmapTexture);
				if (kernel == compute.FindKernel("FishTerrainNodeLeaf"))
					cmd.SetComputeTextureParam(compute, kernel, HolesReadId, Tiles[i].terrainData.holesTexture ?? White());
				cmd.DispatchCompute(compute, kernel, x, y, 1);
			}
		}

		private static int Groups(int count, int size) => Mathf.Max(1, (count + size - 1) / size);

		private Vector4 GridVector()
		{
			float cell = records[0].Grid.x * TerrainHeightmapMath.CellQuads;
			return new Vector4(bounds.min.x, bounds.min.z, cell, cellsX);
		}

		private Vector4 ScaleVector() => new Vector4(cellsZ, TerrainHeightmapMath.MorphStart, TerrainHeightmapMath.LiftPadMetres, Fields);

		/// <summary>The level fields: one per step between levels.</summary>
		private int Fields => Mathf.Max(1, levels - 1);

		private Vector4 LayoutVector() => new Vector4(capacity, nodesPerTile, Tiles.Count, 0f);

		private static Texture2D White() => Texture2D.whiteTexture;


		// ── Per frame ───────────────────────────────────────────────────────

		private static bool Shown(Terrain t) => t != null && t.enabled && t.gameObject.activeInHierarchy;

		/// <summary>Once a frame: which tiles are on (a tile switched off draws nothing), and the ambient the draws are lit by.</summary>
		public void Refresh()
		{
			bool changed = false;
			for (int i = 0; i < Tiles.Count; i++)
			{
				bool now = Shown(Tiles[i]);
				if (now != shown[i])
				{
					shown[i] = now;
					shownWords[i] = now ? 1u : 0u;
					changed = true;
				}
			}
			if (changed)
			{
				shownBuffer.SetData(shownWords);
			}
			// The ambient a terrain is lit by is the scene's ambient probe (it takes no light probes); an indirect draw is given
			// it as its own probe, or it would read none.
			SphericalHarmonicsL2 now2 = RenderSettings.ambientProbe;
			if (!ambientSet || now2 != ambient)
			{
				ambient = now2;
				ambientSet = true;
				ambientOne[0] = now2;
				foreach (var block in tileBlocks)
				{
					block.CopySHCoefficientArraysFrom(ambientOne);
					block.CopyProbeOcclusionArrayFrom(occlusionOne);
				}
			}
		}

		/// <summary>
		/// Chooses this camera's patches (FishTerrainClear + FishTerrainSelect) and draws them: the solid list, the holed one where
		/// a tile with holes is in view, and both again as shadow casters when the main light casts. Returns the draws issued.
		/// </summary>
		public int Draw(ScriptableRenderContext context, Camera camera, ComputeShader compute, CommandBuffer cmd, int selectKernel, int clearKernel, int fenceKernel)
		{
			if (!Built || camera == null)
			{
				return 0;
			}
			bool anyShown = false;
			for (int i = 0; i < shown.Length; i++)
			{
				anyShown |= shown[i];
			}
			if (!anyShown)
			{
				return 0;
			}

			Vector3 eye = camera.transform.position;
			bool orthographic = camera.orthographic;
			float bias = TerrainHeightmapMath.LodBias(ClientTerrainLod.HeightmapPixelError, camera.pixelHeight, camera.fieldOfView, orthographic,
				camera.orthographicSize);

			GeometryUtility.CalculateFrustumPlanes(camera, frustum);
			for (int i = 0; i < 6; i++)
			{
				Plane p = frustum[i];
				// A metre of slack: the jittered projection (STP) moves the frustum by a fraction of a pixel.
				planeVectors[i] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance + 1f);
				shadowPlaneVectors[i] = planeVectors[i];
			}
			Vector4 sweep = Vector4.zero;
			bool shadows = ShadowsWanted(camera, out float shadowDistance, out Vector3 lightDirection);
			if (shadows)
			{
				Vector3 forward = camera.transform.forward;
				var far = new Plane(-forward, eye + forward * shadowDistance);
				shadowPlaneVectors[5] = new Vector4(far.normal.x, far.normal.y, far.normal.z, far.distance + 1f);
				float fall = (bounds.size.y + TerrainHeightmapMath.LiftPadMetres) / Mathf.Max(-lightDirection.y, 0.1f);
				Vector3 along = lightDirection * Mathf.Min(fall, 4000f);
				sweep = new Vector4(along.x, along.y, along.z, 1f);
			}

			cmd.Clear();
			cmd.SetGlobalVector(ViewId, new Vector4(eye.x, eye.y, eye.z, bias));
			cmd.SetGlobalFloat(OrthoId, orthographic ? 1f : 0f);
			cmd.SetComputeVectorParam(compute, ViewId, new Vector4(eye.x, eye.y, eye.z, bias));
			cmd.SetComputeFloatParam(compute, OrthoId, orthographic ? 1f : 0f);
			cmd.SetComputeVectorParam(compute, GridId, GridVector());
			cmd.SetComputeVectorParam(compute, ScaleId, ScaleVector());
			cmd.SetComputeVectorParam(compute, LayoutId, LayoutVector());
			cmd.SetComputeVectorArrayParam(compute, PlanesId, planeVectors);
			cmd.SetComputeVectorArrayParam(compute, ShadowPlanesId, shadowPlaneVectors);
			cmd.SetComputeVectorParam(compute, SweepId, sweep);

			cmd.SetComputeBufferParam(compute, clearKernel, ArgsId, args);
			cmd.DispatchCompute(compute, clearKernel, Tiles.Count, 1, 1);

			cmd.SetComputeBufferParam(compute, selectKernel, TilesId, tileBuffer);
			cmd.SetComputeBufferParam(compute, selectKernel, NodesId, nodeBuffer);
			cmd.SetComputeBufferParam(compute, selectKernel, ShownId, shownBuffer);
			cmd.SetComputeBufferParam(compute, selectKernel, ArgsId, args);
			cmd.SetComputeBufferParam(compute, selectKernel, PatchesOutId, patches);
			cmd.DispatchCompute(compute, selectKernel, Groups(Tiles.Count * nodesPerTile, 64), 1, 1);
			if (TerrainHeightmapSystem.NeedsArgsFence)
			{
				cmd.SetComputeBufferParam(compute, fenceKernel, FenceId, args);
				cmd.SetComputeBufferParam(compute, fenceKernel, FenceSinkId, fenceSink);
				cmd.DispatchCompute(compute, fenceKernel, 1, 1, 1);
			}
			context.ExecuteCommandBuffer(cmd);
			cmd.Clear();

			Terrain first = Tiles[0];
			var rp = new RenderParams(solid)
			{
				camera = camera,
				layer = first.gameObject.layer,
				renderingLayerMask = first.renderingLayerMask,
				worldBounds = bounds,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = true,
				lightProbeUsage = LightProbeUsage.CustomProvided,
				reflectionProbeUsage = first.reflectionProbeUsage,
				motionVectorMode = MotionVectorGenerationMode.Camera,
				matProps = tileBlocks[0],
			};
			int draws = 0;
			for (int i = 0; i < Tiles.Count; i++)
			{
				if (!shown[i]) continue;
				Terrain tile = Tiles[i];
				rp.matProps = tileBlocks[i];
				rp.layer = tile.gameObject.layer;
				rp.renderingLayerMask = tile.renderingLayerMask;
				rp.worldBounds = new Bounds(tile.GetPosition() + tile.terrainData.size * 0.5f,
					tile.terrainData.size + Vector3.up * (2f * TerrainHeightmapMath.LiftPadMetres));
				Submit(TerrainHeightmapMath.ListSolid, solid, ShadowCastingMode.Off);
				if (tileHoled[i]) Submit(TerrainHeightmapMath.ListHoled, holed, ShadowCastingMode.Off);
				if (shadows && tile.shadowCastingMode != ShadowCastingMode.Off)
				{
					Submit(TerrainHeightmapMath.ListShadowSolid, solid, ShadowCastingMode.ShadowsOnly);
					if (tileHoled[i]) Submit(TerrainHeightmapMath.ListShadowHoled, holed, ShadowCastingMode.ShadowsOnly);
				}

				void Submit(int list, Material material, ShadowCastingMode casting)
				{
					int command = i * TerrainHeightmapMath.Lists + list;
					rp.material = material;
					rp.shadowCastingMode = casting;
					Graphics.RenderMeshIndirect(rp, mesh, args, 1, command);
					draws++;
				}
			}

			return draws;
		}


		private Bounds TileBounds(int i)
		{
			Vector3 p = new Vector3(records[i].Origin.x, records[i].Origin.y, records[i].Origin.z);
			Vector3 size = Tiles[i].terrainData.size;
			var b = new Bounds();
			b.SetMinMax(p - Vector3.up, p + size + Vector3.up * TerrainHeightmapMath.LiftPadMetres);
			return b;
		}

		/// <summary>Whether the main light casts shadows this camera draws, how far, and which way its light travels.</summary>
		private static bool ShadowsWanted(Camera camera, out float distance, out Vector3 lightDirection)
		{
			distance = 0f;
			lightDirection = Vector3.down;
			var pipeline = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
			if (pipeline == null || !pipeline.supportsMainLightShadows || camera.cameraType == CameraType.Reflection)
			{
				return false;
			}
			Light sun = RenderSettings.sun;
			if (sun == null || !sun.isActiveAndEnabled || sun.shadows == LightShadows.None)
			{
				return false;
			}
			distance = Mathf.Min(pipeline.shadowDistance, camera.farClipPlane);
			lightDirection = sun.transform.forward;
			return distance > 0f;
		}

		/// <summary>Whether the holes are known from the GPU yet (until then every tile with a holes texture counts as holed).</summary>
		public bool HolesKnown => holesKnown;

		/// <summary>Tiles drawn, and how many of them have holes (for the probe's report line).</summary>
		public int HoledTiles
		{
			get
			{
				int count = 0;
				foreach (bool h in tileHoled)
				{
					count += h ? 1 : 0;
				}
				return count;
			}
		}

		/// <summary>A one-line summary (for the log and the probe).</summary>
		public string Describe() => $"'{Scene.name}': {Tiles.Count} tile(s), heightmap {quads + 1}², {levels} levels, {Tiles.Count * nodesPerTile} nodes, field {cellsX}x{cellsZ}, {HoledTiles} tile(s) with holes{(holesKnown ? string.Empty : " (unconfirmed)")}";

		// ── Release ─────────────────────────────────────────────────────────

		/// <summary>Gives every tile its own heightmap drawing back and frees everything.</summary>
		public void Release()
		{
			if (taken)
			{
				for (int i = 0; i < Tiles.Count; i++)
				{
					if (Tiles[i] != null)
					{
						Tiles[i].drawHeightmap = drewHeightmap[i];
					}
				}
				taken = false;
			}
			Built = false;
			ReleaseGpu();
		}

		private void ReleaseGpu()
		{
			Release(ref tileBuffer);
			Release(ref shownBuffer);
			Release(ref nodeBuffer);
			Release(ref fieldBuffer);
			Release(ref fieldScratch);
			Release(ref roughCells);
			Release(ref patches);
			Release(ref args);
			Release(ref fenceSink);
			tileBlocks = null;
			if (solid != null)
			{
				Object.Destroy(solid);
			}
			if (holed != null)
			{
				Object.Destroy(holed);
			}
			solid = holed = null;
			both[0] = both[1] = null;
		}

		private static void Release(ref GraphicsBuffer buffer)
		{
			buffer?.Release();
			buffer = null;
		}

		private static void Release(ref RenderTexture texture)
		{
			if (texture != null)
			{
				texture.Release();
				Object.Destroy(texture);
			}
			texture = null;
		}
	}
}
