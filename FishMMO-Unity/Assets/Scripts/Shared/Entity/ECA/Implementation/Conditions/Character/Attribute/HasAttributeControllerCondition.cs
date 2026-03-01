using System;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character has an attribute controller component.
	/// </summary>
	[Serializable]
	public class HasAttributeControllerCondition : BaseCondition
	{
		/// <summary>
		/// Evaluates whether the character (or event target) has an attribute controller.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has an attribute controller; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
			if (characterToCheck == null)
			{
				Log.Warning("HasAttributeControllerCondition", "Character does not exist.");
				return false;
			}
			// Try to get the attribute controller from the character.
			if (!characterToCheck.TryGet(out ICharacterAttributeController characterAttributeController))
			{
				Log.Warning("HasAttributeControllerCondition", "Character does not have an ICharacterAttributeController.");
				return false;
			}
			return true;
		}
	}
}