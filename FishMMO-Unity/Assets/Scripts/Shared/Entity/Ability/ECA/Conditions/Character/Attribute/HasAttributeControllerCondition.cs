using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Attribute Controller Condition", menuName = "FishMMO/Triggers/Conditions/Attribute/Has Attribute Controller", order = 1)]
	public class HasAttributeControllerCondition : BaseCondition
	{
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("HasAttributeControllerCondition", "Character does not exist.");
				return false;
			}
			if (!characterToCheck.TryGet(out ICharacterAttributeController characterAttributeController))
			{
				Log.Warning("HasAttributeControllerCondition", "Character does not have an ICharacterAttributeController.");
				return false;
			}
			return true;
		}
	}
}