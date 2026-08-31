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
		/// Salt distinguishing this selector's stream from any other consumer of the same event.
		/// </summary>
		/// <remarks>
		/// A constant, deliberately. The seed must be a function of things every peer agrees on —
		/// the initiator's network id and the event's tick — and nothing else. Anything drawn from
		/// local state (a frame count, an instance id, a list length) would put the two peers on
		/// different streams, which is the entire failure this exists to prevent.
		/// </remarks>
		private const int RandomSelectionSalt = 0x5241_4E44; // "RAND"

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
		[Tooltip("Size of the pool the single random target is drawn from, nearest first. 0 or less draws from every candidate in the radius.")]
		[Min(0)]
		public int MaxHits = 16;



		/// <summary>
		/// Returns a random <see cref="GameObject"/> from all within <see cref="Radius"/> of the context.
		/// </summary>
		/// <param name="eventData">The event driving the selection.</param>
		/// <returns>An enumerable containing one random <see cref="GameObject"/>, or empty if none found.</returns>
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

		/// <summary>Builds the candidate set, orders it, then draws one index from it.</summary>
		/// <remarks>
		/// <para>
		/// <b>The order is half of the determinism.</b> A reproducible index into an unordered list is
		/// still an arbitrary choice — the index means a different character depending on what order
		/// the broadphase happened to fill the buffer in. Sorting is what makes "index 3" name the
		/// same character on every run.
		/// </para>
		/// <para>
		/// <b>The order is distance, not identity, and that bounds what it guarantees.</b>
		/// <see cref="TargetOrdering.SortByDistance"/> ranks by distance from the origin and only
		/// breaks exact ties by identity, so two peers holding a character at different interpolated
		/// positions can order the candidates differently and draw a different winner from the same
		/// generator state. That is not a defect here because this selector is server-only
		/// (<see cref="TargetSelector.IsAuthoritativePeer"/>) — the roll is made once, where the
		/// positions are authoritative, and its result reaches everyone else through the
		/// authoritative paths. It is stated because it is the reason this must stay server-only:
		/// a distance-ordered cap can never be made peer-agreed by arithmetic.
		/// </para>
		/// <para>
		/// <b><see cref="MaxHits"/> is applied BEFORE the draw</b>, so the roll is taken from the
		/// <see cref="MaxHits"/> nearest bodies rather than from every candidate in the radius.
		/// </para>
		/// <para>
		/// <b>The generator is the other half.</b> This used to fall back to
		/// <see cref="DeterministicRNG.Shared"/> whenever the event carried no generator, which is
		/// every event type except an ability collision. That instance is seeded from
		/// <c>Environment.TickCount</c> and is shared by the whole process, so the roll was neither
		/// reproducible nor agreed on. An event that was not handed a generator now derives one from
		/// its own identity; an event that was keeps it.
		/// </para>
		/// </remarks>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
			Collider[] hits = NewHitBuffer();
			List<GameObject> candidates = new List<GameObject>();
			/* One key per candidate, so the roll below draws from a set of BODIES rather than of
			 * colliders — see TargetOrdering.DedupeByBody. The candidate itself stays the collider's
			 * GameObject: consumers that want the character resolve it through EventData.SetTarget,
			 * which walks the parents, and a selector is free to return scenery that has none. */
			List<GameObject> keys = new List<GameObject>();
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
				 * caster rigged that way could draw itself out of its own random selection. */
				GameObject key = TargetOrdering.ResolveHitKey(hit, out ICharacter _);
				if (ReferenceEquals(key, contextKey) || !AreConditionsMet(hit.gameObject, eventData))
				{
					continue;
				}
				candidates.Add(hit.gameObject);
				keys.Add(key);
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, hit.gameObject, Vector3.Distance(origin, hit.transform.position)));
			}

			/* Nearest first, identity as the tiebreak, so a MaxHits cap keeps the closest candidates
			 * rather than the lowest ObjectIds (the previous SortStable ranked by identity alone and
			 * the distance passed to Rank was never consulted). */
			TargetOrdering.SortByDistance(ranks);
			/* Between the sort and the cap. A duplicate is not merely a wasted slot here: it is a
			 * loaded die. Two hitboxes on one body occupied two entries and that body was drawn twice
			 * as often as a single-collider one, which is a bias no amount of determinism downstream
			 * can undo. */
			TargetOrdering.DedupeByBody(ranks, keys);
			TargetOrdering.ApplyMaxHits(ranks, MaxHits);

			if (ranks.Count == 0)
			{
				return;
			}

			DeterministicRNG rng = ResolveRNG(eventData);
			int index = rng.Range(0, ranks.Count);
			results.Add(candidates[ranks[index].Index]);
		}

		/// <summary>
		/// The generator this selection draws from: the event's own when one was threaded onto it,
		/// otherwise a stream derived from the event's identity under this selector's salt.
		/// </summary>
		private DeterministicRNG ResolveRNG(EventData eventData)
		{
			if (eventData == null)
			{
				return new DeterministicRNG(EventData.DeriveSeed(0, 0u, RandomSelectionSalt));
			}
			if (eventData.HasExplicitRNG)
			{
				return eventData.RNG;
			}
			return eventData.DeriveRNG(RandomSelectionSalt);
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
