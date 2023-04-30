using System.Collections.Generic;
using UnityEngine;

public static class SkinnedMeshRendererExtensions
{
	private static Dictionary<string, Dictionary<string, Transform>> boneCache = new Dictionary<string, Dictionary<string, Transform>>();

	public static void ClearBoneCache(this SkinnedMeshRenderer renderer)
	{
		SkinnedMeshRendererExtensions.ClearBoneCache();
	}
	public static void ClearBoneCache()
	{
		foreach (Dictionary<string, Transform> set in boneCache.Values)
		{
			set.Clear();
		}
		boneCache.Clear();
	}

	public static void SetSkeleton(this SkinnedMeshRenderer renderer, Transform skeleton)
	{
		if (renderer == null || skeleton == null)
		{
			return;
		}
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

		Transform toDestroy = renderer.transform.parent;
		renderer.transform.SetParent(skeleton);
		MonoBehaviour.Destroy(toDestroy.gameObject);
	}
}