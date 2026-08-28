using FishMMO.Shared.Core;
using FishNet.Managing.Timing;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Spatial queries resolved against where characters were when the caster's client saw them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every hit-resolving query in the ability system should route through here rather than calling
	/// <see cref="PhysicsScene"/> directly. Both entry points execute the query <b>eagerly inside</b>
	/// the rewind scope and return a count, so no caller can accidentally hold characters displaced
	/// while it enumerates results — the failure that would apply damage and run ECA actions against
	/// a world several hundred milliseconds stale.
	/// </para>
	/// <para>
	/// When there is nothing to compensate — a server-driven caster, a client whose tick bookkeeping
	/// is not yet established, or a scene with no recorded history — the query runs uncompensated.
	/// That is the behaviour these call sites had before, so an unregistered character degrades
	/// accuracy instead of dropping the hit.
	/// </para>
	/// </remarks>
	public static class LagCompensatedQuery
	{
		/// <summary>Overlap query resolved against the caster's view of the world.</summary>
		public static int OverlapSphere(
			EventData eventData, GameObject context, Vector3 center, float radius,
			Collider[] hits, LayerMask mask)
		{
			if (context == null || hits == null)
			{
				return 0;
			}

			PhysicsScene physicsScene = context.scene.GetPhysicsScene();

			int count;
			if (TryResolveRewind(eventData, out ICharacter caster, out uint rewindTick))
			{
				using (LagCompensationRegistry.Rewind(context.scene, rewindTick, caster))
				{
					count = physicsScene.OverlapSphere(center, radius, hits, mask, QueryTriggerInteraction.UseGlobal);
				}
			}
			else
			{
				count = physicsScene.OverlapSphere(center, radius, hits, mask, QueryTriggerInteraction.UseGlobal);
			}

			/* Ordered before it is handed back, so buffer order stops being an input to anything.
			 * Callers cap at a MaxHits, take the first match, or roll an index against this array; all
			 * three were reading a broadphase ordering that is neither reproducible across runs nor
			 * agreed between peers. Sorting here fixes every caller at once rather than asking each to
			 * remember. */
			TargetOrdering.SortColliders(hits, count);
			return count;
		}

		/// <summary>Raycast resolved against the caster's view of the world.</summary>
		/// <remarks>
		/// The hitscan path, and the one that needs compensation most: a ray is infinitely thin, so
		/// unlike a volume it has no tolerance to absorb the staleness of a live-position query.
		/// </remarks>
		public static int Raycast(
			EventData eventData, GameObject context, Vector3 origin, Vector3 direction, float distance,
			RaycastHit[] hits, LayerMask mask)
		{
			if (context == null || hits == null)
			{
				return 0;
			}

			PhysicsScene physicsScene = context.scene.GetPhysicsScene();

			int count;
			if (TryResolveRewind(eventData, out ICharacter caster, out uint rewindTick))
			{
				using (LagCompensationRegistry.Rewind(context.scene, rewindTick, caster))
				{
					count = physicsScene.Raycast(origin, direction, hits, distance, mask, QueryTriggerInteraction.UseGlobal);
				}
			}
			else
			{
				count = physicsScene.Raycast(origin, direction, hits, distance, mask, QueryTriggerInteraction.UseGlobal);
			}

			// Ordered along the ray: the only reading a line effect can act on, and the one Unity's
			// non-allocating overload explicitly does not provide.
			TargetOrdering.SortRaycastHits(hits, count);
			return count;
		}

		/// <summary>Resolves the caster and the tick its client was rendering peers at.</summary>
		public static bool TryResolveRewind(EventData eventData, out ICharacter caster, out uint rewindTick)
		{
			caster = eventData?.Initiator;
			rewindTick = 0u;

			TimeManager timeManager = caster?.NetworkObject?.TimeManager;
			if (timeManager == null)
			{
				return false;
			}

			return LagCompensationTick.TryResolve(caster, timeManager, out rewindTick);
		}
	}
}
