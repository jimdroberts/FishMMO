using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New HasInventoryItemCondition", menuName = "FishMMO/Triggers/Conditions/Inventory/Has Inventory Item", order = 1)]
	public class HasInventoryItemCondition : BaseCondition
	{
		[Tooltip("All items listed must be present in the required amount.")]
		public BaseItemTemplate[] RequiredItems;
		public int RequiredAmount = 1;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (characterToCheck == null)
			{
				Log.Warning("HasInventoryItemCondition", "Character does not exist.");
				return false;
			}
			if (RequiredItems == null || RequiredItems.Length == 0)
			{
				Log.Warning("HasInventoryItemCondition", "No RequiredItems assigned.");
				return false;
			}
			if (!characterToCheck.TryGet(out IInventoryController inventoryController))
			{
				Log.Warning("HasInventoryItemCondition", "Character does not have an IInventoryController.");
				return false;
			}
			foreach (var item in RequiredItems)
			{
				if (item == null)
				{
					Log.Warning("HasInventoryItemCondition", "A RequiredItem entry is null.");
					return false;
				}
				if (inventoryController.GetItemCount(item) < RequiredAmount)
				{
					return false;
				}
			}
			return true;
		}

		public override string GetFormattedDescription()
		{
			if (RequiredItems == null || RequiredItems.Length == 0)
				return $"Requires at least {RequiredAmount} of each required item (none specified).";
			var itemNames = string.Join(", ", System.Array.ConvertAll(RequiredItems, i => i != null ? i.Name : "[Unassigned Item]"));
			return $"Requires at least {RequiredAmount} of each: {itemNames}.";
		}
	}
}