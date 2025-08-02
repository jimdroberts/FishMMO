using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	/// <summary>
	/// Condition that checks if a character is in a party, with optional inversion.
	/// </summary>
	[CreateAssetMenu(fileName = "HasPartyCondition", menuName = "FishMMO/Triggers/Conditions/Party/Has Party", order = 0)]
	public class HasPartyCondition : BaseCondition
	{
		/// <summary>
		/// If true, the condition passes if the character is NOT in a party.
		/// </summary>
		[Tooltip("If true, the condition passes if the character is NOT in a party.")]
		public bool InvertResult = false;

		/// <summary>
		/// Evaluates whether the character (or event target) is in a party, or not in a party if <see cref="InvertResult"/> is true.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is (or is not) in a party, depending on <see cref="InvertResult"/>; otherwise, false.</returns>
		/// <remarks>
		/// This method checks for a party controller and evaluates the party membership. If <see cref="InvertResult"/> is true, the logic is inverted.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			// Check if the character has a party controller.
			if (!characterToCheck.TryGet(out IPartyController partyController))
			{
				Log.Warning("HasPartyCondition", $"Character '{characterToCheck?.Name}' does not have a Party Controller. Condition failed.");
				return false;
			}
			// A character is considered in a party if their party ID is not zero.
			bool isInParty = partyController.ID != 0;
			if (InvertResult)
			{
				if (isInParty)
				{
					Log.Debug("HasPartyCondition", $"Character '{characterToCheck?.Name}' is in a party, but 'invertResult' is true. Condition failed.");
				}
				return !isInParty;
			}
			else
			{
				if (!isInParty)
				{
					Log.Debug("HasPartyCondition", $"Character '{characterToCheck?.Name}' is not in a party. Condition failed.");
				}
				return isInParty;
			}
		}

		/// <summary>
		/// Returns a formatted description of the party requirement for UI display.
		/// </summary>
		/// <returns>A string describing the party membership requirement.</returns>
		public override string GetFormattedDescription()
		{
			return InvertResult
				? "Requires the character to NOT be in a party."
				: "Requires the character to be in a party.";
		}
	}
}