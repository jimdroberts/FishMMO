using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Static cache of prefab colliders keyed by ability template ID.
	/// Extracted from <see cref="AbilityObject"/> to keep that class focused on lifecycle and collision.
	/// Avoids repeated GetComponent calls on the prefab every spawn.
	/// </summary>
	public static class AbilityPrefabColliderCache
	{
		private static readonly Dictionary<int, Collider> Cache = new Dictionary<int, Collider>();

		/// <summary>
		/// Clears the cache. Call after addressable bundle reloads to prevent stale collider references.
		/// </summary>
		public static void Clear() => Cache.Clear();

		/// <summary>
		/// Returns the cached Collider from the ability's prefab, or null if none exists.
		/// Caches on first access. Detects and self-heals stale entries from addressable reloads.
		/// </summary>
		/// <param name="template">The ability template whose prefab collider to get.</param>
		/// <returns>The cached Collider from the prefab, or null if the prefab has no Collider.</returns>
		public static Collider GetPrefabCollider(AbilityTemplate template)
		{
			if (template.AbilityObjectPrefab == null) return null;

			if (Cache.TryGetValue(template.ID, out Collider collider))
			{
				if (collider != null)
				{
					if (collider.gameObject != template.AbilityObjectPrefab)
					{
						Debug.LogWarning(
							"[AbilityPrefabColliderCache] ID collision or stale prefab detected. Self-healing. " +
							"Call Clear() after addressable catalogue updates to avoid this.");
						Cache.Remove(template.ID);
					}
					else
					{
						return collider;
					}
				}
				else
				{
					Cache.Remove(template.ID);
				}
			}

			collider = template.AbilityObjectPrefab.GetComponent<Collider>();
			Cache[template.ID] = collider;
			return collider;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearOnDomainReload() => Cache.Clear();
	}
}
