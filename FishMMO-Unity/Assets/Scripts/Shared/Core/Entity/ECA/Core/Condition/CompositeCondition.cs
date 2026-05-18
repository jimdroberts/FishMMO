using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Operator for composite conditions: AND (all must pass) or OR (at least one must pass).
	/// </summary>
	public enum ConditionOperator { AND, OR }

	/// <summary>
	/// Condition that evaluates a list of sub-conditions using AND or OR logic.
	/// Serialized inline via [SerializeReference].
	/// </summary>
	[Serializable]
	public class CompositeCondition : BaseCondition
	{
		/// <summary>
		/// The operator used to combine sub-conditions (AND or OR).
		/// </summary>
		public ConditionOperator Operator = ConditionOperator.AND;

		/// <summary>
		/// The list of sub-conditions to evaluate.
		/// </summary>
		[SerializeReference, SubclassSelector]
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Evaluates the composite condition using the specified operator.
		/// Returns true if all conditions pass (AND) or at least one passes (OR).
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Event data for the condition.</param>
		/// <returns>True if the composite condition passes; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (Conditions == null || Conditions.Count == 0)
			{
				return true;
			}

			if (Operator == ConditionOperator.AND)
			{
				foreach (var condition in Conditions)
				{
					if (condition == null || !condition.Check(initiator, eventData))
					{
						return false;
					}
				}
				return true;
			}
			else
			{
				foreach (var condition in Conditions)
				{
					if (condition != null && condition.Check(initiator, eventData))
					{
						return true;
					}
				}
				return false;
			}
		}
	}
}