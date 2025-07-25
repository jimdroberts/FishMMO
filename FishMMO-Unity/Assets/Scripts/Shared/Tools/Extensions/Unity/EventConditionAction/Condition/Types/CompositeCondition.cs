using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public enum ConditionOperator { AND, OR }

	[CreateAssetMenu(fileName = "New Composite Condition", menuName = "FishMMO/Conditions/Composite Condition", order = 1)]
	public class CompositeCondition : BaseCondition
	{
		public ConditionOperator Operator = ConditionOperator.AND;
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (Conditions == null || Conditions.Count == 0)
			{
				// No conditions defined, implicitly true.
				return true;
			}

			if (Operator == ConditionOperator.AND)
			{
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

		public override string GetFormattedDescription()
		{
			string op = Operator == ConditionOperator.AND ? "AND" : "OR";
			return $"Composite condition: all sub-conditions must be true or at least one. Current: {op}.";
		}
	}
}