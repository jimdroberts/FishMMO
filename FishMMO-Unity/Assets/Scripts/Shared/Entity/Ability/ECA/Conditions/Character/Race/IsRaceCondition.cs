using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is of a specific race.
	/// </summary>
	[CreateAssetMenu(fileName = "New Race Condition", menuName = "FishMMO/Triggers/Conditions/Race/Is Race Condition", order = 1)]
	public class IsRaceCondition : BaseCondition
	{
		/// <summary>
		/// The required race template for the condition to pass.
		/// </summary>
		public RaceTemplate RequiredRace;

		/// <summary>
		/// Evaluates whether the character (or event target) is of the required race.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is of the required race; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("IsRaceCondition", "Character does not exist.");
				return false;
			}
			// Ensure a required race is assigned.
			if (RequiredRace == null)
			{
				Log.Warning("IsRaceCondition", "RequiredRace is not assigned.");
				return false;
			}
			// Only player characters have a race ID.
			if (characterToCheck is IPlayerCharacter playerCharacter)
			{
				return playerCharacter.RaceID == RequiredRace.ID;
			}
			Log.Warning("IsRaceCondition", "Character is not a player character.");
			return false;
		}

		/// <summary>
		/// Returns a formatted description of the race requirement for UI display.
		/// </summary>
		/// <returns>A string describing the required race.</returns>
		public override string GetFormattedDescription()
		{
			string raceName = RequiredRace != null ? RequiredRace.Name : "[Unassigned Race]";
			return $"Requires the character to be of race: {raceName}.";
		}
	}
}