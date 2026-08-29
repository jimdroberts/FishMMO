using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for selecting targets in an ability or event context.
	/// Implementations consume the current <see cref="EventData"/> (its <see cref="EventData.Target"/>
	/// or <see cref="EventData.Initiator"/> serve as the spatial / contextual reference)
	/// and yield one or more <see cref="GameObject"/>s for triggers, conditions or actions to operate on.
	/// <para>
	/// <b>Asset safety:</b> selectors are serialized inline on Trigger ScriptableObjects via
	/// <c>[SerializeReference]</c>. Unity cannot serialize references to scene GameObjects from
	/// asset files, so selectors intentionally hold no direct scene references. To "pick a
	/// specific scene object" from an asset-based Trigger, use
	/// <see cref="NamedSceneObjectTargetSelector"/> or <see cref="TaggedSceneObjectTargetSelector"/>
	/// — they resolve scene objects at runtime by name or tag. For inline (MonoBehaviour-hosted)
	/// triggers in a scene, prefer setting <see cref="EventData.Target"/> at the invocation
	/// site so the trigger receives the picked GameObject through standard event flow.
	/// </para>
	/// </summary>
	[Serializable]
	public abstract class TargetSelector
	{
		/// <summary>
		/// Conditions that must be met for a target to be valid. Evaluated per-candidate
		/// inside <see cref="AreConditionsMet"/>. Honors the ambient
		/// <see cref="EventData.ConditionFilter"/> via <see cref="TriggerExecution.AreConditionsMet"/>.
		/// </summary>
		[Tooltip("Conditions that must be met for a target to be valid.")]
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Selects targets based on the supplied event context.
		/// </summary>
		/// <param name="eventData">The event data driving the selection. <see cref="EventData.Target"/>
		/// (when set) or <see cref="EventData.Initiator"/> typically serves as the spatial origin.</param>
		/// <returns>The selected GameObjects.</returns>
		public abstract IEnumerable<GameObject> SelectTargets(EventData eventData);

		/// <summary>
		/// Returns a short, designer-facing tooltip line describing this selector's targeting
		/// (e.g. "Nearest enemy within 10m"), or <c>null</c> when the selector has nothing
		/// to contribute. Override on selectors that have player-visible targeting semantics.
		/// </summary>
		public virtual string GetTooltipContribution() => null;

		/// <summary>
		/// Returns the spatial origin for this selector — preferring <see cref="EventData.Target"/>,
		/// falling back to <see cref="EventData.Initiator"/>'s GameObject.
		/// </summary>
		/// <param name="eventData">The current event data.</param>
		/// <returns>A GameObject to use as a spatial reference, or null.</returns>

		/// <summary>
		/// True when this peer is the one allowed to resolve a physics query into targets.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Server only, and not "not a replicate tick".</b> Every physics selector used to open
		/// with <c>if (tickData.IsReplicateTick) yield break;</c>, on the theory that a replicate tick
		/// means a client replay. It does not. <c>AbilityObject</c>'s self-target dispatch and its
		/// OnPreSpawn/OnSpawn dispatches, and <c>AbilityController</c>'s activation triggers, all
		/// attach a <c>TickEventData</c> built from the replicate <c>PredictionTick</c> — on the
		/// server as well. So the guard fired on the server too, and a point-blank area ability
		/// selected nothing and dealt no damage on any peer. <c>AbilityApplyAreaAction</c> was
		/// corrected for exactly this and gates on the peer instead; this is the same rule, applied
		/// at the selector.
		/// </para>
		/// <para>
		/// The tick payload is left on the event untouched. It still carries real information for the
		/// tick-domain consumers downstream (buff stamping, cooldowns) — it just never meant what the
		/// old guard read it as.
		/// </para>
		/// </remarks>
		protected static bool IsAuthoritativePeer(EventData eventData) => EcaAuthority.IsServer(eventData);

		/// <summary>
		/// Delegate for the body of a rewound gather.
		/// </summary>
		protected delegate void GatherTargets(EventData eventData, GameObject context, List<GameObject> results);

		/// <summary>
		/// Collects candidates inside a single rewind scope and returns them once it has closed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is <see cref="ChainTargetSelector"/>'s pattern, generalised, and it exists for two
		/// reasons the chain learned first.
		/// </para>
		/// <para>
		/// <b>Ranking must read the same world the query did.</b> <see cref="LagCompensatedQuery"/>
		/// closes its rewind before returning, so a selector that queried through it and then measured
		/// distances was selecting from the rewound world and ranking by live positions — the nearest
		/// candidate by one measure and a different one by the other, differing by the peer's speed
		/// times its latency. Conditions are evaluated inside the scope for the same reason: a range
		/// condition that disagrees with the query that produced the candidate is not a filter, it is
		/// a coin toss.
		/// </para>
		/// <para>
		/// <b>Nothing is yielded while the world is displaced.</b> Selectors are iterators and the
		/// consumer between two yields is the damage and ECA pipeline. Materialising the whole result
		/// first means the scope has closed before any of that runs.
		/// </para>
		/// </remarks>
		/// <param name="eventData">The event driving the selection.</param>
		/// <param name="context">The spatial origin object; its scene is the one rewound.</param>
		/// <param name="results">Receives the selected objects, in the gather's order.</param>
		/// <param name="gather">The query and ranking to run under the scope.</param>
		protected static void GatherRewound(EventData eventData, GameObject context, List<GameObject> results, GatherTargets gather)
		{
			if (LagCompensatedQuery.TryResolveRewind(eventData, out ICharacter caster, out RewindTarget target))
			{
				/* A nested Rewind is refused by the registry rather than stacked, so a selector
				 * invoked from inside somebody else's scope (a region action fanning out over several
				 * characters in one tick) runs against that outer rewind instead of corrupting it. */
				using (LagCompensationRegistry.Rewind(context.scene, target, caster))
				{
					gather(eventData, context, results);
				}
			}
			else
			{
				gather(eventData, context, results);
			}
		}

		/// <summary>
		/// Query buffer size for a selector whose authored cap is <paramref name="maxHits"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Deliberately larger than the cap. Sizing the buffer at exactly <c>MaxHits</c> makes the
		/// physics broadphase perform the truncation, in its own order, before the selector ever sees
		/// the candidates — so a cap of 5 in a crowd of 20 picked five arbitrary characters and the
		/// deterministic sort that follows had nothing to work with. Querying wide and capping after
		/// the sort is what makes the cap mean "the first five in a defined order".
		/// </para>
		/// <para>
		/// <b>A starting size, not a limit.</b> It only moves the truncation point from <c>MaxHits</c>
		/// up to <c>MaxHits * 4</c>; every caller must still grow the buffer through
		/// <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> when a query comes back full.
		/// </para>
		/// <para>
		/// The rule itself lives on <see cref="TargetOrdering"/> now, because the ability actions
		/// resolve hits without a selector and need the identical arithmetic — it was duplicated
		/// inline in <c>AbilityApplyAreaAction</c> for exactly as long as it lived somewhere only
		/// selectors could reach. This forwarder stays so the selectors read naturally.
		/// </para>
		/// </remarks>
		protected static int QueryBufferSize(int maxHits) => TargetOrdering.QueryBufferSize(maxHits);

		/* RewoundOverlapSphere and RewoundRaycast were removed rather than left unused.
		 *
		 * They wrapped LagCompensatedQuery, which closes its rewind scope before it returns — correct
		 * for a caller that only wants the hit set, and a trap for a selector, which then ranks and
		 * filters those hits by LIVE positions. Every selector that used them selected out of the
		 * caster's view of the world and then decided between the candidates using the server's,
		 * which at 300 ms is a different answer. The replacement is GatherRewound: one scope, held
		 * open across the query, the ranking and the conditions, closed before anything is yielded.
		 * Leaving the old helpers in place as a shorter-looking option is how the next selector
		 * reintroduces the bug. */

		protected static GameObject GetContext(EventData eventData)
		{
			if (eventData == null) return null;
			if (eventData.Target != null) return eventData.Target;
			return eventData.Initiator?.GameObject;
		}

		/// <summary>
		/// Evaluates this selector's per-target <see cref="Conditions"/> against the candidate target.
		/// Builds a forked <see cref="EventData"/> scoped to the candidate so conditions see the right
		/// <see cref="EventData.Target"/> / <see cref="EventData.TargetCharacter"/>, then delegates to
		/// <see cref="TriggerExecution.AreConditionsMet"/> so nested conditions' own
		/// <see cref="BaseCondition.TargetSelector"/> and <see cref="BaseCondition.Combine"/> settings
		/// are honored uniformly with top-level Trigger conditions.
		/// </summary>
		/// <param name="target">The candidate target GameObject.</param>
		/// <param name="eventData">The parent event data, or null.</param>
		/// <returns>True when no conditions exist, or all conditions pass.</returns>
		protected bool AreConditionsMet(GameObject target, EventData eventData)
		{
			if (Conditions == null || Conditions.Count == 0)
			{
				return true;
			}

			if (target == null)
			{
				return false;
			}

			EventData scoped = ForkForCandidate(target, eventData);
			return TriggerExecution.AreConditionsMet(Conditions, scoped);
		}

		/// <summary>
		/// Builds a per-candidate event data clone scoped to <paramref name="target"/>.
		/// </summary>
		/// <param name="target">The candidate target GameObject.</param>
		/// <param name="eventData">The parent event data.</param>
		/// <returns>A new event data scoped to the candidate.</returns>
		private EventData ForkForCandidate(GameObject target, EventData eventData)
		{
			if (eventData != null)
			{
				return eventData.Fork(target);
			}

			// No parent event data — synthesize a minimal scope.
			EventData fallback = new EventData(null);
			fallback.SetTarget(target);
			return fallback;
		}
	}
}