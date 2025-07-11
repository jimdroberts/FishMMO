using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if the initiator has a specific item equipped in a given slot.
	/// Requires an ItemEventData in the EventData, or checks by TemplateID and Slot.
	/// </summary>
	[CreateAssetMenu(fileName = "HasEquippedItemCondition", menuName = "Runtimes/Conditions/Has Equipped Item Condition")]
	public class HasEquippedItemCondition : BaseCondition
	{
		// You can set these directly in the inspector if you want a hardcoded condition
		public EquippableItemTemplate ItemTemplate;

		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			if (initiator == null)
			{
				Log.Warning("HasEquippedItemCondition", "Initiator is null.");
				return false;
			}

			if (!initiator.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("HasEquippedItemCondition", "Initiator does not have an EquipmentController.");
				return false;
			}

			if (ItemTemplate == null)
			{
				Log.Warning("HasEquippedItemCondition", "ItemTemplate is null.");
				return false;
			}

			int templateIDToCheck = ItemTemplate.ID;
			ItemSlot slotToCheck = ItemTemplate.Slot;

			// Override with event data if available
			if (eventData != null && eventData.TryGet(out ItemEventData itemEventData))
			{
				if (itemEventData.Item != null)
				{
					templateIDToCheck = itemEventData.Item.Template.ID;
					if (itemEventData.Item.IsEquippable)
					{
						slotToCheck = (itemEventData.Item.Template as EquippableItemTemplate).Slot;
					}
				}
				slotToCheck = itemEventData.TargetSlot;
			}

			if (equipmentController.TryGetItem((int)slotToCheck, out Item equippedItem))
			{
				if (templateIDToCheck == 0) // Just check if any item is equipped in the slot
				{
					return equippedItem != null;
				}
				else // Check for a specific item template ID
				{
					return equippedItem != null && equippedItem.Template.ID == templateIDToCheck;
				}
			}
			return false;
		}
	}
}