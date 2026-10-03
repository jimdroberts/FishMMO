using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// One mesh detail prototype as the instanced detail renderer draws it: the prefab root's mesh and
	/// material (what Unity draws for an instanced mesh detail), its renderer settings, and the bucket a
	/// camera's visible chunks are gathered into — one per prefab and layer across every terrain.
	/// </summary>
	/// <remarks>
	/// Refused (<see cref="Build"/> gives the reason; the terrain is then left to Unity): a texture
	/// (grass billboard) prototype, a mesh drawn through Unity's grass shader (useInstancing off — Unity
	/// merges those into patch meshes with its own shader), and a material without GPU instancing.
	/// Shadows follow the prefab's MeshRenderer (the generator sets them on for ferns, shrubs and cacti).
	/// </remarks>
	public sealed class TerrainDetailModel : IDisposable
	{
		public GameObject Prefab { get; private set; }
		public int Layer { get; private set; }
		public Mesh Mesh { get; private set; }
		public RenderParams Params;
		public bool CastsShadows { get; private set; }
		public bool UsesProbes { get; private set; }

		/// <summary>The mesh's box, for chunk bounds.</summary>
		public Bounds MeshBounds { get; private set; }

		/// <summary>The model's id in the GPU path, −1 on the RenderMeshInstanced fallback.</summary>
		public int GpuId = -1;

		/// <summary>Instances of this model resident on the GPU (the slots' capacity follows it up).</summary>
		public int Resident;

		/// <summary>
		/// The most instances any camera's slot can hold (coverage mode's disc bound), or 0 when there is none
		/// (instance-count mode). Capacity grows with <see cref="Resident"/> up to it, never past it.
		/// </summary>
		public int CapacityCeiling;

		/// <summary>Registers the model with the GPU path (<see cref="GpuId"/>): one level, always drawn within the detail distance.</summary>
		public void RegisterGpu(TerrainGpuRenderer gpu)
		{
			GpuId = -1;
			if (gpu == null)
			{
				return;
			}
			var part = new TerrainGpuRenderer.PartSource
			{
				Mesh = Mesh,
				Submesh = 0,
				Material = Params.material,
				Template = Params,
				CastsShadows = CastsShadows,
			};
			GpuId = gpu.AddModel(new[] { 0f }, new[] { 0f }, 1f, MeshBounds.center, MeshBounds, new[] { new[] { part } }, Prefab != null ? Prefab.name : null);
		}

		public NativeArray<Matrix4x4> Bucket;
		public int Count;
		private Vector3 boundsMin, boundsMax;

		private TerrainDetailModel() { }

		/// <summary>Reads a detail prototype, or returns null with the reason it cannot be drawn here.</summary>
		public static TerrainDetailModel Build(DetailPrototype prototype, int layer, out string reason)
		{
			reason = null;
			if (prototype == null)
			{
				reason = "an empty detail prototype";
				return null;
			}
			GameObject prefab = prototype.prototype;
			if (!prototype.usePrototypeMesh || prefab == null)
			{
				reason = "a texture (grass billboard) detail prototype";
				return null;
			}
			if (!prototype.useInstancing)
			{
				reason = $"detail '{prefab.name}' is drawn by Unity's grass shader (Use GPU Instancing off)";
				return null;
			}
			MeshFilter filter = prefab.GetComponent<MeshFilter>();
			MeshRenderer renderer = prefab.GetComponent<MeshRenderer>();
			Material material = renderer != null ? renderer.sharedMaterial : null;
			if (filter == null || filter.sharedMesh == null || material == null)
			{
				reason = $"detail '{prefab.name}' has no root MeshFilter mesh and MeshRenderer material";
				return null;
			}
			if (!material.enableInstancing)
			{
				reason = $"detail '{prefab.name}' material '{material.name}' does not enable GPU instancing";
				return null;
			}
			var model = new TerrainDetailModel
			{
				Prefab = prefab,
				Layer = layer,
				Mesh = filter.sharedMesh,
				MeshBounds = filter.sharedMesh.bounds,
				CastsShadows = renderer.shadowCastingMode != ShadowCastingMode.Off,
				UsesProbes = renderer.lightProbeUsage == LightProbeUsage.BlendProbes,
			};
			model.Params = new RenderParams(material)
			{
				layer = layer >= 0 ? layer : prefab.layer,
				shadowCastingMode = renderer.shadowCastingMode,
				receiveShadows = renderer.receiveShadows,
				reflectionProbeUsage = renderer.reflectionProbeUsage,
				renderingLayerMask = renderer.renderingLayerMask,
				lightProbeUsage = LightProbeUsage.Off,
				motionVectorMode = MotionVectorGenerationMode.Camera,
			};
			return model;
		}

		/// <summary>Empties the bucket before a camera gathers.</summary>
		public void Clear()
		{
			Count = 0;
		}

		/// <summary>Adds a run of a chunk's instances (expanded from the 48-byte rows), growing the bucket (by doubling) only when it is full.</summary>
		public void AppendRange(NativeArray<FishInstance> source, int start, int count, in Bounds chunk)
		{
			if (count <= 0)
			{
				return;
			}
			int needed = Count + count;
			if (!Bucket.IsCreated || Bucket.Length < needed)
			{
				var grown = new NativeArray<Matrix4x4>(Mathf.Max(needed, Bucket.IsCreated ? Bucket.Length * 2 : 1024), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
				if (Bucket.IsCreated)
				{
					NativeArray<Matrix4x4>.Copy(Bucket, grown, Count);
					Bucket.Dispose();
				}
				Bucket = grown;
			}
			if (Count == 0)
			{
				boundsMin = chunk.min;
				boundsMax = chunk.max;
			}
			else
			{
				boundsMin = Vector3.Min(boundsMin, chunk.min);
				boundsMax = Vector3.Max(boundsMax, chunk.max);
			}
			NativeArray<Matrix4x4> bucket = Bucket;
			for (int i = 0; i < count; i++)
			{
				bucket[Count + i] = source[start + i].ToMatrix();
			}
			Count = needed;
		}

		/// <summary>Draws the bucket for one camera; returns the draws issued.</summary>
		public int Draw(Camera camera, LightProbeUsage probes)
		{
			if (Count == 0)
			{
				return 0;
			}
			var bounds = new Bounds();
			bounds.SetMinMax(boundsMin, boundsMax);
			RenderParams rp = Params;
			rp.camera = camera;
			rp.worldBounds = bounds;
			rp.lightProbeUsage = UsesProbes ? probes : LightProbeUsage.Off;
			return TerrainInstancingShared.DrawBatches(rp, Mesh, 0, Bucket, Count);
		}

		public void Dispose()
		{
			if (Bucket.IsCreated)
			{
				Bucket.Dispose();
			}
			Count = 0;
		}
	}
}
