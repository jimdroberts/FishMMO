using System;
using System.Collections.Generic;
using UnityEngine;
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
		/// The maximum number of colliders to consider per OverlapSphere query.
		/// </summary>
		[Tooltip("The maximum number of colliders to consider per OverlapSphere query.")]
		[Min(1)]
		public int MaxHits = 16;

		/// <summary>
		/// Preallocated array for storing collider hits during OverlapSphere queries.
		/// </summary>
		private Collider[] hits;

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
			EnsureHitBuffer();
			PhysicsScene physicsScene = context.scene.GetPhysicsScene();
			HashSet<GameObject> selected = new HashSet<GameObject>();
			List<GameObject> linkCandidates = new List<GameObject>();
			List<TargetRank> linkRanks = new List<TargetRank>();
			GameObject current = context;
			for (int i = 0; i < ChainLength && current != null; i++)
			{
				if (!AreConditionsMet(current, eventData))
				{
					break;
				}
				selected.Add(current);
				results.Add(current);
				Vector3 center = current.transform.position;
				// Direct query on purpose: the caller already holds the rewind scope, so routing
				// through LagCompensatedQuery would just re-resolve the tick and be refused as a
				// nested scope.
				int hitCount = physicsScene.OverlapSphere(center, ChainRadius, hits, TargetLayer, QueryTriggerInteraction.UseGlobal);
				/* Ties broken by network identity rather than by buffer order. Two candidates at the
				 * same distance is not a hypothetical for a chain — the links radiate outward from a
				 * point and equidistant pairs are common — and picking whichever the broadphase
				 * listed first made the whole remaining walk depend on it. */
				linkCandidates.Clear();
				linkRanks.Clear();
				for (int j = 0; j < hitCount; j++)
				{
					Collider hit = hits[j];
					if (hit == null || selected.Contains(hit.gameObject) || !AreConditionsMet(hit.gameObject, eventData))
					{
						continue;
					}
					linkCandidates.Add(hit.gameObject);
					linkRanks.Add(TargetOrdering.Rank(linkCandidates.Count - 1, hit.gameObject, Vector3.Distance(center, hit.transform.position)));
				}

				int nearest = TargetOrdering.NearestIndex(linkRanks);
				current = nearest >= 0 ? linkCandidates[linkRanks[nearest].Index] : null;
			}
		}

		/// <summary>
		/// Ensures the reusable collider buffer matches <see cref="MaxHits"/>.
		/// </summary>
		private void EnsureHitBuffer()
		{
			/* Wider than MaxHits on purpose — see TargetSelector.QueryBufferSize. A buffer sized at
			 * exactly MaxHits let the broadphase truncate the candidates in its own order before the
			 * nearest-link ranking ran, so a busy radius produced a different chain on each cast. */
			int bufferSize = QueryBufferSize(MaxHits);
			if (hits == null || hits.Length != bufferSize)
			{
				hits = new Collider[bufferSize];
			}
		}
	}
}