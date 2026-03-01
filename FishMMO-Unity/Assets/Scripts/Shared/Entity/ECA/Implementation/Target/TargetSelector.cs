using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for selecting targets in an ability or event context.
	/// Implementations define how to select one or more <see cref="GameObject"/>s based on a given context object.
	/// </summary>
	public abstract class TargetSelector : CachedScriptableObject<TargetSelector>, ICachedObject, IConditionalTargetSelector
	{
		/// <summary>
		/// List of conditions that must be met for a target to be valid.
		/// </summary>
		[Tooltip("Conditions that must be met for a target to be valid.")]
		[SerializeReference, SubclassSelector]
		private List<BaseCondition> conditions = new List<BaseCondition>();

		/// <summary>
		/// Gets or sets the list of conditions that must be met for a target to be valid.
		/// </summary>
		public List<BaseCondition> Conditions { get { return conditions; } set { conditions = value; } }

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
			if (Conditions == null || Conditions.Count == 0) return true;
			if (target == null) return false;

			ICharacter effectiveInitiator = initiator ?? target.GetComponent<ICharacter>();

			EventData effectiveEventData = eventData;
			if (effectiveEventData == null && effectiveInitiator != null)
			{
				effectiveEventData = new TargetEventData(effectiveInitiator, target);
			}

			foreach (var condition in Conditions)
			{
				if (condition != null && !condition.Evaluate(effectiveInitiator, effectiveEventData))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Selects targets based on the provided context <see cref="GameObject"/>.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> in which to select targets (e.g., self, area center, parent, etc.).</param>
		/// <returns>An enumerable collection of selected <see cref="GameObject"/>s.</returns>
		public abstract IEnumerable<GameObject> SelectTargets(GameObject context);
	}
}