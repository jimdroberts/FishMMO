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
			List<TargetRank> ranks = new List<TargetRank>();

			Vector3 center = context.transform.position;
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			// Direct query: the caller already holds the rewind scope, so going through
			// LagCompensatedQuery would only re-resolve the tick and be refused as a nested scope.
			int hitCount = physicsScene.OverlapSphere(center, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);

			for (int i = 0; i < hitCount; i++)
			{
				Collider hit = hits[i];
				if (hit == null || !AreConditionsMet(hit.gameObject, eventData))
				{
					continue;
				}
				candidates.Add(hit.gameObject);
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(center, hit.transform.position)));
			}

			TargetOrdering.SortStable(ranks);
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
			if (hits == null || hits.Length != size)
			{
				hits = new Collider[size];
			}
		}
	}
}
