using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s within a certain radius of the context object.
	/// Useful for area-of-effect abilities or detection.
	/// </summary>
	[Serializable]
	public class AreaTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius of the area effect.
		/// </summary>
		[Tooltip("Radius of the area effect.")]
		[Min(0f)]
		public float Radius = 5f;

		/// <summary>
		/// Maximum number of hits to process in the area.
		/// </summary>
		[Tooltip("Maximum number of hits to process in the area.")]
		[Min(1)]
		public int MaxHits = 5;

		/// <summary>
		/// Layer mask to filter targets in the area.
		/// </summary>
		[Tooltip("Layer mask to filter targets in the area.")]
		public LayerMask TargetLayer = ~0; // All layers by default

		private Collider[] hits;

		/// <summary>
		/// Returns all <see cref="GameObject"/>s within <see cref="Radius"/> of the context object, filtered by <see cref="TargetLayer"/>.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s within the area, or empty if context is null.</returns>
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

		/// <summary>Queries, filters, orders and caps — all inside the caller's rewind scope.</summary>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
			EnsureHitBuffer();
			/* Local, not a reused field. A candidate's conditions can themselves carry selectors and
			 * fire nested triggers, so a shared scratch list is one authored composition away from
			 * being cleared out from under the gather that owns it. */
			List<GameObject> candidates = new List<GameObject>();
			/* One key per candidate, so the cap below counts BODIES rather than colliders — see
			 * TargetOrdering.DedupeByBody. The candidate itself stays the collider's GameObject:
			 * consumers that want the character resolve it through EventData.SetTarget, which walks
			 * the parents, and a selector is free to return scenery that has no character at all. */
			List<GameObject> keys = new List<GameObject>();
			List<TargetRank> ranks = new List<TargetRank>();

			Vector3 center = context.transform.position;
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			// Direct query: the caller already holds the rewind scope, so going through
			// LagCompensatedQuery would only re-resolve the tick and be refused as a nested scope.
			/* Re-queried until the buffer stops coming back full. A non-allocating query returns
			 * at most buffer.Length results and says nothing about how many it discarded, and the
			 * ones it discarded were chosen by the broadphase — so the ranking and the MaxHits cap
			 * below would be ordering an arbitrary subset. The starting size is already wider than
			 * the cap; this covers the crowd that outgrows it. */
			int hitCount;
			while (true)
			{
				hitCount = physicsScene.OverlapSphere(center, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
				if (!TargetOrdering.TryGrowQueryBuffer(ref hits, hitCount))
				{
					break;
				}
			}

			for (int i = 0; i < hitCount; i++)
			{
				Collider hit = hits[i];
				if (hit == null || !AreConditionsMet(hit.gameObject, eventData))
				{
					continue;
				}
				candidates.Add(hit.gameObject);
				keys.Add(TargetOrdering.ResolveHitKey(hit, out ICharacter _));
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(center, hit.transform.position)));
			}

			/* Nearest first, identity as the tiebreak, so a MaxHits cap keeps the closest candidates
			 * rather than the lowest ObjectIds (the previous SortStable ranked by identity alone and
			 * the distance passed to Rank was never consulted). */
			TargetOrdering.SortByDistance(ranks);
			/* Between the sort and the cap, never before the sort: the entry kept for a body is the
			 * first one met, which on a distance-ordered list is that body's nearest collider. Before
			 * this the cap counted colliders, so a target rigged with two hitboxes consumed two slots
			 * of MaxHits and an ability hit fewer characters than it was authored to. */
			TargetOrdering.DedupeByBody(ranks, keys);
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
