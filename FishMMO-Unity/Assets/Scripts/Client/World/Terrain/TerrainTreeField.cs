using System;
using Unity.Collections;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// One terrain's tree instances, read once from its <see cref="TerrainData"/> and laid out for the
	/// instanced renderer: a matrix per instance, sorted by ~64 m chunk and then by prototype, each chunk
	/// with its box and its runs of one prototype.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Per camera</b> (<see cref="Gather"/>): a chunk beyond the draw distance is skipped; one in the
	/// camera's frustum is kept; one outside it is kept only if its box swept along the sun's light still
	/// reaches the frustum within the shadow distance (it can throw a shadow on what is seen). In a kept
	/// chunk, a run whose nearest and farthest instances would be drawn at the same level, outside every
	/// cross-fade band, is copied into that level's bucket whole; otherwise each instance picks its own
	/// level by Unity's LODGroup rule — two levels with complementary fades inside a band — and is
	/// dropped beyond the draw distance, as Unity drops a tree.
	/// </para>
	/// <para>
	/// <b>Nothing is allocated per frame:</b> the matrices are a persistent native array, the buckets
	/// belong to the models and are sized when terrains are added.
	/// </para>
	/// </remarks>
	public sealed class TerrainTreeField : IDisposable
	{
		/// <summary>A run of one prototype's instances inside a chunk.</summary>
		public struct Run
		{
			public int Prototype;
			public int Start;
			public int Count;
			public float MinSize;
			public float MaxSize;
		}

		/// <summary>A chunk: its box (the union of its instances') and its runs.</summary>
		public struct Chunk
		{
			public Bounds Bounds;
			public int FirstRun;
			public int RunCount;
		}

		public Terrain Terrain { get; private set; }
		public TerrainData Data { get; private set; }

		/// <summary>The models by prototype index; null where a prototype has no prefab.</summary>
		public TerrainTreeModel[] Models { get; private set; }

		/// <summary>Instances per prototype index, for the models' bucket capacities.</summary>
		public int[] PrototypeCounts { get; private set; }

		public int InstanceCount { get; private set; }
		public int PrototypeCount { get; private set; }
		public Chunk[] Chunks { get; private set; }
		public Run[] Runs { get; private set; }

		private NativeArray<Matrix4x4> matrices;
		private Vector3[] positions;
		private Vector3[] references;
		private float[] sizes;

		/// <summary>
		/// Lays out a terrain's instances against the models already resolved for its prototypes
		/// (<paramref name="models"/>[i] for prototype i, null to skip it).
		/// </summary>
		public static TerrainTreeField Build(Terrain terrain, TerrainTreeModel[] models, float chunkMetres = TerrainTreeMath.DefaultChunkMetres)
		{
			TerrainData data = terrain.terrainData;
			TerrainTreeField field = Build(terrain.GetPosition(), data.size, data.treeInstances, models, chunkMetres);
			field.Terrain = terrain;
			field.Data = data;
			return field;
		}

		/// <summary>
		/// Lays out instances for a terrain whose corner is at <paramref name="origin"/> and whose size is
		/// <paramref name="size"/> (the terrain-free half of <see cref="Build(Terrain, TerrainTreeModel[], float)"/>).
		/// </summary>
		public static TerrainTreeField Build(Vector3 origin, Vector3 size, TreeInstance[] instances, TerrainTreeModel[] models, float chunkMetres = TerrainTreeMath.DefaultChunkMetres)
		{
			int chunksX = TerrainTreeMath.ChunkCount(size.x, chunkMetres);
			int chunksZ = TerrainTreeMath.ChunkCount(size.z, chunkMetres);
			int chunkCount = chunksX * chunksZ;
			int prototypes = models.Length;

			// Keep only instances whose prototype draws.
			int kept = 0;
			for (int i = 0; i < instances.Length; i++)
			{
				int p = instances[i].prototypeIndex;
				if (p >= 0 && p < prototypes && models[p] != null)
				{
					kept++;
				}
			}
			var source = new int[kept];
			var chunkOf = new int[kept];
			var prototypeOf = new int[kept];
			for (int i = 0, k = 0; i < instances.Length; i++)
			{
				int p = instances[i].prototypeIndex;
				if (p < 0 || p >= prototypes || models[p] == null)
				{
					continue;
				}
				source[k] = i;
				prototypeOf[k] = p;
				chunkOf[k] = TerrainTreeMath.ChunkIndex(instances[i].position.x, instances[i].position.z, chunksX, chunksZ);
				k++;
			}
			var order = new int[kept];
			var chunkStart = new int[chunkCount + 1];
			TerrainTreeMath.SortByChunkThenPrototype(chunkOf, prototypeOf, chunkCount, prototypes, order, chunkStart);

			var field = new TerrainTreeField
			{
				Models = models,
				InstanceCount = kept,
				PrototypeCount = prototypes,
				PrototypeCounts = new int[prototypes],
				matrices = new NativeArray<Matrix4x4>(Mathf.Max(1, kept), Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
				positions = new Vector3[kept],
				references = new Vector3[kept],
				sizes = new float[kept],
			};

			var chunks = new Chunk[chunkCount];
			var runs = new System.Collections.Generic.List<Run>(chunkCount * 4);
			for (int c = 0; c < chunkCount; c++)
			{
				int begin = chunkStart[c], end = chunkStart[c + 1];
				chunks[c].FirstRun = runs.Count;
				bool anyBounds = false;
				for (int k = begin; k < end; k++)
				{
					int keep = order[k];
					TreeInstance t = instances[source[keep]];
					int p = prototypeOf[keep];
					TerrainTreeModel model = models[p];
					Matrix4x4 m = TerrainTreeMath.InstanceMatrix(origin, size, t.position, t.rotation, t.widthScale, t.heightScale);
					float worldSize = TerrainTreeMath.WorldSize(model.Size, t.widthScale, t.heightScale);
					field.matrices[k] = m;
					field.positions[k] = m.GetColumn(3);
					field.references[k] = m.MultiplyPoint3x4(model.LocalReference);
					field.sizes[k] = worldSize;
					field.PrototypeCounts[p]++;

					// The box holds the instance's origin too, so a whole chunk inside the draw distance has every
					// instance's origin inside it (the distance Unity drops a tree at).
					Bounds b = TerrainTreeMath.TransformBounds(m, model.LocalBounds);
					b.Encapsulate(field.positions[k]);
					if (anyBounds)
					{
						chunks[c].Bounds.Encapsulate(b);
					}
					else
					{
						chunks[c].Bounds = b;
						anyBounds = true;
					}

					if (runs.Count == chunks[c].FirstRun || runs[runs.Count - 1].Prototype != p)
					{
						runs.Add(new Run { Prototype = p, Start = k, Count = 0, MinSize = worldSize, MaxSize = worldSize });
					}
					Run run = runs[runs.Count - 1];
					run.Count++;
					run.MinSize = Mathf.Min(run.MinSize, worldSize);
					run.MaxSize = Mathf.Max(run.MaxSize, worldSize);
					runs[runs.Count - 1] = run;
				}
				chunks[c].RunCount = runs.Count - chunks[c].FirstRun;
			}
			field.Chunks = chunks;
			field.Runs = runs.ToArray();
			return field;
		}

		/// <summary>What one camera needs to gather a field: computed once per camera per frame.</summary>
		public struct View
		{
			public Vector3 Position;
			public Plane[] Planes;
			/// <summary>Biased screen-relative height per metre of size per metre of distance (or per metre, orthographic).</summary>
			public float ScreenFactor;
			public bool Orthographic;
			public int MaximumLodLevel;
			public bool Shadows;
			public Vector3 LightDirection;
			public float ShadowDistance;
		}

		/// <summary>
		/// Adds this terrain's instances that <paramref name="view"/> can see (or see the shadows of) to the
		/// models' buckets, out to <paramref name="drawDistance"/> metres, with the terrain's own
		/// <paramref name="lodBiasMultiplier"/>. Returns how many bucket entries were added: an instance in a
		/// cross-fade band is in two levels at once (outgoing −f, incoming +f) and counts twice.
		/// </summary>
		public int Gather(in View view, float drawDistance, float lodBiasMultiplier, TerrainGpuRenderer gpu = null)
		{
			if (InstanceCount == 0 || drawDistance <= 0f)
			{
				return 0;
			}
			int gathered = 0;
			float factor = view.ScreenFactor * lodBiasMultiplier;
			Vector3 eye = view.Position;
			float drawSq = drawDistance * drawDistance;
			for (int c = 0; c < Chunks.Length; c++)
			{
				Chunk chunk = Chunks[c];
				if (chunk.RunCount == 0)
				{
					continue;
				}
				float near = TerrainTreeMath.MinDistance(chunk.Bounds, eye);
				if (near > drawDistance)
				{
					continue;
				}
				if (!TerrainTreeMath.IntersectsFrustum(view.Planes, chunk.Bounds))
				{
					if (!view.Shadows || near > view.ShadowDistance
						|| !TerrainTreeMath.IntersectsFrustum(view.Planes, TerrainTreeMath.ShadowSweep(chunk.Bounds, view.LightDirection, view.ShadowDistance)))
					{
						continue;
					}
				}
				float far = TerrainTreeMath.MaxDistance(chunk.Bounds, eye);
				bool allInRange = far <= drawDistance;
				for (int r = chunk.FirstRun, end = chunk.FirstRun + chunk.RunCount; r < end; r++)
				{
					Run run = Runs[r];
					TerrainTreeModel model = Models[run.Prototype];
					float nearHeight = TerrainTreeMath.RelativeHeight(run.MaxSize, near, factor, view.Orthographic);
					float farHeight = TerrainTreeMath.RelativeHeight(run.MinSize, far, factor, view.Orthographic);
					if (gpu != null && model.GpuId >= 0 && GpuOffset >= 0)
					{
						// The GPU path: the run is culled per instance on the GPU.
						int mask = TerrainGpuMath.LevelMask(nearHeight, farHeight, model.Transitions, model.FadeWidths, view.MaximumLodLevel);
						gpu.AddWork(GpuOffset + run.Start, run.Count, model.GpuId, drawDistance, lodBiasMultiplier, mask, view.Shadows && near <= view.ShadowDistance);
						gathered += run.Count;
						continue;
					}
					if (allInRange)
					{
						// One level for the whole run only when its nearest and farthest points are both outside
						// every cross-fade band (the far one is the lowest relative height: if it is above its
						// level's band, so is every instance of the run).
						int nearLod = TerrainTreeMath.SelectLod(nearHeight, model.Transitions, view.MaximumLodLevel);
						int farLod = TerrainTreeMath.SelectLodFaded(farHeight, model.Transitions, model.FadeWidths, view.MaximumLodLevel,
							out float farFade, out int farPartner, out _);
						if (nearLod == farLod && farFade == 0f && farPartner < 0)
						{
							if (nearLod >= 0)
							{
								model.AppendRange(nearLod, matrices, run.Start, run.Count, chunk.Bounds);
								gathered += run.Count;
							}
							continue;
						}
					}
					for (int i = run.Start, last = run.Start + run.Count; i < last; i++)
					{
						if ((positions[i] - eye).sqrMagnitude > drawSq)
						{
							continue;
						}
						float distance = Vector3.Distance(references[i], eye);
						float height = TerrainTreeMath.RelativeHeight(sizes[i], distance, factor, view.Orthographic);
						int lod = TerrainTreeMath.SelectLodFaded(height, model.Transitions, model.FadeWidths, view.MaximumLodLevel,
							out float fade, out int partner, out float partnerFade);
						if (lod < 0)
						{
							continue;
						}
						// In a band: the outgoing level with −f and the incoming one with +f; after the last
						// level, the outgoing level alone.
						model.Append(lod, matrices[i], fade, chunk.Bounds);
						gathered++;
						if (partner >= 0)
						{
							model.Append(partner, matrices[i], partnerFade, chunk.Bounds);
							gathered++;
						}
					}
				}
			}
			return gathered;
		}

		/// <summary>Where this terrain's instances sit in the GPU instance buffer, −1 when not uploaded.</summary>
		public int GpuOffset { get; private set; } = -1;

		private NativeArray<FishInstance> gpuInstances;

		/// <summary>Makes every instance resident on the GPU path (trees are all resident).</summary>
		public void UploadTo(TerrainGpuRenderer gpu)
		{
			if (gpu == null || InstanceCount == 0 || GpuOffset >= 0)
			{
				return;
			}
			if (!gpuInstances.IsCreated)
			{
				gpuInstances = new NativeArray<FishInstance>(InstanceCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				for (int i = 0; i < InstanceCount; i++)
				{
					gpuInstances[i] = FishInstance.From(matrices[i]);
				}
			}
			GpuOffset = gpu.Upload(gpuInstances, 0, InstanceCount, $"{(Terrain != null ? Terrain.name : "terrain")} trees");
			// Which rock each instance is, for the crevices between rocks (RockContactField; trees are never tagged).
			if (GpuOffset >= 0 && Runs != null)
			{
				foreach (Run run in Runs)
				{
					TerrainTreeModel model = run.Prototype >= 0 && run.Prototype < Models.Length ? Models[run.Prototype] : null;
					if (model != null)
					{
						gpu.TagRun(GpuOffset + run.Start, run.Count, model.GpuId);
					}
				}
			}
		}

		/// <summary>Releases the GPU range.</summary>
		public void FreeFrom(TerrainGpuRenderer gpu)
		{
			if (gpu != null && GpuOffset >= 0)
			{
				gpu.Free(GpuOffset);
			}
			GpuOffset = -1;
		}

		public void Dispose()
		{
			if (matrices.IsCreated)
			{
				matrices.Dispose();
			}
			if (gpuInstances.IsCreated)
			{
				gpuInstances.Dispose();
			}
		}
	}
}
