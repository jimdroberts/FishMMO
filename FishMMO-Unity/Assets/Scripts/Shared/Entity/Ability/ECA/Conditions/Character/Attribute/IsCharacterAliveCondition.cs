using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	/// <summary>
	/// Condition that checks if a character is alive (health > 0), with optional inversion to check for death.
	/// </summary>
	[CreateAssetMenu(fileName = "IsCharacterAliveCondition", menuName = "FishMMO/Triggers/Conditions/Attribute/Is Alive", order = 0)]
	public class IsCharacterAliveCondition : BaseCondition
	{
		/// <summary>
		/// If true, the condition passes if the character is NOT alive (i.e., dead or health <= 0).
		/// </summary>
		[Tooltip("If true, the condition passes if the character is NOT alive (i.e., dead or health <= 0).")]
		public bool InvertResult = false;

		/// <summary>
		/// Evaluates whether the character (or event target) is alive (health > 0), with optional inversion.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is alive (or dead, if inverted); otherwise, false.</returns>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			// Try to get the attribute controller from the character.
			if (!characterToCheck.TryGet(out CharacterAttributeController attributeController))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have a CharacterAttributeController. Condition failed.");
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
			bool finalResult = InvertResult ? !isAlive : isAlive;

			if (!finalResult)
			{
				string status = isAlive ? "is alive" : "is dead (health <= 0)";
				string invertedText = InvertResult ? " (inverted check)" : "";
				Log.Debug("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' failed alive check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}

		/// <summary>
		/// Returns a formatted description of the alive condition for UI display.
		/// </summary>
		/// <returns>A string describing whether the character must be alive or dead.</returns>
		public override string GetFormattedDescription()
		{
			return InvertResult
				? "Requires the character to be dead."
				: "Requires the character to be alive.";
		}
	}
}