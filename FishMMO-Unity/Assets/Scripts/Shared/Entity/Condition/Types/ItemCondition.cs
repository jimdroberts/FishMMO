using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Item Condition", menuName = "FishMMO/Conditions/Item Condition", order = 1)]
	public class ItemCondition : BaseCondition<IPlayerCharacter>
	{
		public ItemTemplateDatabase RequiredItems;

		public override bool Evaluate(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				Debug.LogWarning($"Player character does not exist.");
				return false;
			}
			if (!playerCharacter.TryGet(out IInventoryController inventoryController))
			{
				Debug.LogWarning($"Player character {playerCharacter.CharacterName} does not have an IInventoryController.");
				return false;
			}

			foreach (var requiredItem in RequiredItems.Items)
			{
				if (!inventoryController.ContainsItem(requiredItem.Value))
				{
					// Debug.Log($"Player {playerCharacter.Name} failed item condition: Missing {requiredItem.Value.name}");
					return false;
				}
			}
			return true;
		}
	}
}