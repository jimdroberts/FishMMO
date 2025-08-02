using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Operator for composite conditions: AND (all must pass) or OR (at least one must pass).
	/// </summary>
	public enum ConditionOperator { AND, OR }

	/// <summary>
	/// Condition that evaluates a list of sub-conditions using AND or OR logic.
	/// </summary>
	[CreateAssetMenu(fileName = "New Composite Condition", menuName = "FishMMO/Conditions/Composite Condition", order = 1)]
	public class CompositeCondition : BaseCondition
	{
		/// <summary>
		/// The operator used to combine sub-conditions (AND or OR).
		/// </summary>
		public ConditionOperator Operator = ConditionOperator.AND;

		/// <summary>
		/// The list of sub-conditions to evaluate.
		/// </summary>
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
				// No conditions defined, implicitly true.
				return true;
			}

			if (Operator == ConditionOperator.AND)
			{
				// AND: All conditions must pass
				foreach (var condition in Conditions)
				{
					if (condition == null || !condition.Evaluate(initiator, eventData))
					{
						return false; // If any condition fails, AND fails
					}
				}
				return true; // All conditions passed
			}
			else // Operator == ConditionOperator.OR
			{
				// OR: At least one condition must pass
				foreach (var condition in Conditions)
				{
					if (condition != null && condition.Evaluate(initiator, eventData))
					{
						return true; // If any condition passes, OR passes
					}
				}
				return false; // All conditions failed
			}
		}

		/// <summary>
		/// Returns a description for the composite condition, indicating the current operator.
		/// </summary>
		/// <returns>Description string.</returns>
		public override string GetFormattedDescription()
		{
			string op = Operator == ConditionOperator.AND ? "AND" : "OR";
			return $"Composite condition: all sub-conditions must be true or at least one. Current: {op}.";
		}
	}
}