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
		/// <summary>
	/// Internal cache mapping ability template IDs to their prefab Collider components.
	/// </summary>
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
					/* Identity is judged by the collider's ROOT object, not its own GameObject —
					 * the lookup below reaches child hitboxes, whose own GameObject is never the
					 * prefab root, and comparing against it would evict a valid child entry (and
					 * log the collision warning) on every call. */
					if (collider.transform.root.gameObject != template.AbilityObjectPrefab)
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

			/* Children included, matching AbilityObject.CacheComponents — the two lookups must
			 * resolve the same collider on every peer or the sweep shape diverges. Root-first is
			 * GetComponentInChildren's documented order, so a root collider still wins when both
			 * exist. */
			collider = template.AbilityObjectPrefab.GetComponentInChildren<Collider>(true);
			Cache[template.ID] = collider;
			return collider;
		}

		/// <summary>
		/// Clears the cache on domain reload (e.g., after entering Play Mode in the Editor).
		/// </summary>
		/// <summary>
		/// Half extents of a collider's authored shape, in world scale, along the collider's own
		/// local axes. Reads the shape (size, radius, height, scale) and never
		/// <see cref="Collider.bounds"/>.
		/// </summary>
		/// <remarks>
		/// <see cref="GetPrefabCollider"/> hands back the PREFAB ASSET's collider. A collider that
		/// has never been simulated — a prefab asset, an inactive object — reports an EMPTY bounds,
		/// so anything derived from its bounds is silently zero. That is how a Forward-spawned
		/// Punch came to sit centred on its caster's own surface, half of it inside the caster,
		/// while the AI (already shape-based, see <c>AIAbilityReach</c>) planned as if the volume
		/// reached a full size further: an orc stopped "in reach" and punched the air in front of
		/// the player. Every consumer of the prefab collider's size goes through here so spawn
		/// geometry and reach can never disagree again.
		/// </remarks>
		public static Vector3 ResolveShapeHalfExtents(Collider collider)
		{
			if (collider == null)
			{
				return Vector3.zero;
			}

			Vector3 scale = collider.transform.lossyScale;
			scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

			switch (collider)
			{
				case BoxCollider box:
					return Vector3.Scale(box.size * 0.5f, scale);
				case SphereCollider sphere:
					{
						float radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
						return new Vector3(radius, radius, radius);
					}
				case CapsuleCollider capsule:
					{
						// direction: 0 = X, 1 = Y, 2 = Z — the same scale rules AbilityObjectSweep applies.
						int axis = Mathf.Clamp(capsule.direction, 0, 2);
						float radiusScale;
						float heightScale;
						switch (axis)
						{
							case 0:
								radiusScale = Mathf.Max(scale.y, scale.z);
								heightScale = scale.x;
								break;
							case 1:
								radiusScale = Mathf.Max(scale.x, scale.z);
								heightScale = scale.y;
								break;
							default:
								radiusScale = Mathf.Max(scale.x, scale.y);
								heightScale = scale.z;
								break;
						}
						float radius = capsule.radius * radiusScale;
						Vector3 extents = new Vector3(radius, radius, radius);
						extents[axis] = Mathf.Max(capsule.height * 0.5f * heightScale, radius);
						return extents;
					}
				default:
					// A mesh or terrain collider has no authored primitive; bounds is the only
					// size it knows, valid or not.
					return collider.bounds.extents;
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ClearOnDomainReload() => Cache.Clear();
	}
}
