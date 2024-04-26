using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseSpawnable : CachedScriptableObject<BaseSpawnable>, ICachedObject
	{
		[Tooltip("The spawnable prefab object.")]
		public GameObject Prefab;
		public float MinimumRespawnTime = 0.0f;
		public float MaximumRespawnTime = 0.0f;
	}
}