using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public enum ConditionOperator { AND, OR }

	[CreateAssetMenu(fileName = "New Composite Condition", menuName = "FishMMO/Conditions/Composite Condition", order = 1)]
	public class CompositeCondition : BaseCondition<IPlayerCharacter>
	{
		public ConditionOperator Operator = ConditionOperator.AND;
		public List<BaseCondition<IPlayerCharacter>> Conditions = new List<BaseCondition<IPlayerCharacter>>();

		public override bool Evaluate(IPlayerCharacter playerCharacter)
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
					if (condition == null || !condition.Evaluate(playerCharacter))
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
					if (condition != null && condition.Evaluate(playerCharacter))
					{
						return true; // If any condition passes, OR passes
					}
				}
				return false; // All conditions failed
			}
		}
	}
}