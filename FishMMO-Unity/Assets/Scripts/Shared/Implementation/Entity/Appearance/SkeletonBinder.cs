using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Utility for binding equipment SkinnedMeshRenderers to the character skeleton at runtime.
	/// Provides safe bone binding that does not destroy shared parent hierarchies.
	/// </summary>
	public static class SkeletonBinder
	{
		/// <summary>
		/// Cache of skeleton bone transforms keyed by a generation counter.
		/// When a skeleton is destroyed, its generation is invalidated to prevent stale Transform references
		/// from Unity's instance ID recycling.
		/// </summary>
		private static readonly Dictionary<int, (int generation, Dictionary<string, Transform> boneMap)> SkeletonBoneCache =
			new Dictionary<int, (int, Dictionary<string, Transform>)>();

		/// <summary>
		/// Monotonically increasing generation counter for cache invalidation.
		/// </summary>
		private static int globalCacheGeneration = 0;

		/// <summary>
		/// Binds a SkinnedMeshRenderer to the target skeleton.
		/// Matches the renderer's bone names to transforms in the skeleton hierarchy,
		/// assigns rootBone and bones, and reparents the renderer under the skeleton root.
		///
		/// WARNING: This destroys the renderer's current parent GameObject. Only use when the
		/// parent is a disposable wrapper (e.g., loading a standalone equipment prefab).
		/// For pooled equipment renderers, use <see cref="BindMeshKeepParent"/> instead.
		/// </summary>
		/// <param name="renderer">The equipment SkinnedMeshRenderer to bind.</param>
		/// <param name="skeletonRoot">The root Transform of the character skeleton.</param>
		public static void BindMesh(SkinnedMeshRenderer renderer, Transform skeletonRoot)
		{
			if (renderer == null || skeletonRoot == null)
			{
				Debug.LogError("[SkeletonBinder] BindMesh failed: renderer or skeletonRoot is null.");
				return;
			}

			renderer.SetSkeleton(skeletonRoot);
		}

		/// <summary>
		/// Binds a SkinnedMeshRenderer's bones to the target skeleton WITHOUT reparenting
		/// or destroying the current parent. Safe for pooled equipment renderers that share
		/// a common parent (e.g., EquipmentRoot).
		///
		/// Performs only the bone name matching and assignment of rootBone/bones.
		/// The renderer stays in its original position in the hierarchy.
		/// </summary>
		/// <param name="renderer">The equipment SkinnedMeshRenderer to bind.</param>
		/// <param name="skeletonRoot">The root Transform of the character skeleton.</param>
		/// <returns>True if all bones were matched successfully.</returns>
		public static bool BindMeshKeepParent(SkinnedMeshRenderer renderer, Transform skeletonRoot)
		{
			if (renderer == null || skeletonRoot == null)
			{
				Debug.LogError("[SkeletonBinder] BindMeshKeepParent failed: renderer or skeletonRoot is null.");
				return false;
			}

			if (renderer.bones == null || renderer.bones.Length == 0)
			{
				Debug.LogError("[SkeletonBinder] BindMeshKeepParent failed: renderer has no bones.");
				return false;
			}

			// Build bone lookup from the target skeleton
			Dictionary<string, Transform> skeletonBoneMap = skeletonRoot.GetBones();
			if (skeletonBoneMap == null || skeletonBoneMap.Count == 0)
			{
				Debug.LogError("[SkeletonBinder] BindMeshKeepParent failed: target skeleton has no bones.");
				return false;
			}

			List<Transform> newBones = new List<Transform>(renderer.bones.Length);

			for (int i = 0; i < renderer.bones.Length; i++)
			{
				Transform rendererBone = renderer.bones[i];
				if (rendererBone == null)
				{
					Debug.LogError($"[SkeletonBinder] BindMeshKeepParent: renderer bone at index {i} is null.");
					return false;
				}

				if (!skeletonBoneMap.TryGetValue(rendererBone.name, out Transform targetBone))
				{
					Debug.LogError($"[SkeletonBinder] BindMeshKeepParent: missing bone '{rendererBone.name}' on target skeleton for renderer '{renderer.name}'.");
					return false;
				}

				newBones.Add(targetBone);
			}

			renderer.rootBone = newBones[0];
			renderer.bones = newBones.ToArray();
			return true;
		}

		/// <summary>
		/// Finds a bone Transform by name within the skeleton hierarchy.
		/// Results are cached per skeleton root instance for performance.
		/// Uses a generation counter to prevent stale Transform references from Unity instance ID recycling.
		/// </summary>
		/// <param name="skeletonRoot">The root Transform of the character skeleton.</param>
		/// <param name="boneName">The exact bone name to find.</param>
		/// <returns>The bone Transform, or null if not found.</returns>
		public static Transform GetBoneTransform(Transform skeletonRoot, string boneName)
		{
			if (skeletonRoot == null || string.IsNullOrEmpty(boneName))
			{
				return null;
			}

			int instanceId = skeletonRoot.GetInstanceID();

			// Check cache first — validate generation to prevent stale entries
			if (SkeletonBoneCache.TryGetValue(instanceId, out var entry))
			{
				if (entry.generation == globalCacheGeneration && entry.boneMap != null)
				{
					if (entry.boneMap.TryGetValue(boneName, out Transform cached))
					{
						// Verify the cached Transform is still valid
						if (cached != null)
						{
							return cached;
						}
						// Stale reference — remove and continue to search
						entry.boneMap.Remove(boneName);
					}
				}
				else
				{
					// Generation mismatch — clear old entry
					SkeletonBoneCache.Remove(instanceId);
				}
			}

			// Create fresh cache entry for this skeleton
			if (!SkeletonBoneCache.ContainsKey(instanceId))
			{
				SkeletonBoneCache[instanceId] = (globalCacheGeneration, new Dictionary<string, Transform>());
			}

			Dictionary<string, Transform> boneMap = SkeletonBoneCache[instanceId].boneMap;

			// Recursive search
			Transform found = FindBoneRecursive(skeletonRoot, boneName);
			if (found != null)
			{
				boneMap[boneName] = found;
			}

			return found;
		}

		/// <summary>
		/// Invalidates all bone caches. Call when a character is destroyed or models are swapped.
		/// Increments the generation counter so all existing cache entries are invalidated.
		/// </summary>
		/// <param name="skeletonRoot">The skeleton root whose cache to clear, or null to invalidate all caches globally.</param>
		public static void ClearBoneCache(Transform skeletonRoot = null)
		{
			if (skeletonRoot != null)
			{
				SkeletonBoneCache.Remove(skeletonRoot.GetInstanceID());
			}
			else
			{
				// Global invalidation: increment generation so all entries are stale
				unchecked { globalCacheGeneration++; }
				if (globalCacheGeneration < 0) globalCacheGeneration = 0;
				SkeletonBoneCache.Clear();
				SkinnedMeshRendererExtensions.ClearBoneCache();
			}
		}

		/// <summary>
		/// Recursively searches for a bone by name in the transform hierarchy.
		/// </summary>
		private static Transform FindBoneRecursive(Transform parent, string boneName)
		{
			if (parent.name == boneName)
			{
				return parent;
			}

			for (int i = 0; i < parent.childCount; i++)
			{
				Transform found = FindBoneRecursive(parent.GetChild(i), boneName);
				if (found != null)
				{
					return found;
				}
			}

			return null;
		}
	}
}
