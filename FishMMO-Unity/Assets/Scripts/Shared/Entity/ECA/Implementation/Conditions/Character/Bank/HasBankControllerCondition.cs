using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has a bank controller component.
	/// </summary>
	[Serializable]
	public class HasBankControllerCondition : BaseCondition
	{
		/// <summary>
		/// Evaluates whether the character (or event target) has a bank controller.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has a bank controller; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
			// Check if the character has a bank controller.
			if (characterToCheck.TryGet(out IBankController _))
			{
				return true;
			}
			Log.Warning("HasBankControllerCondition", $"Character {characterToCheck?.Name} does not have a bank controller in EventData.");
			return false;
		}
	}
}