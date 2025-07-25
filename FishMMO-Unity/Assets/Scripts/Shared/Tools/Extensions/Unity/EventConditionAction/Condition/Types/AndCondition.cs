using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;

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
				Log.Warning("AndCondition", "PlayerCharacter is null for AndCondition check.");
				return false;
			}

			foreach (var condition in Conditions)
			{
				if (condition == null)
				{
					Log.Warning("AndCondition", "Null condition found in AndCondition list. Skipping.");
					continue;
				}

				if (!condition.Evaluate(initiator, eventData))
				{
					return false; // If any condition is false, the AND condition is false
				}
			}
			return true; // All conditions met
		}

		public override string GetFormattedDescription()
		{
			return "All sub-conditions must be true.";
		}
	}
}