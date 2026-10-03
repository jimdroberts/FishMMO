using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// One tree prototype prefab as the instanced renderer draws it: its levels of detail (the LODGroup's,
	/// or one level of every mesh renderer when it has none), each level's meshes, materials and
	/// renderer settings, and the per-level matrix buckets a camera's visible instances are gathered into.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it supports.</b> MeshRenderer + MeshFilter parts, under a LODGroup or not, at any local
	/// transform under the prefab root, every material GPU-instanced. A prototype with anything else — a
	/// Tree Creator <see cref="Tree"/>, a SpeedTree <see cref="BillboardRenderer"/>, a skinned mesh, a
	/// material without instancing — is refused (<see cref="Build"/> gives the reason) and the whole
	/// terrain is left to Unity's own drawing, because Unity's tree drawing is switched per terrain.
	/// </para>
	/// <para>
	/// <b>The prefab root's own transform is not applied</b>, only each part's transform relative to it,
	/// as the terrain places the prototype by the instance's matrix alone. The generated prefabs all have
	/// identity roots and identity LOD children.
	/// </para>
	/// <para>
	/// <b>Renderer settings follow the prototype's renderers:</b> shadow casting, receiving, reflection
	/// probes, rendering layer mask. The layer is the terrain's (Unity draws trees on the terrain's layer
	/// unless <see cref="Terrain.preserveTreePrototypeLayers"/>), so a model is keyed by prefab and layer.
	/// </para>
	/// </remarks>
	public sealed class TerrainTreeModel : IDisposable
	{
		/// <summary>One mesh renderer of a level.</summary>
		public sealed class Part
		{
			public Mesh Mesh;
			public Matrix4x4 Local;
			public bool Identity;
			/// <summary>The renderer blends light probes (it is given them where the scene has any).</summary>
			public bool UsesProbes;
			/// <summary>Per material slot: the submesh it draws (Unity draws extra slots on the last submesh again).</summary>
			public int[] Submesh;
			public RenderParams[] Params;
			/// <summary>The part's own matrices when it is not at the root (filled per draw).</summary>
			public NativeArray<Matrix4x4> Scratch;
		}

		/// <summary>One level of detail.</summary>
		public sealed class Level
		{
			public Part[] Parts;
			public NativeArray<Matrix4x4> Bucket;
			/// <summary>Each bucketed instance's <c>_FishLodFade</c>, in matrix order: 0 drawn whole, +f fading in, −f fading out.</summary>
			public NativeArray<float> Fades;
			public int Count;
			/// <summary>How many bucketed instances carry a nonzero fade; with none, no property block is set.</summary>
			public int FadedCount;
			public Vector3 BoundsMin, BoundsMax;
			/// <summary>One property block and one <see cref="TerrainTreeMath.MaxInstancesPerBatch"/> float buffer per batch, made as needed and reused.</summary>
			public MaterialPropertyBlock[] Blocks = Array.Empty<MaterialPropertyBlock>();
			public float[][] Buffers = Array.Empty<float[]>();
		}

		/// <summary>The per-instance LOD cross-fade the vegetation and weather-lit shaders read (FishLodFade.hlsl).</summary>
		public static readonly int LodFadeId = Shader.PropertyToID("_FishLodFade");

		public GameObject Prefab { get; private set; }
		public int Layer { get; private set; }
		public bool HasLodGroup { get; private set; }

		/// <summary>The LODGroup's size (or the parts' largest box side without one).</summary>
		public float Size { get; private set; }

		/// <summary>The LODGroup's reference point in the root's space (the parts' box centre without one).</summary>
		public Vector3 LocalReference { get; private set; }

		/// <summary>Every part's box in the root's space.</summary>
		public Bounds LocalBounds { get; private set; }

		/// <summary>Transition heights, descending; one zero without a LODGroup (always drawn within range).</summary>
		public float[] Transitions { get; private set; }

		/// <summary>
		/// Each level's cross-fade band above its lower transition, in screen-relative height
		/// (<see cref="TerrainTreeMath.FadeWidth"/>): the LOD's own fadeTransitionWidth when set, else
		/// <see cref="TerrainTreeMath.DefaultFadeBandShare"/> of the transition. Zero without a LODGroup.
		/// </summary>
		public float[] FadeWidths { get; private set; }

		public Level[] Levels { get; private set; }

		/// <summary>How many instances the buckets hold room for.</summary>
		public int Capacity { get; private set; }

		/// <summary>Instances in the registered terrains that use this model (the capacity wanted).</summary>
		internal int Users;

		/// <summary>The model's id in the GPU path, −1 when it is drawn by the RenderMeshInstanced fallback.</summary>
		public int GpuId = -1;

		/// <summary>
		/// Registers the model with the GPU path (<see cref="GpuId"/>); a model with a part offset from the root
		/// stays on the fallback (instance matrices must stay shear-free; no per-part matrix is passed).
		/// </summary>
		public void RegisterGpu(TerrainGpuRenderer gpu)
		{
			GpuId = -1;
			if (gpu == null)
			{
				return;
			}
			var levels = new TerrainGpuRenderer.PartSource[Levels.Length][];
			for (int l = 0; l < Levels.Length; l++)
			{
				var parts = new List<TerrainGpuRenderer.PartSource>();
				foreach (Part part in Levels[l].Parts)
				{
					if (!part.Identity)
					{
						return;
					}
					for (int s = 0; s < part.Params.Length; s++)
					{
						parts.Add(new TerrainGpuRenderer.PartSource
						{
							Mesh = part.Mesh,
							Submesh = part.Submesh[s],
							Material = part.Params[s].material,
							Template = part.Params[s],
							CastsShadows = part.Params[s].shadowCastingMode != ShadowCastingMode.Off,
						});
					}
				}
				levels[l] = parts.ToArray();
			}
			GpuId = gpu.AddModel(Transitions, FadeWidths, Size, LocalReference, LocalBounds, levels, Prefab != null ? Prefab.name : null);
		}

		private TerrainTreeModel() { }

		/// <summary>
		/// Reads a prototype prefab into a model, or returns null with <paramref name="reason"/> set when it has
		/// nothing to draw or something the instanced path cannot draw. <paramref name="layer"/> is the layer
		/// every part draws on; −1 keeps each renderer's own (preserveTreePrototypeLayers).
		/// </summary>
		public static TerrainTreeModel Build(GameObject prefab, int layer, out string reason)
		{
			reason = null;
			if (prefab == null)
			{
				reason = "no prefab";
				return null;
			}
			if (prefab.GetComponentInChildren<Tree>(true) != null)
			{
				reason = $"'{prefab.name}' is a Tree Creator / SpeedTree tree";
				return null;
			}
			if (prefab.GetComponentInChildren<BillboardRenderer>(true) != null)
			{
				reason = $"'{prefab.name}' has a BillboardRenderer";
				return null;
			}
			if (prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
			{
				reason = $"'{prefab.name}' has a SkinnedMeshRenderer";
				return null;
			}

			var model = new TerrainTreeModel { Prefab = prefab, Layer = layer };
			Transform root = prefab.transform;

			LODGroup group = prefab.GetComponent<LODGroup>();
			List<Renderer[]> levels = new List<Renderer[]>();
			if (group != null)
			{
				LOD[] lods = group.GetLODs();
				var transitions = new float[lods.Length];
				for (int i = 0; i < lods.Length; i++)
				{
					transitions[i] = lods[i].screenRelativeTransitionHeight;
					levels.Add(lods[i].renderers ?? Array.Empty<Renderer>());
				}
				var widths = new float[lods.Length];
				for (int i = 0; i < lods.Length; i++)
				{
					widths[i] = TerrainTreeMath.FadeWidth(transitions, i, lods[i].fadeTransitionWidth);
				}
				model.Transitions = transitions;
				model.FadeWidths = widths;
				model.HasLodGroup = true;
			}
			else
			{
				levels.Add(prefab.GetComponentsInChildren<MeshRenderer>(true));
				model.Transitions = new[] { 0f };
				model.FadeWidths = new[] { 0f };
			}

			bool anyBounds = false;
			Bounds local = default;
			model.Levels = new Level[levels.Count];
			for (int l = 0; l < levels.Count; l++)
			{
				var parts = new List<Part>();
				foreach (Renderer renderer in levels[l])
				{
					if (renderer == null)
					{
						continue;
					}
					if (!(renderer is MeshRenderer))
					{
						reason = $"'{prefab.name}' LOD{l} has a {renderer.GetType().Name}";
						return null;
					}
					MeshFilter filter = renderer.GetComponent<MeshFilter>();
					Mesh mesh = filter != null ? filter.sharedMesh : null;
					Material[] materials = renderer.sharedMaterials;
					if (mesh == null || materials == null || materials.Length == 0)
					{
						continue;
					}
					foreach (Material material in materials)
					{
						if (material != null && !material.enableInstancing)
						{
							reason = $"'{prefab.name}' material '{material.name}' does not enable GPU instancing";
							return null;
						}
					}

					Matrix4x4 partLocal = LocalToRoot(root, renderer.transform);
					Part part = BuildPart(renderer, mesh, materials, partLocal, layer);
					parts.Add(part);

					Bounds partBounds = TerrainTreeMath.TransformBounds(partLocal, mesh.bounds);
					if (anyBounds)
					{
						local.Encapsulate(partBounds);
					}
					else
					{
						local = partBounds;
						anyBounds = true;
					}
				}
				model.Levels[l] = new Level { Parts = parts.ToArray() };
			}
			if (!anyBounds)
			{
				reason = $"'{prefab.name}' has no mesh to draw";
				return null;
			}
			model.LocalBounds = local;
			if (group != null)
			{
				model.Size = group.size;
				model.LocalReference = group.localReferencePoint;
			}
			else
			{
				model.Size = Mathf.Max(local.size.x, Mathf.Max(local.size.y, local.size.z));
				model.LocalReference = local.center;
			}
			return model;
		}

		private static Part BuildPart(Renderer renderer, Mesh mesh, Material[] materials, Matrix4x4 local, int layer)
		{
			int submeshes = Mathf.Max(1, mesh.subMeshCount);
			var slots = new List<int>();
			var parameters = new List<RenderParams>();
			for (int m = 0; m < materials.Length; m++)
			{
				if (materials[m] == null)
				{
					continue;
				}
				slots.Add(Mathf.Min(m, submeshes - 1));
				parameters.Add(new RenderParams(materials[m])
				{
					layer = layer >= 0 ? layer : renderer.gameObject.layer,
					shadowCastingMode = renderer.shadowCastingMode,
					receiveShadows = renderer.receiveShadows,
					reflectionProbeUsage = renderer.reflectionProbeUsage,
					renderingLayerMask = renderer.renderingLayerMask,
					lightProbeUsage = LightProbeUsage.Off,
					motionVectorMode = MotionVectorGenerationMode.Camera,
				});
			}
			bool identity = IsIdentity(local);
			return new Part
			{
				Mesh = mesh,
				Local = local,
				Identity = identity,
				UsesProbes = renderer.lightProbeUsage == LightProbeUsage.BlendProbes,
				Submesh = slots.ToArray(),
				Params = parameters.ToArray(),
			};
		}

		/// <summary>
		/// <paramref name="part"/>'s matrix in <paramref name="root"/>'s space, composed from the local transforms
		/// between them, so a part sitting on its root comes out exactly identity. Going through world space
		/// instead leaves float noise wherever the root is far from the origin — a scene object thousands of
		/// metres out, unlike a prefab asset — and that noise fails <see cref="IsIdentity"/>.
		/// </summary>
		private static Matrix4x4 LocalToRoot(Transform root, Transform part)
		{
			Matrix4x4 m = Matrix4x4.identity;
			for (Transform t = part; t != root; t = t.parent)
			{
				if (t == null)
				{
					return root.worldToLocalMatrix * part.localToWorldMatrix;   // a renderer outside the root's hierarchy
				}
				m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
			}
			return m;
		}

		/// <summary>True for a matrix that moves nothing (to float noise).</summary>
		public static bool IsIdentity(Matrix4x4 m)
		{
			for (int i = 0; i < 16; i++)
			{
				if (Mathf.Abs(m[i] - Matrix4x4.identity[i]) > 1e-5f)
				{
					return false;
				}
			}
			return true;
		}


		/// <summary>Makes the buckets hold at least <paramref name="capacity"/> instances per level.</summary>
		public void EnsureCapacity(int capacity)
		{
			if (capacity <= Capacity)
			{
				return;
			}
			DisposeBuffers();
			Capacity = capacity;
			foreach (Level level in Levels)
			{
				level.Bucket = new NativeArray<Matrix4x4>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				level.Fades = new NativeArray<float>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				level.Count = 0;
				level.FadedCount = 0;
				foreach (Part part in level.Parts)
				{
					if (!part.Identity)
					{
						part.Scratch = new NativeArray<Matrix4x4>(capacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
					}
				}
			}
		}

		/// <summary>Empties every bucket before a camera gathers.</summary>
		public void Clear()
		{
			foreach (Level level in Levels)
			{
				level.Count = 0;
				level.FadedCount = 0;
			}
		}

		/// <summary>
		/// Adds one instance to a level's bucket with its cross-fade (0 drawn whole), its chunk's box growing
		/// the bucket's.
		/// </summary>
		public void Append(int lod, Matrix4x4 matrix, float fade, in Bounds chunk)
		{
			Level level = Levels[lod];
			if (level.Count >= Capacity)
			{
				return;
			}
			Grow(level, chunk);
			level.Fades[level.Count] = fade;
			level.Bucket[level.Count++] = matrix;
			if (fade != 0f)
			{
				level.FadedCount++;
			}
		}

		/// <summary>Adds a run of instances to a level's bucket at once.</summary>
		public void AppendRange(int lod, NativeArray<Matrix4x4> source, int start, int count, in Bounds chunk)
		{
			Level level = Levels[lod];
			count = Mathf.Min(count, Capacity - level.Count);
			if (count <= 0)
			{
				return;
			}
			Grow(level, chunk);
			NativeArray<Matrix4x4>.Copy(source, start, level.Bucket, level.Count, count);
			// A whole run outside every band: drawn whole.
			NativeArray<float> fades = level.Fades;
			for (int i = level.Count, end = level.Count + count; i < end; i++)
			{
				fades[i] = 0f;
			}
			level.Count += count;
		}

		private static void Grow(Level level, in Bounds chunk)
		{
			if (level.Count == 0)
			{
				level.BoundsMin = chunk.min;
				level.BoundsMax = chunk.max;
				return;
			}
			level.BoundsMin = Vector3.Min(level.BoundsMin, chunk.min);
			level.BoundsMax = Vector3.Max(level.BoundsMax, chunk.max);
		}

		/// <summary>
		/// Draws every filled bucket for one camera, in batches of <see cref="TerrainTreeMath.MaxInstancesPerBatch"/>.
		/// <paramref name="probes"/> is what a probe-blending renderer gets: <see cref="LightProbeUsage.Off"/>
		/// (the scene's ambient probe) where the scene has no baked probes, which is exactly what such a
		/// renderer would be lit by. Returns the number of instanced draws issued.
		/// </summary>
		/// <remarks>
		/// Each batch is drawn from a sub-array starting at its first instance, so instance i of the draw is
		/// matrix i and fade i of the batch's property block (<c>_FishLodFade</c>, one value per instance).
		/// A level with no fading instance draws without a block (every instance reads 0, drawn whole). The
		/// blocks are the level's own, one per batch, reused by every part and submesh of the level and by
		/// the next camera (cameras render one after another, each after its own callback).
		/// </remarks>
		public int Draw(Camera camera, LightProbeUsage probes)
		{
			int draws = 0;
			foreach (Level level in Levels)
			{
				int count = level.Count;
				if (count == 0)
				{
					continue;
				}
				var bounds = new Bounds();
				bounds.SetMinMax(level.BoundsMin, level.BoundsMax);
				foreach (Part part in level.Parts)
				{
					if (!part.Identity)
					{
						Matrix4x4 local = part.Local;
						for (int i = 0; i < count; i++)
						{
							part.Scratch[i] = level.Bucket[i] * local;
						}
					}
				}
				int batches = TerrainTreeMath.BatchCount(count);
				bool faded = level.FadedCount > 0;
				if (faded)
				{
					EnsureBlocks(level, batches);
				}
				for (int b = 0; b < batches; b++)
				{
					TerrainTreeMath.Batch(count, b, out int start, out int size);
					MaterialPropertyBlock block = null;
					if (faded)
					{
						block = level.Blocks[b];
						float[] buffer = level.Buffers[b];
						NativeArray<float>.Copy(level.Fades, start, buffer, 0, size);
						block.SetFloatArray(LodFadeId, buffer);
					}
					foreach (Part part in level.Parts)
					{
						NativeArray<Matrix4x4> matrices = (part.Identity ? level.Bucket : part.Scratch).GetSubArray(start, size);
						for (int s = 0; s < part.Params.Length; s++)
						{
							RenderParams rp = part.Params[s];
							rp.camera = camera;
							rp.worldBounds = bounds;
							rp.lightProbeUsage = part.UsesProbes ? probes : LightProbeUsage.Off;
							rp.matProps = block;
							Graphics.RenderMeshInstanced(rp, part.Mesh, part.Submesh[s], matrices, size, 0);
							draws++;
						}
					}
				}
			}
			return draws;
		}

		/// <summary>Grows a level's per-batch blocks and buffers to <paramref name="batches"/> (allocates only when growing).</summary>
		private static void EnsureBlocks(Level level, int batches)
		{
			if (level.Blocks.Length >= batches)
			{
				return;
			}
			int old = level.Blocks.Length;
			Array.Resize(ref level.Blocks, batches);
			Array.Resize(ref level.Buffers, batches);
			for (int b = old; b < batches; b++)
			{
				level.Blocks[b] = new MaterialPropertyBlock();
				// Always the full batch length: a block fixes an array property's length the first time it is set.
				level.Buffers[b] = new float[TerrainTreeMath.MaxInstancesPerBatch];
			}
		}

		private void DisposeBuffers()
		{
			if (Levels == null)
			{
				return;
			}
			foreach (Level level in Levels)
			{
				if (level.Bucket.IsCreated)
				{
					level.Bucket.Dispose();
				}
				if (level.Fades.IsCreated)
				{
					level.Fades.Dispose();
				}
				level.Count = 0;
				level.FadedCount = 0;
				foreach (Part part in level.Parts)
				{
					if (part.Scratch.IsCreated)
					{
						part.Scratch.Dispose();
					}
				}
			}
			Capacity = 0;
		}

		public void Dispose()
		{
			DisposeBuffers();
		}
	}
}
