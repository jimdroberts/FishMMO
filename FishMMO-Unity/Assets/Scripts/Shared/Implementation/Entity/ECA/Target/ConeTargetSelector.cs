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
		[Tooltip("Maximum number of hits to process.")]
		[Min(1)]
		public int MaxHits = 16;

		/// <summary>
		/// Preallocated array for storing collider hits during OverlapSphere queries.
		/// </summary>
		private Collider[] hits;

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
			EnsureHitBuffer();
			List<GameObject> candidates = new List<GameObject>();
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
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(origin, targetPosition)));
			}

			/* Nearest first, identity as the tiebreak, so a MaxHits cap keeps the closest candidates
			 * rather than the lowest ObjectIds (the previous SortStable ranked by identity alone and
			 * the distance passed to Rank was never consulted). */
			TargetOrdering.SortByDistance(ranks);
			TargetOrdering.ApplyMaxHits(ranks, MaxHits);

			for (int i = 0; i < ranks.Count; ++i)
			{
				results.Add(candidates[ranks[i].Index]);
			}
		}

		/// <summary>
		/// Ensures the reusable collider buffer is wide enough that <see cref="MaxHits"/> is applied
		/// by this selector rather than by the broadphase.
		/// </summary>
		private void EnsureHitBuffer()
		{
			int size = QueryBufferSize(MaxHits);
			/* Grow-only. This used to reallocate whenever the length differed from the authored
			 * size, which silently undid any growth TryGrowQueryBuffer had bought on the previous
			 * query — so a selector in a dense crowd re-truncated on every single cast. */
			if (hits == null || hits.Length < size)
			{
				hits = new Collider[size];
			}
		}
	}
}
