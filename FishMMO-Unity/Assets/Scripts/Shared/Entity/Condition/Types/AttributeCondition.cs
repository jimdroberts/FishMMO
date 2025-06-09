using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Attribute Condition", menuName = "FishMMO/Conditions/Attribute Condition", order = 1)]
	public class AttributeCondition : BaseCondition<IPlayerCharacter>
	{
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();

		public override bool Evaluate(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				Debug.LogWarning($"Player character does not exist.");
				return false;
			}
			if (!playerCharacter.TryGet(out ICharacterAttributeController characterAttributeController))
			{
				Debug.LogWarning($"Player character {playerCharacter.CharacterName} does not have an ICharacterAttributeController.");
				return false;
			}

			foreach (var requiredAttribute in RequiredAttributes)
			{
				if (!characterAttributeController.TryGetAttribute(requiredAttribute.Key, out CharacterAttribute attribute) ||
					attribute.Value < requiredAttribute.Value)
				{
					// Optionally, you could log which attribute failed
					// Debug.Log($"Player {playerCharacter.Name} failed attribute condition: {requiredAttribute.Key} (Need: {requiredAttribute.Value}, Has: {attribute?.Value ?? 0})");
					return false;
				}
			}
			return true;
		}
	}
}