using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New And Condition", menuName = "FishMMO/Conditions/And Condition", order = 0)]
	public class AndCondition : BaseCondition<IPlayerCharacter>
	{
		// A list of other conditions that must ALL be met
		public List<BaseCondition<IPlayerCharacter>> Conditions = new List<BaseCondition<IPlayerCharacter>>();

		public override bool Evaluate(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				Debug.LogWarning("PlayerCharacter is null for AndCondition check.");
				return false;
			}

			foreach (var condition in Conditions)
			{
				if (condition == null)
				{
					Debug.LogWarning("Null condition found in AndCondition list. Skipping.");
					continue;
				}

				if (!condition.Evaluate(playerCharacter))
				{
					return false; // If any condition is false, the AND condition is false
				}
			}
			return true; // All conditions met
		}
	}
}