using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "IsImmortalCondition", menuName = "FishMMO/Triggers/Conditions/Attribute/Is Immortal", order = 0)]
	public class IsImmortalCondition : BaseCondition
	{
		[Header("Immortality Check")]
		[Tooltip("If true, the condition passes if the target character is NOT immortal.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			if (!characterToCheck.TryGet(out ICharacterDamageController damageController))
			{
				Log.Warning("IsImmortalCondition", $"EventData does not contain an ICharacterDamageController. Condition failed. (Character: {characterToCheck?.Name})");
				return false;
			}

			bool isImmortal = damageController.Immortal;
			bool finalResult = InvertResult ? !isImmortal : isImmortal;

			if (!finalResult)
			{
				string status = isImmortal ? "is immortal" : "is mortal";
				string invertedText = InvertResult ? " (inverted check)" : "";
				Log.Debug("IsImmortalCondition", $"(Character: '{characterToCheck?.Name}') failed immortality check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}

		public override string GetFormattedDescription()
		{
			return InvertResult
				? "Requires the character to be mortal."
				: "Requires the character to be immortal.";
		}
	}
}