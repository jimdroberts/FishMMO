using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for selecting targets in an ability or event context.
	/// Implementations define how to select one or more <see cref="GameObject"/>s based on a given context object.
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

		/// <summary>
		/// Gets or sets the list of conditions that must be met for a target to be valid.
		/// </summary>
		public List<BaseCondition> Conditions { get { return conditions; } set { conditions = value; } }

		/// <summary>
		/// Gets or sets the optional scene object selected directly by this selector.
		/// </summary>
		public GameObject TargetOverride { get { return targetOverride; } set { targetOverride = value; } }

		/// <summary>
		/// Gets or sets the optional scene object used as this selector's initiator.
		/// </summary>
		public GameObject InitiatorOverride { get { return initiatorOverride; } set { initiatorOverride = value; } }

		/// <summary>
		/// Resolves this selector's effective initiator.
		/// </summary>
		/// <param name="context">The selection context GameObject.</param>
		/// <param name="fallback">Fallback initiator when no override is assigned.</param>
		/// <returns>The resolved initiator, or null if none exists.</returns>
		public ICharacter ResolveInitiator(GameObject context, ICharacter fallback = null)
		{
			if (initiatorOverride != null && initiatorOverride.TryGetComponent(out ICharacter overrideInitiator))
			{
				return overrideInitiator;
			}

			if (fallback != null)
			{
				return fallback;
			}

			return context != null && context.TryGetComponent(out ICharacter contextInitiator) ? contextInitiator : null;
		}

		/// <summary>
		/// Tries to select the explicit target override.
		/// </summary>
		/// <param name="context">The selection context GameObject.</param>
		/// <param name="target">The selected override target.</param>
		/// <returns>True when an override target exists and passes selector conditions.</returns>
		protected bool TrySelectTargetOverride(GameObject context, out GameObject target)
		{
			if (targetOverride == null)
			{
				target = null;
				return false;
			}

			target = AreConditionsMet(targetOverride, ResolveInitiator(context)) ? targetOverride : null;
			return true;
		}

		/// <summary>
		/// Checks if all conditions are met for a given target.
		/// If no initiator is provided, attempts to extract <see cref="ICharacter"/> from the target <see cref="GameObject"/>.
		/// If no event data is provided, creates a <see cref="TargetEventData"/> wrapping the target.
		/// </summary>
		/// <param name="target">The target <see cref="GameObject"/> being evaluated.</param>
		/// <param name="initiator">The character initiating the selection, or null to extract from target.</param>
		/// <param name="eventData">Optional event data for condition evaluation.</param>
		/// <returns>True if all conditions pass; otherwise, false.</returns>
		protected bool AreConditionsMet(GameObject target, ICharacter initiator = null, EventData eventData = null)
		{
			if (Conditions == null || Conditions.Count == 0)
			{
				return true;
			}

			if (target == null)
			{
				return false;
			}

			ICharacter targetCharacter = target.GetComponent<ICharacter>();
			ICharacter effectiveInitiator = ResolveInitiator(target, initiator ?? targetCharacter);

			EventData effectiveEventData = CreateTargetEventData(eventData, effectiveInitiator, target, targetCharacter);

			for (int i = 0; i < Conditions.Count; i++)
			{
				BaseCondition condition = Conditions[i];
				if (condition != null && !condition.Evaluate(effectiveInitiator, effectiveEventData))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Creates event data for evaluating a candidate target against selector conditions.
		/// </summary>
		/// <param name="eventData">Optional source event data.</param>
		/// <param name="initiator">The effective initiator.</param>
		/// <param name="target">The target GameObject being evaluated.</param>
		/// <param name="targetCharacter">The target character, when the target implements <see cref="ICharacter"/>.</param>
		/// <returns>Event data containing the selected target context.</returns>
		private static EventData CreateTargetEventData(EventData eventData, ICharacter initiator, GameObject target, ICharacter targetCharacter)
		{
			TargetEventData targetEventData = new TargetEventData(initiator, target);
			if (targetCharacter == null)
			{
				return eventData == null ? new EventData(initiator, targetEventData) : new EventData(initiator, eventData, targetEventData);
			}

			CharacterHitEventData characterHitEventData = new CharacterHitEventData(initiator, targetCharacter);
			return eventData == null ?
				new EventData(initiator, targetEventData, characterHitEventData) :
				new EventData(initiator, eventData, targetEventData, characterHitEventData);
		}

		/// <summary>
		/// Selects targets based on the provided context <see cref="GameObject"/>.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> in which to select targets (e.g., self, area center, parent, etc.).</param>
		/// <returns>An enumerable collection of selected <see cref="GameObject"/>s.</returns>
		public abstract IEnumerable<GameObject> SelectTargets(GameObject context);
	}
}