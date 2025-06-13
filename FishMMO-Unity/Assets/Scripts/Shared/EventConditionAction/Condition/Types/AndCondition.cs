using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New And Condition", menuName = "FishMMO/Conditions/And Condition", order = 0)]
	public class AndCondition : BaseCondition
	{
		// A list of other conditions that must ALL be met
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
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

				if (!condition.Evaluate(initiator, eventData))
				{
					return false; // If any condition is false, the AND condition is false
				}
			}
			return true; // All conditions met
		}
	}
}