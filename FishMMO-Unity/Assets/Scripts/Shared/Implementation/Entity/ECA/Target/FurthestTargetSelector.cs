using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the furthest <see cref="GameObject"/> from the context within a given radius and layer mask.
	/// Useful for targeting the most distant enemy, ally, or object.
	/// </summary>
	[Serializable]
	public class FurthestTargetSelector : TargetSelector
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
		/// Starting size of the overlap buffer. <b>Not a cap on the result.</b>
		/// </summary>
		/// <remarks>
		/// This selector emits exactly one target — the furthest candidate — so there is nothing for a
		/// cap to truncate. The value only chooses the buffer's starting size through
		/// <see cref="TargetSelector.QueryBufferSize"/>, and
		/// <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> grows past it whenever a query comes
		/// back full, so setting it low costs a reallocation in a crowd and never a lost candidate.
		/// <para>
		/// It was called <c>MaxHits</c> and tooltipped "maximum number of hits to process" — a name
		/// and a description a designer reads as a cap, on a selector that has none. Renamed rather
		/// than re-documented, because the inspector shows the name.
		/// </para>
		/// </remarks>
		[Tooltip("Starting size of the overlap buffer. This selector returns one target; the value only affects allocation.")]
		[Min(1)]
		/* Renamed from MaxHits. The name was the last thing still claiming a cap after the
		 * tooltip and the remarks were corrected — and a name is what a designer reads in the
		 * inspector. FormerlySerializedAs keeps any value already authored against the old
		 * name; without it a rename silently drops the authored value and uses the C# default. */
		[FormerlySerializedAs("MaxHits")]
		public int QueryBufferHint = 16;



		/// <summary>
		/// Returns the furthest <see cref="GameObject"/> from the context within <see cref="Radius"/>.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable containing the furthest <see cref="GameObject"/>, or empty if none found.</returns>
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
		/// Queries and ranks inside the caller's rewind scope, so the distance that decides the
		/// answer is measured in the same world the candidates came from.
		/// </summary>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
			Collider[] hits = NewHitBuffer();
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
			 * ones it discarded were chosen by the broadphase — so the furthest-candidate choice below would be
			 * choosing from an arbitrary subset. This selector applies no cap at all; the
			 * starting size is only a hint, and this covers the crowd that outgrows it. */
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
				 * caster rigged that way was a candidate for its own furthest-target selection.
				 *
				 * No dedupe pass here: this selector emits exactly one winner, and the furthest
				 * collider of a body is the only winning entry that body could have contributed. */
				if (ReferenceEquals(TargetOrdering.ResolveHitKey(hit, out ICharacter _), contextKey) ||
					!AreConditionsMet(hit.gameObject, eventData))
				{
					continue;
				}
				candidates.Add(hit.gameObject);
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(origin, hit.transform.position)));
			}

			int furthest = TargetOrdering.FurthestIndex(ranks);
			if (furthest >= 0)
			{
				results.Add(candidates[ranks[furthest].Index]);
			}
		}

		/// <summary>
		/// A query buffer wide enough that the broadphase is not the thing deciding which candidates
		/// this selector gets to see.
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
		/// <b>This selector applies no cap</b> — see the remarks on <c>QueryBufferHint</c>, which is only a
		/// sizing hint here. The width still matters: a buffer the query fills is truncated by the
		/// broadphase in its own order, so a candidate that should have won could simply never be
		/// offered. <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> is what covers the crowd that
		/// outgrows the starting size.
		/// </para>
		/// </remarks>
		private Collider[] NewHitBuffer() => new Collider[QueryBufferSize(QueryBufferHint)];
	}
}
