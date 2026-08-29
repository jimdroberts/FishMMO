using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the nearest <see cref="GameObject"/> to the context within a given radius and layer mask.
	/// Useful for targeting the closest enemy, ally, or object.
	/// </summary>
	[Serializable]
	public class NearestTargetSelector : TargetSelector
	{
		/// <summary>
		/// Radius to search for targets.
		/// </summary>
		[Tooltip("Radius to search for targets.")]
		[Min(0f)]
		public float Radius = 10f;

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
		/// Returns the nearest <see cref="GameObject"/> to the context within <see cref="Radius"/>.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable containing the nearest <see cref="GameObject"/>, or empty if none found.</returns>
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

		/// <summary>
		/// Queries and ranks inside the caller's rewind scope.
		/// </summary>
		/// <remarks>
		/// The ranking is the part that moved. It used to happen after
		/// <see cref="LagCompensatedQuery"/> had already closed its scope, so the candidate set came
		/// from where the caster saw those characters and the distances came from where they are now —
		/// two different worlds, differing by the peer's speed times its latency. At 300&#160;ms that
		/// is metres, which is enough to pick a different character as "nearest" than the one the
		/// caster was looking at.
		/// </remarks>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
			EnsureHitBuffer();
			List<GameObject> candidates = new List<GameObject>();
			List<TargetRank> ranks = new List<TargetRank>();

			Vector3 origin = context.transform.position;
			/* The caster's own body, keyed the same way a hit is, so the exclusion below asks "is this
			 * the caster" rather than "is this the caster's root transform" — see
			 * TargetOrdering.ResolveObjectKey. */
			GameObject contextKey = TargetOrdering.ResolveObjectKey(context);
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
				/* Compared on the resolved body, not on the collider. This read
				 * `hit.gameObject == context`, which is false for a caster's own child hitbox — so a
				 * caster rigged that way was a candidate for its own nearest-target selection.
				 *
				 * No dedupe pass here: this selector emits exactly one winner, and the nearest
				 * collider of a body is the only winning entry that body could have contributed. */
				if (ReferenceEquals(TargetOrdering.ResolveHitKey(hit, out ICharacter _), contextKey) ||
					!AreConditionsMet(hit.gameObject, eventData))
				{
					continue;
				}
				candidates.Add(hit.gameObject);
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(origin, hit.transform.position)));
			}

			int nearest = TargetOrdering.NearestIndex(ranks);
			if (nearest >= 0)
			{
				results.Add(candidates[ranks[nearest].Index]);
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
