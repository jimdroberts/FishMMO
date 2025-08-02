using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that evaluates to true if at least one sub-condition is true.
	/// </summary>
	[CreateAssetMenu(fileName = "New Or Condition", menuName = "FishMMO/Conditions/Or Condition", order = 0)]
	public class OrCondition : BaseCondition
	{
		/// <summary>
		/// A list of other conditions, where at least ONE must be met for this condition to be true.
		/// </summary>
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Evaluates the OR condition. Returns true if at least one sub-condition is true.
		/// Logs warnings for null initiator or null conditions.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Event data for the condition.</param>
		/// <returns>True if at least one condition is met; otherwise, false.</returns>
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

				// If any condition is true, the OR condition is true
				if (condition.Evaluate(initiator, eventData))
				{
					return true;
				}
			}
			// No conditions met
			return false;
		}

		/// <summary>
		/// Returns a description for the OR condition.
		/// </summary>
		/// <returns>Description string.</returns>
		public override string GetFormattedDescription()
		{
			return "At least one sub-condition must be true.";
		}
	}
}