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
	/// </summary>
	[Serializable]
	public abstract class TargetSelector : IConditionalTargetSelector
	{
		/// <summary>
		/// List of conditions that must be met for a target to be valid.
		/// </summary>
		[Tooltip("Conditions that must be met for a target to be valid.")]
		[SerializeReference, SubclassSelector]
		private List<BaseCondition> conditions = new List<BaseCondition>();

		/// <summary>
		/// Optional scene object selected directly instead of running this selector's normal target query.
		/// </summary>
		[Tooltip("Optional scene object selected directly instead of running this selector's normal target query.")]
		[SerializeField]
		private GameObject targetOverride;

		/// <summary>
		/// Optional scene object used as the initiator when it implements ICharacter.
		/// </summary>
		[Tooltip("Optional scene object used as the initiator when it implements ICharacter.")]
		[SerializeField]
		private GameObject initiatorOverride;

		/// <inheritdoc/>
		public List<BaseCondition> Conditions { get { return conditions; } set { conditions = value; } }

		/// <summary>
		/// Optional scene object selected directly by this selector.
		/// </summary>
		public GameObject TargetOverride { get { return targetOverride; } set { targetOverride = value; } }

		/// <summary>
		/// Optional scene object used as this selector's initiator.
		/// </summary>
		public GameObject InitiatorOverride { get { return initiatorOverride; } set { initiatorOverride = value; } }

		/// <summary>
		/// Selects targets based on the supplied event context.
		/// </summary>
		/// <param name="eventData">The event data driving the selection. <see cref="EventData.Target"/>
		/// (when set) or <see cref="EventData.Initiator"/> typically serves as the spatial origin.</param>
		/// <returns>The selected GameObjects.</returns>
		public abstract IEnumerable<GameObject> SelectTargets(EventData eventData);

		/// <summary>
		/// Returns the spatial origin for this selector — preferring <see cref="EventData.Target"/>,
		/// falling back to <see cref="EventData.Initiator"/>'s GameObject.
		/// </summary>
		/// <param name="eventData">The current event data.</param>
		/// <returns>A GameObject to use as a spatial reference, or null.</returns>
		protected static GameObject GetContext(EventData eventData)
		{
			if (eventData == null) return null;
			if (eventData.Target != null) return eventData.Target;
			return eventData.Initiator?.GameObject;
		}

		/// <summary>
		/// Tries to yield this selector's explicit <see cref="TargetOverride"/> if assigned.
		/// </summary>
		/// <param name="eventData">The current event data.</param>
		/// <param name="target">The override target when present and conditions pass.</param>
		/// <returns>True when an override was configured (regardless of whether conditions passed).</returns>
		protected bool TrySelectTargetOverride(EventData eventData, out GameObject target)
		{
			if (targetOverride == null)
			{
				target = null;
				return false;
			}

			target = AreConditionsMet(targetOverride, eventData) ? targetOverride : null;
			return true;
		}

		/// <summary>
		/// Evaluates this selector's per-target <see cref="Conditions"/> against the candidate target.
		/// Builds a forked <see cref="EventData"/> scoped to the candidate so conditions see the right
		/// <see cref="EventData.Target"/> / <see cref="EventData.TargetCharacter"/>.
		/// </summary>
		/// <param name="target">The candidate target GameObject.</param>
		/// <param name="eventData">The parent event data, or null.</param>
		/// <returns>True when no conditions exist, or all conditions pass.</returns>
		protected bool AreConditionsMet(GameObject target, EventData eventData)
		{
			if (conditions == null || conditions.Count == 0)
			{
				return true;
			}

			if (target == null)
			{
				return false;
			}

			EventData scoped = ForkForCandidate(target, eventData);

			for (int i = 0; i < conditions.Count; ++i)
			{
				BaseCondition condition = conditions[i];
				if (condition != null && !condition.Evaluate(scoped.Initiator, scoped))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Builds a per-candidate event data clone, honoring <see cref="InitiatorOverride"/> when assigned.
		/// </summary>
		/// <param name="target">The candidate target GameObject.</param>
		/// <param name="eventData">The parent event data.</param>
		/// <returns>A new event data scoped to the candidate.</returns>
		private EventData ForkForCandidate(GameObject target, EventData eventData)
		{
			ICharacter overrideInitiator = null;
			if (initiatorOverride != null)
			{
				initiatorOverride.TryGetComponent(out overrideInitiator);
			}

			if (overrideInitiator != null)
			{
				EventData scoped = new EventData(overrideInitiator);
				scoped.SetTarget(target);
				if (eventData != null)
				{
					scoped.RNG = eventData.RNG;
					scoped.Merge(eventData);
				}
				return scoped;
			}

			if (eventData != null)
			{
				return eventData.Fork(target);
			}

			// No parent event data and no override — synthesize a minimal scope.
			EventData fallback = new EventData(null);
			fallback.SetTarget(target);
			return fallback;
		}
	}
}