using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "IsCharacterNPCCondition", menuName = "FishMMO/Conditions/Character/Is NPC", order = 0)]
	public class IsCharacterNPCCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT an NPC.")]
		public bool invertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			bool isNPC = initiator as NPC;

			// Apply inversion logic
			bool finalResult = invertResult ? !isNPC : isNPC;

			if (!finalResult)
			{
				string status = isNPC ? "is an NPC" : "is not an NPC";
				string invertedText = invertResult ? " (inverted check)" : "";

				Log.Debug($"IsCharacterNPCCondition: Character '{initiator?.Name}' failed NPC check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}
	}
}