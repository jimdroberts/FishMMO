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
		public LayerMask TargetLayer;

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
		/// <param name="context">The starting <see cref="GameObject"/> for the chain selection. Must not be null.</param>
		/// <returns>An enumerable of <see cref="GameObject"/>s representing the chain of selected targets, starting with <paramref name="context"/>.</returns>
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

			if (LagCompensatedQuery.TryResolveRewind(eventData, out FishMMO.Shared.Core.ICharacter caster, out uint rewindTick))
			{
				using (LagCompensationRegistry.Rewind(context.scene, rewindTick, caster))
				{
					BuildChain(eventData, context, chain);
				}
			}
			else
			{
				BuildChain(eventData, context, chain);
			}

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
				GameObject next = null;
				float minDist = float.MaxValue;
				for (int j = 0; j < hitCount; j++)
				{
					Collider hit = hits[j];
					if (hit != null && !selected.Contains(hit.gameObject) && AreConditionsMet(hit.gameObject, eventData))
					{
						float dist = Vector3.Distance(center, hit.transform.position);
						if (dist < minDist)
						{
							minDist = dist;
							next = hit.gameObject;
						}
					}
				}
				current = next;
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