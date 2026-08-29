using System.Collections.Generic;
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
	/// <b>Anything that decides WHICH hits survive belongs inside the scope too</b>, which is why
	/// <see cref="OverlapSphereNearest"/> ranks, deduplicates and caps in there rather than handing
	/// back a raw buffer. A caller that selects its candidates from the rewound world and then ranks
	/// them by live positions is reading two different worlds; at 300&#160;ms that is metres, and it
	/// picks a different victim than the one the caster was looking at. The selectors solve the same
	/// problem with <c>TargetSelector.GatherRewound</c>.
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
		/// <summary>One body a compensated query selected, with the impact information a hit needs.</summary>
		/// <remarks>
		/// Shared by the overlap and the ray so the two hit-resolving shapes hand their callers the
		/// same thing. <see cref="Point"/> and <see cref="Normal"/> are what an impact effect reads;
		/// both are measured inside the rewind scope and carried here, never recomputed afterwards
		/// from a transform that has since been restored.
		/// </remarks>
		public readonly struct CompensatedHit
		{
			/// <summary>The nearest collider on <see cref="Character"/>, or the bare collider hit.</summary>
			public readonly Collider Collider;

			/// <summary>The character that owns <see cref="Collider"/>, or null for scenery.</summary>
			public readonly ICharacter Character;

			/// <summary>Distance from the query origin, measured in the rewound world.</summary>
			public readonly float Distance;

			/// <summary>World point of impact.</summary>
			public readonly Vector3 Point;

			/// <summary>
			/// Surface normal at <see cref="Point"/> for a ray; the reverse of the query direction for
			/// an overlap, which has no surface crossing to take one from.
			/// </summary>
			public readonly Vector3 Normal;

			public CompensatedHit(Collider collider, ICharacter character, float distance, Vector3 point, Vector3 normal)
			{
				Collider = collider;
				Character = character;
				Distance = distance;
				Point = point;
				Normal = normal;
			}
		}

		/// <summary>Query buffer, grown on demand and reused. See <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/>.</summary>
		private static Collider[] overlapBuffer = new Collider[TargetOrdering.QueryBufferSize(0)];

		/// <summary>Raycast buffer, grown the same way.</summary>
		private static RaycastHit[] rayBuffer = new RaycastHit[TargetOrdering.QueryBufferSize(0)];

		/// <summary>Reused rank column, so an area query allocates nothing.</summary>
		private static readonly List<TargetRank> ranks = new List<TargetRank>(64);

		/// <summary>
		/// Dedupe keys for the hits already kept, parallel to the caller's results list.
		/// </summary>
		/// <remarks>
		/// Held alongside rather than re-derived from each kept <see cref="CompensatedHit"/>. The key for a
		/// body with no character is its ROOT — the rigidbody's GameObject — which cannot be recovered
		/// from the kept collider without walking to the rigidbody again, and a re-derivation that
		/// walked only as far as <c>Collider.gameObject</c> would fail to match the second child
		/// collider of the same body. Keeping the key that was actually used removes the question.
		/// </remarks>
		private static readonly List<GameObject> keptKeys = new List<GameObject>(16);

		/// <summary>
		/// The characters nearest <paramref name="center"/>, resolved against the caster's view of the
		/// world, deduplicated per character and capped at <paramref name="maxHits"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Everything that decides the answer happens inside one rewind scope.</b> The query, the
		/// distance measurement and the ranking all read the same displaced world. Splitting them was
		/// the trap this method exists to close: the previous shape returned a raw buffer with the
		/// scope already shut, so the caller selected its candidates from the world the caster saw and
		/// then ranked them by where those characters are NOW — two different worlds, differing by the
		/// target's speed times the caster's latency, which at 300&#160;ms is metres. It is the same
		/// failure <c>TargetSelector.GatherRewound</c> was built for, and the reason
		/// <c>RewoundOverlapSphere</c> was deleted rather than left available.
		/// </para>
		/// <para>
		/// <b>Ordered by distance, not by identity.</b> A cap is only meaningful over an ordered set,
		/// and the order a blast radius means is "nearest first". Identity order is equally
		/// reproducible and gameplay nonsense: truncating it keeps the lowest ObjectIds, so a
		/// three-target AoE in a crowd hit the same three earliest-spawned characters every time and
		/// never the ones standing on the impact point. Ties fall through to
		/// <see cref="TargetOrdering.CompareStable"/>, which every peer computes identically.
		/// </para>
		/// <para>
		/// <b>One entry per character.</b> Keyed through <see cref="TargetOrdering.ResolveHitKey"/>, so
		/// a body with two hitboxes costs one hit and one slot of the cap — otherwise the same ability
		/// hits a different NUMBER of characters depending on how its targets happen to be rigged, and
		/// a character rigged with its collider on a child is dropped entirely. The nearest collider on
		/// a body is the one kept, which falls out of walking the distance order.
		/// </para>
		/// <para>
		/// <b>The cap is applied by early exit, not by truncation.</b> Walking the sorted ranks and
		/// stopping at <paramref name="maxHits"/> distinct characters means the per-candidate component
		/// resolution runs for roughly the cap rather than for the whole crowd. That is where the
		/// saving in an area query is — not in narrowing the physics query, which is one broadphase
		/// traversal either way.
		/// </para>
		/// </remarks>
		/// <param name="eventData">Event whose <c>Initiator</c> is the caster whose view is reconstructed.</param>
		/// <param name="context">Object whose scene is queried and rewound.</param>
		/// <param name="center">Centre of the query, and the point distances are measured from.</param>
		/// <param name="radius">Query radius.</param>
		/// <param name="mask">Layers to query.</param>
		/// <param name="maxHits">Maximum distinct bodies to keep. Zero or less means no cap.</param>
		/// <param name="charactersOnly">
		/// Drop hits that resolve to no <see cref="ICharacter"/> before they consume a slot of the cap.
		/// True for anything that resolves damage: <c>TargetLayerMask</c> defaults to every layer, so a
		/// blast let off beside terrain would otherwise spend its whole cap on scenery and hit nobody.
		/// </param>
		/// <param name="results">Receives the hits, nearest first. Cleared first.</param>
		/// <returns>The number of hits written to <paramref name="results"/>.</returns>
		public static int OverlapSphereNearest(
			EventData eventData, GameObject context, Vector3 center, float radius,
			LayerMask mask, int maxHits, bool charactersOnly, List<CompensatedHit> results)
		{
			if (results == null)
			{
				return 0;
			}
			results.Clear();

			if (context == null)
			{
				return 0;
			}

			PhysicsScene physicsScene = context.scene.GetPhysicsScene();

			if (TryResolveRewind(eventData, out ICharacter caster, out RewindTarget target))
			{
				using (LagCompensationRegistry.Rewind(context.scene, target, caster))
				{
					GatherNearest(physicsScene, center, radius, mask, maxHits, charactersOnly, results);
				}
			}
			else
			{
				GatherNearest(physicsScene, center, radius, mask, maxHits, charactersOnly, results);
			}

			return results.Count;
		}

		/// <summary>
		/// The body of <see cref="OverlapSphereNearest"/>, run under whatever scope the caller opened.
		/// </summary>
		private static void GatherNearest(
			PhysicsScene physicsScene, Vector3 center, float radius,
			LayerMask mask, int maxHits, bool charactersOnly, List<CompensatedHit> results)
		{
			/* Grown until the query stops filling it. A full non-allocating query says nothing about
			 * how many results it discarded, and the ones it discarded were chosen by the broadphase —
			 * so a cap or a sort applied to a truncated buffer is ordering an arbitrary subset. The
			 * starting size is already wider than the cap; this covers the crowd that outgrows it. */
			int count;
			while (true)
			{
				count = physicsScene.OverlapSphere(center, radius, overlapBuffer, mask, QueryTriggerInteraction.UseGlobal);
				if (!TargetOrdering.TryGrowQueryBuffer(ref overlapBuffer, count))
				{
					break;
				}
			}

			if (count < 1)
			{
				return;
			}

			ranks.Clear();
			for (int i = 0; i < count; ++i)
			{
				Collider hit = overlapBuffer[i];
				if (hit == null)
				{
					continue;
				}
				// Distances are read HERE, inside the scope, from the same positions the query used.
				ranks.Add(TargetOrdering.Rank(i, hit.gameObject, Vector3.Distance(center, hit.transform.position)));
			}

			TargetOrdering.SortByDistance(ranks);

			keptKeys.Clear();
			int cap = maxHits > 0 ? maxHits : int.MaxValue;
			for (int i = 0; i < ranks.Count && results.Count < cap; ++i)
			{
				Collider hit = overlapBuffer[ranks[i].Index];
				GameObject key = TargetOrdering.ResolveHitKey(hit, out ICharacter character);
				if (key == null)
				{
					continue;
				}

				/* Skipped BEFORE the cap is charged, not filtered out by the caller afterwards.
				 * Charging the cap for scenery would make the number of characters an ability hits
				 * depend on what else happens to be standing near the blast. */
				if (charactersOnly && character == null)
				{
					continue;
				}

				/* Linear scan over what has been kept, not a HashSet. The kept list is bounded by the
				 * cap — a handful of entries — so this is cheaper than hashing and allocates nothing,
				 * and the walk is in distance order so the entry already kept for a body is always the
				 * nearest one.
				 *
				 * ReferenceEquals, not ==: Unity overloads == on Object to ask the engine whether the
				 * native object is still alive, which is a native crossing per comparison. Both
				 * operands came out of a query in this same frame, so aliveness is not in question and
				 * identity is all this needs. */
				bool duplicate = false;
				for (int j = 0; j < keptKeys.Count; ++j)
				{
					if (ReferenceEquals(keptKeys[j], key))
					{
						duplicate = true;
						break;
					}
				}
				if (duplicate)
				{
					continue;
				}

				keptKeys.Add(key);
				/* Bounds.ClosestPoint, not Collider.ClosestPoint: the latter logs an error for a
				 * non-convex mesh collider rather than answering, and this is only ever an effect
				 * position. The normal points back at the blast, which is the only honest answer an
				 * overlap can give — the shape is already inside, so there is no surface crossing. */
				Vector3 point = hit.bounds.ClosestPoint(center);
				Vector3 away = point - center;
				Vector3 normal = away.sqrMagnitude > 1e-8f ? away.normalized : Vector3.up;
				results.Add(new CompensatedHit(hit, character, ranks[i].Distance, point, normal));
			}

			ranks.Clear();
			keptKeys.Clear();
		}

		/// <summary>
		/// The bodies a ray passes through, in order along it, deduplicated per character and capped
		/// at <paramref name="maxHits"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The hitscan path, and the one that needs compensation most.</b> A ray is infinitely
		/// thin, so unlike a volume it has no tolerance to absorb the staleness of a live-position
		/// query: the difference between a hit and a miss is the target's own width. At 300&#160;ms a
		/// running peer is metres from where the shooter saw it, which is simply a miss.
		/// </para>
		/// <para>
		/// <b>Ordered along the ray.</b> Distance leads and identity only breaks exact ties — a line
		/// is a sequence and every effect authored on one (pierce, falloff, "the first thing you
		/// hit") reads it that way. Unity's non-allocating overload promises no order at all, so
		/// without this the pierce cap chose its victims arbitrarily. This is the one query shape
		/// where the boundary CAN pick the order, because a ray admits only one reading; the overlap
		/// above deliberately does not, which is why its identity sort was removed.
		/// </para>
		/// <para>
		/// <b>One entry per character</b>, so a body with two hitboxes costs one pierce rather than
		/// two, and the nearest of its colliders is the one reported — which falls out of walking the
		/// ray order. A beam re-queried every tick therefore hits each target once per tick, which is
		/// what a beam means; the per-lifetime hit set that stops a projectile draining its hit count
		/// into one victim belongs to <c>AbilityObject</c> and is deliberately not duplicated here.
		/// </para>
		/// </remarks>
		/// <param name="eventData">Event whose <c>Initiator</c> is the caster whose view is reconstructed.</param>
		/// <param name="context">Object whose scene is queried and rewound.</param>
		/// <param name="origin">Where the ray starts.</param>
		/// <param name="direction">Ray direction. Normalised here; a degenerate vector returns nothing.</param>
		/// <param name="distance">Ray length.</param>
		/// <param name="mask">Layers to query.</param>
		/// <param name="maxHits">Maximum distinct bodies to keep. Zero or less means no cap.</param>
		/// <param name="charactersOnly">
		/// Drop hits that resolve to no <see cref="ICharacter"/> before they consume a slot of the cap.
		/// See the note on <see cref="OverlapSphereNearest"/>; for a ray this also means scenery does
		/// not stop a shot, so set it false when a wall should block one.
		/// </param>
		/// <param name="results">Receives the hits, nearest first. Cleared first.</param>
		/// <returns>The number of hits written to <paramref name="results"/>.</returns>
		public static int RaycastNearest(
			EventData eventData, GameObject context, Vector3 origin, Vector3 direction, float distance,
			LayerMask mask, int maxHits, bool charactersOnly, List<CompensatedHit> results)
		{
			if (results == null)
			{
				return 0;
			}
			results.Clear();

			if (context == null || distance <= 0f || direction.sqrMagnitude < 1e-8f)
			{
				return 0;
			}

			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			Vector3 heading = direction.normalized;

			if (TryResolveRewind(eventData, out ICharacter caster, out RewindTarget target))
			{
				using (LagCompensationRegistry.Rewind(context.scene, target, caster))
				{
					GatherAlongRay(physicsScene, origin, heading, distance, mask, maxHits, charactersOnly, results);
				}
			}
			else
			{
				GatherAlongRay(physicsScene, origin, heading, distance, mask, maxHits, charactersOnly, results);
			}

			return results.Count;
		}

		/// <summary>
		/// The body of <see cref="RaycastNearest"/>, run under whatever scope the caller opened.
		/// </summary>
		private static void GatherAlongRay(
			PhysicsScene physicsScene, Vector3 origin, Vector3 direction, float distance,
			LayerMask mask, int maxHits, bool charactersOnly, List<CompensatedHit> results)
		{
			// Grown until the query stops filling it, for the same reason as the overlap above.
			int count;
			while (true)
			{
				count = physicsScene.Raycast(origin, direction, rayBuffer, distance, mask, QueryTriggerInteraction.UseGlobal);
				if (!TargetOrdering.TryGrowQueryBuffer(ref rayBuffer, count))
				{
					break;
				}
			}

			if (count < 1)
			{
				return;
			}

			/* Sorted in place by distance along the ray. Every value the ranking and the results need
			 * — distance, point, normal — was measured by the query itself, inside the scope, and is
			 * carried in the RaycastHit rather than recomputed from a transform. */
			TargetOrdering.SortRaycastHits(rayBuffer, count);

			keptKeys.Clear();
			int cap = maxHits > 0 ? maxHits : int.MaxValue;
			for (int i = 0; i < count && results.Count < cap; ++i)
			{
				RaycastHit hit = rayBuffer[i];
				GameObject key = TargetOrdering.ResolveHitKey(hit.collider, out ICharacter character);
				if (key == null)
				{
					continue;
				}

				if (charactersOnly && character == null)
				{
					continue;
				}

				bool duplicate = false;
				for (int j = 0; j < keptKeys.Count; ++j)
				{
					if (ReferenceEquals(keptKeys[j], key))
					{
						duplicate = true;
						break;
					}
				}
				if (duplicate)
				{
					continue;
				}

				keptKeys.Add(key);
				results.Add(new CompensatedHit(hit.collider, character, hit.distance, hit.point, hit.normal));
			}

			keptKeys.Clear();
		}

		/// <summary>Resolves the caster and the tick its client was rendering peers at.</summary>
		public static bool TryResolveRewind(EventData eventData, out ICharacter caster, out RewindTarget target)
		{
			caster = eventData?.Initiator;
			target = RewindTarget.None;

			TimeManager timeManager = caster?.NetworkObject?.TimeManager;
			if (timeManager == null)
			{
				return false;
			}

			return LagCompensationTick.TryResolve(caster, timeManager, out target);
		}
	}
}
