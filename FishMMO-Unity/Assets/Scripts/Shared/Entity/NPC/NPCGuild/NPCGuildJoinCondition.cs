using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "NPC Guild Join Condition", menuName = "FishMMO/Character/NPC/NPC Guild Join Condition", order = 1)]
	public class NPCGuildJoinCondition : CachedScriptableObject<NPCGuildTemplate>, ICachedObject
	{
		public AbilityResourceDictionary RequiredAttributes = new AbilityResourceDictionary();
		public ItemTemplateDatabase RequiredItems;

		public bool MeetsRequirements(IPlayerCharacter playerCharacter)
		{
			if (!playerCharacter.TryGet(out ICharacterAttributeController characterAttributeController) ||
				!playerCharacter.TryGet(out IInventoryController inventoryController))
			{
				return false;
			}

			// Check if we meet the attribute requirements to join this guild. Use the base value for condition check instead of the final.
			foreach (var requiredAttribute in RequiredAttributes)
			{
				if (!characterAttributeController.TryGetAttribute(requiredAttribute.Key, out CharacterAttribute attribute) ||
					attribute.Value < requiredAttribute.Value)
				{
					return false;
				}
			}

			foreach (var requiredItem in RequiredItems.Items)
			{
				if (!inventoryController.ContainsItem(requiredItem.Value))
				{
					return false;
				}
			}
			return true;
		}
	}
}