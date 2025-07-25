using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "IsCharacterAliveCondition", menuName = "FishMMO/Triggers/Conditions/Attribute/Is Alive", order = 0)]
	public class IsCharacterAliveCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT alive (i.e., dead or health <= 0).")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}

			if (!characterToCheck.TryGet(out CharacterAttributeController attributeController))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have a CharacterAttributeController. Condition failed.");
				return false;
			}

			if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute healthAttribute))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have a Health Resource Attribute. Condition failed.");
				return false;
			}

			bool isAlive = healthAttribute.CurrentValue > 0;
			bool finalResult = InvertResult ? !isAlive : isAlive;

			if (!finalResult)
			{
				string status = isAlive ? "is alive" : "is dead (health <= 0)";
				string invertedText = InvertResult ? " (inverted check)" : "";

				Log.Debug("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' failed alive check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}
	}
}