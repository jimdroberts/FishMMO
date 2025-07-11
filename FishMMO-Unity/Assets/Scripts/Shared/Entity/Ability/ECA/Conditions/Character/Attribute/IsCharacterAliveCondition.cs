using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "IsCharacterAliveCondition", menuName = "FishMMO/Conditions/Character/Is Alive", order = 0)]
	public class IsCharacterAliveCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT alive (i.e., dead or health <= 0).")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// First, try to get the CharacterAttributeController from the initiator.
			// This is crucial because it holds the health attribute.
			if (!initiator.TryGet(out CharacterAttributeController attributeController))
			{
				Log.Warning("IsCharacterAliveCondition", $"Initiator '{initiator?.Name}' does not have a CharacterAttributeController. Condition failed.");
				return false;
			}

			// Next, try to get the Health Resource Attribute from the controller.
			if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute healthAttribute))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{initiator?.Name}' does not have a Health Resource Attribute. Condition failed.");
				return false;
			}

			// A character is considered alive if their current health is greater than 0.
			bool isAlive = healthAttribute.CurrentValue > 0;

			// Apply inversion logic
			bool finalResult = InvertResult ? !isAlive : isAlive;

			if (!finalResult)
			{
				string status = isAlive ? "is alive" : "is dead (health <= 0)";
				string invertedText = InvertResult ? " (inverted check)" : "";

				Log.Debug("IsCharacterAliveCondition", $"Character '{initiator?.Name}' failed alive check. Status: {status}{invertedText}.");
			}

			return finalResult;
		}
	}
}