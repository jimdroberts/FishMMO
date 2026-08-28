using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all <see cref="GameObject"/>s along a line (ray) from the context in a given direction and distance.
	/// Useful for beam, projectile, or piercing effects.
	/// </summary>
	[Serializable]
	public class LineTargetSelector : TargetSelector
	{
		/// <summary>
		/// Length of the line.
		/// </summary>
		[Tooltip("Length of the line.")]
		[Min(0f)]
		public float Length = 10f;

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
		/// Preallocated array for storing raycast hits during line queries.
		/// </summary>
		private RaycastHit[] hits;

		/// <summary>
		/// Returns all <see cref="GameObject"/>s hit by a raycast from the context in its forward direction.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s hit by the ray, or empty if context is null.</returns>
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

		/// <summary>Casts, orders along the ray and caps — inside the caller's rewind scope.</summary>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
			EnsureHitBuffer();

			Vector3 origin = context.transform.position;
			Vector3 direction = context.transform.forward;
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			int hitCount = physicsScene.Raycast(origin, direction, hits, Length, TargetLayer, QueryTriggerInteraction.UseGlobal);

			/* Ordered along the ray, not by identity. A line is a sequence and every effect authored
			 * on one — pierce, beam falloff, "first thing you hit" — reads it that way, so distance is
			 * the meaningful order and identity only breaks exact ties. Unity's non-allocating Raycast
			 * promises no order at all, so without this the cap chose arbitrarily. */
			TargetOrdering.SortRaycastHits(hits, hitCount);

			int kept = 0;
			int cap = Mathf.Max(1, MaxHits);
			for (int i = 0; i < hitCount && kept < cap; i++)
			{
				Collider collider = hits[i].collider;
				if (collider == null || !AreConditionsMet(collider.gameObject, eventData))
				{
					continue;
				}
				results.Add(collider.gameObject);
				++kept;
			}
		}

		/// <summary>
		/// Ensures the reusable raycast buffer is wide enough that <see cref="MaxHits"/> is applied
		/// by this selector rather than by the broadphase.
		/// </summary>
		private void EnsureHitBuffer()
		{
			int size = QueryBufferSize(MaxHits);
			if (hits == null || hits.Length != size)
			{
				hits = new RaycastHit[size];
			}
		}
	}
}
