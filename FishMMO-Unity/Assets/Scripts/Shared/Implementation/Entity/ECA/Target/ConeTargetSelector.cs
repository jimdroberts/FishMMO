using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s within a cone in front of the context object.
	/// Useful for cone-shaped area-of-effect abilities.
	/// </summary>
	[Serializable]
	public class ConeTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius of the cone.
		/// </summary>
		[Tooltip("Radius of the cone.")]
		[Min(0f)]
		public float Radius = 5f;

		/// <summary>
		/// Angle of the cone in degrees.
		/// </summary>
		[Tooltip("Angle of the cone in degrees.")]
		[Range(0f, 360f)]
		public float Angle = 45f;

		/// <summary>
		/// Layer mask to filter targets.
		/// </summary>
		[Tooltip("Layer mask to filter targets.")]
		public LayerMask TargetLayer = ~0;

		/// <summary>
		/// Maximum number of hits to process.
		/// </summary>
		[Tooltip("Maximum number of distinct bodies to affect, nearest first. 0 or less means no cap.")]
		[Min(0)]
		public int MaxHits = 16;



		/// <summary>
		/// Returns all <see cref="GameObject"/>s within a cone in front of the context object.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s within the cone, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (!IsAuthoritativePeer(eventData))
			{
				yield break;
			}

			GameObject context = GetContext(eventData);
			if (context == null) yield break;

			List<GameObject> results = new List<GameObject>();
			GatherRewound(eventData, context, results, Gather);

			for (int i = 0; i < results.Count; ++i)
			{
				yield return results[i];
			}
		}

		/// <summary>Queries, applies the cone test, orders and caps — inside the caller's rewind scope.</summary>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
			Collider[] hits = NewHitBuffer();
			List<GameObject> candidates = new List<GameObject>();
			/* One key per candidate, so the cap below counts BODIES rather than colliders — see
			 * TargetOrdering.DedupeByBody. The candidate itself stays the collider's GameObject:
			 * consumers that want the character resolve it through EventData.SetTarget, which walks
			 * the parents, and a selector is free to return scenery that has no character at all. */
			List<GameObject> keys = new List<GameObject>();
			List<TargetRank> ranks = new List<TargetRank>();

			Vector3 origin = context.transform.position;
			Vector3 forward = context.transform.forward;
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			/* Re-queried until the buffer stops coming back full. A non-allocating query returns
			 * at most buffer.Length results and says nothing about how many it discarded, and the
			 * ones it discarded were chosen by the broadphase — so the ranking and the MaxHits cap
			 * below would be ordering an arbitrary subset. The starting size is already wider than
			 * the cap; this covers the crowd that outgrows it. */
			int hitCount;
			while (true)
			{
				hitCount = physicsScene.OverlapSphere(origin, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
				if (!TargetOrdering.TryGrowQueryBuffer(ref hits, hitCount))
				{
					break;
				}
			}

			for (int i = 0; i < hitCount; i++)
			{
				Collider hit = hits[i];
				if (hit == null)
				{
					continue;
				}

				/* The cone test reads the rewound position, matching the query that produced this
				 * candidate. It also refuses a target sitting exactly on the origin — see
				 * TargetOrdering.IsWithinCone for why a wide cone used to select its own caster. */
				Vector3 targetPosition = hit.transform.position;
				if (!TargetOrdering.IsWithinCone(origin, forward, targetPosition, Angle))
				{
					continue;
				}
				if (!AreConditionsMet(hit.gameObject, eventData))
				{
					continue;
				}

				candidates.Add(hit.gameObject);
				keys.Add(TargetOrdering.ResolveHitKey(hit, out ICharacter _));
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(origin, targetPosition)));
			}

			/* Nearest first, identity as the tiebreak, so a MaxHits cap keeps the closest candidates
			 * rather than the lowest ObjectIds (the previous SortStable ranked by identity alone and
			 * the distance passed to Rank was never consulted). */
			TargetOrdering.SortByDistance(ranks);
			/* Between the sort and the cap, never before the sort: the entry kept for a body is the
			 * first one met, which on a distance-ordered list is that body's nearest collider. Before
			 * this the cap counted colliders, so a target rigged with two hitboxes consumed two slots
			 * of MaxHits and a cone hit fewer characters than it was authored to. */
			TargetOrdering.DedupeByBody(ranks, keys);
			TargetOrdering.ApplyMaxHits(ranks, MaxHits);

			for (int i = 0; i < ranks.Count; ++i)
			{
				results.Add(candidates[ranks[i].Index]);
			}
		}

		/// <summary>
		/// A query buffer wide enough that the cap is applied by this selector rather than by the
		/// broadphase.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Local to one gather, not a field.</b> Selectors are serialized inline on shared assets,
		/// so one instance serves every character that casts the ability — and a candidate's authored
		/// conditions can fire nested triggers that reach this same instance again. A re-entrant gather
		/// re-ran the query into the shared array while the outer loop was still walking it, so the
		/// outer cast resolved against another cast's colliders. The scratch LISTS were made local for
		/// exactly this reason; the buffer was missed.
		/// </para>
		/// <para>
		/// Deliberately wider than the cap: sizing it at exactly MaxHits makes the broadphase perform
		/// the truncation, in its own order, before the selector sees the candidates. The caller still
		/// grows it through <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> when a query comes back
		/// full.
		/// </para>
		/// </remarks>
		private Collider[] NewHitBuffer() => new Collider[QueryBufferSize(MaxHits)];
	}
}
