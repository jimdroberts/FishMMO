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
		/// How many distinct bodies the line may hit. Zero or less pierces everything on it.
		/// </summary>
		/// <remarks>
		/// <c>[Min(0)]</c>, not <c>[Min(1)]</c>. The gather below treats a non-positive cap as
		/// uncapped — matching <see cref="TargetOrdering.CappedCount"/> and every other selector —
		/// and that was introduced precisely so a beam could be authored to pierce everything on its
		/// line. The attribute still clamped the Inspector to 1, so the behaviour existed and could
		/// not be reached.
		/// </remarks>
		[Tooltip("How many distinct bodies the line may hit. 1 stops at the first; 0 pierces everything on the line.")]
		[Min(0)]
		public int MaxHits = 16;



		/// <summary>
		/// Returns all <see cref="GameObject"/>s hit by a raycast from the context in its forward direction.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s hit by the ray, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (!ResolvesTargetsLocally(eventData))
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
			RaycastHit[] hits = NewHitBuffer();

			Vector3 origin = context.transform.position;
			Vector3 direction = context.transform.forward;
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			/* Re-queried until the buffer stops coming back full. A non-allocating query returns
			 * at most buffer.Length results and says nothing about how many it discarded, and the
			 * ones it discarded were chosen by the broadphase — so the ranking and the MaxHits cap
			 * below would be ordering an arbitrary subset. The starting size is already wider than
			 * the cap; this covers the crowd that outgrows it. */
			int hitCount;
			while (true)
			{
				hitCount = physicsScene.Raycast(origin, direction, hits, Length, TargetLayer, QueryTriggerInteraction.UseGlobal);
				if (!TargetOrdering.TryGrowQueryBuffer(ref hits, hitCount))
				{
					break;
				}
			}

			/* Ordered along the ray, not by identity. A line is a sequence and every effect authored
			 * on one — pierce, beam falloff, "first thing you hit" — reads it that way, so distance is
			 * the meaningful order and identity only breaks exact ties. Unity's non-allocating Raycast
			 * promises no order at all, so without this the cap chose arbitrarily. */
			TargetOrdering.SortRaycastHits(hits, hitCount);

			int kept = 0;
			/* One key per body already pierced, so the cap counts BODIES rather than colliders. A ray
			 * reports every collider it passes through, so a target rigged with two hitboxes consumed
			 * two of a beam's pierce slots and the beam stopped short of a victim behind it. Streamed
			 * rather than a DedupeByBody pass because this loop already walks the hits in ray order
			 * and emits as it goes — the first collider met on a body is its entry face, which is the
			 * one a pierce means. */
			List<GameObject> keptKeys = new List<GameObject>();
			/* Zero or less means NO cap, matching TargetOrdering.CappedCount and every other selector.
			 * This used to be Mathf.Max(1, MaxHits), so an author who set MaxHits to 0 on a beam
			 * meaning "pierce everything on the line" got a beam that stopped at the first target —
			 * the one place in the target system where a non-positive cap meant something else. */
			int cap = MaxHits > 0 ? MaxHits : int.MaxValue;
			for (int i = 0; i < hitCount && kept < cap; i++)
			{
				Collider collider = hits[i].collider;
				if (collider == null)
				{
					continue;
				}
				GameObject key = TargetOrdering.ResolveHitKey(collider, out ICharacter _);
				if (TargetOrdering.ContainsBody(keptKeys, key) || !AreConditionsMet(collider.gameObject, eventData))
				{
					continue;
				}
				keptKeys.Add(key);
				/* The result is still the collider the ray hit. Resolution and dedupe are separate
				 * questions: consumers that want the character walk to it through
				 * EventData.SetTarget, and a beam is free to report the wall panel it stopped on. */
				results.Add(collider.gameObject);
				++kept;
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
		private RaycastHit[] NewHitBuffer() => new RaycastHit[QueryBufferSize(MaxHits)];
	}
}
