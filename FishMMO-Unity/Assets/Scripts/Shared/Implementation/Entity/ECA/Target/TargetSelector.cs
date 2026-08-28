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
		/// Runs an overlap query against where characters were when the caster's client saw them.
		/// </summary>
		/// <remarks>
		/// Delegates to <see cref="LagCompensatedQuery"/>, which is shared with the ECA actions that
		/// resolve hits without going through a selector. The query is eager and the rewind closes
		/// before any result is yielded — selectors are iterators, and holding characters displaced
		/// across <c>yield return</c> would run the consumer's damage and ECA work against a stale
		/// world.
		/// </remarks>
		protected static int RewoundOverlapSphere(
			EventData eventData, GameObject context, Vector3 center, float radius,
			Collider[] hits, LayerMask mask)
			=> LagCompensatedQuery.OverlapSphere(eventData, context, center, radius, hits, mask);

		/// <summary>
		/// Runs a raycast against where characters were when the caster's client saw them.
		/// </summary>
		protected static int RewoundRaycast(
			EventData eventData, GameObject context, Vector3 origin, Vector3 direction, float distance,
			RaycastHit[] hits, LayerMask mask)
			=> LagCompensatedQuery.Raycast(eventData, context, origin, direction, distance, hits, mask);

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