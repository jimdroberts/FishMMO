using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	/// <summary>
	/// Condition that checks if a character is an NPC, with optional inversion.
	/// </summary>
	[CreateAssetMenu(fileName = "IsCharacterNPCCondition", menuName = "FishMMO/Triggers/Conditions/Character/Is NPC", order = 0)]
	public class IsCharacterNPCCondition : BaseCondition
	{
		/// <summary>
		/// If true, the condition passes if the character is NOT an NPC.
		/// </summary>
		[Tooltip("If true, the condition passes if the character is NOT an NPC.")]
		public bool invertResult = false;

		/// <summary>
		/// Evaluates whether the character (or event target) is an NPC, or not an NPC if <see cref="invertResult"/> is true.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is (or is not) an NPC, depending on <see cref="invertResult"/>; otherwise, false.</returns>
		/// <remarks>
		/// This method checks if the character is an NPC and applies inversion logic if specified.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			// Check if the character is an NPC.
			bool isNPC = characterToCheck is NPC;

			// Apply inversion logic if needed.
			bool finalResult = invertResult ? !isNPC : isNPC;

			if (!finalResult)
			{
				string status = isNPC ? "is an NPC" : "is not an NPC";
				string invertedText = invertResult ? " (inverted check)" : "";

				Log.Debug("IsCharacterNPCCondition", $"Character '{characterToCheck?.Name}' failed NPC check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}

		/// <summary>
		/// Returns a formatted description of the NPC requirement for UI display.
		/// </summary>
		/// <returns>A string describing the NPC requirement.</returns>
		public override string GetFormattedDescription()
		{
			return invertResult
				? "Requires the character to NOT be an NPC."
				: "Requires the character to be an NPC.";
		}
	}
}