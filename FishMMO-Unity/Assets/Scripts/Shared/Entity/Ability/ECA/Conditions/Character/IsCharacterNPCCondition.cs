using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "IsCharacterNPCCondition", menuName = "FishMMO/Triggers/Conditions/Character/Is NPC", order = 0)]
	public class IsCharacterNPCCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT an NPC.")]
		public bool invertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			bool isNPC = characterToCheck is NPC;

			// Apply inversion logic
			bool finalResult = invertResult ? !isNPC : isNPC;

			if (!finalResult)
			{
				string status = isNPC ? "is an NPC" : "is not an NPC";
				string invertedText = invertResult ? " (inverted check)" : "";

				Log.Debug("IsCharacterNPCCondition", $"Character '{characterToCheck?.Name}' failed NPC check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}

		public override string GetFormattedDescription()
		{
			return invertResult
				? "Requires the character to NOT be an NPC."
				: "Requires the character to be an NPC.";
		}
	}
}