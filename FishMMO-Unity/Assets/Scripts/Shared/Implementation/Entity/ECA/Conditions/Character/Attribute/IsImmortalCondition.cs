using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is immortal (cannot be killed), with optional inversion to check for mortality.
	/// </summary>
	[Serializable]
	public class IsImmortalCondition : BaseCondition
	{
		[Header("Immortality Check")]
		/// <summary>
		/// If true, the condition passes if the target character is NOT immortal (inverts the result).
		/// </summary>
		[Tooltip("If true, the condition passes if the target character is NOT immortal.")]
		public bool InvertResult = false;

		/// <summary>
		/// Evaluates whether the character (or event target) is immortal, with optional inversion.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is immortal (or mortal, if inverted); otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);

			// Try to get the damage controller from the character.
			if (!characterToCheck.TryGet(out ICharacterDamageController damageController))
			{
				Log.Warning("IsImmortalCondition", $"EventData does not contain an ICharacterDamageController. Condition failed. (Character: {characterToCheck?.Name})");
				return false;
			}

			// Check if the character is immortal.
			bool isImmortal = damageController.Immortal;
			// Optionally invert the result.
			bool finalResult = InvertResult ? !isImmortal : isImmortal;

			if (!finalResult)
			{
				string status = isImmortal ? "is immortal" : "is mortal";
				string invertedText = InvertResult ? " (inverted check)" : "";
				Log.Debug("IsImmortalCondition", $"(Character: '{characterToCheck?.Name}') failed immortality check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}

	}
}