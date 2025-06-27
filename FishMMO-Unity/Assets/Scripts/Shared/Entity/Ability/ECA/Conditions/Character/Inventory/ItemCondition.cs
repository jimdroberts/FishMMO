using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Item Condition", menuName = "FishMMO/Conditions/Item Condition", order = 1)]
	public class ItemCondition : BaseCondition
	{
		public ItemTemplateDatabase RequiredItems;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning($"Player character does not exist.");
				return false;
			}
			if (!initiator.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning($"Player character does not have an IInventoryController.");
				return false;
			}

			foreach (var requiredItem in RequiredItems.Items)
			{
				if (!inventoryController.ContainsItem(requiredItem.Value))
				{
					// Log.Debug($"Player {playerCharacter.Name} failed item condition: Missing {requiredItem.Value.name}");
					return false;
				}
			}
			return true;
		}
	}
}