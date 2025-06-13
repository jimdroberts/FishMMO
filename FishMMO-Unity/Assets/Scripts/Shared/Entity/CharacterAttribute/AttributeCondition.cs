using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Attribute Condition", menuName = "FishMMO/Conditions/Attribute Condition", order = 1)]
	public class AttributeCondition : BaseCondition
	{
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Debug.LogWarning($"Character does not exist.");
				return false;
			}
			if (!initiator.TryGet(out ICharacterAttributeController characterAttributeController))
			{
				Debug.LogWarning($"Character does not have an ICharacterAttributeController.");
				return false;
			}

			foreach (var requiredAttribute in RequiredAttributes)
			{
				if (!characterAttributeController.TryGetAttribute(requiredAttribute.Key, out CharacterAttribute attribute) ||
					attribute.Value < requiredAttribute.Value)
				{
					// Debug.Log($"Player {playerCharacter.Name} failed attribute condition: {requiredAttribute.Key} (Need: {requiredAttribute.Value}, Has: {attribute?.Value ?? 0})");
					return false;
				}
			}
			return true;
		}
	}
}