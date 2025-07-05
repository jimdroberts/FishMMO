using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if an item can be equipped by the initiator.
	/// Requires an ItemEventData in the EventData.
	/// </summary>
	[CreateAssetMenu(fileName = "CanEquipItemCondition", menuName = "Runtimes/Conditions/Can Equip Item Condition")]
	public class CanEquipItemCondition : BaseCondition
	{
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			if (initiator == null || eventData == null)
			{
				Log.Warning("CanEquipItemCondition", "Initiator or EventData is null.");
				return false;
			}

			if (!eventData.TryGet(out ItemEventData itemEventData))
			{
				Log.Warning("CanEquipItemCondition", "EventData does not contain ItemEventData.");
				return false;
			}

			Item itemToEquip = itemEventData.Item;
			IItemContainer sourceContainer = itemEventData.SourceContainer;
			ItemSlot targetSlot = itemEventData.TargetSlot;

			if (itemToEquip == null)
			{
				Log.Warning("CanEquipItemCondition", "Item to equip is null in ItemEventData.");
				return false;
			}

			if (!itemToEquip.IsEquippable)
			{
				//Log.Debug($"CanEquipItemCondition", "Item {itemToEquip.Template.name} is not equippable.");
				return false;
			}

			EquippableItemTemplate equippableTemplate = itemToEquip.Template as EquippableItemTemplate;
			if (equippableTemplate == null)
			{
				Log.Warning($"CanEquipItemCondition", "Item {itemToEquip.Template.name} does not have an EquippableItemTemplate.");
				return false;
			}

			// If a specific target slot is provided, ensure it matches the item's slot
			if (targetSlot != equippableTemplate.Slot)
			{
				//Log.Debug($"CanEquipItemCondition", "Target slot {targetSlot} does not match item's required slot {equippableTemplate.Slot}.");
				return false;
			}

			// Get the EquipmentController from the initiator
			if (!initiator.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("CanEquipItemCondition", "Initiator does not have an EquipmentController.");
				return false;
			}

			// Check if the EquipmentController can manipulate (e.g., character is not busy)
			if (!equipmentController.CanManipulate())
			{
				//Log.Debug("CanEquipItemCondition", "EquipmentController cannot manipulate items right now.");
				return false;
			}

			// If there's an item in the target slot, check if it can be unequipped and potentially swapped
			if (equipmentController.TryGetItem((byte)equippableTemplate.Slot, out Item existingItemInSlot))
			{
				// If the existing item is the same as the one we want to equip (already equipped)
				if (existingItemInSlot.ID == itemToEquip.ID && existingItemInSlot.Template.ID == itemToEquip.Template.ID)
				{
					//Log.Debug($"CanEquipItemCondition", "Item {itemToEquip.Template.name} is already equipped in the target slot.");
					return false; // Already equipped
				}

				// If we are swapping, the source container must be able to accept the existing item
				if (sourceContainer != null && !sourceContainer.CanAddItem(existingItemInSlot))
				{
					//Log.Debug($"CanEquipItemCondition", "Source container cannot accommodate existing item {existingItemInSlot.Template.name}.");
					return false;
				}
			}
			else // No item in the target slot
			{
				// If we're moving from a container, ensure the container has the item and can remove it
				if (sourceContainer != null && !sourceContainer.ContainsItem(itemToEquip.Template)) // Assuming Contains method exists
				{
					//Log.Debug($"CanEquipItemCondition", "Source container does not contain item {itemToEquip.Template.name}.");
					return false;
				}
			}

			// Additional checks can be added here, e.g., level requirements, class requirements etc.
			// Example:
			// if (itemToEquip.Template is ILevelRequirement levelReq && initiator.Level < levelReq.RequiredLevel) return false;
			// if (itemToEquip.Template is IClassRequirement classReq && initiator.CharacterClass != classReq.RequiredClass) return false;

			return true;
		}
	}

	/// <summary>
	/// Action that attempts to equip an item.
	/// Requires an ItemEventData in the EventData.
	/// </summary>
	[CreateAssetMenu(fileName = "EquipItemAction", menuName = "Runtimes/Actions/Equip Item Action")]
	public class EquipItemAction : BaseAction
	{
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null || eventData == null)
			{
				Log.Warning("EquipItemAction", "Initiator or EventData is null. Cannot execute action.");
				return;
			}

			if (!eventData.TryGet(out ItemEventData itemEventData))
			{
				Log.Warning("EquipItemAction", "EventData does not contain ItemEventData. Cannot execute action.");
				return;
			}

			Item itemToEquip = itemEventData.Item;
			int inventoryIndex = itemEventData.InventoryIndex;
			IItemContainer sourceContainer = itemEventData.SourceContainer;
			ItemSlot targetSlot = itemEventData.TargetSlot;

			if (itemToEquip == null)
			{
				Log.Warning("EquipItemAction", "Item to equip is null in ItemEventData.");
				return;
			}

			if (!initiator.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("EquipItemAction", "Initiator does not have an EquipmentController.");
				return;
			}

			// It's good practice to re-evaluate conditions here or assume the calling system already did.
			// For robustness, you might want to call the CanEquipItemCondition here, or ensure the event system
			// only triggers this action if the conditions are met.
			// If you want to be super robust:
			// var canEquipCondition = CachedScriptableObject<CanEquipItemCondition>.Get("CanEquipItemCondition"); // Assuming a method to get scriptable objects by name
			// if (canEquipCondition != null && !canEquipCondition.Evaluate(initiator, eventData))
			// {
			//     Log.Warning("EquipItemAction: Conditions for equipping item were not met. Aborting action.");
			//     return;
			// }

			// Ensure the target slot is correctly determined from the item if not explicitly provided
			if (itemToEquip.IsEquippable && itemToEquip.Template is EquippableItemTemplate equippableTemplate)
			{
				targetSlot = equippableTemplate.Slot;
			}

			bool success = equipmentController.Equip(itemToEquip, inventoryIndex, sourceContainer, targetSlot);

			if (success)
			{
				Log.Debug("EquipItemAction", $"Successfully equipped {itemToEquip.Template.name} to {targetSlot}.");
			}
			else
			{
				Log.Warning("EquipItemAction", $"Failed to equip {itemToEquip.Template.name} to {targetSlot}.");
			}
		}
	}

	/// <summary>
	/// Action that attempts to unequip an item.
	/// Requires an ItemEventData in the EventData, or a specific slot.
	/// </summary>
	[CreateAssetMenu(fileName = "UnequipItemAction", menuName = "Runtimes/Actions/Unequip Item Action")]
	public class UnequipItemAction : BaseAction
	{
		public ItemSlot SourceSlotToUnequip = ItemSlot.Head;

		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (initiator == null)
			{
				Log.Warning("UnequipItemAction", "Initiator is null. Cannot execute action.");
				return;
			}

			if (!initiator.TryGet(out EquipmentController equipmentController))
			{
				Log.Warning("UnequipItemAction", "Initiator does not have an EquipmentController.");
				return;
			}

			ItemSlot unequipSlot = SourceSlotToUnequip;
			IItemContainer targetContainer = null;

			// Override with EventData
			if (eventData != null && eventData.TryGet(out ItemEventData itemEventData))
			{
				if (itemEventData.TargetSlot != unequipSlot)
				{
					unequipSlot = itemEventData.TargetSlot;
				}
				targetContainer = itemEventData.SourceContainer; // SourceContainer in ItemEventData represents where the item is going
			}

			if (targetContainer == null)
			{
				Log.Warning("UnequipItemAction", "No target container specified to unequip item to. Aborting.");
				return;
			}

			bool success = equipmentController.Unequip(targetContainer, (byte)unequipSlot, out List<Item> modifiedItems);

			if (success)
			{
				Log.Debug("UnequipItemAction", $"Successfully unequipped item from {unequipSlot}.");
				// You might want to do something with modifiedItems here, e.g., update UI
			}
			else
			{
				Log.Warning("UnequipItemAction", $"Failed to unequip item from {unequipSlot}.");
			}
		}
	}
}