using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Or Condition", menuName = "FishMMO/Conditions/Or Condition", order = 0)]
	public class OrCondition : BaseCondition
	{
		// A list of other conditions, where at least ONE must be met
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning("OrCondition", "PlayerCharacter is null for OrCondition check.");
				return false;
			}

			foreach (var condition in Conditions)
			{
				if (condition == null)
				{
					Log.Warning("OrCondition", "Null condition found in OrCondition list. Skipping.");
					continue;
				}

				if (condition.Evaluate(initiator, eventData))
				{
					return true; // If any condition is true, the OR condition is true
				}
			}
			return false; // No conditions met
		}
	}
}