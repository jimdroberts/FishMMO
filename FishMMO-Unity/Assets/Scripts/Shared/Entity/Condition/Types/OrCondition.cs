using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Or Condition", menuName = "FishMMO/Conditions/Or Condition", order = 0)]
	public class OrCondition : BaseCondition<IPlayerCharacter>
	{
		// A list of other conditions, where at least ONE must be met
		public List<BaseCondition<IPlayerCharacter>> Conditions = new List<BaseCondition<IPlayerCharacter>>();

		public override bool Evaluate(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				Debug.LogWarning("PlayerCharacter is null for OrCondition check.");
				return false;
			}

			foreach (var condition in Conditions)
			{
				if (condition == null)
				{
					Debug.LogWarning("Null condition found in OrCondition list. Skipping.");
					continue;
				}

				if (condition.Evaluate(playerCharacter))
				{
					return true; // If any condition is true, the OR condition is true
				}
			}
			return false; // No conditions met
		}
	}
}