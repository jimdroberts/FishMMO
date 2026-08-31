using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects a chain of <see cref="GameObject"/>s starting from the context object, such as for chain lightning or similar effects.
	/// Each link in the chain is the closest unselected <see cref="GameObject"/> within <see cref="ChainRadius"/> of the previous target.
	/// </summary>
	[Serializable]
	public class ChainTargetSelector : TargetSelector
	{
		/// <summary>
		/// The maximum number of targets to select in the chain (including the initial context).
		/// </summary>
		[Tooltip("The maximum number of targets to select in the chain (including the initial context).")]
		[Min(1)]
		public int ChainLength = 3;

		/// <summary>
		/// The radius to search for the next target in the chain, in Unity units.
		/// </summary>
		[Tooltip("The radius to search for the next target in the chain, in Unity units.")]
		[Min(0f)]
		public float ChainRadius = 5f;

		/// <summary>
		/// The layer mask used to filter which <see cref="GameObject"/>s can be selected as chain targets.
		/// </summary>
		[Tooltip("The layer mask used to filter which GameObjects can be selected as chain targets.")]
		public LayerMask TargetLayer = ~0;

		/// <summary>
		/// Sizing hint for the per-jump overlap buffer. <b>Not a cap on the chain.</b>
		/// </summary>
		/// <remarks>
		/// The chain's actual limit is <see cref="ChainLength"/>, which bounds the walk directly;
		/// each jump keeps exactly one link (the nearest unvisited candidate) however many
		/// candidates the query returned. This value only chooses the buffer's STARTING size
		/// through <see cref="TargetSelector.QueryBufferSize"/>, and
		/// <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> grows past it whenever a query comes
		/// back full — so setting it low costs a reallocation in a crowd, never a lost candidate.
		/// </remarks>
		[Tooltip("Starting size of the per-jump overlap buffer. The chain limit is ChainLength; this only affects allocation.")]
		[Min(1)]
		/* Renamed from MaxHits. The name was the last thing still claiming a cap after the
		 * tooltip and the remarks were corrected — and a name is what a designer reads in the
		 * inspector. FormerlySerializedAs keeps any value already authored against the old
		 * name; without it a rename silently drops the authored value and uses the C# default. */
		[FormerlySerializedAs("MaxHits")]
		public int QueryBufferHint = 16;



		/// <summary>
		/// Selects a chain of <see cref="GameObject"/>s starting from the given context object.
		/// Each subsequent target is the closest unselected <see cref="GameObject"/> within <see cref="ChainRadius"/> of the previous one.
		/// The chain will contain at most <see cref="ChainLength"/> targets.
		/// </summary>
		/// <param name="eventData">The event driving the selection; its context object starts the chain.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s representing the chain of selected targets, starting with the context object.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			/* Server only. The old guard here refused any event carrying a replicate tick, which
			 * the server's own spawn and self-target dispatches also carry — see
			 * TargetSelector.IsAuthoritativePeer. */
			if (!IsAuthoritativePeer(eventData))
			{
				yield break;
			}

			GameObject context = GetContext(eventData);
			if (context == null) yield break;

			/* The whole chain is walked EAGERLY, under a single rewind scope, and only then
			 * yielded. Two reasons, both learned the expensive way:
			 *
			 * 1. One scope, not one per link. Every link used to open its own rewind through
			 *    LagCompensatedQuery — N links cost N x (displace every character + two
			 *    Physics.SyncTransforms passes). The registry refuses nested scopes, so one outer
			 *    scope here means the whole walk runs against a single consistent rewound world.
			 *
			 * 2. No yields while the world is displaced. Selectors are iterators, and the
			 *    consumer between yields is the damage/ECA pipeline — running that against
			 *    rewound colliders would apply effects to a world hundreds of milliseconds
			 *    stale. Materialising first means the scope is closed before any consumer runs.
			 *
			 * A side effect of walking inside the scope is that link RANKING now reads rewound
			 * positions, matching the query that produced the candidates — the old code selected
			 * from the rewound world but ranked by live distance. Conditions are evaluated inside
			 * the scope for the same consistency reason; they gate the traversal itself. */
			List<GameObject> chain = new List<GameObject>(Mathf.Max(1, ChainLength));

			GatherRewound(eventData, context, chain, BuildChain);

			for (int i = 0; i < chain.Count; i++)
			{
				yield return chain[i];
			}
		}

		/// <summary>
		/// Walks the chain from the context, appending each accepted link to <paramref name="results"/>.
		/// Runs entirely inside the caller's rewind scope (or uncompensated when there is none).
		/// </summary>
		private void BuildChain(EventData eventData, GameObject context, List<GameObject> results)
		{
			Collider[] hits = NewHitBuffer();
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			/* Keyed on the BODY a link belongs to, not on the collider that was hit. It held
			 * `hit.gameObject`, so a target rigged with two hitboxes was never marked as visited by
			 * the first of them — the walk chained straight back onto a victim it had already struck
			 * through its other collider and burned a link doing it. The keys are resolved through
			 * TargetOrdering.ResolveHitKey, which collapses colliders sharing a rigidbody or a
			 * character and leaves everything else — a wall, a building — keyed to itself. */
			HashSet<GameObject> selected = new HashSet<GameObject>();
			List<GameObject> linkCandidates = new List<GameObject>();
			List<GameObject> linkKeys = new List<GameObject>();
			List<TargetRank> linkRanks = new List<TargetRank>();
			GameObject current = context;
			GameObject currentKey = TargetOrdering.ResolveObjectKey(context);
			for (int i = 0; i < ChainLength && current != null; i++)
			{
				if (!AreConditionsMet(current, eventData))
				{
					break;
				}
				selected.Add(currentKey);
				/* The link itself is still the object the query returned. Resolution and dedupe are
				 * separate questions: consumers that want the character walk to it through
				 * EventData.SetTarget, and a chain is free to arc between scene objects. */
				results.Add(current);
				Vector3 center = current.transform.position;
				// Direct query on purpose: the caller already holds the rewind scope, so routing
				// through LagCompensatedQuery would just re-resolve the tick and be refused as a
				// nested scope.
				/* Re-queried until the buffer stops coming back full. A non-allocating query returns
				 * at most buffer.Length results and says nothing about how many it discarded, and the
				 * ones it discarded were chosen by the broadphase — so the walk below would be
				 * choosing from an arbitrary subset. This selector applies no cap at all; the
				 * starting size is only a hint, and this covers the crowd that outgrows it. */
				int hitCount;
				while (true)
				{
					hitCount = physicsScene.OverlapSphere(center, ChainRadius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
					if (!TargetOrdering.TryGrowQueryBuffer(ref hits, hitCount))
					{
						break;
					}
				}
				/* Ties broken by network identity rather than by buffer order. Two candidates at the
				 * same distance is not a hypothetical for a chain — the links radiate outward from a
				 * point and equidistant pairs are common — and picking whichever the broadphase
				 * listed first made the whole remaining walk depend on it. */
				linkCandidates.Clear();
				linkKeys.Clear();
				linkRanks.Clear();
				for (int j = 0; j < hitCount; j++)
				{
					Collider hit = hits[j];
					if (hit == null)
					{
						continue;
					}
					GameObject key = TargetOrdering.ResolveHitKey(hit, out ICharacter _);
					if (selected.Contains(key) || !AreConditionsMet(hit.gameObject, eventData))
					{
						continue;
					}
					linkCandidates.Add(hit.gameObject);
					linkKeys.Add(key);
					linkRanks.Add(TargetOrdering.Rank(linkCandidates.Count - 1, hit.gameObject, Vector3.Distance(center, hit.transform.position)));
				}

				/* No dedupe over the link candidates: only the nearest becomes the next link, and the
				 * nearest collider of a body is the only entry that body could have won with. The
				 * body is marked visited on the next pass, which is what stops the walk returning. */
				int nearest = TargetOrdering.NearestIndex(linkRanks);
				if (nearest >= 0)
				{
					current = linkCandidates[linkRanks[nearest].Index];
					currentKey = linkKeys[linkRanks[nearest].Index];
				}
				else
				{
					current = null;
					currentKey = null;
				}
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