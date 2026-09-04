using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// How far an ability can actually hit something from, as the AI should reason about it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="Ability.Range"/> is <c>Speed × LifeTime</c>: the distance a projectile travels.
	/// Every ability that does not travel — a punch spawned in front of the caster, a ball of
	/// flame held at the hands, a self buff — reports a range of ZERO, and the combat planner
	/// compared the distance to the target against that zero: the Attack intent was unreachable,
	/// so a melee orc walked up to its target and stood there forever, and a caster with the same
	/// kit kept its archetype's preferred distance and never cast. Issue #220.
	/// </para>
	/// <para>
	/// A stationary ability reaches as far as its object extends from the caster: the caster's
	/// own radius, plus the spawn offset (one half-extent of the ability object, see the Forward
	/// case in <c>AbilityObject</c>), plus the object's half-extent again, plus a little slack for
	/// the target's own body.
	/// </para>
	/// </remarks>
	public static class AIAbilityReach
	{
		/// <summary>Allowance for the target's body and NavMesh sampling error, in metres.</summary>
		public const float REACH_SLACK = 0.25f;

		/// <summary>No ability is treated as reaching less than this; a zero reach is unplannable.</summary>
		public const float MIN_REACH = 1.0f;

		/// <summary>
		/// Reach assumed for a stationary ability spawned at the aim hit point. The AI aims at its
		/// target, so this is the cast range it will engage at.
		/// </summary>
		public const float DEFAULT_TARGETED_REACH = 20.0f;

		/// <summary>
		/// The distance from the caster at which <paramref name="ability"/> can hit a target.
		/// </summary>
		/// <param name="ability">The ability.</param>
		/// <param name="casterRadius">The caster's body radius (NavMeshAgent or collider radius).</param>
		/// <returns>The reach in metres, or 0 for a null ability.</returns>
		public static float Resolve(Ability ability, float casterRadius)
		{
			if (ability == null || ability.Template == null)
			{
				return 0f;
			}

			float range = ability.Range;
			if (range > 0f)
			{
				return range;
			}

			float halfExtent = ResolvePrefabHalfExtent(AbilityPrefabColliderCache.GetPrefabCollider(ability.Template));
			return ResolveFromExtents(ability.Template.AbilitySpawnTarget, casterRadius, halfExtent);
		}

		/// <summary>
		/// The reach of an ability whose object does not travel, from the geometry involved.
		/// </summary>
		/// <remarks>Pure, so the rule can be pinned without prefabs.</remarks>
		/// <param name="spawnTarget">Where the ability object is placed.</param>
		/// <param name="casterRadius">The caster's body radius.</param>
		/// <param name="prefabHalfExtent">Half the ability object's horizontal size.</param>
		/// <returns>The reach in metres.</returns>
		public static float ResolveFromExtents(AbilitySpawnTarget spawnTarget, float casterRadius, float prefabHalfExtent)
		{
			if (spawnTarget == AbilitySpawnTarget.Target)
			{
				return DEFAULT_TARGETED_REACH;
			}

			float reach = Mathf.Max(casterRadius, 0f) + 2f * Mathf.Max(prefabHalfExtent, 0f) + REACH_SLACK;
			return Mathf.Max(MIN_REACH, reach);
		}

		/// <summary>
		/// Half the horizontal footprint of an ability object's collider, scale included.
		/// </summary>
		/// <remarks>
		/// Read from the collider's shape rather than <c>bounds</c>: the cache hands back the
		/// collider on the prefab ASSET, which has never been through a physics update and reports
		/// empty bounds.
		/// </remarks>
		/// <param name="collider">The prefab collider, or null.</param>
		/// <returns>The half-extent in metres, 0 when unknown.</returns>
		public static float ResolvePrefabHalfExtent(Collider collider)
		{
			if (collider == null)
			{
				return 0f;
			}

			Vector3 scale = collider.transform.lossyScale;
			float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

			switch (collider)
			{
				case BoxCollider box:
					return Mathf.Max(box.size.x, box.size.z) * 0.5f * horizontalScale;
				case SphereCollider sphere:
					return sphere.radius * Mathf.Max(horizontalScale, Mathf.Abs(scale.y));
				case CapsuleCollider capsule:
					return Mathf.Max(capsule.radius, capsule.height * 0.5f) * Mathf.Max(horizontalScale, Mathf.Abs(scale.y));
				default:
					Vector3 extents = collider.bounds.extents;
					return Mathf.Max(extents.x, extents.z);
			}
		}
	}
}
