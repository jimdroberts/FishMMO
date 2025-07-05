using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "IsImmortalCondition", menuName = "FishMMO/Conditions/Character/Is Immortal", order = 0)]
	public class IsImmortalCondition : BaseCondition
	{
		[Header("Immortality Check")]
		[Tooltip("If true, the condition passes if the target character is NOT immortal.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Attempt to get the ICharacterDamageController from the eventData.
			if (!initiator.TryGet(out ICharacterDamageController damageController))
			{
				Log.Warning("IsImmortalCondition", $"EventData does not contain an ICharacterDamageController. Condition failed. (Initiator: {initiator?.Name})");
				return false;
			}

			// Check the Immortal property of the defender's damage controller.
			bool isImmortal = damageController.Immortal;

			// Apply inversion logic
			bool finalResult = InvertResult ? !isImmortal : isImmortal;

			// Optional: Detailed logging for debugging
			if (!finalResult) // If the condition ultimately failed
			{
				string status = isImmortal ? "is immortal" : "is mortal";
				string invertedText = InvertResult ? " (inverted check)" : "";

				Log.Debug("IsImmortalCondition", $"(Initiator: '{initiator?.Name}') failed immortality check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}
	}
}