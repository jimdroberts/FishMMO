using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is alive (health > 0), with optional inversion to check for death.
	/// </summary>
	[Serializable]
	public class IsCharacterAliveCondition : BaseCondition
	{
		/// <summary>
		/// If true, the condition passes if the character is NOT alive (i.e., dead or health <= 0).
		/// </summary>
		[Tooltip("If true, the condition passes if the character is NOT alive (i.e., dead or health <= 0).")]
		public bool Invert = false;

		/// <summary>
		/// Evaluates whether the character (or event target) is alive (health > 0), with optional inversion.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is alive (or dead, if inverted); otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);

			// Try to get the attribute controller from the character.
			if (!characterToCheck.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have an ICharacterAttributeController. Condition failed.");
				return false;
			}

			// Try to get the health attribute from the controller.
			if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute healthAttribute))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have a Health Resource Attribute. Condition failed.");
				return false;
			}

			// Check if the character is alive (health > 0).
			bool isAlive = healthAttribute.CurrentValue > 0;
			// Optionally invert the result.
			bool finalResult = Invert ? !isAlive : isAlive;

			if (!finalResult)
			{
				string status = isAlive ? "is alive" : "is dead (health <= 0)";
				string invertedText = Invert ? " (inverted check)" : "";
				Log.Debug("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' failed alive check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}

	}
}