using UnityEngine;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that evaluates to true only if all sub-conditions are true.
	/// </summary>
	[CreateAssetMenu(fileName = "New And Condition", menuName = "FishMMO/Conditions/And Condition", order = 0)]
	public class AndCondition : BaseCondition
	{
		/// <summary>
		/// A list of other conditions that must ALL be met for this condition to be true.
		/// </summary>
		public List<BaseCondition> Conditions = new List<BaseCondition>();

		/// <summary>
		/// Evaluates the AND condition. Returns true only if all sub-conditions are true.
		/// Logs warnings for null initiator or null conditions.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Event data for the condition.</param>
		/// <returns>True if all conditions are met; otherwise, false.</returns>
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

				// If any condition is false, the AND condition is false
				if (!condition.Evaluate(initiator, eventData))
				{
					return false;
				}
			}
			// All conditions met
			return true;
		}

		/// <summary>
		/// Returns a description for the AND condition.
		/// </summary>
		/// <returns>Description string.</returns>
		public override string GetFormattedDescription()
		{
			return "All sub-conditions must be true.";
		}
	}
}