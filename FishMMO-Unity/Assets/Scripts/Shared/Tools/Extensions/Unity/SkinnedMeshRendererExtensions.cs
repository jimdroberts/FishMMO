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
		/// </summary>
		/// <param name="renderer">The SkinnedMeshRenderer to update.</param>
		/// <param name="skeleton">The root Transform of the new skeleton.</param>
		public static void SetSkeleton(this SkinnedMeshRenderer renderer, Transform skeleton)
		{
			if (renderer == null || skeleton == null)
			{
				return;
			}
			// Get all bones from the skeleton by name.
			Dictionary<string, Transform> bones = skeleton.GetBones();
			List<Transform> newBones = new List<Transform>();
			foreach (Transform rendererBone in renderer.bones)
			{
				Transform bone;
				if (!bones.TryGetValue(rendererBone.name, out bone))
				{
					throw new UnityException("Missing bone(" + rendererBone.name + ")");
				}
				newBones.Add(bone);
			}
			renderer.rootBone = newBones[0];
			renderer.bones = newBones.ToArray();

			// Reparent the renderer to the new skeleton and destroy the old parent GameObject.
			Transform toDestroy = renderer.transform.parent;
			renderer.transform.SetParent(skeleton);
			MonoBehaviour.Destroy(toDestroy.gameObject);
		}
	}
}