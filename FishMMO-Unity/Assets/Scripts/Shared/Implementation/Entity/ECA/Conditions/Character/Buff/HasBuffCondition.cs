using System;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character currently has a specific buff applied.
	/// </summary>
	[Serializable]
	public class HasBuffCondition : BaseCondition
	{
		/// <summary>
		/// The buff template to check for on the character.
		/// </summary>
		public BaseBuffTemplate BuffTemplate;

		/// <summary>
		/// Evaluates whether the character (or event target) currently has the specified buff.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character has the buff; otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
			// Check if the character and buff controller exist.
			if (characterToCheck == null || !characterToCheck.TryGet(out IBuffController buffController))
				return false;
			// If no buff template is assigned, the condition cannot be met.
			if (BuffTemplate == null)
				return false;
			// Check if the buff controller contains the buff by template ID.
			return buffController.Buffs != null && buffController.Buffs.ContainsKey(BuffTemplate.ID);
		}

	}
}