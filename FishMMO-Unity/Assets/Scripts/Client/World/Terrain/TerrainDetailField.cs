using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// One terrain's detail layers as the instanced detail renderer draws them: a grid of ~32 m chunks,
	/// each built on demand from the baked detail map (deterministic per world cell,
	/// <see cref="TerrainDetailMath"/>), cached while a camera's detail distance reaches it, and released
	/// when none has for a while.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Lifecycle.</b> Every camera that gathers marks the chunks within its detail distance as wanted
	/// this frame; a wanted chunk that is not built is queued (<see cref="CollectMissing"/>) and built by
	/// the system nearest-first within a per-frame time budget, so walking never hitches; a built chunk
	/// that nobody has wanted for <see cref="TerrainDetailInstancing.KeepFrames"/> frames goes back to a
	/// shared pool with its buffers, which the next build reuses. Rebuilding a chunk gives the same plants.
	/// </para>
	/// <para>
	/// <b>A chunk</b> holds its instances' matrices sorted by prototype, a run per prototype, and its box.
	/// Gathering copies whole runs of chunks inside the frustum (or whose box swept along the sun can
	/// shadow it, for a chunk with a shadow-casting prototype) into the prototypes' buckets: no per-plant
	/// work per frame. Plants past the detail distance need no culling of their own — the vegetation
	/// shader has dissolved them before it (<c>_FishVegetationFade.xy</c>).
	/// </para>
	/// <para>
	/// <b>What a build allocates:</b> Unity's <see cref="TerrainData.GetDetailLayer"/> returns a new array
	/// per prototype present in the chunk, and <see cref="TerrainData.GetSupportedLayers(int,int,int,int)"/>
	/// one small one. Nothing else; nothing at all once the chunks in reach are built.
	/// </para>
	/// </remarks>
	public sealed class TerrainDetailField : IDisposable
	{
		/// <summary>One built chunk.</summary>
		public sealed class Chunk
		{
			/// <summary>The instances, 48-byte rows as the GPU path uploads them, sorted by prototype.</summary>
			public NativeArray<FishInstance> Instances;
			public int Count;
			/// <summary>Where the chunk sits in the GPU instance buffer, −1 when it is not uploaded.</summary>
			public int GpuOffset = -1;
			/// <summary>Where each prototype's run starts in <see cref="Instances"/> (length prototypes + 1).</summary>
			public int[] RunStart = Array.Empty<int>();
			public Bounds Bounds;
			public bool CastsShadows;
			public int LastWanted;

			public void EnsureCapacity(int capacity)
			{
				if (Instances.IsCreated && Instances.Length >= capacity)
				{
					return;
				}
				if (Instances.IsCreated)
				{
					Instances.Dispose();
				}
				Instances = new NativeArray<FishInstance>(Mathf.Max(capacity, 256), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			}

			public void Dispose()
			{
				if (Instances.IsCreated)
				{
					Instances.Dispose();
				}
				Count = 0;
			}
		}

		/// <summary>A chunk a camera wants that is not built yet.</summary>
		public struct Missing
		{
			public TerrainDetailField Field;
			public int Index;
			public float Distance;
		}

		/// <summary>Chunks released by every field, kept with their buffers for the next build.</summary>
		private static readonly Stack<Chunk> pool = new Stack<Chunk>();
		private static readonly List<int[,]> layers = new List<int[,]>();
		private static readonly List<int> layerPrototypes = new List<int>();

		public Terrain Terrain { get; private set; }
		public TerrainData Data { get; private set; }
		public TerrainDetailModel[] Models { get; private set; }
		public int Resolution { get; private set; }
		public int ChunkCells { get; private set; }
		public int ChunksX { get; private set; }
		public int ChunksZ { get; private set; }
		public int BuiltCount => built.Count;

		/// <summary>The density scale the chunks were built at (the system rebuilds them when it changes).</summary>
		public float DensityScale { get; private set; }

		private Vector3 origin;
		private Vector3 size;
		private float cellX, cellZ;
		private bool coverageMode;
		private float[] coverage;
		private Chunk[] chunks;
		private int[] queuedFrame;
		private readonly List<int> built = new List<int>();

		/// <summary>Lays out a terrain's chunk grid; nothing is built until a camera wants it.</summary>
		public static TerrainDetailField Create(Terrain terrain, TerrainDetailModel[] models, float densityScale, float chunkMetres = TerrainDetailMath.DefaultChunkMetres)
		{
			TerrainData data = terrain.terrainData;
			int resolution = Mathf.Max(1, data.detailResolution);
			var field = new TerrainDetailField
			{
				Terrain = terrain,
				Data = data,
				Models = models,
				Resolution = resolution,
				DensityScale = densityScale,
				origin = terrain.GetPosition(),
				size = data.size,
				coverageMode = data.detailScatterMode == DetailScatterMode.CoverageMode,
				coverage = new float[models.Length],
			};
			field.cellX = field.size.x / resolution;
			field.cellZ = field.size.z / resolution;
			field.ChunkCells = TerrainDetailMath.ChunkCells(field.cellX, chunkMetres);
			field.ChunksX = (resolution + field.ChunkCells - 1) / field.ChunkCells;
			field.ChunksZ = field.ChunksX;
			field.chunks = new Chunk[field.ChunksX * field.ChunksZ];
			field.queuedFrame = new int[field.chunks.Length];
			for (int i = 0; i < field.queuedFrame.Length; i++)
			{
				field.queuedFrame[i] = -1;
			}
			for (int p = 0; p < models.Length; p++)
			{
				if (models[p] != null && field.coverageMode)
				{
					float c = data.ComputeDetailCoverage(p);
					field.coverage[p] = float.IsNaN(c) || c < 0f ? 0f : c;
				}
			}
			return field;
		}

		/// <summary>
		/// The most instances of one prototype that can lie within <paramref name="distance"/> of any point
		/// (coverage mode: every cell of the disc fully covered, each holding its whole count rounded up), or
		/// −1 when there is no such bound (instance-count mode). The GPU culls by that distance, so no camera's
		/// slot can hold more.
		/// </summary>
		public long CapacityBound(int prototype, float distance)
		{
			if (!coverageMode || Models[prototype] == null)
			{
				return -1;
			}
			return TerrainDetailMath.DiscBound(distance, cellX, cellZ, coverage[prototype], Density(prototype));
		}

		/// <summary>Sets the density scale the next builds use (release the chunks first).</summary>
		public void SetDensityScale(float densityScale)
		{
			DensityScale = densityScale;
		}

		/// <summary>The chunk at an index, or null when it is not built.</summary>
		public Chunk ChunkAt(int index) => chunks[index];

		private float ChunkMetresX => ChunkCells * cellX;
		private float ChunkMetresZ => ChunkCells * cellZ;

		/// <summary>
		/// Marks every chunk within <paramref name="distance"/> of the view (in the ground plane) as wanted this
		/// frame, adds the unbuilt ones to <paramref name="missing"/> once per frame, and copies the built
		/// ones the view can see into the models' buckets. Returns the instances gathered.
		/// </summary>
		public int Gather(in TerrainTreeField.View view, float distance, int frame, List<Missing> missing, bool useGpu = true, System.Text.StringBuilder trace = null)
		{
			TerrainGpuRenderer gpu = useGpu ? Gpu : null;
			if (distance <= 0f)
			{
				return 0;
			}
			float localX = view.Position.x - origin.x, localZ = view.Position.z - origin.z;
			float sideX = ChunkMetresX, sideZ = ChunkMetresZ;
			TerrainDetailMath.ChunkRange(localX, distance, sideX, ChunksX, out int minX, out int maxX);
			TerrainDetailMath.ChunkRange(localZ, distance, sideZ, ChunksZ, out int minZ, out int maxZ);
			int gathered = 0;
			for (int cz = minZ; cz <= maxZ; cz++)
			{
				for (int cx = minX; cx <= maxX; cx++)
				{
					float ground = TerrainDetailMath.DistanceToSquare(localX, localZ, cx * sideX, cz * sideZ, Mathf.Max(sideX, sideZ));
					if (ground > distance)
					{
						continue;
					}
					int index = cz * ChunksX + cx;
					Chunk chunk = chunks[index];
					if (chunk == null)
					{
						if (queuedFrame[index] != frame)
						{
							queuedFrame[index] = frame;
							missing.Add(new Missing { Field = this, Index = index, Distance = ground });
						}
						trace?.Append($"\n    {Terrain.name} chunk {index} ({ground:F0} m): not built yet (queued)");
						continue;
					}
					chunk.LastWanted = frame;
					if (chunk.Count == 0 || TerrainTreeMath.MinDistance(chunk.Bounds, view.Position) > distance)
					{
						trace?.Append($"\n    {Terrain.name} chunk {index} ({ground:F0} m): {(chunk.Count == 0 ? "empty" : "beyond the detail distance")}, {chunk.Count} instances, GPU offset {chunk.GpuOffset}");
						continue;
					}
					if (!TerrainTreeMath.IntersectsFrustum(view.Planes, chunk.Bounds))
					{
						if (!chunk.CastsShadows || !view.Shadows || TerrainTreeMath.MinDistance(chunk.Bounds, view.Position) > view.ShadowDistance
							|| !TerrainTreeMath.IntersectsFrustum(view.Planes, TerrainTreeMath.ShadowSweep(chunk.Bounds, view.LightDirection, view.ShadowDistance)))
						{
							trace?.Append($"\n    {Terrain.name} chunk {index} ({ground:F0} m): outside the frustum, {chunk.Count} instances");
							continue;
						}
					}
					if (trace != null)
					{
						bool onGpu = gpu != null && chunk.GpuOffset >= 0;
						trace.Append($"\n    {Terrain.name} chunk {index} ({ground:F0} m): {chunk.Count} instances, GPU offset {chunk.GpuOffset} → {(onGpu ? "GPU work" : gpu == null ? "CPU path (no GPU for this camera)" : "CPU bucket (not uploaded)")}, bounds y {chunk.Bounds.min.y:F1}…{chunk.Bounds.max.y:F1}");
					}
					int[] runs = chunk.RunStart;
					for (int p = 0; p < Models.Length; p++)
					{
						int count = runs[p + 1] - runs[p];
						if (count <= 0)
						{
							continue;
						}
						TerrainDetailModel model = Models[p];
						if (gpu != null && model.GpuId >= 0 && chunk.GpuOffset >= 0)
						{
							gpu.AddWork(chunk.GpuOffset + runs[p], count, model.GpuId, distance, 1f, 1,
								model.CastsShadows && view.Shadows && TerrainTreeMath.MinDistance(chunk.Bounds, view.Position) <= view.ShadowDistance,
								TerrainDetailMath.ThinStartMetres, TerrainDetailMath.ThinKeepAtDistance);
						}
						else
						{
							model.AppendRange(chunk.Instances, runs[p], count, chunk.Bounds);
						}
						gathered += count;
					}
				}
			}
			return gathered;
		}

		/// <summary>Builds one chunk now (from the pool where it can). Returns its instance count.</summary>
		public int Build(int index)
		{
			if (chunks[index] != null)
			{
				return chunks[index].Count;
			}
			Chunk chunk = pool.Count > 0 ? pool.Pop() : new Chunk();
			int prototypes = Models.Length;
			if (chunk.RunStart.Length != prototypes + 1)
			{
				chunk.RunStart = new int[prototypes + 1];
			}
			int cx = index % ChunksX, cz = index / ChunksX;
			int x0 = cx * ChunkCells, z0 = cz * ChunkCells;
			int w = Mathf.Min(ChunkCells, Resolution - x0), h = Mathf.Min(ChunkCells, Resolution - z0);

			// Which prototypes have anything here, and their maps; then the count, then the instances.
			layers.Clear();
			layerPrototypes.Clear();
			int[] present = Data.GetSupportedLayers(x0, z0, w, h);
			Array.Sort(present);
			int total = 0;
			foreach (int p in present)
			{
				if (p < 0 || p >= prototypes || Models[p] == null)
				{
					continue;
				}
				int[,] map = Data.GetDetailLayer(x0, z0, w, h, p);
				layers.Add(map);
				layerPrototypes.Add(p);
				total += CountLayer(map, p, x0, z0, w, h);
			}
			chunk.EnsureCapacity(total);
			chunk.Count = 0;
			chunk.CastsShadows = false;
			bool any = false;
			Bounds bounds = default;
			int next = 0;
			for (int p = 0; p <= prototypes; p++)
			{
				chunk.RunStart[p] = chunk.Count;
				if (p == prototypes)
				{
					break;
				}
				if (next < layerPrototypes.Count && layerPrototypes[next] == p)
				{
					int before = chunk.Count;
					FillLayer(chunk, layers[next], p, x0, z0, w, h, ref bounds, ref any);
					if (chunk.Count > before && Models[p].CastsShadows)
					{
						chunk.CastsShadows = true;
					}
					next++;
				}
			}
			if (any)
			{
				// Room for the wind's sway and the ground sink, so a chunk is never culled while a plant in it shows.
				bounds.Expand(1f);
			}
			chunk.Bounds = bounds;
			chunk.LastWanted = Time.frameCount;
			Upload(chunk, index);
			chunks[index] = chunk;
			built.Add(index);
			layers.Clear();
			return chunk.Count;
		}

		private float Density(int prototype)
		{
			DetailPrototypeSettings s = settings[prototype];
			return s.UseDensityScaling ? DensityScale : 1f;
		}

		private int CountLayer(int[,] map, int p, int x0, int z0, int w, int h)
		{
			int total = 0;
			float area = cellX * cellZ;
			int seed = settings[p].Seed;
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					int value = map[z, x];
					if (value <= 0)
					{
						continue;
					}
					WorldCell(x0 + x, z0 + z, out int wx, out int wz);
					total += TerrainDetailMath.CountInCell(TerrainDetailMath.ExpectedInCell(value, coverageMode, coverage[p], area, Density(p)),
						TerrainDetailMath.Hash(wx, wz, p, seed, 0));
				}
			}
			return total;
		}

		private void WorldCell(int gx, int gz, out int wx, out int wz)
		{
			wx = TerrainDetailMath.WorldCell(origin.x + (gx + 0.5f) * cellX, cellX);
			wz = TerrainDetailMath.WorldCell(origin.z + (gz + 0.5f) * cellZ, cellZ);
		}

		private void FillLayer(Chunk chunk, int[,] map, int p, int x0, int z0, int w, int h, ref Bounds bounds, ref bool any)
		{
			DetailPrototypeSettings s = settings[p];
			Bounds mesh = Models[p].MeshBounds;
			float area = cellX * cellZ;
			float density = Density(p);
			NativeArray<FishInstance> instances = chunk.Instances;
			for (int z = 0; z < h; z++)
			{
				for (int x = 0; x < w; x++)
				{
					int value = map[z, x];
					if (value <= 0)
					{
						continue;
					}
					int gx = x0 + x, gz = z0 + z;
					WorldCell(gx, gz, out int wx, out int wz);
					int n = TerrainDetailMath.CountInCell(TerrainDetailMath.ExpectedInCell(value, coverageMode, coverage[p], area, density),
						TerrainDetailMath.Hash(wx, wz, p, s.Seed, 0));
					for (int k = 0; k < n; k++)
					{
						uint hash = TerrainDetailMath.Hash(wx, wz, p, s.Seed, k + 1);
						Matrix4x4 m = Place(s, gx, gz, hash);
						instances[chunk.Count++] = FishInstance.From(m);
						Bounds b = TerrainTreeMath.TransformBounds(m, mesh);
						if (any)
						{
							bounds.Encapsulate(b);
						}
						else
						{
							bounds = b;
							any = true;
						}
					}
				}
			}
		}

		/// <summary>One instance's matrix: jittered in its cell, on the ground, turned, sized by the noise, leaning with the slope as the prototype asks.</summary>
		private Matrix4x4 Place(in DetailPrototypeSettings s, int gx, int gz, uint hash)
		{
			Vector2 inCell = TerrainDetailMath.InCell(hash, s.Jitter);
			float nx = (gx + inCell.x) / Resolution, nz = (gz + inCell.y) / Resolution;
			float worldX = origin.x + nx * size.x, worldZ = origin.z + nz * size.z;
			var position = new Vector3(worldX, origin.y + Data.GetInterpolatedHeight(nx, nz), worldZ);
			Vector2 scale = TerrainDetailMath.Size(TerrainDetailMath.Noise(worldX * s.NoiseSpread, worldZ * s.NoiseSpread, s.Seed),
				s.MinWidth, s.MaxWidth, s.MinHeight, s.MaxHeight);
			Quaternion rotation = Quaternion.AngleAxis(TerrainDetailMath.Yaw(hash), Vector3.up);
			Vector3 normal = Data.GetInterpolatedNormal(nx, nz);
			if (s.AlignToGround > 0f)
			{
				Vector3 up = Vector3.Slerp(Vector3.up, normal, s.AlignToGround);
				rotation = Quaternion.FromToRotation(Vector3.up, up) * rotation;
			}
			// Into the slope. An upright plant stands on the height under its middle, so on a slope its downhill
			// edge hangs over the ground by its foot's reach times the slope — 20 to 30 cm for a fern, a shrub or
			// a patch of litter on a 25° bank, against the few centimetres the shader sinks every detail. It is
			// set down by most of that (SlopeSinkShare of the drop across its foot, as much of the slope as it
			// does not lean with), never more than MaxSlopeSink: the uphill side goes into the bank, as a real
			// plant's base does, and nothing floats. In the matrix, so every path — the GPU's, the CPU's, the
			// shadows — sets it down alike.
			float slope = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * (1f - s.AlignToGround);
			if (slope > 1e-3f && s.FootRadius > 0f)
			{
				position.y -= Mathf.Min(SlopeSinkShare * s.FootRadius * scale.x * Mathf.Tan(Mathf.Min(slope, 1.2f)), MaxSlopeSink);
			}
			return Matrix4x4.TRS(position, rotation, new Vector3(scale.x, scale.y, scale.x));
		}

		/// <summary>Releases chunks nobody has wanted since <paramref name="oldestFrame"/> to the pool.</summary>
		public void ReleaseStale(int oldestFrame)
		{
			for (int i = built.Count - 1; i >= 0; i--)
			{
				int index = built[i];
				if (chunks[index].LastWanted < oldestFrame)
				{
					Release(i);
				}
			}
		}

		/// <summary>Releases every chunk (the density scale changed, or the terrain is let go).</summary>
		public void ReleaseAll()
		{
			for (int i = built.Count - 1; i >= 0; i--)
			{
				Release(i);
			}
		}

		private void Release(int builtSlot)
		{
			int index = built[builtSlot];
			Chunk chunk = chunks[index];
			chunks[index] = null;
			built[builtSlot] = built[built.Count - 1];
			built.RemoveAt(built.Count - 1);
			FreeGpu(chunk);
			chunk.Count = 0;
			pool.Push(chunk);
		}

		/// <summary>The GPU path, or null on the fallback: chunks are uploaded when built and freed when released.</summary>
		public TerrainGpuRenderer Gpu;

		private void Upload(Chunk chunk, int index)
		{
			chunk.GpuOffset = -1;
			if (Gpu == null || chunk.Count == 0)
			{
				return;
			}
			chunk.GpuOffset = Gpu.Upload(chunk.Instances, 0, chunk.Count, $"{(Terrain != null ? Terrain.name : "terrain")} details chunk {index}");
			for (int p = 0; p < Models.Length; p++)
			{
				int count = chunk.RunStart[p + 1] - chunk.RunStart[p];
				TerrainDetailModel model = Models[p];
				if (count > 0 && model.GpuId >= 0)
				{
					model.Resident += count;
					// Grow-only and doubling, so a walk relays the slots out a handful of times at most, and
					// never past the disc bound (a slot holds no more than the detail distance can contain,
					// however many chunks outside it are still resident). Never shrinks while playing.
					int capacity = Gpu.CapacityOf(model.GpuId);
					int ceiling = model.CapacityCeiling > 0 ? model.CapacityCeiling : int.MaxValue;
					if (model.Resident > capacity && capacity < ceiling)
					{
						Gpu.SetCapacity(model.GpuId, Mathf.Min(ceiling, Mathf.Max(model.Resident, 2 * capacity, 1024)));
					}
				}
			}
		}

		private void FreeGpu(Chunk chunk)
		{
			if (Gpu == null || chunk.GpuOffset < 0)
			{
				chunk.GpuOffset = -1;
				return;
			}
			Gpu.Free(chunk.GpuOffset);
			chunk.GpuOffset = -1;
			for (int p = 0; p < Models.Length; p++)
			{
				int count = chunk.RunStart[p + 1] - chunk.RunStart[p];
				if (count > 0 && Models[p].GpuId >= 0)
				{
					Models[p].Resident -= count;
				}
			}
		}

		/// <summary>How much of the drop across its foot a plant on a slope is set down by (Place).</summary>
		private const float SlopeSinkShare = 0.6f;
		/// <summary>The most a plant is set down into a slope, m.</summary>
		private const float MaxSlopeSink = 0.4f;

		/// <summary>The per-prototype placement settings, read once from the detail prototypes.</summary>
		public struct DetailPrototypeSettings
		{
			/// <summary>How far the plant's foot reaches from its root, m at scale 1: the widest of its lowest sixth (FootRadiusOf).</summary>
			public float FootRadius;
			public int Seed;
			public float Jitter;
			public float NoiseSpread;
			public float MinWidth, MaxWidth, MinHeight, MaxHeight;
			public float AlignToGround;
			public bool UseDensityScaling;

			public static DetailPrototypeSettings From(DetailPrototype p)
			{
				return new DetailPrototypeSettings
				{
					Seed = p.noiseSeed,
					Jitter = p.positionJitter,
					NoiseSpread = Mathf.Max(1e-4f, p.noiseSpread),
					MinWidth = p.minWidth,
					MaxWidth = Mathf.Max(p.minWidth, p.maxWidth),
					MinHeight = p.minHeight,
					MaxHeight = Mathf.Max(p.minHeight, p.maxHeight),
					AlignToGround = Mathf.Clamp01(p.alignToGround),
					UseDensityScaling = p.useDensityScaling,
					FootRadius = FootRadiusOf(p),
				};
			}

			/// <summary>
			/// How far a prototype's foot reaches from its root, m: the furthest any vertex in the lowest sixth of
			/// its height stands from the root across the ground — what hangs off a slope. From the vertices where
			/// the mesh can be read, half its bounds' width where it cannot; 0 for a texture detail.
			/// </summary>
			private static float FootRadiusOf(DetailPrototype p)
			{
				if (!p.usePrototypeMesh || p.prototype == null)
				{
					return 0f;
				}
				MeshFilter filter = p.prototype.GetComponentInChildren<MeshFilter>();
				Mesh mesh = filter != null ? filter.sharedMesh : null;
				if (mesh == null)
				{
					return 0f;
				}
				if (!mesh.isReadable)
				{
					return 0.5f * Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
				}
				Vector3[] vertices = mesh.vertices;
				float lowest = float.MaxValue, highest = float.MinValue;
				foreach (Vector3 v in vertices)
				{
					lowest = Mathf.Min(lowest, v.y);
					highest = Mathf.Max(highest, v.y);
				}
				float foot = lowest + (highest - lowest) * 0.16f;
				float reach = 0f;
				foreach (Vector3 v in vertices)
				{
					if (v.y <= foot)
					{
						reach = Mathf.Max(reach, Mathf.Sqrt(v.x * v.x + v.z * v.z));
					}
				}
				return reach;
			}
		}

		private DetailPrototypeSettings[] settings = Array.Empty<DetailPrototypeSettings>();

		/// <summary>Sets the placement settings (one per prototype, in the terrain's order).</summary>
		public void SetPrototypes(DetailPrototype[] prototypes)
		{
			settings = new DetailPrototypeSettings[prototypes.Length];
			for (int i = 0; i < prototypes.Length; i++)
			{
				if (prototypes[i] != null)
				{
					settings[i] = DetailPrototypeSettings.From(prototypes[i]);
				}
			}
		}

		/// <summary>Empties the shared chunk pool, freeing its buffers (the system's shutdown).</summary>
		public static void DisposePool()
		{
			while (pool.Count > 0)
			{
				pool.Pop().Dispose();
			}
		}

		public void Dispose()
		{
			ReleaseAll();
		}
	}
}
