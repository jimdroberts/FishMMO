using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Extension methods for SkinnedMeshRenderer, including skeleton assignment and bone cache management.
	/// </summary>
	public static class SkinnedMeshRendererExtensions
	{
		/// <summary>
		/// Caches bone transforms by renderer and bone name for quick lookup.
		/// Key: renderer name, Value: Dictionary of bone name to Transform.
		/// </summary>
		private static Dictionary<string, Dictionary<string, Transform>> boneCache = new Dictionary<string, Dictionary<string, Transform>>();

		/// <summary>
		/// Clears the bone cache for all renderers.
		/// </summary>
		/// <param name="renderer">The SkinnedMeshRenderer to clear cache for (calls static method).</param>
		public static void ClearBoneCache(this SkinnedMeshRenderer renderer)
		{
			SkinnedMeshRendererExtensions.ClearBoneCache();
		}

		/// <summary>
		/// Clears the static bone cache for all renderers.
		/// </summary>
		public static void ClearBoneCache()
		{
			foreach (Dictionary<string, Transform> set in boneCache.Values)
			{
				set.Clear();
			}
			boneCache.Clear();
		}

		/// <summary>
		/// Sets the skeleton for a SkinnedMeshRenderer by matching bone names to the provided skeleton transform.
		/// Destroys the previous parent GameObject after reparenting.
		///
		/// WARNING: This destroys the renderer's parent GameObject. For pooled equipment renderers that share
		/// a common parent (e.g., EquipmentRoot), use <see cref="SkeletonBinder.BindMeshKeepParent"/> instead.
		/// </summary>
		/// <param name="renderer">The SkinnedMeshRenderer to update.</param>
		/// <param name="skeleton">The root Transform of the new skeleton.</param>
		/// <returns>True if the skeleton was successfully set, false if an error occurred.</returns>
		public static bool TrySetSkeleton(this SkinnedMeshRenderer renderer, Transform skeleton)
		{
			if (renderer == null || skeleton == null)
			{
				return false;
			}

			// Get all bones from the skeleton by name.
			Dictionary<string, Transform> bones;
			try
			{
				bones = skeleton.GetBones();
			}
			catch (System.Exception ex)
			{
				Debug.LogError($"[SkinnedMeshRendererExtensions] Failed to get bones from skeleton '{skeleton.name}': {ex.Message}");
				return false;
			}

			if (bones == null || bones.Count == 0)
			{
				Debug.LogError($"[SkinnedMeshRendererExtensions] Skeleton '{skeleton.name}' has no bones.");
				return false;
			}

			List<Transform> newBones = new List<Transform>();
			foreach (Transform rendererBone in renderer.bones)
			{
				if (rendererBone == null)
				{
					Debug.LogError($"[SkinnedMeshRendererExtensions] Renderer '{renderer.name}' has a null bone reference.");
					return false;
				}

				if (!bones.TryGetValue(rendererBone.name, out Transform bone))
				{
					Debug.LogError($"[SkinnedMeshRendererExtensions] Missing bone '{rendererBone.name}' on skeleton '{skeleton.name}' for renderer '{renderer.name}'. Equipment mesh is not compatible with this skeleton.");
					return false;
				}
				newBones.Add(bone);
			}

			renderer.rootBone = newBones[0];
			renderer.bones = newBones.ToArray();

			// Reparent the renderer to the new skeleton and destroy the old parent GameObject.
			Transform toDestroy = renderer.transform.parent;
			renderer.transform.SetParent(skeleton);
			MonoBehaviour.Destroy(toDestroy.gameObject);
			return true;
		}

		/// <summary>
		/// Sets the skeleton for a SkinnedMeshRenderer by matching bone names to the provided skeleton transform.
		/// Destroys the previous parent GameObject after reparenting.
		///
		/// WARNING: This destroys the renderer's parent GameObject. For pooled equipment renderers that share
		/// a common parent (e.g., EquipmentRoot), use <see cref="SkeletonBinder.BindMeshKeepParent"/> instead.
		/// This method throws on missing bones — prefer <see cref="TrySetSkeleton"/> in async callbacks.
		/// </summary>
		/// <param name="renderer">The SkinnedMeshRenderer to update.</param>
		/// <param name="skeleton">The root Transform of the new skeleton.</param>
		public static void SetSkeleton(this SkinnedMeshRenderer renderer, Transform skeleton)
		{
			if (!TrySetSkeleton(renderer, skeleton))
			{
				throw new UnityException($"Failed to set skeleton on renderer '{renderer?.name}'.");
			}
		}
	}
}
