using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects a random <see cref="GameObject"/> from all within a given radius and layer mask.
	/// Useful for random targeting effects or abilities.
	/// </summary>
	[Serializable]
	public class RandomTargetSelector : TargetSelector
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
		/// Returns a random <see cref="GameObject"/> from all within <see cref="Radius"/> of the context.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> to search from.</param>
		/// <returns>An enumerable containing one random <see cref="GameObject"/>, or empty if none found.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			// Physics queries are non-deterministic across client/server.
			// Suppress during prediction replay to prevent target divergence.
			if (eventData != null && eventData.TryGet(out TickEventData tickData) && tickData.IsReplicateTick)
			{
				yield break;
			}

			GameObject context = GetContext(eventData);
			if (context == null) yield break;
			EnsureHitBuffer();
			var scene = context.scene;
			PhysicsScene physicsScene = scene.GetPhysicsScene();
			Vector3 origin = context.transform.position;
			int hitCount = physicsScene.OverlapSphere(origin, Radius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
			List<GameObject> candidates = new List<GameObject>();
			for (int i = 0; i < hitCount; i++)
			{
				Collider hit = hits[i];
				if (hit != null && hit.gameObject != context && AreConditionsMet(hit.gameObject, eventData))
					candidates.Add(hit.gameObject);
			}
			if (candidates.Count > 0)
			{
				DeterministicRNG rng = eventData?.RNG ?? DeterministicRNG.Shared;
				int idx = rng.Range(0, candidates.Count);
				yield return candidates[idx];
			}
		}

		/// <summary>
		/// Ensures the reusable collider buffer matches <see cref="MaxHits"/>.
		/// </summary>
		private void EnsureHitBuffer()
		{
			int maxHits = Mathf.Max(1, MaxHits);
			if (hits == null || hits.Length != maxHits)
			{
				hits = new Collider[maxHits];
			}
		}
	}
}